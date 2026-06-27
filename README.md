# Uriel.Profiler

Drop-in, **release-safe** runtime profiler for .NET MAUI (iOS + Android).
Streams chunked **JSON-L** telemetry to the console with near-zero overhead.
No debugger, no `#if DEBUG`, no source commitment — pull the NuGet, flip it on.

## Why

Mobile MAUI profiling tooling is thin. Uriel collects memory/GC telemetry
in-process, in release, with negligible cost, and forwards it (plus on-event
exception/hang stacks) out the one channel you can always read: the console.

**Counter sources — two, by necessity:**
- `GcPollSampler` polls the always-available `GC.*` APIs on a timer. This is the
  reliable path **on every target, including iOS and Android** (both run Mono).
- `RuntimeCounterListener` (`EventListener` + `System.Runtime` EventCounters) adds
  ~27 richer counters (cpu, jit, threadpool, lock contention) — but **only on
  CoreCLR (desktop)**. Mono does not deliver these EventCounters in-process, so on
  device this source is silently empty and the poll sampler carries the load.

## Activate

**One-liner (reliable):**
```csharp
using Uriel;

builder.UseUriel(o =>
{
    o.Channels = Channels.All;     // Counters | Exceptions | Hangs | Gc
    o.CounterIntervalSec = 1;
});
```

**Zero-code (field toggle, best-effort):** set env var `URIEL_AUTOSTART=1`.

**On-demand markers anywhere:**
```csharp
UrielProfiler.CaptureStack("before-heavy-parse");
UrielProfiler.Mark("frame-time-ms", 16.7);
```

## What you get

| Channel | Source | Cost |
|---|---|---|
| `Counters` | **all targets:** heap-bytes, alloc-rate-bps, gen0/1/2-gc-count, proc-working-set, gc-committed/fragmented (poll sampler). **desktop bonus:** cpu, gen sizes, jit, threadpool, lock-contention (EventCounters) | periodic, very low |
| `Exceptions` | first-chance + unhandled, with stack | on event |
| `Hangs` | UI-thread heartbeat watchdog, reports stall latency (with startup grace) | on event |
| `Gc` | gc summaries (opt-in, heavier) | burst |

> Validated live on an iOS simulator (driven via `qorvex` UI automation): meta,
> marks, on-demand stacks, exceptions, and the hang watchdog all confirmed in the
> device console; poll-sampler memory counters confirmed flowing on Mono/iOS.

## Performance contract

- Hot path **only enqueues** to a bounded lock-free channel — never blocks, never throws.
- On saturation: **drop-on-full** (lose a sample, never stall the app).
- All JSON serialization + base64 chunking happens on **one background thread**.
- A profiler must never crash its host: every collector is wrapped.

## Wire format

One line per chunk. Chunked to ≤800 bytes — sized for iOS `os_log`, the tightest
console (it truncates/corrupts long lines; logcat's limit is looser at ~4000).
The sentinel is **ASCII** (`@@URIEL@@`) on purpose: `os_log` mangles multibyte
UTF-8 on long lines, which would break a literal `grep`.

```
@@URIEL@@ {"s":1042,"c":[1,3],"d":"<base64 fragment of inner json>"}
```

Reassemble: group by `s`, order by `c[0]`, concat `d`, base64-decode → inner record:

```json
{"t":1719400000123,"k":"counter","n":"alloc-rate","v":20480}
```

Inner fields: `t` unix-ms · `k` kind · `n` name · `v` value · `x` text/stack · `sid` session id.

## Read the stream

```bash
adb logcat | python3 tools/uriel_parse.py        # Android, live
python3 tools/uriel_parse.py < captured.log       # from your capture
# → one inner JSON record per line; pipe to jq / pandas
```

## Build

Needs the MAUI workload:
```bash
dotnet workload install maui
dotnet pack src/Uriel/Uriel.csproj -c Release
```

## Build & validate on simulator

```bash
dotnet workload install maui
# Intel Macs: simulators need the x64 RID (not arm64)
dotnet build samples/UrielDemo/UrielDemo.csproj -f net9.0-ios -c Debug \
  -p:RuntimeIdentifier=iossimulator-x64
# boot a sim, install, launch with console capture, drive the UI:
tools/qorvex_drive.sh uriel <SIM_UDID> com.companyname.urieldemo /tmp/uriel.log
grep '@@URIEL@@' /tmp/uriel.log | python3 tools/uriel_parse.py
```

## Status

v0.1 — validated end-to-end on iOS simulator via `qorvex` automation.
Memory = managed heap / GC (poll sampler) + desktop EventCounters bonus.
Native RSS (true process memory via mach/Android P/Invoke) and full GC-event
decoding are the next pass.
