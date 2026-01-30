using Microsoft.Maui.Devices.Sensors;

namespace FoodStreetMobile.Views;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
        GetUserLocation();
    }

    async void GetUserLocation()
    {
        try
        {
            var request = new GeolocationRequest(
                GeolocationAccuracy.Best,
                TimeSpan.FromSeconds(10));

            var location = await Geolocation.Default.GetLocationAsync(request);

            if (location != null)
            {
                // Gửi tọa độ sang JS
                await Dispatcher.DispatchAsync(async () =>
                {
                    await webView.EvaluateJavaScriptAsync(
                        $"showUserLocation({location.Latitude}, {location.Longitude});"
                    );
                });
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Lỗi GPS", ex.Message, "OK");
        }
    }
}
