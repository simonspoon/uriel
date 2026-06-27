# Reading the Stream

[← Channels](Channels.md) · [Wiki home](README.md) · Next: [API Reference →](API-Reference.md)

Uriel writes to the **console**. Getting it back = capture the console → grep the sentinel → reassemble with [`uriel_parse.py`](../tools/uriel_parse.py).

```
 device console ──capture──► raw log ──grep '@@URIEL@@'──► chunks ──uriel_parse.py──► JSON-L ──► jq/pandas
```

The capture step differs per platform. **The reassembly step is identical everywhere.**

---

## The parser (same on every platform)

[`tools/uriel_parse.py`](../tools/uriel_parse.py) — stdlib only, no install.

```bash
python3 tools/uriel_parse.py < captured.log        # from a file
adb logcat | python3 tools/uriel_parse.py          # live pipe
python3 tools/uriel_parse.py --sentinel '§URIEL§'  # custom marker
python3 tools/uriel_parse.py --raw                 # emit inner JSON verbatim, no re-pretty
```

What it does: groups chunks by `s` (seq), orders by `c[0]`, concatenates `d`, base64-decodes → one inner JSON record per line, with `seq` added. Malformed/truncated chunks are skipped (a seq gap will show the loss).

Output:
```json
{"t":1719400000123,"k":"counter","n":"heap-bytes","v":8557240,"sid":"my-run","seq":2}
```

---

## 🖥️ Desktop (CoreCLR) — easiest, no device

```bash
dotnet run --project samples/Harness -c Release | python3 tools/uriel_parse.py
```
Console = stdout. This is also where the **EventCounters bonus** (27 rich counters) shows up.

---

## 🍏 iOS simulator

`.NET Console.WriteLine` → `NSLog`/`os_log`. **Do NOT use `log stream`** — it drops messages under load. Use `simctl launch --console-pty`, a lossless per-process pipe.

```bash
# build (Intel Macs need the x64 simulator RID)
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-x64

# boot + launch with lossless console capture (see tools/ios_validate.sh)
xcrun simctl boot <UDID>
: > /tmp/uriel.log
nohup xcrun simctl launch --console-pty <UDID> com.companyname.urieldemo >/tmp/uriel.log 2>&1 &

# ...interact with the app, then reassemble
grep '@@URIEL@@' /tmp/uriel.log | python3 tools/uriel_parse.py
```

> **Why `--console-pty` not `log stream`:** `log stream` reports *"Messages dropped during live streaming"* under high volume and loses big records. `--console-pty` is a direct pipe with no drops. Rationale lives in [`tools/qorvex_drive.sh`](../tools/qorvex_drive.sh).

To drive the UI automatically (taps every demo button), see [Demo & Validation](Demo-and-Validation.md).

---

## 🤖 Android emulator / device

`.NET Console.WriteLine` → Android log, **tag `DOTNET`**. `adb logcat` is a direct pipe off the ring buffer — capture continuously, no drops.

```bash
# build a SELF-CONTAINED apk (critical — see warning)
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-android -c Debug \
  -p:EmbedAssembliesIntoApk=true

adb install -r .../com.companyname.urieldemo-Signed.apk
adb shell monkey -p com.companyname.urieldemo 1     # launch

# capture (live or to file), then reassemble
adb logcat | grep '@@URIEL@@' | python3 tools/uriel_parse.py
# or: adb logcat -d > /tmp/uriel.logcat.log ; grep '@@URIEL@@' /tmp/uriel.logcat.log | python3 tools/uriel_parse.py
```

On the wire (before reassembly):
```
I/DOTNET ( 13918): @@URIEL@@ {"s":1,"c":[1,1],"d":"eyJ0IjoxNzgyNTg0OTU2MDU1LCJrIjoibWV0YSIs..."}
```

> ⚠️ **Self-contained APK required.** A bare `adb install` of a Debug *fast-deploy* APK **SIGABRTs** (*"No assemblies found … Fast Deployment"*). Build with `-p:EmbedAssembliesIntoApk=true` or `dotnet build -t:Install`. See [Troubleshooting](Troubleshooting.md#android-app-crashes-on-launch-sigabrt).

---

## Pipe it onward

Once it's JSON-L, it's just data:

```bash
# only heap counters
... | python3 tools/uriel_parse.py | jq -c 'select(.k=="counter" and .n=="heap-bytes")'

# every exception type
... | python3 tools/uriel_parse.py | jq -r 'select(.k=="exception") | .n' | sort | uniq -c

# into pandas
python3 -c "import pandas,sys,json; df=pandas.DataFrame(json.loads(l) for l in sys.stdin); print(df.describe())" \
  < records.jsonl
```

---

## Platform capture cheat-sheet

| Platform | Runtime | Console goes to | Capture with | Gotcha |
|---|---|---|---|---|
| Desktop | CoreCLR | stdout | pipe directly | — (also gets EventCounters bonus) |
| iOS sim | Mono | os_log | `simctl launch --console-pty` | **never** `log stream` (drops) |
| Android | Mono | logcat tag `DOTNET` | `adb logcat` | APK must embed assemblies |

---

Next: [API Reference →](API-Reference.md)
