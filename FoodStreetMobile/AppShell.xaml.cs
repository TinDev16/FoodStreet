namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();
        FlyoutBehavior = FlyoutBehavior.Disabled;

        var tabBar = new TabBar();

        tabBar.Items.Add(new Tab
        {
            Icon = "ic_tab_home.svg",
            Items =
            {
                new ShellContent
                {
                    Route = "HomePage",
                    Content = new HomePage()
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
                    Content = new ProfilePage()
                }
            }
        });

        Items.Add(tabBar);
    }
}
