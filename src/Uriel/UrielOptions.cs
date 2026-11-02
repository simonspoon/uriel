namespace Uriel;

/// <summary>Opt-in collectors. Flags so callers compose exactly what they pay for.</summary>
[Flags]
public enum Channels
{
    None        = 0,
    Counters    = 1 << 0,   // System.Runtime EventCounters (cheap, periodic)
    Exceptions  = 1 << 1,   // first-chance + unhandled, with stack
    Hangs       = 1 << 2,   // UI-thread watchdog, stack on stall
    Gc          = 1 << 3,   // one record per GC (gen counts, heap, pause); opt-in

    Default     = Counters | Exceptions | Hangs,
    All         = Counters | Exceptions | Hangs | Gc,
}

public sealed class UrielOptions
{
    /// <summary>Which collectors run. Default = counters + exceptions + hangs.</summary>
    public Channels Channels { get; set; } = Channels.Default;

    /// <summary>EventCounter polling interval, seconds. Higher = cheaper.</summary>
    public double CounterIntervalSec { get; set; } = 1.0;

    /// <summary>UI-thread stall (ms) that counts as a hang. Only used if Hangs enabled.</summary>
    public int HangThresholdMs { get; set; } = 250;

    /// <summary>Suppress hang reports for this long after Start — the UI thread is busy booting.</summary>
    public int HangStartupGraceMs { get; set; } = 3000;

    /// <summary>First-chance exceptions: capture Environment.StackTrace for the first N of each type+message.</summary>
    public int ExceptionStackLimit { get; set; } = 5;

    /// <summary>First-chance exceptions: full records per second per type; beyond that they're counted per type ("first-chance-suppressed").</summary>
    public int ExceptionMaxPerSec { get; set; } = 20;

    /// <summary>Max queued records before drop-on-full kicks in. Bounds memory + back-pressure.</summary>
    public int QueueCapacity { get; set; } = 8192;

    /// <summary>
    /// Max UTF-8 bytes per console line before chunking. Default 800: small enough that
    /// no single line hits iOS os_log's truncation/corruption ceiling. (logcat tolerates
    /// ~4000, but os_log is the tighter constraint, so we size for the worst case.)
    /// </summary>
    public int MaxChunkBytes { get; set; } = 800;

    /// <summary>
    /// Sentinel prefix so your listener can grep Uriel lines out of console noise.
    /// MUST be ASCII: iOS routes Console through os_log, which mangles multibyte UTF-8
    /// (e.g. '§' C2A7) on long lines, breaking a literal grep. Keep it plain ASCII.
    /// </summary>
    public string Sentinel { get; set; } = "@@URIEL@@";

    /// <summary>Optional run id stamped on every record, to correlate one session's stream.</summary>
    public string? SessionId { get; set; }
}
