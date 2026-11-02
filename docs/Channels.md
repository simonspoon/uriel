# Channels & Collectors

[← Configuration](Configuration.md) · [Wiki home](README.md) · Next: [Reading the Stream →](Reading-the-Stream.md)

Four channels. Each maps to a collector. This page = what each one **emits**, when, and the full counter catalog.

```
 Channels.Counters  ──►  GcPollSampler  +  RuntimeCounterListener   (periodic)
 Channels.Exceptions ─►  EventHooks  (first-chance + unhandled)     (on event)
 Channels.Hangs      ─►  EventHooks  (UI-thread watchdog)           (on event)
 Channels.Gc         ─►  GcEventSampler  (one record per GC)        (per GC, opt-in)

 + always available:  UrielProfiler.Mark()  /  .CaptureStack()      (on demand)
```

Each record carries a **kind** (`k`). See [Wire Format](Wire-Format.md) for the field layout.

| `k` | `RecKind` | Emitted by | Channel |
|---|---|---|---|
| `meta` | `Meta` | session start / config echo, `dropped-records` | always |
| `counter` | `Counter` | poll sampler, EventCounters, `Mark()` | `Counters` |
| `stack` | `Stack` | `CaptureStack()` | always (on demand) |
| `exception` | `Exception` | first-chance + unhandled hooks | `Exceptions` |
| `hang` | `Hang` | UI watchdog | `Hangs` |
| `gc` | `Gc` | one record per GC | `Gc` |

---

## `Counters` — the workhorse

Two sources feed this one channel. **Which fires depends on the runtime** (see [Platform Support](Platform-Support.md)).

### Source 1 — `GcPollSampler` (reliable EVERYWHERE, incl. iOS+Android Mono)
Polls always-available `GC.*` APIs on a timer (`CounterIntervalSec`). [Source](../src/Uriel/GcPollSampler.cs).

| Counter name (`n`) | Meaning | Notes |
|---|---|---|
| `heap-bytes` | managed heap retained now | `GC.GetTotalMemory(false)` |
| `alloc-total-bytes` | cumulative bytes allocated | monotonic; `GetTotalAllocatedBytes(precise: true)` so it moves between GCs on Mono |
| `alloc-rate-bps` | derived allocation rate, **bytes/sec** | computed between samples |
| `gen0-gc-count` | gen-0 collections | monotonic. On Mono = nursery (minor) |
| `gen1-gc-count` | gen-1 collections | monotonic. On Mono = major |
| `gen2-gc-count` | gen-2 collections | monotonic. **CoreCLR only** — emitted for generations up to `GC.MaxGeneration`; Mono SGen has two, so no gen2 there |
| `jni-gref-count` | JNI global references (live Java↔managed peers) | **Android only**, `JniRuntime.CurrentRuntime.GlobalReferenceCount` |
| `jni-weak-gref-count` | JNI weak global references | **Android only** |
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
| `FirstChanceException` | exception type name | `0` | `"{Message}\n{stack}"` |
| (throttled first-chance) | `"first-chance-suppressed"` | count in the last second | exception type name |
| `UnhandledException` | `"Unhandled"` | `1` | `"{FullName}: {Message}\n{StackTrace}"` |

**Stacks.** Mono leaves `ex.StackTrace` null at first chance, so for the first `ExceptionStackLimit` (default 5) occurrences of each type+message (tracked for up to 1024 distinct type+message pairs; messages that embed ids use them up fast) Uriel records `Environment.StackTrace` instead — it includes the throw site (under a couple of Uriel/handler frames). Later repeats carry `ex.StackTrace`, which is null on Mono.

**Floods.** At most `ExceptionMaxPerSec` (default 20) full records per second **per type** (short type name; the window resets each second, so a burst can reach ~2× across a boundary). The rest are counted and emitted once a second as one `first-chance-suppressed` record per type, so a storm (e.g. thousands of `ArgumentOutOfRangeException`/s) costs a few lines, not thousands, and doesn't hide other exception types.

```json
{"k":"exception","n":"InvalidOperationException","v":0,"x":"demo fault\n","seq":173}
```

---

## `Hangs` — UI-thread watchdog

Round-trips a heartbeat through the MAUI main thread and keeps the heartbeat's post time until the pong comes back, so each stall is measured end to end — two records per stall:

```
 bg timer (period = HangThresholdMs/2)
   │  post heartbeat ─────► MainThread.BeginInvokeOnMainThread(pong)
   │                              │
   │  tick: pong back?            │
   │    no, waited ≥ threshold (first time, past startup grace)
   │      → emit "ui-stall-begin"  v = ms waited so far   (last-known state if the app dies mid-freeze)
   │    still no → keep waiting, emit nothing
   │                              ▼
   │                         pong runs on UI thread:
   │                           stalled ≥ threshold → emit "ui-stall"  v = real stall length (ms)
   ▼  next tick posts a fresh heartbeat
```

| Knob | Effect |
|---|---|
| `HangThresholdMs` (250) | how long a stall must be to count |
| `HangStartupGraceMs` (3000) | ignore stalls this long after `Start` (UI booting) |

A 14 s freeze gives one `ui-stall-begin` (v≈250) and one `ui-stall` (v≈14000), not a run of capped records. Precision is about one timer period (`HangThresholdMs/2`). If no pong arrives within 60 s, Uriel emits `ui-stall` (v = time waited, x says it gave up) and re-arms, so a lost heartbeat can't silence the watchdog.

> ⚠️ Time the app spends suspended in the background counts as stall: a `ui-stall` on resume can span the whole background period. Correlate with your app lifecycle logs (`OnSleep`/`OnResume`). Stays quiet on desktop (no UI dispatcher — `#if HAS_MAUI`).

> Managed code can't cross-thread stack-walk the UI thread on mobile — Uriel reports **latency**, you localize with your own correlated logs.

```json
{"k":"hang","n":"ui-stall-begin","v":250,"x":"UI thread has not serviced heartbeat; stall in progress","seq":198}
{"k":"hang","n":"ui-stall","v":14003,"x":"UI thread stall, measured heartbeat post to service","seq":231}
```

---

## `Gc` — one record per collection

Opt-in (`Channels.Gc`), not in `Default`. [`GcEventSampler`](../src/Uriel/GcEventSampler.cs) uses a finalizer sentinel (no GC event API reaches managed code on Mono), so it works on every runtime. Records are written on the finalizer thread just after the GC.

| `n` | `v` | `x` |
|---|---|---|
| `"gc"` | highest generation collected since the last record | `count=N heap=BYTES[ pause-ms=MS]` |

- Volume: one record per GC. An allocation-heavy app can GC hundreds of times a second, which can fill the queue (you'll see `dropped-records`). Enable it for focused runs.
- `count` > 1 when GCs ran back to back before the finalizer thread caught up.
- `pause-ms` comes from `GC.GetTotalPauseDuration()` and is omitted where the runtime reports 0.
- **Not available in-process:** Android GC **bridge** time and native crashes (SGen asserts/SIGSEGV kill the process without running managed code). For those use the Mono GC log (`debug.mono.log=gc`) and the tombstone; the last Uriel record before death (e.g. a `ui-stall-begin`) bounds the time.

```json
{"k":"gc","n":"gc","v":0,"x":"count=1 heap=19733672 pause-ms=0.321","seq":21}
```

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
