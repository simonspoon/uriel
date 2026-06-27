using System.Runtime.CompilerServices;

namespace Uriel;

/// <summary>
/// Zero-code activation path. When the Uriel assembly is loaded, if the env var
/// URIEL_AUTOSTART is truthy, profiling starts with defaults — no call site needed.
///
/// Caveat (honest): a [ModuleInitializer] only runs once *something* in this
/// assembly is touched/loaded. Referencing the package but never invoking it may
/// not load the module. The reliable activation is builder.UseUriel(...).
/// Env autostart is best-effort for "flip it on in the field without a rebuild".
/// </summary>
internal static class AutoStart
{
    [ModuleInitializer]
    internal static void Init()
    {
        try
        {
            var flag = Environment.GetEnvironmentVariable("URIEL_AUTOSTART");
            if (string.IsNullOrEmpty(flag)) return;
            if (flag is "1" or "true" or "TRUE" or "yes" or "on")
                UrielProfiler.Start();
        }
        catch { /* never break host startup */ }
    }
}
