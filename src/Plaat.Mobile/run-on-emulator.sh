#!/usr/bin/env bash
#
# run-on-emulator.sh - one-shot local dev loop for Plaat.Mobile:
#   1. starts the Plaat.Api backend (bound to 0.0.0.0 so the emulator can reach it)
#   2. starts the Android emulator and waits for it to finish booting
#   3. builds Plaat.Mobile and installs it on that emulator
#   4. points the app's saved server address at the local backend
#   5. launches the app and signs in with the default admin credentials
#
# Usage:
#   ./Plaat.Mobile/run-on-emulator.sh                     # everything, default AVD
#   ./Plaat.Mobile/run-on-emulator.sh --no-backend        # backend already running elsewhere
#   ./Plaat.Mobile/run-on-emulator.sh --server http://192.168.1.10:5014
#   ./Plaat.Mobile/run-on-emulator.sh --avd plaat
#   ./Plaat.Mobile/run-on-emulator.sh --clean            # uninstall first, fresh install
#   ./Plaat.Mobile/run-on-emulator.sh --no-login         # stop at the login screen
#
# Login uses admin/Admin123! (the user Plaat.Api seeds on first run); override with
# PLAAT_USERNAME / PLAAT_PASSWORD.
#
# The emulator and the backend are left running after the script exits (that's the point -
# you keep using them). Stop them with:  adb emu kill   and   kill <backend pid printed below>.
#
# Config: Plaat.Mobile/deploy-to-phone.env (copy from deploy-to-phone.env.example) - the same
# gitignored file deploy-to-phone.sh uses, since it holds the machine-specific toolchain paths.
# Any variable in it can also just be exported before running this script.
#
# Prereqs: .NET 10 SDK on PATH, Android SDK with an AVD created (see CLAUDE.md).

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
ENV_FILE="${MOBILE_DEPLOY_ENV:-${SCRIPT_DIR}/deploy-to-phone.env}"

# --- load config -----------------------------------------------------------
if [[ -f "${ENV_FILE}" ]]; then
  # shellcheck disable=SC1090
  set -a; source "${ENV_FILE}"; set +a
elif [[ -z "${ANDROID_SDK:-}" || -z "${JAVA_HOME:-}" ]]; then
  echo "!! Config file not found: ${ENV_FILE}" >&2
  echo "   Copy ${SCRIPT_DIR}/deploy-to-phone.env.example to deploy-to-phone.env and edit it," >&2
  echo "   or export ANDROID_SDK and JAVA_HOME yourself before running this script." >&2
  exit 1
fi

: "${ANDROID_SDK:?set ANDROID_SDK in ${ENV_FILE}}"
: "${JAVA_HOME:?set JAVA_HOME in ${ENV_FILE}}"

AVD="${EMULATOR_AVD:-plaat}"
PKG="${MOBILE_PACKAGE:-com.plaat.mobile}"
BACKEND_PORT="${BACKEND_PORT:-5014}"
BACKEND_URI="${BACKEND_URI:-}"
START_BACKEND=1
CLEAN_INSTALL=0
DO_LOGIN=1
LOGIN_USER="${PLAAT_USERNAME:-admin}"
LOGIN_PASS="${PLAAT_PASSWORD:-Admin123!}"
LOG_DIR="${MOBILE_LOG_DIR:-/tmp/plaat-emulator}"

export JAVA_HOME
export PATH="${JAVA_HOME}/bin:${ANDROID_SDK}/platform-tools:${ANDROID_SDK}/emulator:${PATH}"

# --- args ------------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --no-backend) START_BACKEND=0; shift ;;
    --clean)      CLEAN_INSTALL=1; shift ;;
    --no-login)   DO_LOGIN=0; shift ;;
    --server)     BACKEND_URI="${2:?--server needs a URI}"; shift 2 ;;
    --avd)        AVD="${2:?--avd needs an AVD name}"; shift 2 ;;
    -h|--help)    sed -n '2,25p' "${BASH_SOURCE[0]}"; exit 0 ;;
    *) echo "Unknown argument: $1 (see --help)" >&2; exit 1 ;;
  esac
done

mkdir -p "${LOG_DIR}"

# The emulator reaches the host over the LAN address, not localhost/10.0.2.2 - see CLAUDE.md.
if [[ -z "${BACKEND_URI}" ]]; then
  HOST_IP="$(ip route get 1.1.1.1 2>/dev/null | sed -n 's/.* src \([0-9.]*\).*/\1/p' | head -1)"
  : "${HOST_IP:?could not detect the LAN IP of this machine - pass --server http://<ip>:${BACKEND_PORT}}"
  BACKEND_URI="http://${HOST_IP}:${BACKEND_PORT}"
fi

backend_is_up() {
  # Any HTTP status proves something is listening and speaking HTTP (/ itself is a 404).
  curl -fsS -o /dev/null --max-time 2 "$1/" 2>/dev/null || \
  [[ -n "$(curl -sS -o /dev/null -w '%{http_code}' --max-time 2 "$1/" 2>/dev/null | grep -v '^000$')" ]]
}

# --- 1. backend ------------------------------------------------------------
BACKEND_LOG="${LOG_DIR}/plaat-api.log"
if [[ ${START_BACKEND} -eq 1 ]]; then
  if backend_is_up "${BACKEND_URI}"; then
    echo ">> Backend already reachable at ${BACKEND_URI} - leaving it alone."
  else
    echo ">> Starting Plaat.Api (profile http_local, binds 0.0.0.0:${BACKEND_PORT}) -> ${BACKEND_LOG}"
    setsid nohup dotnet run --project "${REPO_ROOT}/Plaat.Api" --launch-profile http_local \
      >"${BACKEND_LOG}" 2>&1 &
    BACKEND_PID=$!
    echo "   backend pid ${BACKEND_PID} (stop it later with: kill ${BACKEND_PID})"
    for _ in $(seq 60); do
      backend_is_up "${BACKEND_URI}" && break
      kill -0 "${BACKEND_PID}" 2>/dev/null || { echo "!! Backend exited; see ${BACKEND_LOG}" >&2; tail -20 "${BACKEND_LOG}" >&2; exit 1; }
      sleep 2
    done
    backend_is_up "${BACKEND_URI}" || { echo "!! Backend did not come up at ${BACKEND_URI}; see ${BACKEND_LOG}" >&2; exit 1; }
  fi
else
  echo ">> Skipping backend start (--no-backend); app will point at ${BACKEND_URI}"
fi
echo ">> Backend for the app: ${BACKEND_URI}"

# --- 2. emulator -----------------------------------------------------------
adb start-server >/dev/null 2>&1 || true
SERIAL="$(adb devices | awk '$2=="device" && $1 ~ /^emulator-/ {print $1; exit}')"

if [[ -n "${SERIAL}" ]]; then
  echo ">> Reusing running emulator: ${SERIAL}"
else
  emulator -list-avds | grep -qx "${AVD}" || {
    echo "!! AVD '${AVD}' not found. Available:" >&2; emulator -list-avds >&2; exit 1; }
  echo ">> Starting emulator '${AVD}' -> ${LOG_DIR}/emulator.log"
  setsid nohup emulator -avd "${AVD}" -netdelay none -netspeed full \
    >"${LOG_DIR}/emulator.log" 2>&1 &
  for _ in $(seq 90); do
    SERIAL="$(adb devices | awk '$2=="device" && $1 ~ /^emulator-/ {print $1; exit}')"
    [[ -n "${SERIAL}" ]] && break
    sleep 2
  done
  [[ -n "${SERIAL}" ]] || { echo "!! Emulator never appeared in 'adb devices'; see ${LOG_DIR}/emulator.log" >&2; exit 1; }
fi

echo ">> Waiting for boot to complete on ${SERIAL}..."
adb -s "${SERIAL}" wait-for-device
adb -s "${SERIAL}" shell 'while [ "$(getprop sys.boot_completed)" != "1" ]; do sleep 1; done'
adb -s "${SERIAL}" shell input keyevent 82 >/dev/null 2>&1 || true   # dismiss lock screen
echo ">> Emulator ready: ${SERIAL}"

# --- 3. build + install ----------------------------------------------------
install_app() {
  dotnet build "${REPO_ROOT}/Plaat.Mobile" -t:Install -f net10.0-android \
    -p:AndroidSdkDirectory="${ANDROID_SDK}" \
    -p:JavaSdkDirectory="${JAVA_HOME}" \
    -p:AdbTarget="-s ${SERIAL}"
}

# ServerAddressStore keeps the address in MAUI Preferences, which on Android is the app's default
# SharedPreferences file: shared_prefs/<package>_preferences.xml. (Not to be confused with
# shared_prefs/<package>.microsoft.maui.essentials.preferences.xml - that one is SecureStorage's
# encrypted store, where the refresh token lives.) Seeding it before first launch means MauiProgram
# builds IPlaatClient against the local backend and the login screen comes up pre-filled with it.
# ServerBaseUri is the app's only plain preference, so writing the whole file loses nothing.
# `run-as` is what lets adb write in there (debug builds are debuggable).
seed_server_address() {
  local pref_file="shared_prefs/${PKG}_preferences.xml"
  local xml="<?xml version='1.0' encoding='utf-8' standalone='yes' ?><map><string name=\"ServerBaseUri\">${BACKEND_URI}/</string></map>"

  if adb -s "${SERIAL}" shell "run-as ${PKG} sh -c 'mkdir -p shared_prefs && cat > ${pref_file}' <<'XMLEOF'
${xml}
XMLEOF" >/dev/null 2>&1 &&
     adb -s "${SERIAL}" shell "run-as ${PKG} cat ${pref_file}" 2>/dev/null | grep -q "${BACKEND_URI}"; then
    echo ">> Server address seeded: ${BACKEND_URI} (${pref_file})"
  else
    echo "!! Could not seed the server address automatically." >&2
    echo "   Type it on the app's login screen instead: ${BACKEND_URI}" >&2
  fi
}

# --- UI driving (uiautomator, so taps follow the real layout instead of fixed coordinates) ---
ui_dump() {
  adb -s "${SERIAL}" shell uiautomator dump /sdcard/plaat-ui.xml >/dev/null 2>&1 || return 1
  adb -s "${SERIAL}" shell cat /sdcard/plaat-ui.xml 2>/dev/null | tr -d '\r'
}

# Centre point ("x y") of the first node in the dump (stdin) matching $1.
node_center() {
  tr '<' '\n' | grep -m1 -- "$1" |
    sed -n 's/.*bounds="\[\([0-9]*\),\([0-9]*\)\]\[\([0-9]*\),\([0-9]*\)\].*/\1 \2 \3 \4/p' |
    awk '{ printf "%d %d\n", ($1 + $3) / 2, ($2 + $4) / 2 }'
}

tap_node() {  # $1 = dump, $2 = node pattern
  local xy
  xy="$(printf '%s' "$1" | node_center "$2")"
  [[ -n "${xy}" ]] || return 1
  # shellcheck disable=SC2086
  adb -s "${SERIAL}" shell input tap ${xy}
}

# Fills in the login screen and waits for the home page. The session survives in SecureStorage,
# so on later runs the app restores it by itself and this just detects that and returns.
auto_login() {
  local dump="" state=""
  for _ in $(seq 20); do
    dump="$(ui_dump || true)"
    if printf '%s' "${dump}" | grep -q 'hint="Username"'; then state=login; break; fi
    if printf '%s' "${dump}" | grep -q 'text="Home"'; then state=home; break; fi
    sleep 1
  done

  case "${state}" in
    home) echo ">> Already signed in - session restored."; return 0 ;;
    "")   echo "!! Could not read the app UI; not logging in automatically." >&2; return 1 ;;
  esac

  echo ">> Signing in as ${LOGIN_USER}..."
  tap_node "${dump}" 'hint="Username"' || return 1
  adb -s "${SERIAL}" shell "input text '${LOGIN_USER}'"
  tap_node "${dump}" 'hint="Password"' || return 1
  adb -s "${SERIAL}" shell "input text '${LOGIN_PASS}'"
  adb -s "${SERIAL}" shell input keyevent 111   # close the keyboard, it covers the button
  sleep 1

  # Re-dump first: an error message from a previous attempt shifts the button down.
  dump="$(ui_dump || true)"
  tap_node "${dump}" 'class="android.widget.Button"' || return 1

  for _ in $(seq 20); do
    dump="$(ui_dump || true)"
    printf '%s' "${dump}" | grep -q 'text="Home"' && { echo ">> Signed in."; return 0; }
    sleep 1
  done

  echo "!! Login did not complete. The app says:" >&2
  # TextViews only - the EditTexts would just echo back the address/username we typed.
  printf '%s' "${dump}" | tr '<' '\n' | grep 'class="android.widget.TextView"' |
    grep -o 'text="[^"]*"' | grep -vE 'text=""|text="Plaat"' | head -3 >&2
  return 1
}

# Returns non-zero if the app is not alive shortly after launching - a MAUI app whose Fast
# Deployment payload is stale (e.g. after `adb shell pm clear`) aborts at startup with
# "No assemblies found in .../.__override__", which otherwise just looks like nothing happened.
launch_app() {
  adb -s "${SERIAL}" shell am force-stop "${PKG}" >/dev/null 2>&1 || true
  adb -s "${SERIAL}" shell monkey -p "${PKG}" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
  # Waiting for the app to be the *resumed* activity, not merely for a pid: a crashing app does
  # have a pid for a moment, then dies and leaves the launcher (or a crash dialog) on top.
  for _ in $(seq 30); do
    if adb -s "${SERIAL}" shell dumpsys activity activities | grep -m1 -i "topResumedActivity" | grep -q "${PKG}"; then
      return 0
    fi
    sleep 1
  done
  return 1
}

echo ">> Source revision: $(git -C "${REPO_ROOT}" log --oneline -1)"

if [[ ${CLEAN_INSTALL} -eq 1 ]]; then
  echo ">> Uninstalling ${PKG} first (--clean)"
  adb -s "${SERIAL}" uninstall "${PKG}" >/dev/null 2>&1 || true
fi

echo ">> Building + installing Plaat.Mobile..."
install_app

# --- 4. point the app at the local backend + 5. launch ---------------------
seed_server_address

echo ">> Launching ${PKG}..."
adb -s "${SERIAL}" logcat -c >/dev/null 2>&1 || true
if ! launch_app; then
  echo "!! App did not start - the install on the device is probably stale. Reinstalling clean..." >&2
  adb -s "${SERIAL}" uninstall "${PKG}" >/dev/null 2>&1 || true
  install_app
  seed_server_address   # a clean install wipes the data dir, so seed again
  launch_app || {
    echo "!! App still fails to start. Last crash lines:" >&2
    adb -s "${SERIAL}" logcat -d -t 300 | grep -iE "AndroidRuntime|FATAL|Abort message" | tail -10 >&2
    exit 1
  }
fi
adb -s "${SERIAL}" shell dumpsys activity activities | grep -m1 -i "topResumedActivity" || true

# --- 6. sign in ------------------------------------------------------------
LOGIN_NOTE="signed in as ${LOGIN_USER}"
if [[ ${DO_LOGIN} -eq 1 ]]; then
  auto_login || LOGIN_NOTE="NOT signed in - finish on the login screen (${LOGIN_USER} / ${LOGIN_PASS})"
else
  LOGIN_NOTE="left at the login screen (--no-login)"
fi

cat <<EOF

>> Done.
   Emulator : ${SERIAL} (stop with: adb emu kill)
   Backend  : ${BACKEND_URI} (log: ${BACKEND_LOG})
   App      : ${LOGIN_NOTE}
   Screenshot: adb -s ${SERIAL} exec-out screencap -p > /tmp/plaat.png
EOF


# fuser -k 5014/tcp
