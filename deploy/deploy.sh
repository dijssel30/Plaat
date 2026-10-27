#!/usr/bin/env bash
#
# deploy.sh - build Plaat.Api + Plaat.Frontend (Release, self-contained, single file)
#             and roll them out to the target machine over SSH.
#
# Usage:
#   deploy/deploy.sh                 # build + deploy both apps
#   deploy/deploy.sh api             # build + deploy only the API
#   deploy/deploy.sh frontend        # build + deploy only the frontend
#   deploy/deploy.sh rollback        # roll both apps back to their newest backup
#   deploy/deploy.sh rollback api    # roll only the API back
#   deploy/deploy.sh list-backups    # show retained backups on the target
#
# Config: deploy/deploy.env  (copy from deploy/deploy.env.example). Any variable
# can also be set in the environment. Override the file with DEPLOY_ENV=/path.
#
set -euo pipefail

# ---------------------------------------------------------------------------
# Load config
# ---------------------------------------------------------------------------
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${DEPLOY_ENV:-${SCRIPT_DIR}/deploy.env}"

case "${1:-}" in
    -h|--help|help) sed -n '3,15p' "${BASH_SOURCE[0]}" | sed 's/^#//; s/^ //'; exit 0 ;;
esac

if [[ -f "${ENV_FILE}" ]]; then
    # shellcheck disable=SC1090
    set -a; source "${ENV_FILE}"; set +a
else
    echo "!! Config file not found: ${ENV_FILE}" >&2
    echo "   Copy deploy/deploy.env.example to deploy/deploy.env and edit it." >&2
    exit 1
fi

: "${SSH_HOST:?set SSH_HOST in ${ENV_FILE}}"
: "${SSH_USER:?set SSH_USER in ${ENV_FILE}}"
SSH_PORT="${SSH_PORT:-22}"
SSH_SUDO="${SSH_SUDO-sudo}"
SYSTEMCTL_USER="${SYSTEMCTL_USER:-0}"
SUDO_PASSWORD="${SUDO_PASSWORD:-}"   # target sudo password; empty => prompt once if needed
REMOTE_BASE="${REMOTE_BASE:?set REMOTE_BASE}"
REMOTE_API_DIR="${REMOTE_API_DIR:-${REMOTE_BASE}/api}"
REMOTE_FRONTEND_DIR="${REMOTE_FRONTEND_DIR:-${REMOTE_BASE}/frontend}"
REMOTE_BACKUP_DIR="${REMOTE_BACKUP_DIR:-${REMOTE_BASE}/backups}"
REMOTE_STAGING="${REMOTE_STAGING:-${REMOTE_BASE}/.deploy-staging}"
KEEP_BACKUPS="${KEEP_BACKUPS:-3}"; (( KEEP_BACKUPS < 1 )) && KEEP_BACKUPS=1
API_SERVICE="${API_SERVICE:-plaat-api}"
FRONTEND_SERVICE="${FRONTEND_SERVICE:-plaat-frontend}"
API_HEALTH_URL="${API_HEALTH_URL:-http://127.0.0.1:5014/swagger/index.html}"
FRONTEND_HEALTH_URL="${FRONTEND_HEALTH_URL:-http://127.0.0.1:5179/}"
HEALTH_RETRIES="${HEALTH_RETRIES:-10}"
HEALTH_DELAY="${HEALTH_DELAY:-3}"
PUBLISH_PROFILE="${PUBLISH_PROFILE:-linux-x64}"
# Path component in <PublishDir> of the .pubxml (bin/Release/net10.0/<RID>/publish).
PUBLISH_RID="${PUBLISH_RID:-linux-x64}"
APPSETTINGS_ON_DIFF="${APPSETTINGS_ON_DIFF:-prompt}"

# The deploy only ever writes/removes files that are part of the published build
# (tracked via a manifest) - the SQLite DB, DataProtectionKeys, logs and any other
# runtime state on the target are never touched. These lists are an extra guard used
# for the very first deploy and for anything you dropped in the app dir by hand:
# rsync --exclude patterns, relative to each app dir, space separated.
PROTECT_PATHS_API="${PROTECT_PATHS_API:-Plaat.db Plaat.db-shm Plaat.db-wal Plaat.db-journal *.log logs}"
PROTECT_PATHS_FRONTEND="${PROTECT_PATHS_FRONTEND:-DataProtectionKeys *.log logs}"

STAMP="$(date +%Y%m%d-%H%M%S)"

# ---------------------------------------------------------------------------
# ssh / rsync helpers
# ---------------------------------------------------------------------------
SSH_TARGET="${SSH_USER}@${SSH_HOST}"

# Connection multiplexing: authenticate once (incl. a typed SSH password), then
# every subsequent ssh/rsync call reuses that one connection.
CONTROL_DIR="$(mktemp -d "${TMPDIR:-/tmp}/plaat-deploy.XXXXXX")"
SSH_OPTS=(-p "${SSH_PORT}" -o ConnectTimeout=10
    -o ControlMaster=auto -o ControlPath="${CONTROL_DIR}/cm-%C" -o ControlPersist=120)
[[ -n "${SSH_KEY:-}" ]] && SSH_OPTS+=(-i "${SSH_KEY}")

cleanup() {
    ssh "${SSH_OPTS[@]}" -O exit "${SSH_TARGET}" 2>/dev/null || true
    rm -rf "${CONTROL_DIR}"
}
trap cleanup EXIT

remote()  { ssh "${SSH_OPTS[@]}" "${SSH_TARGET}" "$@"; }
push()    { rsync -rlptz --checksum --delete -e "ssh ${SSH_OPTS[*]}" "$1" "${SSH_TARGET}:$2"; }

# Set by preflight: 0 = user units, 1 = plain sudo (NOPASSWD), 2 = sudo -S with SUDO_PASSWORD
SUDO_MODE=1
[[ "${SYSTEMCTL_USER}" == "1" ]] && SUDO_MODE=0

svc() {
    # $* = systemctl args, e.g. "stop plaat-api"
    case "${SUDO_MODE}" in
        0) remote "systemctl --user $*" ;;
        2) printf '%s\n' "${SUDO_PASSWORD}" | remote "${SSH_SUDO} -S -p '' systemctl $*" ;;
        *) remote "${SSH_SUDO:+${SSH_SUDO} }systemctl $*" ;;
    esac
}

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!\033[0m  %s\n' "$*" >&2; }
die()  { printf '\033[1;31mXX\033[0m  %s\n' "$*" >&2; exit 1; }

# ---------------------------------------------------------------------------
# App metadata
# ---------------------------------------------------------------------------
# name -> project path / remote dir / binary / service / health url / preserve list
app_project() { case "$1" in api) echo "Plaat.Api";; frontend) echo "Plaat.Frontend";; esac; }
app_binary()  { case "$1" in api) echo "Plaat.Api";; frontend) echo "Plaat.Frontend";; esac; }
app_remote()  { case "$1" in api) echo "${REMOTE_API_DIR}";; frontend) echo "${REMOTE_FRONTEND_DIR}";; esac; }
app_service() { case "$1" in api) echo "${API_SERVICE}";; frontend) echo "${FRONTEND_SERVICE}";; esac; }
app_health()  { case "$1" in api) echo "${API_HEALTH_URL}";; frontend) echo "${FRONTEND_HEALTH_URL}";; esac; }
app_protect() { case "$1" in api) echo "${PROTECT_PATHS_API}";; frontend) echo "${PROTECT_PATHS_FRONTEND}";; esac; }
app_publish_dir() { echo "${REPO_ROOT}/$(app_project "$1")/bin/Release/net10.0/${PUBLISH_RID}/publish"; }

MANIFEST=".plaat-manifest"

# turn "a b *.log" into "--exclude='a' --exclude='b' --exclude='*.log'"
build_excludes() {
    local p out=""
    for p in $1; do out+=" --exclude='${p}'"; done
    echo "${out}"
}

# ---------------------------------------------------------------------------
# Preflight
# ---------------------------------------------------------------------------
preflight() {
    command -v dotnet >/dev/null || die "dotnet SDK not found on PATH"
    command -v rsync  >/dev/null || die "rsync not found on this machine"

    log "Connecting to ${SSH_TARGET} (you may be asked for your SSH password once) ..."
    remote true || die "cannot SSH to ${SSH_TARGET}"
    remote "command -v rsync >/dev/null" || die "rsync not installed on the target"
    remote "command -v curl  >/dev/null" || warn "curl missing on target - health checks will be skipped"

    [[ "${SUDO_MODE}" == "0" ]] && return 0   # --user units, no sudo needed
    [[ -z "${SSH_SUDO}" ]]      && { SUDO_MODE=1; return 0; }

    if remote "${SSH_SUDO} -n systemctl --version >/dev/null 2>&1"; then
        SUDO_MODE=1                            # passwordless sudo works
        return 0
    fi

    # sudo needs a password on the target
    if [[ -z "${SUDO_PASSWORD}" ]]; then
        [[ -t 0 ]] || die "sudo needs a password on the target: set SUDO_PASSWORD in ${ENV_FILE} \
or configure NOPASSWD for systemctl (see deploy/README.md)"
        read -r -s -p "   sudo password for ${SSH_USER}@${SSH_HOST}: " SUDO_PASSWORD; echo
    fi
    printf '%s\n' "${SUDO_PASSWORD}" | remote "${SSH_SUDO} -S -p '' systemctl --version >/dev/null 2>&1" \
        || die "sudo password rejected on the target (or ${SSH_USER} may not run systemctl via sudo)"
    SUDO_MODE=2
}

# ---------------------------------------------------------------------------
# Build
# ---------------------------------------------------------------------------
build() {
    local app="$1" proj; proj="$(app_project "$app")"
    log "Publishing ${proj} (Release, self-contained, single file) ..."
    rm -rf "$(app_publish_dir "$app")"
    dotnet publish "${REPO_ROOT}/${proj}/${proj}.csproj" \
        -p:PublishProfile="${PUBLISH_PROFILE}" \
        --nologo -v minimal
    local pubdir; pubdir="$(app_publish_dir "$app")"
    [[ -f "${pubdir}/$(app_binary "$app")" ]] \
        || die "expected binary not found after publish: ${pubdir}/$(app_binary "$app")"
    # Manifest of exactly the files this release owns - the deploy writes/removes
    # only these, never anything else living in the target app dir.
    ( cd "${pubdir}" && find . -type f ! -name "${MANIFEST}" | sed 's|^\./||' | LC_ALL=C sort > "${MANIFEST}" )
}

# ---------------------------------------------------------------------------
# appsettings drift check  (runs before any service is touched)
# Resolves every appsettings*.json in the freshly-built publish dir:
#   keep      -> replace the file in the publish dir with the target's copy
#   overwrite -> leave this machine's file in place
# ---------------------------------------------------------------------------
resolve_appsettings() {
    local app="$1" pubdir remotedir f tmp_remote decision
    pubdir="$(app_publish_dir "$app")"
    remotedir="$(app_remote "$app")"

    shopt -s nullglob
    for path in "${pubdir}"/appsettings*.json; do
        f="$(basename "${path}")"
        tmp_remote="$(mktemp)"
        if ! remote "cat '${remotedir}/${f}' 2>/dev/null" > "${tmp_remote}"; then
            : # non-zero just means file absent
        fi
        if [[ ! -s "${tmp_remote}" ]]; then
            log "[${app}] ${f}: not present on target yet - will upload this machine's copy"
            rm -f "${tmp_remote}"; continue
        fi
        if diff -q "${tmp_remote}" "${path}" >/dev/null; then
            rm -f "${tmp_remote}"; continue   # identical, nothing to decide
        fi

        warn "[${app}] ${f} on the target differs from this machine:"
        diff -u --label "target:${f}" "${tmp_remote}" --label "local:${f}" "${path}" || true

        decision="${APPSETTINGS_ON_DIFF}"
        if [[ "${decision}" == "prompt" ]]; then
            if [[ -t 0 ]]; then
                local ans
                read -r -p "   [k]eep target / [o]verwrite with local / [a]bort ? [k] " ans
                case "${ans:-k}" in
                    o|O) decision="overwrite" ;;
                    a|A) die "aborted by user" ;;
                    *)   decision="keep" ;;
                esac
            else
                warn "no TTY - defaulting to 'keep' for ${f}"
                decision="keep"
            fi
        fi

        if [[ "${decision}" == "keep" ]]; then
            cp "${tmp_remote}" "${path}"
            log "[${app}] ${f}: keeping the target's version"
        else
            log "[${app}] ${f}: overwriting the target with this machine's version"
        fi
        rm -f "${tmp_remote}"
    done
    shopt -u nullglob
}

# ---------------------------------------------------------------------------
# Install a release dir onto the live app dir (all target-local work).
#   remote_apply <src_dir> <live_dir> <binary> <excludes> [backup_dir]
#
#   - copies every file from <src_dir> into <live_dir> (adds + updates)
#   - deletes ONLY files that a previous run installed and this release drops
#     (old manifest minus new manifest)
#   - never writes or deletes anything else: the SQLite DB, DataProtectionKeys,
#     logs, uploads - all untouched
#   - if <backup_dir> is given, the currently-installed app files are copied
#     there first (runtime state excluded)
# ---------------------------------------------------------------------------
remote_apply() {
    local src="$1" live="$2" binary="$3" excludes="$4" backup="${5:-}"
    remote "bash -s" <<REMOTE_EOF
set -euo pipefail
export LC_ALL=C
src='${src}'; live='${live}'; binary='${binary}'; backup='${backup}'; mani='${MANIFEST}'
mkdir -p "\$live"

if [ -n "\$backup" ] && [ -d "\$live" ]; then
    mkdir -p "\$backup"
    if [ -f "\$live/\$mani" ]; then
        rsync -rlpt --ignore-missing-args --files-from="\$live/\$mani" "\$live/" "\$backup/" || true
    else
        rsync -rlpt ${excludes} "\$live/" "\$backup/"   # pre-manifest: best-effort snapshot
    fi
    # the backup always self-describes, so a rollback to it is exact
    ( cd "\$backup" && find . -type f ! -name "\$mani" | sed 's|^\./||' | sort > "\$mani" )
fi

stale="\$(mktemp)"
if [ -f "\$live/\$mani" ] && [ -f "\$src/\$mani" ]; then
    comm -23 <(sort "\$live/\$mani") <(sort "\$src/\$mani") > "\$stale"
else
    : > "\$stale"
fi

rsync -rlpt --checksum ${excludes} "\$src/" "\$live/"

while IFS= read -r f; do
    [ -n "\$f" ] || continue
    case "\$f" in
        appsettings*.json) continue ;;   # config: never auto-removed, even if a build omits it
    esac
    rm -f "\$live/\$f"
done < "\$stale"
rm -f "\$stale"

chmod +x "\$live/\$binary" 2>/dev/null || true
REMOTE_EOF
}

# ---------------------------------------------------------------------------
# Deploy one already-built app  (service is assumed stopped)
# ---------------------------------------------------------------------------
deploy_app() {
    local app="$1" pubdir live staging backup binary excludes
    pubdir="$(app_publish_dir "$app")"
    live="$(app_remote "$app")"
    staging="${REMOTE_STAGING}/${app}"
    backup="${REMOTE_BACKUP_DIR}/${app}/${STAMP}"
    binary="$(app_binary "$app")"
    excludes="$(build_excludes "$(app_protect "$app")")"

    log "[${app}] uploading release to staging"
    remote "mkdir -p '${staging}'"
    push "${pubdir}/" "${staging}/"    # rsync --delete: staging mirrors the publish output exactly

    log "[${app}] installing app files -> ${live}  (DB / DataProtectionKeys / logs left as-is)"
    remote_apply "${staging}" "${live}" "${binary}" "${excludes}" "${backup}"

    eval "BACKUP_${app}='${backup}'"   # remembered for auto-rollback on a failed health check
}

# ---------------------------------------------------------------------------
# Health check
# ---------------------------------------------------------------------------
health_ok() {
    local app="$1" url; url="$(app_health "$app")"
    remote "command -v curl >/dev/null" || { warn "[${app}] no curl on target - skipping health check"; return 0; }
    local i code
    for (( i=1; i<=HEALTH_RETRIES; i++ )); do
        code="$(remote "curl -s -o /dev/null -w '%{http_code}' --max-time 5 '${url}' || true")"
        if [[ -n "${code}" && "${code}" != "000" ]]; then
            log "[${app}] healthy (HTTP ${code} from ${url})"
            return 0
        fi
        sleep "${HEALTH_DELAY}"
    done
    warn "[${app}] no response from ${url} after ${HEALTH_RETRIES} attempts"
    return 1
}

# ---------------------------------------------------------------------------
# Rollback one app to a specific backup dir (or its newest backup)
# ---------------------------------------------------------------------------
rollback_app() {
    local app="$1" target="${2:-}" live binary excludes
    live="$(app_remote "$app")"
    binary="$(app_binary "$app")"
    excludes="$(build_excludes "$(app_protect "$app")")"
    if [[ -z "${target}" ]]; then
        target="$(remote "ls -1d '${REMOTE_BACKUP_DIR}/${app}'/*/ 2>/dev/null | LC_ALL=C sort | tail -n1" || true)"
        target="${target%/}"
    fi
    [[ -n "${target}" ]] || { warn "[${app}] no backup to roll back to"; return 1; }
    remote "test -d '${target}'" || { warn "[${app}] backup dir missing: ${target}"; return 1; }
    local n; n="$(remote "wc -l < '${target}/${MANIFEST}' 2>/dev/null || echo 0")"
    if [[ "${n//[[:space:]]/}" == "0" ]]; then
        warn "[${app}] backup ${target} is empty (it predates the first real deploy) - refusing to roll back to it"
        return 1
    fi
    log "[${app}] rolling back to ${target} (${n// /} files, app files only)"
    remote_apply "${target}" "${live}" "${binary}" "${excludes}" ""
}

prune_backups() {
    local app="$1"
    remote "cd '${REMOTE_BACKUP_DIR}/${app}' 2>/dev/null || exit 0
        ls -1d */ 2>/dev/null | sort | head -n -${KEEP_BACKUPS} | while read -r d; do
            echo '   pruning old backup: '\"\$d\"; rm -rf -- \"\$d\"
        done"
}

# ---------------------------------------------------------------------------
# Commands
# ---------------------------------------------------------------------------
cmd_deploy() {
    local apps=("$@")
    [[ ${#apps[@]} -eq 0 ]] && apps=(api frontend)

    preflight
    for app in "${apps[@]}"; do build "${app}"; done
    for app in "${apps[@]}"; do resolve_appsettings "${app}"; done

    # Stop in reverse dependency order (frontend Requires plaat-api).
    log "Stopping services"
    for app in frontend api; do
        [[ " ${apps[*]} " == *" ${app} "* ]] || continue
        svc stop "$(app_service "$app")" || warn "stop $(app_service "$app") returned non-zero"
    done

    for app in "${apps[@]}"; do deploy_app "${app}"; done

    # Start in dependency order, health-check as we go; roll back on failure.
    local started=()
    for app in api frontend; do
        [[ " ${apps[*]} " == *" ${app} "* ]] || continue
        log "Starting $(app_service "$app")"
        svc start "$(app_service "$app")" || warn "start $(app_service "$app") returned non-zero"
        started+=("${app}")
        if ! health_ok "${app}"; then
            warn "[${app}] unhealthy - rolling back this deploy"
            svc stop "$(app_service "$app")" || true
            local bvar="BACKUP_${app}"
            rollback_app "${app}" "${!bvar:-}" || true
            svc start "$(app_service "$app")" || true
            health_ok "${app}" || warn "[${app}] still unhealthy after rollback - check journalctl -u $(app_service "$app")"
            # bring back anything we already started for a consistent state
            die "deploy failed on ${app}; rolled back. Other apps left running: ${started[*]}"
        fi
    done

    for app in "${apps[@]}"; do prune_backups "${app}"; done
    log "Deploy complete: ${apps[*]}  (backup stamp ${STAMP}, keeping ${KEEP_BACKUPS} per app)"
}

cmd_rollback() {
    local apps=("$@")
    [[ ${#apps[@]} -eq 0 ]] && apps=(api frontend)
    preflight
    for app in frontend api; do
        [[ " ${apps[*]} " == *" ${app} "* ]] || continue
        svc stop "$(app_service "$app")" || true
    done
    local ok=1
    for app in "${apps[@]}"; do rollback_app "${app}" || ok=0; done
    for app in api frontend; do
        [[ " ${apps[*]} " == *" ${app} "* ]] || continue
        svc start "$(app_service "$app")" || warn "start $(app_service "$app") returned non-zero"
        health_ok "${app}" || warn "[${app}] unhealthy after rollback"
    done
    (( ok )) || die "one or more apps could not be rolled back (see warnings above)"
    log "Rollback complete: ${apps[*]}"
}

cmd_list_backups() {
    for app in api frontend; do
        echo "== ${app} =="
        remote "ls -1dt '${REMOTE_BACKUP_DIR}/${app}'/*/ 2>/dev/null" || echo "   (none)"
    done
}

# ---------------------------------------------------------------------------
# Entry point
# ---------------------------------------------------------------------------
COMMAND="${1:-deploy}"; shift || true
case "${COMMAND}" in
    deploy)                    cmd_deploy "$@" ;;
    api|frontend)              cmd_deploy "${COMMAND}" "$@" ;;
    rollback)                  cmd_rollback "$@" ;;
    list-backups|backups)      cmd_list_backups ;;
    *) die "unknown command: ${COMMAND} (try: deploy | api | frontend | rollback | list-backups)" ;;
esac
