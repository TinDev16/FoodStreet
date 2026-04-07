using System.Linq;
using FoodStreetMobile.Services;

namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    private readonly AuthService _authService;

    public AppShell(
        MainPage mainPage,
        HomePage homePage,
        ProfilePage profilePage,
        AuthPage authPage,
        RegisterPage registerPage,
        AuthService authService)
    {
        InitializeComponent();
        _authService = authService;
        FlyoutBehavior = FlyoutBehavior.Disabled;

        Items.Add(new ShellContent
        {
            Route = "AuthPage",
            Content = authPage,
            Title = "Auth"
        });

        Items.Add(new ShellContent
        {
            Route = "RegisterPage",
            Content = registerPage,
            Title = "Register"
        });

        var tabBar = new TabBar
        {
            Route = "MainTabs"
        };

        tabBar.Items.Add(new Tab
        {
            Icon = "ic_tab_home.svg",
            Items =
            {
                new ShellContent
                {
                    Route = "HomePage",
                    Content = homePage
                }
            }
        });

        tabBar.Items.Add(new Tab
        {
            Icon = "ic_tab_explore.svg",
            Items =
            {
                new ShellContent
                {
                    Route = "MainPage",
                    Content = mainPage
                }
            }
        });

        tabBar.Items.Add(new Tab
        {
            Icon = "ic_tab_profile.svg",
            Items =
            {
                new ShellContent
                {
                    Route = "ProfilePage",
                    Content = profilePage
                }
            }
        });

        Items.Add(tabBar);

        Loaded += OnShellLoaded;
    }

    private async void OnShellLoaded(object? sender, EventArgs e)
    {
        Loaded -= OnShellLoaded;
        await ApplyInitialRouteAsync();
    }

    private async Task ApplyInitialRouteAsync()
    {
        try
        {
            if (!_authService.IsLoggedIn)
            {
                await GoToAsync("//AuthPage");
                return;
            }

            NavigateToMainTabsTab(0);
        }
        catch
        {
        }
    }

    /// <summary>
    /// Chuyển sang TabBar và chọn tab theo chỉ số (0=Trang chủ, 1=Bản đồ, 2=Cá nhân).
    /// Tránh dùng URI //MainTabs/... — trên Android dễ gây lỗi khi đổi từ màn Auth.
    /// </summary>
    public void NavigateToMainTabsTab(int tabIndex)
    {
        var tabBar = Items.OfType<TabBar>().FirstOrDefault();
        if (tabBar is null || tabBar.Items.Count == 0)
        {
            return;
        }

        CurrentItem = tabBar;
        var i = Math.Clamp(tabIndex, 0, tabBar.Items.Count - 1);
        tabBar.CurrentItem = tabBar.Items[i];
    }
}
