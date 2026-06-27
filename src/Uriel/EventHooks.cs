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
    private readonly long _startMs;

    public EventHooks(ChunkedConsoleSink sink, UrielOptions opt)
    {
        _sink = sink;
        _opt = opt;
        _startMs = NowMs();

        if (opt.Channels.HasFlag(Channels.Exceptions))
        {
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandled;
        }

        if (opt.Channels.HasFlag(Channels.Hangs))
            StartHangWatchdog();
    }

    private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    private void OnFirstChance(object? _, FirstChanceExceptionEventArgs e)
    {
        var ex = e.Exception;
        _sink.Enqueue(new Record(NowMs(), RecKind.Exception,
            string.Intern(ex.GetType().Name), 0, $"{ex.Message}\n{ex.StackTrace}"));
    }

    private void OnUnhandled(object? _, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            _sink.Enqueue(new Record(NowMs(), RecKind.Exception, "Unhandled", 1,
                $"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}"));
    }

    // --- UI-thread hang watchdog -----------------------------------------
    // Round-trip a heartbeat through the MAUI main thread. If the pong doesn't
    // come back within the threshold, the UI thread is stalled — emit a Hang
    // with the observed latency. (Cross-thread UI stack-walk isn't available in
    // managed code on mobile; latency + your own correlated logs localize it.)
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
            if (waited >= _opt.HangThresholdMs && NowMs() - _startMs >= _opt.HangStartupGraceMs)
            {
                _sink.Enqueue(new Record(NowMs(), RecKind.Hang, "ui-stall", waited,
                    "UI thread did not service heartbeat within threshold"));
                _awaitingPong = false; // re-arm; avoid spamming every tick of the same hang
            }
            return;
        }

        _awaitingPong = true;
        Interlocked.Exchange(ref _heartbeatPostedTicks, NowMs());
#if HAS_MAUI
        try
        {
            Microsoft.Maui.ApplicationModel.MainThread.BeginInvokeOnMainThread(() => _awaitingPong = false);
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

    public void Dispose()
    {
        if (_opt.Channels.HasFlag(Channels.Exceptions))
        {
            AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandled;
        }
        _hangTimer?.Dispose();
    }
}
