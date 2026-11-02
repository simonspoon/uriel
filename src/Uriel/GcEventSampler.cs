namespace Uriel;

/// <summary>
/// Channels.Gc: one record per garbage collection, on every runtime (incl. Mono).
/// No GC event API reaches managed code on Mono, so we use the classic finalizer
/// sentinel: an unreferenced gen0 object whose finalizer runs after the GC that collects
/// it and allocates a fresh one for the next GC (re-registering the same object would
/// promote it, so it would only fire on older-generation GCs).
///
/// Record: k=gc, n="gc", v=highest generation collected since the last record,
/// x="count=N heap=BYTES[ pause-ms=MS]" — count = largest per-generation collection delta,
/// &gt; 1 when GCs ran back to back before the finalizer thread got to us; pause-ms only where the runtime reports pause time
/// (GC.GetTotalPauseDuration; 0 on runtimes that don't track it, then omitted).
/// Fires after the GC, on the finalizer thread, so t is "shortly after", not the start.
/// </summary>
internal sealed class GcEventSampler : IDisposable
{
    private readonly ChunkedConsoleSink _sink;
    private readonly int[] _lastCounts;
    private TimeSpan _lastPause;
    private volatile bool _stopped;

    public GcEventSampler(ChunkedConsoleSink sink)
    {
        _sink = sink;
        _lastCounts = new int[Math.Clamp(GC.MaxGeneration, 0, 2) + 1];
        for (int g = 0; g < _lastCounts.Length; g++) _lastCounts[g] = GC.CollectionCount(g);
        _lastPause = SafePause();
        _ = new Sentinel(this);   // deliberately unreferenced
    }

    private static TimeSpan SafePause()
    {
        try { return GC.GetTotalPauseDuration(); }
        catch { return TimeSpan.Zero; }
    }

    /// <summary>Called from the sentinel's finalizer. Returns false once stopped.</summary>
    private bool OnCollected()
    {
        if (_stopped || Environment.HasShutdownStarted) return false;
        try
        {
            int top = -1, count = 0;
            for (int g = 0; g < _lastCounts.Length; g++)
            {
                int c = GC.CollectionCount(g);
                if (c != _lastCounts[g]) top = g;
                count = Math.Max(count, c - _lastCounts[g]);
                _lastCounts[g] = c;
            }
            if (top < 0) return true;   // finalizer ran without a new GC (shouldn't happen)

            TimeSpan pause = SafePause();
            double pauseMs = (pause - _lastPause).TotalMilliseconds;
            _lastPause = pause;

            string x = $"count={count} heap={GC.GetTotalMemory(false)}";
            if (pauseMs > 0)
                x += " pause-ms=" + pauseMs.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

            _sink.Enqueue(new Record(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), RecKind.Gc, "gc", top, x));
        }
        catch { /* a profiler must never crash its host */ }
        return true;
    }

    public void Dispose() => _stopped = true;

    private sealed class Sentinel
    {
        private readonly GcEventSampler _owner;
        public Sentinel(GcEventSampler owner) => _owner = owner;

        ~Sentinel()
        {
            if (_owner.OnCollected())
                _ = new Sentinel(_owner);
        }
    }
}
