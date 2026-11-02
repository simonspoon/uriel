# Platform Support

[← Wire Format](Wire-Format.md) · [Wiki home](README.md) · Next: [Performance →](Performance.md)

The single most important thing to understand: **iOS and Android run Mono; desktop runs CoreCLR.** That split decides which counters you get. Nothing here is a bug — it's documented runtime reality.

```
   CoreCLR (desktop)                 Mono (iOS + Android)
   ─────────────────                 ────────────────────
   poll sampler        ✅            poll sampler        ✅   ← the reliable floor
   EventCounters (~27) ✅            EventCounters       ✗   ← never arrive in-process
```

---

## Feature × platform matrix

From the live [test report](TEST_REPORT.md) (v0.1.0). ✅ confirmed live · ⚪ absent **by design**.

| Feature | Channel | Desktop (CoreCLR) | iOS (Mono) | Android (Mono) |
|---|---|:--:|:--:|:--:|
| Session meta | `Meta` | ✅ | ✅ | ✅ |
| Heap / alloc / GC poll counters | `Counters` (poll) | ✅ | ✅ | ✅ |
| `proc-working-set` | `Counters` (poll) | ✅ | ⚪ WS=0 on Mono iOS | ✅ 163 MB |
| `gc-committed` / `gc-fragmented` | `Counters` (poll) | ✅ | ✅ | ✅ |
| Rich EventCounters (cpu, jit, threadpool, lock — 27) | `Counters` (EventListener) | ✅ | ⚪ Mono n/a | ⚪ Mono n/a |
| Custom `Mark(name,val,note)` | `Counters` | ✅ | ✅ | ✅ |
| On-demand `CaptureStack` | `Stack` | ✅ | ✅ | ✅ |
| First-chance exception + stack | `Exceptions` | ✅ | ✅ | ✅ |
| Per-GC records (`gc`) | `Gc` | ✅ | ⚪ not yet validated | ⚪ not yet validated |
| JNI global ref counts | `Counters` (poll) | n/a | n/a | ⚪ not yet validated |
| UI-thread hang watchdog | `Hangs` | n/a¹ | ✅ | ✅ |
| Chunk + base64 + reassembly | wire | ✅ | ✅ | ✅ |
| Multi-chunk split / rejoin | wire | ✅ 5-chunk | ✅ 2-chunk | ✅ 2-chunk |

¹ Desktop build has no MAUI UI dispatcher, so the watchdog stays intentionally quiet (`#if HAS_MAUI`).

---

## Console plumbing per platform

| Platform | Runtime | `Console.WriteLine` lands in | Capture | Tight line limit |
|---|---|---|---|---|
| Desktop | CoreCLR | stdout | pipe directly | none |
| iOS | Mono | `os_log` / `NSLog` | `simctl launch --console-pty` | **truncates/corrupts long lines** → 800 B chunks + ASCII sentinel |
| Android | Mono | logcat tag `DOTNET` | `adb logcat` | ~4000 B (looser) |

> Uriel sizes for the **worst case (iOS)** so one build works on both. See [Wire Format](Wire-Format.md) and [Configuration → MaxChunkBytes](Configuration.md#maxchunkbytes-default-800).

---

## Honest caveats

These are **observed runtime limits, not failures** — documenting them so you're not surprised.

| Caveat | Why | Impact |
|---|---|---|
| **EventCounters empty on both devices** | Mono doesn't deliver them in-process | poll sampler carries the load; expected |
| **iOS omits `proc-working-set`** | `Environment.WorkingSet` returns 0 on Mono/iOS | Uriel correctly skips it; native RSS via P/Invoke is [roadmap](#roadmap) |
| **First-chance `ex.StackTrace` is null on Mono** | Mono fills it in only as the exception unwinds | Uriel substitutes `Environment.StackTrace` for the first `ExceptionStackLimit` of each type+message; later repeats have the message only |
| **gen2 count only on CoreCLR** | Mono SGen has two generations (nursery + major) | `gen0`=nursery, `gen1`=major; no `gen2-gc-count` on Mono |
| **No GC bridge time / native-crash marker** | no managed API for bridge time; native crashes kill the process without running managed code | use the Mono GC log and the tombstone; the last Uriel record bounds the time of death |
| **`sid` is whatever you set** | the demo hardcodes `"ios-demo"` even on Android | cosmetic; the engine stamps your `SessionId` verbatim |
| **Hang watchdog reports latency, not a UI stack** | managed code can't cross-thread stack-walk the UI thread on mobile | you localize with your own correlated logs |

---

## Roadmap (next pass)

- **Native RSS** — true process memory via mach (iOS) / Android P/Invoke, to fill the `proc-working-set` gap on iOS.
- **GC bridge time** on Android — no in-process managed API today.

---

## Build targets

`Uriel.csproj` multi-targets:

| TFM | MAUI? | Purpose |
|---|---|---|
| `net9.0` | no | desktop-testable engine, **workload-free**, console-runnable; `MauiIntegration.cs` excluded |
| `net9.0-ios` | yes | iOS head, `HAS_MAUI` defined, hang watchdog live |
| `net9.0-android` | yes | Android head, `HAS_MAUI` defined |

**.NET 10:** the package's `net9.0-*` assets (and its `Microsoft.Maui.Controls` 9.0.120 dependency) resolve fine in a .NET 10 MAUI app — reported working in a MAUI 10.0.100 Release/AOT Android app (not part of this repo's own test runs).

`HAS_MAUI` gates the UI-dispatcher code in [`EventHooks`](../src/Uriel/EventHooks.cs). On plain `net9.0` the watchdog pongs immediately and stays quiet.

---

Next: [Performance →](Performance.md)
