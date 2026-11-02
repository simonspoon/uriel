# Overview

[← Wiki home](README.md) · Next: [Getting Started →](Getting-Started.md)

---

## What it is

A NuGet you drop into a .NET MAUI app. It collects runtime telemetry **in-process, in release builds**, and writes it to the **console** as chunked JSON-L. You read it back off the device log and reassemble it into one JSON record per line.

## Why it exists

Mobile MAUI profiling tooling is thin. The usual profilers want a debugger attached or a debug build. Uriel needs **neither**:

| Constraint on device | Uriel's answer |
|---|---|
| No debugger attached | In-process collectors, no remote agent |
| Release build, no `#if DEBUG` | Always-on code, never gated behind DEBUG |
| Only the console is reliably readable | Stream everything out the console |
| iOS `os_log` truncates/corrupts long lines | Chunk to ≤800 B, base64, ASCII sentinel |
| Profiler must not crash the app | Every collector wrapped; drop-on-full queue |

## The core problem it solves: counters on Mono

There are **two** counter sources, and which one works depends on the runtime:

```
                    ┌─────────────────────────────────────────────┐
                    │  Where does memory/GC telemetry come from?  │
                    └─────────────────────────────────────────────┘
                                      │
            ┌─────────────────────────┴──────────────────────────┐
            ▼                                                     ▼
   CoreCLR  (desktop)                                   Mono  (iOS + Android)
   ────────────────────                                 ────────────────────
   RuntimeCounterListener                               RuntimeCounterListener
     EventListener on                                     ✗ EventCounters never
     "System.Runtime"                                       arrive in-process
     → ~27 rich counters                                  → silently empty
     (cpu, jit, threadpool,                              GcPollSampler
      lock contention…)                                    polls GC.* on a timer
   GcPollSampler ALSO runs                                 → heap, alloc-rate,
                                                             gen counts, working-set
   ─────────────────────                                 ─────────────────────
   BOTH sources active                                   ONLY the poll sampler
```

**Takeaway:** `GcPollSampler` is the reliable path on every target. `RuntimeCounterListener` is a **desktop bonus** — on device it's silently empty by design, not a bug. See [Platform Support](Platform-Support.md).

## Architecture — the data path

```
┌────────────────────────────────────────────────────────────────────────────┐
│ COLLECTORS  (each wrapped in try/catch — a profiler must never crash host)  │
│                                                                              │
│  GcPollSampler ──┐   RuntimeCounterListener ──┐   EventHooks ──┐             │
│   (timer poll)   │    (EventListener)         │   (exceptions, │             │
│                  │                            │    hang watchdog)            │
│  UrielProfiler.Mark() / .CaptureStack()  ─────┼────────────────┤             │
└──────────────────┼────────────────────────────┼────────────────┼────────────┘
                   │            enqueue Record (struct, interned strings)
                   ▼            ▼                ▼
        ┌───────────────────────────────────────────────┐
        │  ChunkedConsoleSink                            │   HOT PATH ENDS HERE
        │  bounded Channel<Record>  (cap 8192)           │   caller never blocks
        │  FullMode = DropWrite  ── lose sample, not app │
        └───────────────────────┬───────────────────────┘
                                │  one LongRunning bg thread
                                ▼
        ┌───────────────────────────────────────────────┐
        │  PumpAsync:                                    │
        │   1. hand-rolled JSON (deterministic, low-alloc)│
        │   2. UTF-8 → base64                            │
        │   3. split into ≤MaxChunkBytes chunks          │
        │   4. Console.WriteLine each chunk              │
        └───────────────────────┬───────────────────────┘
                                ▼
              @@URIEL@@ {"p":"9f3a01c2","s":1042,"c":[1,3],"d":"<base64>"}  ← on the wire
                                │
                  os_log (iOS) / logcat tag DOTNET (Android) / stdout (desktop)
                                ▼
              grep '@@URIEL@@' | python3 tools/uriel_parse.py   ← off the device
                                ▼
              {"t":...,"k":"counter","n":"heap-bytes","v":8557240,"seq":2}
```

## Source map

| File | Role |
|---|---|
| [`Uriel.cs`](../src/Uriel/Uriel.cs) | `UrielProfiler` — public entry: `Start`, `Stop`, `Mark`, `CaptureStack` |
| [`UrielOptions.cs`](../src/Uriel/UrielOptions.cs) | `Channels` flags + all config |
| [`Record.cs`](../src/Uriel/Record.cs) | `RecKind` + the immutable `Record` struct on the wire |
| [`ChunkedConsoleSink.cs`](../src/Uriel/ChunkedConsoleSink.cs) | output engine: queue + bg pump + JSON + chunking |
| [`GcPollSampler.cs`](../src/Uriel/GcPollSampler.cs) | **cross-platform** counter source (polls `GC.*`) |
| [`RuntimeCounterListener.cs`](../src/Uriel/RuntimeCounterListener.cs) | **desktop-bonus** counter source (EventCounters) |
| [`EventHooks.cs`](../src/Uriel/EventHooks.cs) | exceptions + UI-thread hang watchdog |
| [`GcEventSampler.cs`](../src/Uriel/GcEventSampler.cs) | `Channels.Gc`: one record per GC (finalizer sentinel) |
| [`buildTransitive/Uriel.Profiler.targets`](../src/Uriel/buildTransitive/Uriel.Profiler.targets) | `UrielAutoStart=true` → build injects the `Start()` call |
| [`MauiIntegration.cs`](../src/Uriel/MauiIntegration.cs) | `builder.UseUriel(…)` one-liner |
| [`AutoStart.cs`](../src/Uriel/AutoStart.cs) | `URIEL_AUTOSTART=1` env activation (best-effort; needs the assembly loaded) |

---

Next: [Getting Started →](Getting-Started.md)
