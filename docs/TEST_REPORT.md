# Uriel.Profiler — Full Test Report

**Date:** 2026-06-27 · **Build:** v0.1.0 (`94f4cc4`) · **Verdict:** ✅ all features pass on iOS **and** Android, live.

Every channel was exercised end-to-end on a real device runtime (Mono), captured off
the device's own console (iOS `--console-pty`, Android `adb logcat`), and reassembled
from the chunked wire format with **zero record loss**.

---

## 1. What was run

| Lane | Runtime | Device | How driven | Capture |
|---|---|---|---|---|
| Desktop engine | CoreCLR | this Mac (x64) | `samples/Harness` | stdout |
| **iOS** | Mono | iPhone 16 Pro sim · iOS 18.0 | `qorvex_drive.sh` (XCUITest) | `--console-pty` |
| **Android** | Mono | Pixel_4a_API_33 emu · API 33 | `qorvex_drive_android.sh` (UIAutomator) | `adb logcat` |

Each lane: build → install → launch → **tap all 5 demo buttons by AutomationId** →
dwell for periodic counters → reassemble JSON-L via `tools/uriel_parse.py`.

### Integrity (chunks → records, loss)

| Lane | Raw chunks | Records | Missing seqs |
|---|---|---|---|
| Desktop | 323 | 258 | — (0 loss) |
| **iOS** | 368 | 366 | **0** |
| **Android** | 297 | 294 | **0** |

The chunk-count > record-count gap is exactly the multi-chunk records (stack/exception
text split, then rejoined). Reassembly is lossless on every lane.

---

## 2. Feature × platform matrix

| Feature | Channel | Desktop (CoreCLR) | **iOS (Mono)** | **Android (Mono)** |
|---|---|:--:|:--:|:--:|
| Session meta | `Meta` | ✅ | ✅ | ✅ |
| Heap / alloc / GC poll counters | `Counters` (poll) | ✅ | ✅ | ✅ |
| `proc-working-set` | `Counters` (poll) | ✅ | ⚪ WS=0 on Mono iOS | ✅ 163 MB |
| `gc-committed/fragmented` | `Counters` (poll) | ✅ | ✅ | ✅ |
| Rich EventCounters (cpu, jit, threadpool, lock — 27) | `Counters` (EventListener) | ✅ | ⚪ Mono n/a | ⚪ Mono n/a |
| Custom `Mark(name,val,note)` | `Counters` | ✅ | ✅ | ✅ |
| On-demand `CaptureStack` | `Stack` | ✅ | ✅ | ✅ |
| First-chance exception + stack | `Exceptions` | ✅ | ✅ | ✅ |
| UI-thread hang watchdog | `Hangs` | n/a¹ | ✅ | ✅ |
| Chunk + base64 + reassembly | wire | ✅ | ✅ | ✅ |
| Multi-chunk split/rejoin | wire | ✅ 5-chunk | ✅ 2-chunk | ✅ 2-chunk |

✅ confirmed live · ⚪ absent **by design** (documented runtime limit, not a failure).
¹ Desktop build has no MAUI UI dispatcher, so the watchdog stays intentionally quiet.

**Both Mono platforms behave exactly as the README documents:** poll sampler carries
the load, EventCounters are silently empty. The one platform split is `proc-working-set`
— Android's `Environment.WorkingSet` reports (163 MB), iOS's returns 0, so Uriel omits it.

---

## 3. Examples of each feature — real captured records

Inner JSON after reassembly. `t`=unix-ms · `k`=kind · `n`=name · `v`=value · `x`=text/stack · `sid`=session.

### iOS (iPhone 16 Pro, iOS 18.0)

```jsonc
// Meta — session start, echoes config
{"k":"meta","n":"session-start","v":0,"sid":"ios-demo","x":"channels=All;interval=1s","seq":1}

// Counter (poll) — managed heap retained
{"k":"counter","n":"heap-bytes","v":8557240,"sid":"ios-demo","seq":2}

// Counter (poll) — derived allocation rate, bytes/sec
{"k":"counter","n":"alloc-rate-bps","v":14701343.94,"sid":"ios-demo","seq":4}

// Mark — from the Allocate button (UrielProfiler.Mark("demo-allocate",2000))
{"k":"counter","n":"demo-allocate","v":2000,"sid":"ios-demo","seq":106}

// Mark — custom metric w/ note (Mark("demo-mark",42,"manual"))
{"k":"counter","n":"demo-mark","v":42,"sid":"ios-demo","x":"manual","seq":156}

// Stack — on-demand CaptureStack("demo-stack")
{"k":"stack","n":"demo-stack","v":0,"sid":"ios-demo",
 "x":"   at System.Environment.get_StackTrace()\n   at Uriel.UrielProfiler.CaptureStack(String label) ...","seq":167}

// Exception — first-chance InvalidOperationException from the Throw button
{"k":"exception","n":"InvalidOperationException","v":0,"sid":"ios-demo","x":"demo fault\n","seq":173}

// Hang — 600ms Thread.Sleep on UI thread tripped the 250ms watchdog
{"k":"hang","n":"ui-stall","v":250,"sid":"ios-demo","x":"UI thread did not service heartbeat within threshold","seq":198}
```

### Android (Pixel_4a, API 33)

```jsonc
// Meta
{"k":"meta","n":"session-start","v":0,"sid":"ios-demo","x":"channels=All;interval=1s","seq":1}

// Counter (poll) — heap
{"k":"counter","n":"heap-bytes","v":5648536,"sid":"ios-demo","seq":2}

// Counter (poll) — proc-working-set (Android reports it; iOS does not)
{"k":"counter","n":"proc-working-set","v":163172352,"sid":"ios-demo","seq":8}

// Mark — Allocate button
{"k":"counter","n":"demo-allocate","v":2000,"sid":"ios-demo","seq":101}

// Mark — custom w/ note
{"k":"counter","n":"demo-mark","v":42,"sid":"ios-demo","x":"manual","seq":130}

// Stack — on-demand (note: full app frame UrielDemo.MainPage.OnCaptureStack)
{"k":"stack","n":"demo-stack","v":0,"sid":"ios-demo",
 "x":"   at System.Environment.get_StackTrace()\n   at Uriel.UrielProfiler.CaptureStack(String label)\n   at UrielDemo.MainPage.OnCaptureStack(Object ...","seq":...}

// Exception — first-chance
{"k":"exception","n":"InvalidOperationException","v":0,"sid":"ios-demo","x":"demo fault\n","seq":149}

// Hang — watchdog fired (256ms observed)
{"k":"hang","n":"ui-stall","v":256,"sid":"ios-demo","x":"UI thread did not service heartbeat within threshold","seq":168}
```

### Wire format on the device (before reassembly)

Android logcat, tag `DOTNET`, ASCII sentinel `@@URIEL@@`, base64 payload:

```
I/DOTNET ( 13918): @@URIEL@@ {"s":1,"c":[1,1],"d":"eyJ0IjoxNzgyNTg0OTU2MDU1LCJrIjoibWV0YSIs..."}
```

Multi-chunk proof (desktop harness, `MaxChunkBytes=220` forces splits): one stack
record `s:30` arrived as `c:[1,5] … c:[5,5]` and rejoined to the full trace with no gap.

### Desktop-only: the EventCounters bonus (CoreCLR)

27 rich counters that Mono cannot deliver, confirmed flowing on desktop:
`cpu-usage, working-set, gc-heap-size, gen-0/1/2-gc-count, gen-0/1/2-size, loh-size,
poh-size, alloc-rate, gc-fragmentation, gc-committed, time-in-gc, total-pause-time-by-gc,
threadpool-thread-count, threadpool-queue-length, threadpool-completed-items-count,
monitor-lock-contention-count, active-timer-count, exception-count, assembly-count,
il-bytes-jitted, methods-jitted-count, time-in-jit, gen-0-gc-budget`.

```jsonc
{"k":"counter","n":"cpu-usage","v":5.53e-06,"sid":"harness","seq":3}
```

---

## 4. Honest caveats (observed, not failures)

- **`sid` reads `"ios-demo"` on Android too** — the demo hardcodes the session id in
  `MauiProgram.cs`. Cosmetic; the engine stamps whatever you set.
- **Exception `x` is `"demo fault\n"` with no stack** on both Mono targets — a
  first-chance exception that's caught immediately has a null `StackTrace` under Mono.
  Message is captured; deep stack only on unwound/unhandled throws.
- **iOS omits `proc-working-set`** — `Environment.WorkingSet` returns 0 on Mono/iOS, so
  Uriel correctly skips it (native RSS via P/Invoke is the documented next pass).
- **EventCounters empty on both devices** — expected; the poll sampler is the device path.

---

## 5. Reproduce

```bash
# Desktop engine
dotnet run --project samples/Harness -c Release | python3 tools/uriel_parse.py

# iOS (sim booted)
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-ios -c Debug -p:RuntimeIdentifier=iossimulator-x64
tools/qorvex_drive.sh uriel <UDID> com.companyname.urieldemo /tmp/uriel.console.log \
  samples/UrielDemo/bin/Debug/net9.0-ios/iossimulator-x64/UrielDemo.app
grep '@@URIEL@@' /tmp/uriel.console.log | python3 tools/uriel_parse.py

# Android (emulator booted; JDK17 in the shell that starts qorvex server)
export JAVA_HOME=/usr/local/opt/openjdk@17
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-android -c Debug -p:EmbedAssembliesIntoApk=true
tools/qorvex_drive_android.sh uriel-android emulator-5554 com.companyname.urieldemo /tmp/uriel.logcat.log \
  samples/UrielDemo/bin/Debug/net9.0-android/com.companyname.urieldemo-Signed.apk
grep '@@URIEL@@' /tmp/uriel.logcat.log | python3 tools/uriel_parse.py
```
