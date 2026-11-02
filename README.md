# Uriel.Profiler

[![NuGet](https://img.shields.io/nuget/v/Uriel.Profiler.svg)](https://www.nuget.org/packages/Uriel.Profiler)
[![CI](https://github.com/simonspoon/uriel/actions/workflows/ci.yml/badge.svg)](https://github.com/simonspoon/uriel/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

Drop-in, **release-safe** runtime profiler for .NET MAUI (iOS + Android).
Streams chunked **JSON-L** telemetry to the console with near-zero overhead.
No debugger, no `#if DEBUG`, no source commitment — pull the NuGet, flip it on.

## Install

```bash
dotnet add package Uriel.Profiler
```

Then activate it (see below). Requires .NET 9 / .NET MAUI.

## Why

Mobile MAUI profiling tooling is thin. Uriel collects memory/GC telemetry
in-process, in release, with negligible cost, and forwards it (plus on-event
exception/hang stacks) out the one channel you can always read: the console.

## 📚 Documentation

Full user wiki in **[`docs/`](docs/README.md)** — start at the [home page](docs/README.md).
Quick links: [Getting Started](docs/Getting-Started.md) · [Configuration](docs/Configuration.md) ·
[Channels](docs/Channels.md) · [Reading the Stream](docs/Reading-the-Stream.md) ·
[Wire Format](docs/Wire-Format.md) · [Platform Support](docs/Platform-Support.md) ·
[Troubleshooting](docs/Troubleshooting.md).

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

**Zero-code (no call site):** in the app's project file:
```xml
<PropertyGroup><UrielAutoStart>true</UrielAutoStart></PropertyGroup>
```
The package's `buildTransitive` targets inject a `[ModuleInitializer]` into your app that calls `UrielProfiler.Start()` with defaults (defaults only: don't combine it with `UseUriel(configure)`/`Start(configure)` (the first `Start` wins, so your options would be ignored). C# projects only.). (The `URIEL_AUTOSTART=1` env var alone can't work in an unmodified app: nothing loads the Uriel assembly, so its initializer never runs.)

**On-demand markers anywhere:**
```csharp
UrielProfiler.CaptureStack("before-heavy-parse");
UrielProfiler.Mark("frame-time-ms", 16.7);
```

## What you get

| Channel | Source | Cost |
|---|---|---|
| `Counters` | **all targets:** heap-bytes, alloc-rate-bps, gen0/1(/2)-gc-count, proc-working-set, jni-gref-count (Android), gc-committed/fragmented (poll sampler). **desktop bonus:** cpu, gen sizes, jit, threadpool, lock-contention (EventCounters) | periodic, very low |
| `Exceptions` | first-chance + unhandled, with stack (first 5 per type+message), floods throttled to counts | on event |
| `Hangs` | UI-thread heartbeat watchdog: `ui-stall-begin` at threshold, `ui-stall` with the real stall length | on event |
| `Gc` | one record per GC: generation, heap, pause time where the runtime reports it (opt-in) | per GC |

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
@@URIEL@@ {"p":"9f3a01c2","s":1042,"c":[1,3],"d":"<base64 fragment of inner json>"}
```

Reassemble: group by `p` (per-process nonce) + `s`, order by `c[0]`, concat `d`, base64-decode → inner record:

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
Native RSS (true process memory via mach/Android P/Invoke) is the next pass.

Runs on .NET 9 and .NET 10 MAUI apps: the package targets `net9.0-*` and pulls
`Microsoft.Maui.Controls` 9.0.120, which was reported resolving fine in a MAUI
10.0.100 Release/AOT Android app.

See [CHANGELOG.md](CHANGELOG.md) for release history.

## Contributing

Issues and PRs welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) and the
[Code of Conduct](CODE_OF_CONDUCT.md). For security reports, see
[SECURITY.md](SECURITY.md).

## License

[MIT](LICENSE) © Simon Spoon
