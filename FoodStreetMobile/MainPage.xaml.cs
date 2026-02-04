using FoodStreetMobile.ViewModels;
using Mapsui;
using Mapsui.Layers;
using Mapsui.Projections;
using Mapsui.Styles;
using Mapsui.Tiling;
using Mapsui.UI.Maui;
using Microsoft.Maui.Devices.Sensors;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace FoodStreetMobile;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;
    private readonly MemoryLayer _poiLayer = new() { Name = "pois" };
    private readonly MemoryLayer _activePoiLayer = new() { Name = "active" };
    private readonly MemoryLayer _userLayer = new() { Name = "user" };
    private bool _hasCenteredOnUser;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PoisLoaded += OnPoisLoaded;
        _viewModel.ActivePoiChanged += OnActivePoiChanged;
        _viewModel.UserLocationChanged += OnUserLocationChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.InitializeAsync();
    }

    private void OnPoisLoaded(IReadOnlyList<PoiViewModel> pois)
    {
        var map = new Mapsui.Map();
        map.Layers.Add(OpenStreetMap.CreateTileLayer());

        _poiLayer.Features = pois.Select(poi => CreatePoiFeature(poi, "#1F2937", "#F9FAFB")).ToList();
        _activePoiLayer.Features = new List<IFeature>();
        _userLayer.Features = new List<IFeature>();

        map.Layers.Add(_poiLayer);
        map.Layers.Add(_activePoiLayer);
        map.Layers.Add(_userLayer);

        PoiMap.Map = map;

        // Center the map on Vinh Khanh area by default (10.7601, 106.7029).
        var center = SphericalMercator.FromLonLat(106.702035, 10.764111 );
        PoiMap.Map.Home = navigator => navigator.CenterOn(center.x, center.y);
        _hasCenteredOnUser = true;
    }

    private void OnActivePoiChanged(PoiViewModel? active)
    {
        if (PoiMap.Map is null)
        {
            return;
        }

        _activePoiLayer.Features = active is null
            ? new List<IFeature>()
            : new List<IFeature> { CreatePoiFeature(active, "#B45309", "#FCD34D", 20) };

        _activePoiLayer.DataHasChanged();
    }

    private void OnUserLocationChanged(MauiLocation location)
    {
        if (PoiMap.Map is null)
        {
            return;
        }

        _userLayer.Features = new List<IFeature> { CreateUserFeature(location) };
        _userLayer.DataHasChanged();

        if (_hasCenteredOnUser)
        {
            return;
        }

        _hasCenteredOnUser = true;
        var center = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        PoiMap.Map.Home = navigator => navigator.CenterOn(center.x, center.y);
    }

    private static IFeature CreatePoiFeature(PoiViewModel poi, string strokeHex, string fillHex, int size = 16)
    {
        var world = SphericalMercator.FromLonLat(poi.Longitude, poi.Latitude);
        var feature = new Mapsui.Layers.PointFeature(world.x, world.y);
        feature.Styles.Add(CreatePointStyle(strokeHex, fillHex, size));
        feature.Styles.Add(new LabelStyle
        {
            Text = poi.Name,
            ForeColor = ParseColor("#111827"),
            BackColor = new Mapsui.Styles.Brush(ParseColor("#FFFFFF", 200)),
            Offset = new Offset(0, -24),
            HorizontalAlignment = LabelStyle.HorizontalAlignmentEnum.Center
        });
        return feature;
    }

    private static IFeature CreateUserFeature(MauiLocation location)
    {
        var world = SphericalMercator.FromLonLat(location.Longitude, location.Latitude);
        var feature = new Mapsui.Layers.PointFeature(world.x, world.y);
        feature.Styles.Add(CreatePointStyle("#2563EB", "#93C5FD", 14));
        return feature;
    }

    private static SymbolStyle CreatePointStyle(string strokeHex, string fillHex, int size)
    {
        var stroke = ParseColor(strokeHex);
        var fill = ParseColor(fillHex);

        return new SymbolStyle
        {
            SymbolScale = size / 16d,
            Fill = new Mapsui.Styles.Brush(fill),
            Outline = new Mapsui.Styles.Pen(stroke, 2),
            SymbolType = SymbolType.Ellipse
        };
    }

    private static Mapsui.Styles.Color ParseColor(string hex, byte alphaOverride = 255)
    {
        var cleaned = hex.TrimStart('#');
        if (cleaned.Length == 6)
        {
            var r = Convert.ToByte(cleaned.Substring(0, 2), 16);
            var g = Convert.ToByte(cleaned.Substring(2, 2), 16);
            var b = Convert.ToByte(cleaned.Substring(4, 2), 16);
            return Mapsui.Styles.Color.FromArgb(alphaOverride, r, g, b);
        }

        if (cleaned.Length == 8)
        {
            var a = Convert.ToByte(cleaned.Substring(0, 2), 16);
            var r = Convert.ToByte(cleaned.Substring(2, 2), 16);
            var g = Convert.ToByte(cleaned.Substring(4, 2), 16);
            var b = Convert.ToByte(cleaned.Substring(6, 2), 16);
            return Mapsui.Styles.Color.FromArgb(a, r, g, b);
        }

        return Mapsui.Styles.Color.FromArgb(alphaOverride, 0, 0, 0);
    }
}
