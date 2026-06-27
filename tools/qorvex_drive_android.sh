#!/usr/bin/env bash
# Drive the UrielDemo app on Android through qorvex to exercise every Uriel
# telemetry channel, while logcat (where @@URIEL@@ JSON-L lands) is captured.
#
# WHY logcat (not iOS --console-pty): on Android, .NET Console.WriteLine is
# redirected to the Android log under tag "DOTNET". `adb logcat` is a direct
# pipe off the device ring buffer — capture it to a file continuously and it
# does not drop like iOS `log stream`. We launch the app with qorvex
# (UIAutomator agent) and let logcat own the capture in parallel.
#
# Usage: qorvex_drive_android.sh [SESSION] [SERIAL] [PKG] [LOG] [APK_PATH]
set -euo pipefail

SESSION="${1:-uriel-android}"
SERIAL="${2:-emulator-5554}"
PKG="${3:-com.companyname.urieldemo}"
LOG="${4:-/tmp/uriel.logcat.log}"
APK="${5:-}"   # optional: path to .apk (or .aab) to (re)install first

export ANDROID_HOME="${ANDROID_HOME:-/usr/local/share/android-commandlinetools}"
export ANDROID_SDK_ROOT="$ANDROID_HOME"
# qorvex shells out to `adb`, so it must be on PATH (not just via $ADB below).
export PATH="$ANDROID_HOME/platform-tools:$ANDROID_HOME/emulator:$PATH"
ADB="$ANDROID_HOME/platform-tools/adb"

Q() { qorvex -s "$SESSION" "$@"; }

# NOTE on env: qorvex shells out to `adb` AND builds its UIAutomator agent with
# Gradle (AGP needs JDK 17). Before running, in the SAME shell that starts the
# qorvex SERVER, export:
#   export JAVA_HOME=/usr/local/opt/openjdk@17   # AGP 8.x requires Java 17
#   export QORVEX_ANDROID_JAVA_HOME=$JAVA_HOME
# If the server was started with an older JDK, `qorvex -s $S stop` then re-start.
# Also: the APK MUST be self-contained (build with -p:EmbedAssembliesIntoApk=true,
# or use `dotnet build -t:Install`). A bare `adb install` of a Debug fast-deploy
# APK SIGABRTs ("No assemblies found ... Fast Deployment").

# 1. qorvex server + session for this device (agent comes LATER — see step 4).
echo "▶ ensure qorvex server/session on $SERIAL"
Q start -d "$SERIAL"   >/dev/null 2>&1 || true
Q use-device "$SERIAL" >/dev/null 2>&1 || true

# 2. (optional) install the app.
[ -n "$APK" ] && { echo "▶ install $APK"; "$ADB" -s "$SERIAL" install -r -g "$APK"; }

# 3. Start a clean, continuous logcat capture (lossless to file).
echo "▶ capture logcat → $LOG"
"$ADB" -s "$SERIAL" logcat -c || true
: > "$LOG"
nohup "$ADB" -s "$SERIAL" logcat -v brief > "$LOG" 2>&1 &
LOGCAT_PID=$!
trap '[ -n "${LOGCAT_PID:-}" ] && kill "$LOGCAT_PID" 2>/dev/null || true' EXIT

# 4. Launch the app with ADB (NOT `qorvex start-target` — that needs the agent,
# and connecting the UIAutomator agent during the splash transition wedges it,
# "no active window to dump"). Launch first, wait for MainActivity foreground,
# THEN attach the agent to the live app.
"$ADB" -s "$SERIAL" shell am force-stop "$PKG" 2>/dev/null || true
sleep 1
echo "▶ launch $PKG via adb"
"$ADB" -s "$SERIAL" shell monkey -p "$PKG" -c android.intent.category.LAUNCHER 1 >/dev/null 2>&1
echo "▶ wait for $PKG MainActivity to be foreground"
for i in $(seq 1 30); do
  cur=$("$ADB" -s "$SERIAL" shell dumpsys window 2>/dev/null | grep -m1 mCurrentFocus)
  case "$cur" in *"$PKG"*"MainActivity"*) break;; esac
  sleep 1
done
sleep 2
# The agent's first connect often binds to the stale splash window and can't
# dump the live app ("no active window to dump"). Re-running start-agent rebinds
# it. Retry connect + a screen-info probe until lblStatus is actually visible.
echo "▶ connect UIAutomator agent to the live app (retry until it can dump)"
ready=0
for attempt in 1 2 3 4 5; do
  Q use-device "$SERIAL"       >/dev/null 2>&1 || true
  Q start-agent --platform android >/dev/null 2>&1 || true
  Q set-target "$PKG"          >/dev/null 2>&1 || true
  if Q screen-info --pretty 2>/dev/null | grep -q lblStatus; then
    echo "  agent ready (attempt $attempt)"; ready=1; break
  fi
  echo "  attempt $attempt: agent not bound yet, retrying…"; sleep 2
done
[ "$ready" = 1 ] || { echo "✗ agent never bound to live app"; exit 1; }
Q screen-info --pretty | grep -E "btn|lblStatus" || true
Q screenshot --tag uriel-android-loaded >/dev/null

# 5. Tap each button by AutomationId (falls back to visible label text).
tap_one() {
  local id="$1" label="$2"
  Q tap "$id" -o 6000 --tag "$id" 2>/dev/null \
    || { echo "  (id '$id' missed; tapping label '$label')"; Q tap "$label" -l -o 6000 --tag "$id-label"; }
  sleep 1.5
}
echo "▶ drive buttons"
tap_one btnAllocate "Allocate"
tap_one btnStack    "Capture Stack"
tap_one btnMark     "Mark"
tap_one btnThrow    "Throw"
tap_one btnHang     "Hang"

echo "▶ lblStatus after hang: $(Q get-value lblStatus | head -1)"

echo "▶ dwell for periodic counters (Uriel interval 1s)"
sleep 12
echo "▶ done — parse with: grep '@@URIEL@@' $LOG | python3 tools/uriel_parse.py"
