#!/usr/bin/env bash
# Boot an iOS simulator, install + launch the UrielDemo app with stdout captured,
# then leave it foregrounded for qorvex to drive. Console output (where Uriel's
# §URIEL§ JSON-L lands) is teed to $LOG so we can reassemble it with uriel_parse.py.
#
# Usage: ios_validate.sh <SIM_UDID> <BUNDLE_ID> <APP_PATH> [LOG]
set -euo pipefail

UDID="${1:?sim udid}"
BUNDLE="${2:?bundle id}"
APP="${3:?path to .app}"
LOG="${4:-/tmp/urieldemo.console.log}"

echo "▶ booting $UDID"
xcrun simctl boot "$UDID" 2>/dev/null || true
xcrun simctl bootstatus "$UDID" -b

echo "▶ installing $APP"
xcrun simctl install "$UDID" "$APP"

echo "▶ launching $BUNDLE (stdout → $LOG)"
: > "$LOG"
# --console-pty streams the app's stdout/stderr (where .NET Console.WriteLine goes)
# Run in background so the script returns and qorvex can drive the UI.
nohup xcrun simctl launch --console-pty "$UDID" "$BUNDLE" >"$LOG" 2>&1 &
echo "launch pid $!"
echo "▶ ready. tail -f $LOG to watch; drive with qorvex."
