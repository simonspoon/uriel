using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Uriel;

/// <summary>
/// Event-driven collectors: exceptions (first-chance + unhandled) and a UI-thread
/// hang watchdog. Each fires a stack-bearing record only when something happens,
/// so steady-state cost is ~zero.
/// </summary>
internal sealed class EventHooks : IDisposable
{
    private readonly ChunkedConsoleSink _sink;
    private readonly UrielOptions _opt;
    private Timer? _hangTimer;
    private long _heartbeatPostedTicks;
    private volatile bool _awaitingPong;
    private volatile bool _stallReported;   // ui-stall-begin already emitted for this heartbeat
    private int _stalePongs;                // abandoned heartbeats whose pong may still arrive
    private const int AbandonHeartbeatMs = 60_000;
    private readonly Action _pong;
    private readonly long _startMs;

    // first-chance throttling
    private Timer? _excTimer;
    private readonly ConcurrentDictionary<string, int> _stacksTaken = new();   // type|message -> stacks captured
    private readonly ConcurrentDictionary<string, ExcCounts> _perType = new(); // type -> this interval's counts
    [ThreadStatic] private static bool _inHandler;
    private const int MaxStackKeys = 1024;

    public EventHooks(ChunkedConsoleSink sink, UrielOptions opt)
    {
        _sink = sink;
        _opt = opt;
        _startMs = NowMs();
        _pong = OnPong;

        if (opt.Channels.HasFlag(Channels.Exceptions))
        {
            _excTimer = new Timer(FlushSuppressed, null, 1000, 1000);
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        }

        if (opt.Channels.HasFlag(Channels.Hangs))
            StartHangWatchdog();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    // First-chance: Mono leaves ex.StackTrace null at first chance, so the first
    // ExceptionStackLimit occurrences of each type+message capture Environment.StackTrace
    // (which includes the throw site). Floods are capped at ExceptionMaxPerSec full
    // records per second per type; the rest are counted and emitted once a second as
    // "first-chance-suppressed" (v = count, x = type name).
    private void OnFirstChance(object? _, FirstChanceExceptionEventArgs e)
    {
        if (_inHandler) return;   // never recurse if something below throws
        _inHandler = true;
        try
        {
            var ex = e.Exception;
            string type = ex.GetType().Name;

            var counts = _perType.GetOrAdd(type, static _ => new ExcCounts());
            if (Interlocked.Increment(ref counts.Full) > _opt.ExceptionMaxPerSec)
            {
                Interlocked.Increment(ref counts.Suppressed);
                return;
            }

            string? stack = ex.StackTrace;
            if (_opt.ExceptionStackLimit > 0)
            {
                string key = type + "|" + ex.Message;
                if (_stacksTaken.TryGetValue(key, out int taken) || _stacksTaken.Count < MaxStackKeys)
                {
                    if (taken < _opt.ExceptionStackLimit)
                    {
                        _stacksTaken[key] = taken + 1;
                        stack = Environment.StackTrace;
                    }
                }
            }

            _sink.Enqueue(new Record(NowMs(), RecKind.Exception,
                string.Intern(type), 0, $"{ex.Message}\n{stack}"));
        }
        catch { /* a profiler must never crash its host */ }
        finally { _inHandler = false; }
    }

    private void FlushSuppressed(object? _)
    {
        long now = NowMs();
        foreach (var kv in _perType)
        {
            Interlocked.Exchange(ref kv.Value.Full, 0);
            int n = Interlocked.Exchange(ref kv.Value.Suppressed, 0);
            if (n > 0)
                _sink.Enqueue(new Record(now, RecKind.Exception, "first-chance-suppressed", n, kv.Key));
        }
    }

    private void OnUnhandled(object? _, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            _sink.Enqueue(new Record(NowMs(), RecKind.Exception, "Unhandled", 1,
                $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"));
    }

    // --- UI-thread hang watchdog -----------------------------------------
    // Round-trip a heartbeat through the MAUI main thread. The heartbeat's post
    // time is kept until its pong arrives, so a stall is measured end to end:
    //   - "ui-stall-begin" once, when the wait first crosses the threshold
    //     (a last-known-state marker if the app dies mid-freeze);
    //   - "ui-stall" once, when the pong lands, with v = the real stall length.
    // (Cross-thread UI stack-walk isn't available in managed code on mobile;
    // duration + your own correlated logs localize it.)
    private void StartHangWatchdog()
    {
        int period = Math.Max(50, _opt.HangThresholdMs / 2);
        _hangTimer = new Timer(Tick, null, period, period);
    }

    private void Tick(object? _)
    {
        if (_awaitingPong)
        {
            long waited = NowMs() - Interlocked.Read(ref _heartbeatPostedTicks);
            // ignore stalls during the startup grace window (UI thread is busy booting)
            if (!_stallReported && waited >= _opt.HangThresholdMs && InGraceWindowPassed())
            {
                _stallReported = true; // once per heartbeat; the pong reports the full length
                _sink.Enqueue(new Record(NowMs(), RecKind.Hang, "ui-stall-begin", waited,
                    "UI thread has not serviced heartbeat; stall in progress"));
            }
            else if (waited >= AbandonHeartbeatMs)
            {
                // The pong may never come (dispatcher torn down). Report what we know and
                // re-arm with a fresh heartbeat so the watchdog doesn't die; the old pong,
                // if it ever runs, is ignored (dispatch is FIFO, so it runs before newer ones).
                Interlocked.Increment(ref _stalePongs);
                if (InGraceWindowPassed())
                    _sink.Enqueue(new Record(NowMs(), RecKind.Hang, "ui-stall", waited,
                        "no heartbeat response; gave up waiting (UI thread still stalled or dispatcher gone)"));
                _awaitingPong = false;
            }
            return;
        }

        _stallReported = false;
        Interlocked.Exchange(ref _heartbeatPostedTicks, NowMs());
        _awaitingPong = true;
#if HAS_MAUI
        try
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(_pong);
        }
        catch
        {
            _awaitingPong = false; // no dispatcher yet (early startup) — skip this round
        }
#else
        // Desktop/test build: no UI dispatcher. Pong immediately so the watchdog stays quiet.
        _awaitingPong = false;
#endif
    }

    private sealed class ExcCounts { public int Full; public int Suppressed; }

    private bool InGraceWindowPassed() => NowMs() - _startMs >= _opt.HangStartupGraceMs;

    // Runs on the UI thread when the heartbeat is finally serviced.
    private void OnPong()
    {
        int stale;
        while ((stale = Volatile.Read(ref _stalePongs)) > 0)
            if (Interlocked.CompareExchange(ref _stalePongs, stale - 1, stale) == stale)
                return;   // pong of an abandoned heartbeat
        long stalled = NowMs() - Interlocked.Read(ref _heartbeatPostedTicks);
        if (stalled >= _opt.HangThresholdMs && InGraceWindowPassed())
            _sink.Enqueue(new Record(NowMs(), RecKind.Hang, "ui-stall", stalled,
                "UI thread stall, measured heartbeat post to service"));
        _awaitingPong = false;
    }

    public void Dispose()
    {
        if (_opt.Channels.HasFlag(Channels.Exceptions))
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        }
        _hangTimer?.Dispose();
        _excTimer?.Dispose();
    }
}
