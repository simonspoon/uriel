using Microsoft.Extensions.Logging;
using Uriel;

namespace UrielDemo;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		builder.UseUriel(o =>
		{
			o.Channels = Channels.All;
			o.CounterIntervalSec = 1;
			o.SessionId = "ios-demo";
		});

		return builder.Build();
	}
}
