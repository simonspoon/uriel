#!/usr/bin/env bash
# Drive the UrielDemo app through qorvex to exercise every Uriel telemetry channel,
# while its console (where §URIEL§ JSON-L lands) is captured losslessly.
#
# WHY console-pty (not `log stream`): on iOS, .NET Console.WriteLine is routed
# through NSLog/os_log. `simctl spawn ... log stream` drops messages under load
# ("Messages dropped during live streaming") and loses the high-volume / large
# records. `simctl launch --console-pty` is a direct per-process pipe with no
# drops, so we launch the app that way and let qorvex drive the *foreground* app
# WITHOUT calling start-target (qorvex's XCUITest agent attaches to whatever is
# foreground, so console-pty keeps owning the launch and the capture stays clean).
#
# Usage: qorvex_drive.sh [SESSION] [UDID] [BUNDLE] [LOG] [APP_PATH]
set -euo pipefail

SESSION="${1:-uriel}"
UDID="${2:-2E93506F-EA47-442A-999E-33B1D8092B92}"
BUNDLE="${3:-com.companyname.urieldemo}"
LOG="${4:-/tmp/uriel.console.log}"
APP="${5:-}"   # optional: path to .app to (re)install first

Q() { qorvex -s "$SESSION" "$@"; }

# 1. qorvex server + session + automation agent (XCUITest runner) for this sim.
echo "▶ ensure qorvex agent on $UDID"
qorvex -s "$SESSION" start -d "$UDID" >/dev/null 2>&1 || true
Q use-device "$UDID" >/dev/null 2>&1 || true
Q start-agent --platform ios >/dev/null 2>&1 || true

# 2. (optional) install, then launch the app under a lossless console pipe.
[ -n "$APP" ] && xcrun simctl install "$UDID" "$APP"
echo "▶ launch $BUNDLE under --console-pty → $LOG"
xcrun simctl terminate "$UDID" "$BUNDLE" 2>/dev/null || true
sleep 1
: > "$LOG"
nohup xcrun simctl launch --console-pty "$UDID" "$BUNDLE" >"$LOG" 2>&1 &
sleep 6

# 3. Point qorvex at the bundle (NO start-target — keep console-pty's launch).
Q set-target "$BUNDLE"

echo "▶ wait for UI + confirm render"
Q wait-for lblStatus -o 15000
Q screen-info --pretty | grep -E "btn|lblStatus" || true
Q screenshot --tag uriel-loaded >/dev/null

# 4. Tap each button by AutomationId (falls back to visible label text).
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
