namespace FoodStreetMobile;

public partial class RegisterPage : ContentPage
{
    public RegisterPage(ViewModels.AuthViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}
