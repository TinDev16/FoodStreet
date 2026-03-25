namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage, HomePage homePage, ProfilePage profilePage)
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
    }
}
