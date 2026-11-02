# Performance

[← Platform Support](Platform-Support.md) · [Wiki home](README.md) · Next: [Demo & Validation →](Demo-and-Validation.md)

Uriel is built to ship in **release**. The design rule: **a profiler must never crash, block, or noticeably slow its host.**

---

## The performance contract

| Guarantee | Mechanism |
|---|---|
| Hot path **never blocks** | enqueue is `Channel.Writer.TryWrite` — returns immediately |
| Hot path **never throws** | `TryWrite` doesn't throw; collectors wrapped in try/catch |
| **Drop-on-full**, never stall | `BoundedChannelFullMode.DropWrite` — lose a sample before stalling the app |
| **Never crashes the host** | every collector + the bg pump swallow exceptions |
| Serialization off the hot path | all JSON + base64 + chunking on **one** `LongRunning` bg thread |
| Low alloc at the call site | `Record` is a `struct`; names are `string.Intern`-ed |

> Source of truth: [`ChunkedConsoleSink`](../src/Uriel/ChunkedConsoleSink.cs).

---

## What the hot path actually costs

```
caller thread                          background thread (one, LongRunning)
─────────────                          ────────────────────────────────────
Mark() / counter tick                  WaitToReadAsync → TryRead loop
   │                                       │
   ├─ new Record(struct)  ~0 heap          ├─ hand-rolled JSON (reused StringBuilder)
   ├─ string.Intern(name)                  ├─ UTF-8 encode → base64
   └─ TryWrite(record) ──────────────►     ├─ split into ≤MaxChunkBytes chunks
        returns instantly                  └─ Console.WriteLine each chunk
        (drops if queue full)                  (try/catch — never crashes host)
```

The caller pays for: one struct, an intern lookup, a lock-free enqueue. **That's it.** Everything expensive is deferred.

---

## Queue behavior (back-pressure)

```
 QueueCapacity = 8192 (default)
 ┌──────────────────────────────┐
 │ ▓▓▓▓▓▓▓▓░░░░░░░░░░░░░░░░░░░░░ │  not full → TryWrite succeeds
 └──────────────────────────────┘
 ┌──────────────────────────────┐
 │ ▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓▓ │  FULL → DropWrite: sample lost, app NOT stalled
 └──────────────────────────────┘
```

- `SingleReader = true` (the one pump), `SingleWriter = false` (many collectors).
- Trade-off: under a sustained burst you may **lose samples**, never throughput. That's the intended choice for a release profiler.
- Bursty workload losing data? Raise `QueueCapacity` (costs memory). See [Configuration](Configuration.md).

---

## Cost per channel

| Channel | Steady-state cost |
|---|---|
| `Counters` (poll) | a handful of cheap `GC.*` reads + a few enqueues **once per `CounterIntervalSec`**. The precise allocated-bytes read briefly pauses threads on Mono to sum allocation counters — once per interval |
| `Counters` (EventCounters) | runtime delivers samples on its own timer; we just forward (desktop only) |
| `Exceptions` | **~zero** until an exception fires; a stack walk for the first `ExceptionStackLimit` of each type+message; floods capped per type per second |
| `Hangs` | one timer tick every `HangThresholdMs/2`; a heartbeat round-trip |
| `Gc` | one small allocation + one record (serialized on the pump) per GC — can be hundreds/sec in allocation-heavy apps; opt-in, not in `Default` |
| `Mark` / `CaptureStack` | only when you call them (`CaptureStack` walks the stack — don't call in a tight loop) |

> Cheapest knob: **raise `CounterIntervalSec`.** 1 s is fine for most apps; 5 s for a lean production stream.

---

## Shutdown

`Stop()` (and `Dispose`) calls `Complete()` then waits up to **2 s** for the pump to drain the channel — so the last samples flush before exit. Optional; usually you run for the app's lifetime.

---

## Release-safety details

| Concern | How it's handled |
|---|---|
| Gated behind DEBUG? | **No** — never `#if DEBUG`. `DebugType=portable` keeps symbols for stack quality even in Release. |
| Reflection-based serialization? | No — hand-rolled JSON, deterministic field order. |
| Could a collector exception escape? | No — every `Tick`/handler/pump body is wrapped. |
| `[ModuleInitializer]` in a library? | Deliberate (env autostart); `CA2255` suppressed in the csproj. |

---

Next: [Demo & Validation →](Demo-and-Validation.md) — see it run end-to-end.
