namespace FoodStreetMobile;

public partial class PoiViewHistoryPage : ContentPage
{
    private readonly ViewModels.PoiViewHistoryViewModel _viewModel;

    public PoiViewHistoryPage(ViewModels.PoiViewHistoryViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.RefreshAsync();
    }
}

