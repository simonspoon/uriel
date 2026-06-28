# Changelog

All notable changes to **Uriel.Profiler** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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
