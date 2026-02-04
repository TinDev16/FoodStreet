namespace FoodStreetMobile;

public partial class AppShell : Shell
{
    public AppShell(MainPage mainPage)
    {
        InitializeComponent();

        Items.Add(new ShellContent
        {
            Title = "Kham pha",
            Content = mainPage,
            Route = "MainPage"
        });
    }
}
