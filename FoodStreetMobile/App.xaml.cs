namespace FoodStreetMobile;

public partial class App : Application
{
    private readonly AppShell _appShell;

    public App(AppShell appShell, Services.AppLanguageService languageService)
    {
        InitializeComponent();
        _appShell = appShell;
        Services.CrashLogger.Initialize();
        languageService.Initialize();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        return new Window(_appShell);
    }
}
