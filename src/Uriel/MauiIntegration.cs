using Microsoft.Maui.Hosting;

namespace Uriel;

/// <summary>One-liner activation for MAUI startup.</summary>
public static class MauiIntegration
{
    /// <example>
    /// builder.UseUriel(o => {
    ///     o.Channels = Channels.All;
    ///     o.CounterIntervalSec = 1;
    /// });
    /// </example>
    public static MauiAppBuilder UseUriel(this MauiAppBuilder builder, Action<UrielOptions>? configure = null)
    {
        UrielProfiler.Start(configure);
        return builder;
    }
}
