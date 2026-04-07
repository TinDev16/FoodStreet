namespace FoodStreetMobile;

public partial class AuthPage : ContentPage
{
    public AuthPage(ViewModels.AuthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
