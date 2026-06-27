# Demo & Validation

[← Performance](Performance.md) · [Wiki home](README.md) · Next: [Troubleshooting →](Troubleshooting.md)

Two runnable samples + the automation scripts that produced the [test report](TEST_REPORT.md).

---

## Samples at a glance

| Sample | TFM | What it is | Run |
|---|---|---|---|
| [`samples/Harness`](../samples/Harness/Program.cs) | `net9.0` | desktop smoke test of the engine | `dotnet run -c Release` |
| [`samples/UrielDemo`](../samples/UrielDemo) | MAUI | iOS/Android app, one button per feature | build + deploy to device |

---

## Desktop harness — fastest verification

[`samples/Harness/Program.cs`](../samples/Harness/Program.cs): allocates in a loop, captures stacks, throws caught exceptions, sets `MaxChunkBytes = 220` to **force multi-chunk splits**.

```bash
dotnet run --project samples/Harness -c Release | python3 tools/uriel_parse.py
```

Exercises: `Mark` · `CaptureStack` · first-chance exceptions · poll counters · the **EventCounters bonus** (CoreCLR) · multi-chunk reassembly. `Stop()` drains before exit.

---

## UrielDemo — the device app

Wired in [`MauiProgram.cs`](../samples/UrielDemo/MauiProgram.cs):
```csharp
builder.UseUriel(o => { o.Channels = Channels.All; o.CounterIntervalSec = 1; o.SessionId = "ios-demo"; });
```

Five buttons (each has an `AutomationId` so automation can tap them) — [`MainPage.xaml.cs`](../samples/UrielDemo/MainPage.xaml.cs):

| Button | `AutomationId` | Does | Produces |
|---|---|---|---|
| Allocate | `btnAllocate` | allocates 2000×1 KB, GCs, `Mark("demo-allocate",2000)` | counter movement + mark |
| Throw | `btnThrow` | throws + catches `InvalidOperationException("demo fault")` | `exception` record |
| Hang | `btnHang` | `Thread.Sleep(600)` on UI thread | `hang` record (trips 250 ms watchdog) |
| Capture Stack | `btnStack` | `CaptureStack("demo-stack")` | `stack` record |
| Mark | `btnMark` | `Mark("demo-mark",42,"manual")` | counter record w/ note |

---

## Automation scripts ([`tools/`](../tools))

These drive the demo through [`qorvex`](../tools/qorvex-reference.txt) UI automation while capturing the console losslessly.

| Script | Platform | Driver | Capture |
|---|---|---|---|
| [`qorvex_drive.sh`](../tools/qorvex_drive.sh) | iOS sim | XCUITest agent | `simctl launch --console-pty` |
| [`qorvex_drive_android.sh`](../tools/qorvex_drive_android.sh) | Android emu | UIAutomator agent | `adb logcat` |
| [`ios_validate.sh`](../tools/ios_validate.sh) | iOS sim | (manual) boot+install+launch | `--console-pty` |
| [`uriel_parse.py`](../tools/uriel_parse.py) | any | — | reassembles chunks → JSON-L |

Each lane: **build → install → launch → tap all 5 buttons by AutomationId → dwell for counters → reassemble.**

---

## Reproduce the test report

```bash
# Desktop engine
dotnet run --project samples/Harness -c Release | python3 tools/uriel_parse.py

# iOS (sim booted)
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-x64
tools/qorvex_drive.sh uriel <UDID> com.companyname.urieldemo /tmp/uriel.console.log \
  samples/UrielDemo/bin/Debug/net9.0-ios/iossimulator-x64/UrielDemo.app
grep '@@URIEL@@' /tmp/uriel.console.log | python3 tools/uriel_parse.py

# Android (emulator booted; JDK17 in the shell that starts the qorvex server)
export JAVA_HOME=/usr/local/opt/openjdk@17
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-android -c Debug \
  -p:EmbedAssembliesIntoApk=true
tools/qorvex_drive_android.sh uriel-android emulator-5554 com.companyname.urieldemo /tmp/uriel.logcat.log \
  samples/UrielDemo/bin/Debug/net9.0-android/com.companyname.urieldemo-Signed.apk
grep '@@URIEL@@' /tmp/uriel.logcat.log | python3 tools/uriel_parse.py
```

> ⚠️ **Android env:** the qorvex UIAutomator agent builds with Gradle/AGP 8.x → needs **JDK 17**, set in the *same shell that starts the qorvex server* (`export JAVA_HOME=/usr/local/opt/openjdk@17; export QORVEX_ANDROID_JAVA_HOME=$JAVA_HOME`). APK must embed assemblies. See [Troubleshooting](Troubleshooting.md). The repo memory note `android-qorvex-testing.md` tracks these gotchas.

---

## Test report headline (v0.1.0, `94f4cc4`)

✅ All features pass on iOS **and** Android, live, with **zero record loss**:

| Lane | Runtime | Device | Raw chunks | Records | Missing seqs |
|---|---|---|---|---|---|
| Desktop | CoreCLR | this Mac (x64) | 323 | 258 | — |
| iOS | Mono | iPhone 16 Pro sim · iOS 18.0 | 368 | 366 | **0** |
| Android | Mono | Pixel_4a_API_33 emu | 297 | 294 | **0** |

Full detail + real captured records: [`TEST_REPORT.md`](TEST_REPORT.md).

---

Next: [Troubleshooting →](Troubleshooting.md)
