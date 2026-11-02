# Changelog

All notable changes to **Uriel.Profiler** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- `Channels.Gc` now does something: one `gc` record per collection (generation, count, heap, pause-ms where the runtime reports it). It was a no-op before.
- Hang watchdog measures the whole stall: `ui-stall-begin` when the threshold is crossed, then one `ui-stall` with the real duration when the UI thread answers (previously one capped ~threshold record every ~0.5 s).
- First-chance exceptions capture `Environment.StackTrace` for the first `ExceptionStackLimit` (5) of each type+message (Mono leaves `ex.StackTrace` null), and floods are capped at `ExceptionMaxPerSec` (20) per type; the rest become `first-chance-suppressed` count records.
- `dropped-records` meta record when drop-on-full loses records.
- Per-process nonce `p` in the chunk envelope; `uriel_parse.py` keys on it and adds `proc` to each record.
- Android: `jni-gref-count` / `jni-weak-gref-count` counters.
- `UrielAutoStart=true` MSBuild property (`buildTransitive` targets) for zero-code activation.

### Changed
- `gen2-gc-count` is only emitted where the runtime has a gen2 (not on Mono, where it duplicated gen1).
- `alloc-total-bytes` uses the precise count, so `alloc-rate-bps` no longer reads 0 between GCs on Mono.
- Docs: .NET 10 MAUI apps are supported.

## [0.1.0] - 2026-06-27

Initial public release.

### Added
- Drop-in, release-safe MAUI runtime profiler (`builder.UseUriel(...)`), no debugger and no `#if DEBUG`.
- `GcPollSampler` — timer-polled `GC.*` counters that work on every target, including Mono on iOS/Android.
- `RuntimeCounterListener` — ~27 richer `System.Runtime` EventCounters on CoreCLR/desktop.
- Channels: `Counters`, `Exceptions` (first-chance + unhandled, with stacks), `Hangs` (UI-thread watchdog), `Gc`.
- On-demand markers: `UrielProfiler.CaptureStack(...)` and `UrielProfiler.Mark(...)`.
- Zero-code activation via the `URIEL_AUTOSTART=1` environment variable.
- Bounded lock-free channel with drop-on-full backpressure; all serialization on one background thread.
- `@@URIEL@@` chunked JSON-L wire format (≤800 bytes/line) sized for iOS `os_log`.
- `tools/uriel_parse.py` stream reassembler.
- Multi-target package: `net9.0` (engine, console-runnable) plus `net9.0-ios` / `net9.0-android` MAUI heads.

[Unreleased]: https://github.com/simonspoon/uriel/compare/v0.1.0...HEAD
[0.1.0]: https://github.com/simonspoon/uriel/releases/tag/v0.1.0
