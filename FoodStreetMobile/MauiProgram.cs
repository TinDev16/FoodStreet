using FoodStreetMobile.Services;
using FoodStreetMobile.ViewModels;
using Microsoft.Extensions.Logging;

namespace FoodStreetMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
 		builder
 			.UseMauiApp<App>()
 			.UseMauiMaps()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});

        builder.Services.AddSingleton<PoiRepository>();
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<PoiSyncService>();
        builder.Services.AddSingleton<GeofenceEngine>();
        builder.Services.AddSingleton<NarrationEngine>();
        builder.Services.AddSingleton<LocationTracker>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<AppShell>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

#if ANDROID
        Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            handler.PlatformView.BackgroundTintList =
                Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
        });
#endif

        return builder.Build();
    }
}

