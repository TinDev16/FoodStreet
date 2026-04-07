namespace FoodStreetMobile;

public partial class ProfilePage : ContentPage
{
    private readonly ViewModels.ProfileViewModel _viewModel;

    public ProfilePage(ViewModels.ProfileViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.OnAppearingAsync();
    }
}
