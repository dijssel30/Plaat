#!/usr/bin/env bash
#
# deploy-to-phone.sh - build the current Plaat.Mobile source and install/launch
#                      it on a physically-connected Android phone over USB (adb).
#
# Usage:
#   ./Plaat.Mobile/deploy-to-phone.sh            # auto-pick the single connected device
#   ./Plaat.Mobile/deploy-to-phone.sh <serial>   # target a specific device (see: adb devices)
#
# Config: Plaat.Mobile/deploy-to-phone.env  (copy from deploy-to-phone.env.example).
# That file holds machine-specific toolchain paths and is gitignored. Any variable
# in it can also just be exported in the environment before running this script.
#
# Prereqs:
#   - .NET 10 SDK on PATH.
#   - USB debugging enabled on the phone, cable plugged in, and the
#     "Allow USB debugging" prompt accepted for this computer.

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
PKG="${MOBILE_PACKAGE:-com.plaat.mobile}"
# Release by default: this is the build you actually use day to day, and a Debug build (JIT, Fast
# Deployment) starts several times slower. MOBILE_CONFIGURATION=Debug for a quicker build to debug with.
CONFIGURATION="${MOBILE_CONFIGURATION:-Release}"

export JAVA_HOME
export PATH="${JAVA_HOME}/bin:${ANDROID_SDK}/platform-tools:${PATH}"

# --- pick target device --------------------------------------------------------
SERIAL="${1:-${MOBILE_DEVICE_SERIAL:-}}"
if [[ -z "${SERIAL}" ]]; then
  mapfile -t DEVICES < <(adb devices | awk 'NR>1 && $2=="device" {print $1}')
  if [[ ${#DEVICES[@]} -eq 0 ]]; then
    echo "No authorized adb device found. Plug in the phone, unlock it, and accept the USB-debugging prompt." >&2
    adb devices -l >&2
    exit 1
  fi
  if [[ ${#DEVICES[@]} -gt 1 ]]; then
    echo "Multiple devices connected; pass one explicitly or set MOBILE_DEVICE_SERIAL:" >&2
    printf '  %s\n' "${DEVICES[@]}" >&2
    exit 1
  fi
  SERIAL="${DEVICES[0]}"
fi

echo ">> Target device: ${SERIAL}"
adb -s "${SERIAL}" devices -l | grep "${SERIAL}" || true
echo ">> Source revision: $(git -C "${REPO_ROOT}" log --oneline -1)"

# --- build + install + launch ------------------------------------------------
# `-t:Run` builds, installs, and starts the app. AdbTarget pins it to our chosen serial.
echo ">> Configuration: ${CONFIGURATION}"
dotnet build "${REPO_ROOT}/Plaat.Mobile" -t:Run -f net10.0-android -c "${CONFIGURATION}" \
  -p:AndroidSdkDirectory="${ANDROID_SDK}" \
  -p:JavaSdkDirectory="${JAVA_HOME}" \
  -p:AdbTarget="-s ${SERIAL}"

# --- verify ------------------------------------------------------------------
echo ">> Installed version:"
adb -s "${SERIAL}" shell dumpsys package "${PKG}" | grep -E "versionName|lastUpdateTime" || true

echo ">> Launching..."
adb -s "${SERIAL}" shell monkey -p "${PKG}" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1 || true
sleep 3
adb -s "${SERIAL}" shell dumpsys activity activities | grep -m1 -i "topResumedActivity" || true

echo ">> Done."
