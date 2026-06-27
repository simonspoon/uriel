namespace Uriel;

/// <summary>
/// The reliable, cross-platform counter source. The runtime's EventCounters
/// (see <see cref="RuntimeCounterListener"/>) are only delivered to EventListener
/// on CoreCLR — on the Mono runtime that backs .NET for iOS AND Android they never
/// arrive. So we poll the always-available GC.* surface on a background timer and
/// emit the same kind of memory/GC telemetry directly. Works on every target.
///
/// Cost: a handful of cheap reads + a few enqueues once per interval. No allocation
/// in steady state beyond the Record structs.
/// </summary>
internal sealed class GcPollSampler : IDisposable
{
    private readonly ChunkedConsoleSink _sink;
    private readonly Timer _timer;
    private long _lastAllocBytes;
    private long _lastSampleMs;

    public GcPollSampler(ChunkedConsoleSink sink, double intervalSec)
    {
        _sink = sink;
        int ms = (int)Math.Max(100, intervalSec * 1000);
        _lastAllocBytes = SafeAllocatedBytes();
        _lastSampleMs = NowMs();
        _timer = new Timer(Tick, null, ms, ms);
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void Tick(object? _)
    {
        long now = NowMs();
        try
        {
            // managed heap currently retained
            Emit(now, "heap-bytes", GC.GetTotalMemory(forceFullCollection: false));

            // cumulative allocations -> derive a rate (bytes/sec) without forcing GC
            long alloc = SafeAllocatedBytes();
            Emit(now, "alloc-total-bytes", alloc);
            long dt = now - _lastSampleMs;
            if (dt > 0 && alloc >= _lastAllocBytes)
                Emit(now, "alloc-rate-bps", (alloc - _lastAllocBytes) * 1000.0 / dt);
            _lastAllocBytes = alloc;
            _lastSampleMs = now;

            // per-gen collection counts (monotonic)
            Emit(now, "gen0-gc-count", GC.CollectionCount(0));
            Emit(now, "gen1-gc-count", GC.CollectionCount(1));
            Emit(now, "gen2-gc-count", GC.CollectionCount(2));

            // process working set (RSS-ish) where the platform reports it; 0 on some Mono targets
            long ws = Environment.WorkingSet;
            if (ws > 0) Emit(now, "proc-working-set", ws);

            // richer GC info when the runtime supports it (CoreCLR; partial/none on Mono)
            try
            {
                var gi = GC.GetGCMemoryInfo();
                if (gi.HeapSizeBytes > 0)
                {
                    Emit(now, "gc-committed-bytes", gi.TotalCommittedBytes);
                    Emit(now, "gc-fragmented-bytes", gi.FragmentedBytes);
                }
            }
            catch { /* not available on this runtime */ }
        }
        catch { /* a profiler must never crash its host */ }
    }

    private void Emit(long t, string name, double val) =>
        _sink.Enqueue(new Record(t, RecKind.Counter, string.Intern(name), val));

    private static long SafeAllocatedBytes()
    {
        try { return GC.GetTotalAllocatedBytes(precise: false); }
        catch { return 0; }
    }

    public void Dispose() => _timer.Dispose();
}
