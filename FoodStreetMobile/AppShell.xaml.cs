namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        FlyoutBehavior = FlyoutBehavior.Disabled;

        var tabBar = new TabBar();

        tabBar.Items.Add(new ShellContent
        {
            Title = "Trang chủ",
            Icon = "ic_tab_home.svg",
            Route = "HomePage",
            Content = new HomePage()
        });

        tabBar.Items.Add(new ShellContent
        {
            Title = "Khám phá",
            Icon = "ic_tab_explore.svg",
            Route = "MainPage",
            Content = mainPage
        });

        tabBar.Items.Add(new ShellContent
        {
            Title = "Cá nhân",
            Icon = "ic_tab_profile.svg",
            Route = "ProfilePage",
            Content = new ProfilePage()
        });

        Items.Add(tabBar);
    }
}
