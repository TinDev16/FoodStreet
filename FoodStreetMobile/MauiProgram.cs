using FoodStreetMobile.Services;
using FoodStreetMobile.ViewModels;
using Microsoft.Extensions.Logging;
#if ANDROID || IOS
using Microsoft.Maui.Handlers;
#endif
#if IOS
using WebKit;
#endif

namespace FoodStreetMobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .ConfigureMauiHandlers(handlers =>
            {
            #if ANDROID
                handlers.AddHandler<
                    Microsoft.Maui.Controls.Maps.Map,
                    CustomMapHandler>();
            #endif
            })
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<PoiRepository>();
        builder.Services.AddSingleton<AppDatabase>();
        builder.Services.AddSingleton<PoiSyncService>();
        builder.Services.AddSingleton<DeepLinkService>();
        builder.Services.AddSingleton<AppLanguageService>();
        builder.Services.AddSingleton<GeofenceEngine>();
        builder.Services.AddSingleton<NarrationEngine>();
        builder.Services.AddSingleton<LocationTracker>();
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<ProfileViewModel>();
        builder.Services.AddSingleton<HomePage>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddSingleton<ProfilePage>();
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

        WebViewHandler.Mapper.AppendToMapping("AllowMediaAutoplay", (handler, view) =>
        {
            if (handler.PlatformView.Settings is null)
            {
                return;
            }

            handler.PlatformView.Settings.JavaScriptEnabled = true;
            handler.PlatformView.Settings.MediaPlaybackRequiresUserGesture = false;
        });
#endif

#if IOS
        WebViewHandler.Mapper.AppendToMapping("AllowMediaAutoplay", (handler, view) =>
        {
            handler.PlatformView.Configuration.AllowsInlineMediaPlayback = true;
            handler.PlatformView.Configuration.MediaTypesRequiringUserActionForPlayback = WKAudiovisualMediaTypes.None;
        });
#endif

        return builder.Build();
    }
}
