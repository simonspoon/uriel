namespace Uriel;

/// <summary>What a telemetry record represents. One byte, kept tiny on purpose.</summary>
public enum RecKind : byte
{
    Counter,    // periodic EventCounter sample (cpu, heap, alloc-rate, ...)
    Gc,         // gc event summary
    Exception,  // first-chance / unhandled exception
    Hang,       // foreground hang detected by watchdog
    Stack,      // on-demand stack capture
    Meta,       // session start, config echo, heartbeat
}

/// <summary>
/// Immutable value pushed onto the channel from the hot path.
/// Struct + interned strings = no heap churn at the call site;
/// all serialization happens later on the flush thread.
/// </summary>
public readonly struct Record
{
    public readonly long T;          // unix epoch ms
    public readonly RecKind Kind;
    public readonly string Name;     // counter name / event label (interned)
    public readonly double Val;      // numeric payload (0 when N/A)
    public readonly string? Text;    // message / stack (null on hot counters)

    public Record(long t, RecKind kind, string name, double val, string? text = null)
    {
        T = t; Kind = kind; Name = name; Val = val; Text = text;
    }
}
