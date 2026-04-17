using System.Linq;

namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    public AppShell(
        MainPage mainPage,
        HomePage homePage,
        ProfilePage profilePage)
    {
        InitializeComponent();
        FlyoutBehavior = FlyoutBehavior.Disabled;

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
