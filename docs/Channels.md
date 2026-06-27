# Channels & Collectors

[← Configuration](Configuration.md) · [Wiki home](README.md) · Next: [Reading the Stream →](Reading-the-Stream.md)

Four channels. Each maps to a collector. This page = what each one **emits**, when, and the full counter catalog.

```
 Channels.Counters  ──►  GcPollSampler  +  RuntimeCounterListener   (periodic)
 Channels.Exceptions ─►  EventHooks  (first-chance + unhandled)     (on event)
 Channels.Hangs      ─►  EventHooks  (UI-thread watchdog)           (on event)
 Channels.Gc         ─►  gc summaries                               (burst, opt-in)

 + always available:  UrielProfiler.Mark()  /  .CaptureStack()      (on demand)
```

Each record carries a **kind** (`k`). See [Wire Format](Wire-Format.md) for the field layout.

| `k` | `RecKind` | Emitted by | Channel |
|---|---|---|---|
| `meta` | `Meta` | session start / config echo | always |
| `counter` | `Counter` | poll sampler, EventCounters, `Mark()` | `Counters` |
| `stack` | `Stack` | `CaptureStack()` | always (on demand) |
| `exception` | `Exception` | first-chance + unhandled hooks | `Exceptions` |
| `hang` | `Hang` | UI watchdog | `Hangs` |
| `gc` | `Gc` | gc summaries | `Gc` |

---

## `Counters` — the workhorse

Two sources feed this one channel. **Which fires depends on the runtime** (see [Platform Support](Platform-Support.md)).

### Source 1 — `GcPollSampler` (reliable EVERYWHERE, incl. iOS+Android Mono)
Polls always-available `GC.*` APIs on a timer (`CounterIntervalSec`). [Source](../src/Uriel/GcPollSampler.cs).

| Counter name (`n`) | Meaning | Notes |
|---|---|---|
| `heap-bytes` | managed heap retained now | `GC.GetTotalMemory(false)` |
| `alloc-total-bytes` | cumulative bytes allocated | monotonic |
| `alloc-rate-bps` | derived allocation rate, **bytes/sec** | computed between samples |
| `gen0-gc-count` | gen-0 collections | monotonic |
| `gen1-gc-count` | gen-1 collections | monotonic |
| `gen2-gc-count` | gen-2 collections | monotonic |
| `proc-working-set` | process RSS-ish | **only when platform reports it** — Android yes (~163 MB), iOS=0 so **omitted** |
| `gc-committed-bytes` | committed heap | from `GC.GetGCMemoryInfo()` when available |
| `gc-fragmented-bytes` | fragmented heap | ditto |

### Source 2 — `RuntimeCounterListener` (DESKTOP BONUS — CoreCLR only)
`EventListener` on `"System.Runtime"`. **Silently empty on Mono/device** by design. [Source](../src/Uriel/RuntimeCounterListener.cs).

~27 rich counters CoreCLR delivers that Mono won't:
```
cpu-usage · working-set · gc-heap-size · gen-0/1/2-gc-count · gen-0/1/2-size
loh-size · poh-size · alloc-rate · gc-fragmentation · gc-committed · time-in-gc
total-pause-time-by-gc · threadpool-thread-count · threadpool-queue-length
threadpool-completed-items-count · monitor-lock-contention-count
active-timer-count · exception-count · assembly-count · il-bytes-jitted
methods-jitted-count · time-in-jit · gen-0-gc-budget
```

> Mean counters report a `Mean`; incrementing counters report an `Increment` — the listener handles both.

> 💡 **Why two sources?** The whole reason Uriel exists. EventCounters are the *nice* API but never arrive on Mono. The poll sampler is the dependable floor. Full reasoning: [Overview → counters on Mono](Overview.md#the-core-problem-it-solves-counters-on-mono).

---

## `Exceptions` — first-chance + unhandled

[`EventHooks`](../src/Uriel/EventHooks.cs) subscribes to `AppDomain.CurrentDomain`:

| Event | `n` | `v` | `x` |
|---|---|---|---|
| `FirstChanceException` | exception type name | `0` | `"{Message}\n{StackTrace}"` |
| `UnhandledException` | `"Unhandled"` | `1` | `"{FullName}: {Message}\n{StackTrace}"` |

> ⚠️ **Mono caveat:** a first-chance exception caught immediately has a **null `StackTrace`** under Mono — you get the message, deep stack only on unwound/unhandled throws. See [Platform Support](Platform-Support.md#honest-caveats).

```json
{"k":"exception","n":"InvalidOperationException","v":0,"x":"demo fault\n","seq":173}
```

---

## `Hangs` — UI-thread watchdog

Round-trips a heartbeat through the MAUI main thread. If the pong doesn't return within `HangThresholdMs`, the UI thread is stalled → emit a `hang` with the observed latency.

```
 bg timer (period = HangThresholdMs/2)
   │  post heartbeat ─────► MainThread.BeginInvokeOnMainThread(pong)
   │                              │
   │  next tick: pong back?  ◄────┘   yes → fine, re-arm
   │            no, waited ≥ threshold AND past startup grace
   ▼
  emit  {"k":"hang","n":"ui-stall","v":<ms waited>,"x":"UI thread did not service heartbeat..."}
```

| Knob | Effect |
|---|---|
| `HangThresholdMs` (250) | how long a stall must be to count |
| `HangStartupGraceMs` (3000) | ignore stalls this long after `Start` (UI booting) |

Guards built in: re-arms after firing so it **won't spam** every tick of one hang; stays quiet on desktop (no UI dispatcher — `#if HAS_MAUI`).

> Managed code can't cross-thread stack-walk the UI thread on mobile — Uriel reports **latency**, you localize with your own correlated logs.

```json
{"k":"hang","n":"ui-stall","v":250,"x":"UI thread did not service heartbeat within threshold","seq":198}
```

---

## `Gc` — gc summaries

Opt-in (`Channels.Gc`), heavier, bursty. Not in `Default`. Full GC-event decoding is a [next-pass item](Platform-Support.md#roadmap).

---

## On-demand: `Mark` & `CaptureStack` (no channel needed)

Always work once `Start` has run — they share the same stream.

```csharp
UrielProfiler.Mark("demo-mark", 42, "manual");   // → counter record, x="manual"
UrielProfiler.CaptureStack("demo-stack");         // → stack record, x=Environment.StackTrace
```

```json
{"k":"counter","n":"demo-mark","v":42,"x":"manual","seq":156}
{"k":"stack","n":"demo-stack","v":0,"x":"   at System.Environment.get_StackTrace()...","seq":167}
```

See these wired into buttons in the [demo](Demo-and-Validation.md).

---

Next: [Reading the Stream →](Reading-the-Stream.md) — get these records off a real device.
