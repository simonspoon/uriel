# Glossary

[← Troubleshooting](Troubleshooting.md) · [Wiki home](README.md)

Quick definitions for terms used across the wiki.

| Term | Means |
|---|---|
| **JSON-L** | JSON Lines — one JSON object per line. Uriel's output format after reassembly. |
| **Chunk** | One console line. A record too big for `MaxChunkBytes` is split into several. Envelope: `{"s","c","d"}`. See [Wire Format](Wire-Format.md). |
| **Sentinel** | ASCII marker (`@@URIEL@@`) prefixed to every line so you can `grep` Uriel out of console noise. |
| **Record** | One telemetry datum — the immutable struct on the hot path. Fields: `t,k,n,v,x` (+`sid`). [source](../src/Uriel/Record.cs) |
| **`seq` / `s`** | Sequence number, unique per record, shared across its chunks. Reassembly groups by it; gaps reveal loss. |
| **`k` (kind)** | `meta` · `counter` · `stack` · `exception` · `hang` · `gc`. The `RecKind` enum. |
| **`sid`** | Session id — your `SessionId`, stamped on every record to correlate one run. |
| **Channel** | A toggleable collector group: `Counters`, `Exceptions`, `Hangs`, `Gc`. [Channels](Channels.md). |
| **Poll sampler** | `GcPollSampler` — polls `GC.*` on a timer. The **reliable, cross-platform** counter source. |
| **EventCounters** | `System.Runtime`'s built-in periodic counters. Rich but **CoreCLR-only** (`RuntimeCounterListener`). |
| **CoreCLR** | The desktop .NET runtime. Delivers EventCounters. |
| **Mono** | The runtime behind .NET for **iOS and Android**. Does **not** deliver EventCounters in-process. [Platform Support](Platform-Support.md). |
| **First-chance exception** | Notification the runtime raises the moment an exception is thrown, *before* any catch. |
| **Hang watchdog** | Heartbeat round-trip through the UI thread; if the pong is late → UI is stalled → emit a `hang`. |
| **Drop-on-full** | Bounded-queue policy: when full, drop the new sample rather than block the app (`BoundedChannelFullMode.DropWrite`). |
| **`MaxChunkBytes`** | Max UTF-8 bytes per console line before chunking. Default 800 (sized for iOS `os_log`). |
| **`os_log`** | iOS system log that `Console.WriteLine` routes through. Truncates/corrupts long lines + mangles multibyte UTF-8 → drives the chunk size + ASCII sentinel. |
| **logcat** | Android system log. `Console.WriteLine` lands under tag `DOTNET`. Line limit ~4000 B. |
| **`--console-pty`** | `simctl launch` flag — a lossless per-process console pipe on iOS. Use instead of `log stream` (which drops). |
| **RSS / working set** | Resident set size — real process memory. `proc-working-set` reports it where the platform can (Android yes, iOS=0). |
| **qorvex** | UI-automation tool driving the demo app (XCUITest on iOS, UIAutomator on Android) to validate channels live. [Demo](Demo-and-Validation.md). |
| **`[ModuleInitializer]`** | Method that runs when the assembly loads — backs `URIEL_AUTOSTART` (best-effort). |
| **MAUI head** | A platform-specific build target (`net9.0-ios`, `net9.0-android`) vs the plain `net9.0` engine. |

---

[← Back to Wiki home](README.md)
