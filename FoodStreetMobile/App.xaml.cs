using Microsoft.Maui.ApplicationModel;

namespace FoodStreetMobile;

public partial class App : Application
{
    private readonly AppShell _appShell;
    private readonly Services.DeepLinkService _deepLinkService;

    public App(AppShell appShell, Services.AppLanguageService languageService, Services.DeepLinkService deepLinkService, Services.PoiSyncService poiSyncService)
    {
        InitializeComponent();
        _appShell = appShell;
        _deepLinkService = deepLinkService;
        Services.CrashLogger.Initialize();
        languageService.Initialize();

        var _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await poiSyncService.TrackActivityAsync("ping", null, languageService.CurrentLanguage);
                }
                catch { }
                await Task.Delay(TimeSpan.FromSeconds(35));
            }
        });
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_appShell);
        
        window.Created += (s, e) => {
            // Optional: Start ping when created
        };

        window.Resumed += (s, e) => {
            var languageService = Handler?.MauiContext?.Services.GetService<Services.AppLanguageService>();
            var poiSyncService = Handler?.MauiContext?.Services.GetService<Services.PoiSyncService>();
            if (poiSyncService != null && languageService != null)
            {
                _ = poiSyncService.TrackActivityAsync("ping", null, languageService.CurrentLanguage);
            }
        };

        window.Stopped += (s, e) => { // Equiv to OnSleep
            var languageService = Handler?.MauiContext?.Services.GetService<Services.AppLanguageService>();
            var poiSyncService = Handler?.MauiContext?.Services.GetService<Services.PoiSyncService>();
            if (poiSyncService != null && languageService != null)
            {
                _ = poiSyncService.TrackActivityAsync("offline", null, languageService.CurrentLanguage);
            }
        };

        return window;
    }

    protected override void OnAppLinkRequestReceived(Uri uri)
    {
        base.OnAppLinkRequestReceived(uri);

        if (!_deepLinkService.TryQueueFromUri(uri))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                if (Shell.Current is not null)
                {
                    if (Shell.Current is AppShell shell)
                    {
                        shell.NavigateToMainTabsTab(1);
                    }
                }
            }
            catch
            {
            }
        });
    }
}
