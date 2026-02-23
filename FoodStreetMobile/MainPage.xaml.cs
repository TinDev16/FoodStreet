using FoodStreetMobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace FoodStreetMobile;

public partial class MainPage : ContentPage
{
    private sealed class SearchPlaceResult
    {
        public required string Name { get; init; }
        public required string Address { get; init; }
        public double Latitude { get; init; }
        public double Longitude { get; init; }
        public double Importance { get; init; }
        public string? ImageUrl { get; init; }
        public string? PlaceId { get; init; }
        public bool HasCoordinates => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
    }

    private sealed class DirectionsResult
    {
        public required string DistanceText { get; init; }
        public required string DurationText { get; init; }
        public required List<MauiLocation> Path { get; init; }
    }

    private const string GoogleMapsApiKey = "AIzaSyAg9cHLgybrf3Edkl8ZK9nuRuQpF9nzCNY";
    private const double DefaultLatitude = 10.762011;
    private const double DefaultLongitude =  106.703465;
    private const double DefaultZoomRadiusKm = 0.08;
    private static readonly HttpClient HttpClient = new();

    private readonly MainViewModel _viewModel;
    private readonly List<SearchPlaceResult> _searchResults = new();

    private bool _hasCenteredOnUser;
    private bool _isLocationSetupDone;
    private MauiLocation? _lastUserLocation;
    private SearchPlaceResult? _selectedSearchResult;
    private CancellationTokenSource? _searchTypingCts;
    private string _lastSearchQuery = string.Empty;
    private Pin? _activePoiPin;
    private Pin? _searchPin;
    private Polyline? _routePolyline;
    private string? _lastRouteSummary;

    private bool _sheetInitialized;
    private double _sheetExpandedTranslation;
    private double _sheetPartialTranslation;
    private double _sheetHiddenTranslation;
    private double _sheetPanStartTranslation;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PoisLoaded += OnPoisLoaded;
        _viewModel.ActivePoiChanged += OnActivePoiChanged;
        _viewModel.UserLocationChanged += OnUserLocationChanged;
        SizeChanged += OnPageSizeChanged;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureBottomSheetLayout();
        await EnsureUserLocationEnabledAsync();
        await _viewModel.InitializeAsync();
    }

    private void OnPoisLoaded(IReadOnlyList<PoiViewModel> pois)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            PoiMap.IsShowingUser = true;
            PoiMap.Pins.Clear();
            ClearRoute();

            _activePoiPin = null;
            _searchPin = null;
            _lastRouteSummary = null;

            foreach (var poi in pois)
            {
                PoiMap.Pins.Add(CreatePoiPin(poi));
            }

            MoveMapTo(DefaultLatitude, DefaultLongitude, DefaultZoomRadiusKm);
            _hasCenteredOnUser = false;
        });
    }

    private void OnActivePoiChanged(PoiViewModel? active)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RemovePin(_activePoiPin);
            _activePoiPin = null;

            if (active is null)
            {
                return;
            }

            _activePoiPin = new Pin
            {
                Label = $"{active.Name} (Dang gan)",
                Address = $"{active.Latitude.ToString(CultureInfo.InvariantCulture)}, {active.Longitude.ToString(CultureInfo.InvariantCulture)}",
                Type = PinType.Place,
                Location = new MauiLocation(active.Latitude, active.Longitude)
            };

            PoiMap.Pins.Add(_activePoiPin);
        });
    }

    private void OnUserLocationChanged(MauiLocation location)
    {
        _lastUserLocation = location;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PoiMap.IsShowingUser = true;
            if (_hasCenteredOnUser)
            {
                return;
            }

            _hasCenteredOnUser = true;
            MoveMapTo(location.Latitude, location.Longitude, 1.2);
        });
    }

    private async Task EnsureUserLocationEnabledAsync()
    {
        if (_isLocationSetupDone)
        {
            return;
        }

        _isLocationSetupDone = true;
        try
        {
            var status = await Permissions.CheckStatusAsync<Permissions.LocationWhenInUse>();
            if (status != PermissionStatus.Granted)
            {
                status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
            }

            if (status != PermissionStatus.Granted)
            {
                PoiMap.IsShowingUser = false;
                await DisplayAlertAsync("Thong bao", "Can cap quyen vi tri de hien thi vi tri hien tai.", "OK");
                return;
            }

            PoiMap.IsShowingUser = true;

            var location = await Geolocation.GetLastKnownLocationAsync();
            if (location is null)
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
                location = await Geolocation.GetLocationAsync(request);
            }

            if (location is not null)
            {
                OnUserLocationChanged(location);
            }
        }
        catch (FeatureNotSupportedException)
        {
            PoiMap.IsShowingUser = false;
        }
        catch (PermissionException)
        {
            PoiMap.IsShowingUser = false;
        }
        catch
        {
            PoiMap.IsShowingUser = false;
        }
    }

    private async void OnSearchPlaceClicked(object? sender, EventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            await SearchPlaceAsync(forceSelection: true);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void OnLocateMeClicked(object? sender, EventArgs e)
    {
        var locateButton = sender as ImageButton;
        if (locateButton is not null)
        {
            locateButton.IsEnabled = false;
        }

        try
        {
            await EnsureUserLocationEnabledAsync();

            if (_lastUserLocation is null)
            {
                var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
                var location = await Geolocation.GetLocationAsync(request);
                if (location is not null)
                {
                    OnUserLocationChanged(location);
                }
            }

            if (_lastUserLocation is null)
            {
                await DisplayAlertAsync("Thong bao", "Chua lay duoc vi tri hien tai.", "OK");
                return;
            }

            MoveMapTo(_lastUserLocation.Latitude, _lastUserLocation.Longitude, 0.8);
        }
        catch
        {
            await DisplayAlertAsync("Thong bao", "Khong the xac dinh vi tri hien tai.", "OK");
        }
        finally
        {
            if (locateButton is not null)
            {
                locateButton.IsEnabled = true;
            }
        }
    }

    private async void OnPlaceSearchCompleted(object? sender, EventArgs e)
    {
        await SearchPlaceAsync(forceSelection: true);
    }

    private async void OnPlaceSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _searchTypingCts?.Cancel();
        _searchTypingCts?.Dispose();

        var query = e.NewTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            HideSearchResults();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchTypingCts = cts;

        try
        {
            await Task.Delay(220, cts.Token);
            var results = await SearchPlacesAsync(query, cts.Token);
            if (!cts.IsCancellationRequested)
            {
                BindSearchResults(results, keepVisible: true);
            }
        }
        catch
        {
            HideSearchResults();
        }
    }

    private async Task SearchPlaceAsync(bool forceSelection)
    {
        var query = PlaceSearchEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            await DisplayAlertAsync("Thong bao", "Hay nhap dia diem can tim.", "OK");
            return;
        }

        try
        {
            List<SearchPlaceResult> results;
            if (string.Equals(query, _lastSearchQuery, StringComparison.OrdinalIgnoreCase) && _searchResults.Count > 0)
            {
                results = _searchResults.ToList();
            }
            else
            {
                results = await SearchPlacesAsync(query, CancellationToken.None);
            }

            BindSearchResults(results, keepVisible: !forceSelection || results.Count > 1);
            if (results.Count == 0)
            {
                await DisplayAlertAsync("Thong bao", "Khong tim thay dia diem.", "OK");
                return;
            }

            if (forceSelection)
            {
                await SelectSearchResultAsync(results[0]);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Loi", $"Khong the tim dia diem: {ex.Message}", "OK");
        }
    }

    private async void OnSearchResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchPlaceResult result)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await SelectSearchResultAsync(result);
    }

    private async Task SelectSearchResultAsync(SearchPlaceResult result)
    {
        var resolved = await ResolveSearchResultAsync(result);
        if (!resolved.HasCoordinates)
        {
            await DisplayAlertAsync("Thong bao", "Khong the lay toa do cho dia diem nay.", "OK");
            return;
        }

        _selectedSearchResult = resolved;
        _lastRouteSummary = null;
        ClearRoute();

        PlaceSearchEntry.Text = resolved.Name;
        PlaceSearchEntry.Unfocus();
        HideSearchResults();

        UpdateBottomSheetContent(resolved);
        await ShowSheetPartialAsync();
        await DrawSearchResultAsync(resolved);
    }

    private async Task<SearchPlaceResult> ResolveSearchResultAsync(SearchPlaceResult result)
    {
        if (result.HasCoordinates)
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(result.PlaceId))
        {
            var details = await QueryGooglePlaceDetailsAsync(result.PlaceId, CancellationToken.None);
            if (details is not null)
            {
                return details;
            }
        }

        var fallback = await QueryGoogleGeocodeAsync(result.Address, 1, applyVnFilter: false, applyBoundedBias: true, CancellationToken.None);
        return fallback.FirstOrDefault() ?? result;
    }

    private async Task<List<SearchPlaceResult>> SearchPlacesAsync(string query, CancellationToken cancellationToken)
    {
        if (TryParseCoordinateInput(query, out var coordinate))
        {
            _lastSearchQuery = query;
            return new List<SearchPlaceResult> { coordinate };
        }

        var list = await QueryGoogleAutocompleteAsync(query, 8, applyVnFilter: true, applyLocationBias: true, cancellationToken);
        if (list.Count == 0)
        {
            list = await QueryGoogleAutocompleteAsync(query, 8, applyVnFilter: false, applyLocationBias: true, cancellationToken);
        }

        if (list.Count == 0)
        {
            list = await QueryGoogleGeocodeAsync(query, 8, applyVnFilter: true, applyBoundedBias: true, cancellationToken);
        }

        if (list.Count == 0)
        {
            list = await QueryGoogleGeocodeAsync(query, 8, applyVnFilter: false, applyBoundedBias: true, cancellationToken);
        }

        if (list.Count == 0)
        {
            list = await QueryGoogleGeocodeAsync(query, 8, applyVnFilter: false, applyBoundedBias: false, cancellationToken);
        }

        _lastSearchQuery = query;
        return RankSearchResults(query, list);
    }

    private async Task<List<SearchPlaceResult>> QueryGoogleAutocompleteAsync(
        string query,
        int limit,
        bool applyVnFilter,
        bool applyLocationBias,
        CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"input={Uri.EscapeDataString(query)}",
            "language=vi",
            "types=establishment|geocode",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        if (applyVnFilter)
        {
            parameters.Add("components=country:vn");
        }

        if (applyLocationBias && _lastUserLocation is not null)
        {
            parameters.Add($"location={_lastUserLocation.Latitude.ToString(CultureInfo.InvariantCulture)},{_lastUserLocation.Longitude.ToString(CultureInfo.InvariantCulture)}");
            parameters.Add("radius=15000");
        }

        var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<SearchPlaceResult>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal)
            && !string.Equals(status, "ZERO_RESULTS", StringComparison.Ordinal))
        {
            return new List<SearchPlaceResult>();
        }

        if (!document.RootElement.TryGetProperty("predictions", out var predictionsNode)
            || predictionsNode.ValueKind != JsonValueKind.Array)
        {
            return new List<SearchPlaceResult>();
        }

        var results = new List<SearchPlaceResult>();
        foreach (var item in predictionsNode.EnumerateArray())
        {
            if (!TryBuildAutocompleteResult(item, out var result))
            {
                continue;
            }

            results.Add(result);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<List<SearchPlaceResult>> QueryGoogleGeocodeAsync(
        string query,
        int limit,
        bool applyVnFilter,
        bool applyBoundedBias,
        CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"address={Uri.EscapeDataString(query)}",
            "language=vi",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        if (applyVnFilter)
        {
            parameters.Add("components=country:VN");
            parameters.Add("region=vn");
        }

        if (applyBoundedBias && _lastUserLocation is not null)
        {
            const double delta = 0.08;
            var south = _lastUserLocation.Latitude - delta;
            var west = _lastUserLocation.Longitude - delta;
            var north = _lastUserLocation.Latitude + delta;
            var east = _lastUserLocation.Longitude + delta;
            var bounds = $"{south.ToString(CultureInfo.InvariantCulture)},{west.ToString(CultureInfo.InvariantCulture)}|{north.ToString(CultureInfo.InvariantCulture)},{east.ToString(CultureInfo.InvariantCulture)}";
            parameters.Add($"bounds={Uri.EscapeDataString(bounds)}");
        }

        var url = $"https://maps.googleapis.com/maps/api/geocode/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<SearchPlaceResult>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
        {
            return new List<SearchPlaceResult>();
        }

        if (!document.RootElement.TryGetProperty("results", out var resultsNode)
            || resultsNode.ValueKind != JsonValueKind.Array)
        {
            return new List<SearchPlaceResult>();
        }

        var results = new List<SearchPlaceResult>();
        foreach (var item in resultsNode.EnumerateArray())
        {
            if (!TryBuildGeocodeResult(item, out var result))
            {
                continue;
            }

            results.Add(result);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<SearchPlaceResult?> QueryGooglePlaceDetailsAsync(string placeId, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"place_id={Uri.EscapeDataString(placeId)}",
            "language=vi",
            "fields=name,formatted_address,geometry,photos",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        var url = $"https://maps.googleapis.com/maps/api/place/details/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("result", out var resultNode))
        {
            return null;
        }

        return TryBuildPlaceDetailsResult(resultNode, placeId, out var result) ? result : null;
    }

    private static bool TryBuildAutocompleteResult(JsonElement item, out SearchPlaceResult result)
    {
        result = null!;
        var placeId = item.TryGetProperty("place_id", out var placeIdNode) ? placeIdNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return false;
        }

        var description = item.TryGetProperty("description", out var descriptionNode)
            ? descriptionNode.GetString() ?? string.Empty
            : string.Empty;
        var mainText = string.Empty;

        if (item.TryGetProperty("structured_formatting", out var formattingNode)
            && formattingNode.TryGetProperty("main_text", out var mainTextNode))
        {
            mainText = mainTextNode.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(mainText))
        {
            mainText = description.Split(',').FirstOrDefault()?.Trim() ?? "Dia diem";
        }

        result = new SearchPlaceResult
        {
            Name = mainText,
            Address = description,
            Latitude = double.NaN,
            Longitude = double.NaN,
            Importance = 1.0,
            PlaceId = placeId
        };

        return true;
    }

    private static bool TryBuildGeocodeResult(JsonElement item, out SearchPlaceResult result)
    {
        result = null!;
        if (!item.TryGetProperty("geometry", out var geometryNode)
            || !geometryNode.TryGetProperty("location", out var locationNode)
            || !locationNode.TryGetProperty("lat", out var latNode)
            || !locationNode.TryGetProperty("lng", out var lonNode))
        {
            return false;
        }

        var lat = latNode.GetDouble();
        var lon = lonNode.GetDouble();
        var address = item.TryGetProperty("formatted_address", out var addressNode)
            ? addressNode.GetString() ?? string.Empty
            : string.Empty;

        var name = address.Split(',').FirstOrDefault()?.Trim() ?? "Dia diem";
        result = new SearchPlaceResult
        {
            Name = name,
            Address = string.IsNullOrWhiteSpace(address) ? "Khong co dia chi" : address,
            Latitude = lat,
            Longitude = lon,
            Importance = 0.7,
            PlaceId = item.TryGetProperty("place_id", out var placeIdNode) ? placeIdNode.GetString() : null
        };
        return true;
    }

    private bool TryBuildPlaceDetailsResult(JsonElement item, string placeId, out SearchPlaceResult result)
    {
        result = null!;
        if (!item.TryGetProperty("geometry", out var geometryNode)
            || !geometryNode.TryGetProperty("location", out var locationNode)
            || !locationNode.TryGetProperty("lat", out var latNode)
            || !locationNode.TryGetProperty("lng", out var lonNode))
        {
            return false;
        }

        var name = item.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "Dia diem" : "Dia diem";
        var address = item.TryGetProperty("formatted_address", out var addressNode) ? addressNode.GetString() ?? "Khong co dia chi" : "Khong co dia chi";

        string? photoUrl = null;
        if (item.TryGetProperty("photos", out var photosNode) && photosNode.ValueKind == JsonValueKind.Array)
        {
            var first = photosNode.EnumerateArray().FirstOrDefault();
            if (first.TryGetProperty("photo_reference", out var refNode))
            {
                var photoRef = refNode.GetString();
                if (!string.IsNullOrWhiteSpace(photoRef))
                {
                    photoUrl = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth=900&photo_reference={Uri.EscapeDataString(photoRef)}&key={Uri.EscapeDataString(GoogleMapsApiKey)}";
                }
            }
        }

        result = new SearchPlaceResult
        {
            Name = name,
            Address = address,
            Latitude = latNode.GetDouble(),
            Longitude = lonNode.GetDouble(),
            Importance = 1.0,
            ImageUrl = photoUrl,
            PlaceId = placeId
        };
        return true;
    }

    private List<SearchPlaceResult> RankSearchResults(string query, IEnumerable<SearchPlaceResult> results)
    {
        var normalized = query.Trim().ToLowerInvariant();

        return results
            .Select(r =>
            {
                var score = r.Importance;
                var n = r.Name.ToLowerInvariant();
                var a = r.Address.ToLowerInvariant();

                if (n == normalized) score += 3.0;
                else if (n.StartsWith(normalized, StringComparison.Ordinal)) score += 1.4;
                else if (n.Contains(normalized, StringComparison.Ordinal)) score += 0.9;

                if (a.Contains(normalized, StringComparison.Ordinal)) score += 0.3;
                if (_lastUserLocation is not null && r.HasCoordinates)
                {
                    var km = MauiLocation.CalculateDistance(_lastUserLocation.Latitude, _lastUserLocation.Longitude, r.Latitude, r.Longitude, DistanceUnits.Kilometers);
                    score += Math.Max(0, 1.2 - Math.Min(40, km) / 25d);
                }

                return new { Result = r, Score = score };
            })
            .OrderByDescending(x => x.Score)
            .Select(x => x.Result)
            .Take(8)
            .ToList();
    }

    private static bool TryParseCoordinateInput(string query, out SearchPlaceResult result)
    {
        result = null!;
        var segments = query.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        if (!double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return false;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return false;
        }

        result = new SearchPlaceResult
        {
            Name = "Toa do",
            Address = $"{lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}",
            Latitude = lat,
            Longitude = lon,
            Importance = 0.6
        };

        return true;
    }

    private void BindSearchResults(List<SearchPlaceResult> results, bool keepVisible)
    {
        _searchResults.Clear();
        _searchResults.AddRange(results);
        SearchResultsView.ItemsSource = null;
        SearchResultsView.ItemsSource = _searchResults;
        SearchResultsView.IsVisible = keepVisible && _searchResults.Count > 0;
    }

    private void HideSearchResults() => SearchResultsView.IsVisible = false;

    private Task DrawSearchResultAsync(SearchPlaceResult destination)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RemovePin(_searchPin);
            _searchPin = new Pin
            {
                Label = destination.Name,
                Address = destination.Address,
                Type = PinType.SearchResult,
                Location = new MauiLocation(destination.Latitude, destination.Longitude)
            };
            PoiMap.Pins.Add(_searchPin);
            CenterSearchPinInVisibleMap(destination);
        });

        return Task.CompletedTask;
    }

    private void CenterSearchPinInVisibleMap(SearchPlaceResult destination)
    {
        UpdateMapMarginBySheet();
        MoveMapTo(destination.Latitude, destination.Longitude, 1.0);
    }

    private void MoveMapTo(double latitude, double longitude, double radiusKm)
    {
        PoiMap.MoveToRegion(MapSpan.FromCenterAndRadius(
            new MauiLocation(latitude, longitude),
            Distance.FromKilometers(radiusKm)));
    }

    private static Pin CreatePoiPin(PoiViewModel poi)
    {
        return new Pin
        {
            Label = poi.Name,
            Address = $"Ban kinh {Math.Round(poi.RadiusMeters)} m",
            Type = PinType.Place,
            Location = new MauiLocation(poi.Latitude, poi.Longitude)
        };
    }

    private void RemovePin(Pin? pin)
    {
        if (pin is not null && PoiMap.Pins.Contains(pin))
        {
            PoiMap.Pins.Remove(pin);
        }
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        EnsureBottomSheetLayout();
    }

    private void EnsureBottomSheetLayout()
    {
        if (Height <= 0 || SearchBottomSheet is null)
        {
            return;
        }

        var expandedHeight = Math.Max(300, Height * 0.92);
        var partialVisibleHeight = Math.Max(220, Height * 0.40);
        _sheetExpandedTranslation = 0;
        _sheetPartialTranslation = Math.Max(0, expandedHeight - partialVisibleHeight);
        _sheetHiddenTranslation = expandedHeight + 24;

        SearchBottomSheet.HeightRequest = expandedHeight;
        if (!_sheetInitialized)
        {
            SearchBottomSheet.TranslationY = _sheetHiddenTranslation;
            _sheetInitialized = true;
        }
        else
        {
            SearchBottomSheet.TranslationY = Math.Clamp(SearchBottomSheet.TranslationY, _sheetExpandedTranslation, _sheetHiddenTranslation);
        }

        UpdateMapMarginBySheet();
    }

    private async Task ShowSheetPartialAsync()
    {
        EnsureBottomSheetLayout();
        await AnimateBottomSheetToAsync(_sheetPartialTranslation, 220, Easing.CubicOut);
    }

    private async Task AnimateBottomSheetToAsync(double targetTranslation, uint duration, Easing easing)
    {
        targetTranslation = Math.Clamp(targetTranslation, _sheetExpandedTranslation, _sheetHiddenTranslation);
        await SearchBottomSheet.TranslateToAsync(0, targetTranslation, duration, easing);
        UpdateMapMarginBySheet();

        if (_selectedSearchResult is not null)
        {
            CenterSearchPinInVisibleMap(_selectedSearchResult);
        }
    }

    private void UpdateMapMarginBySheet()
    {
        if (!_sheetInitialized)
        {
            PoiMap.Margin = new Thickness(0);
            return;
        }

        var visibleHeight = Math.Max(0, SearchBottomSheet.Height - SearchBottomSheet.TranslationY);
        PoiMap.Margin = new Thickness(0, 0, 0, visibleHeight);
    }

    private void OnBottomSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (!_sheetInitialized)
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _sheetPanStartTranslation = SearchBottomSheet.TranslationY;
                break;
            case GestureStatus.Running:
                var next = Math.Clamp(_sheetPanStartTranslation + e.TotalY, _sheetExpandedTranslation, _sheetHiddenTranslation);
                SearchBottomSheet.TranslationY = next;
                UpdateMapMarginBySheet();
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _ = SnapBottomSheetAsync();
                break;
        }
    }

    private async Task SnapBottomSheetAsync()
    {
        var current = SearchBottomSheet.TranslationY;
        var closeThreshold = _sheetPartialTranslation + ((_sheetHiddenTranslation - _sheetPartialTranslation) * 0.45);
        var expandThreshold = _sheetPartialTranslation * 0.45;

        if (current >= closeThreshold)
        {
            await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 170, Easing.CubicIn);
            return;
        }

        if (current <= expandThreshold)
        {
            await AnimateBottomSheetToAsync(_sheetExpandedTranslation, 170, Easing.CubicOut);
            return;
        }

        await AnimateBottomSheetToAsync(_sheetPartialTranslation, 170, Easing.CubicOut);
    }

    private void UpdateBottomSheetContent(SearchPlaceResult result)
    {
        SheetTitleLabel.Text = result.Name;
        SheetAddressLabel.Text = string.IsNullOrWhiteSpace(_lastRouteSummary)
            ? result.Address
            : $"{result.Address}\n{_lastRouteSummary}";
        SheetImage.Source = string.IsNullOrWhiteSpace(result.ImageUrl)
            ? ImageSource.FromFile("dotnet_bot.png")
            : ImageSource.FromUri(new Uri(result.ImageUrl));
    }

    private async void OnDirectionsClicked(object? sender, EventArgs e)
    {
        if (_selectedSearchResult is null)
        {
            return;
        }

        if (!_selectedSearchResult.HasCoordinates)
        {
            await DisplayAlertAsync("Thong bao", "Dia diem nay chua co toa do hop le.", "OK");
            return;
        }

        var destination = new MauiLocation(_selectedSearchResult.Latitude, _selectedSearchResult.Longitude);
        if (_lastUserLocation is not null)
        {
            var route = await QueryGoogleDirectionsAsync(_lastUserLocation, destination, CancellationToken.None);
            if (route is not null && route.Path.Count > 1)
            {
                DrawRouteOnMap(route);
                _lastRouteSummary = $"{route.DistanceText} - {route.DurationText}";
                UpdateBottomSheetContent(_selectedSearchResult);
            }
        }

        var lat = _selectedSearchResult.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = _selectedSearchResult.Longitude.ToString(CultureInfo.InvariantCulture);

        var mapsUrl = _lastUserLocation is null
            ? $"https://www.google.com/maps/dir/?api=1&destination={lat},{lon}&travelmode=driving"
            : $"https://www.google.com/maps/dir/?api=1&origin={_lastUserLocation.Latitude.ToString(CultureInfo.InvariantCulture)},{_lastUserLocation.Longitude.ToString(CultureInfo.InvariantCulture)}&destination={lat},{lon}&travelmode=driving";

        await Launcher.Default.OpenAsync(mapsUrl);
    }

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        if (_selectedSearchResult is null)
        {
            return;
        }

        await DisplayAlertAsync("Da luu", $"Da luu {_selectedSearchResult.Name}.", "OK");
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_selectedSearchResult is null)
        {
            return;
        }

        var lat = _selectedSearchResult.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = _selectedSearchResult.Longitude.ToString(CultureInfo.InvariantCulture);
        var text = $"{_selectedSearchResult.Name}\n{_selectedSearchResult.Address}\nhttps://www.google.com/maps/search/?api=1&query={lat},{lon}";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = "Chia se dia diem",
            Text = text
        });
    }

    private async Task<DirectionsResult?> QueryGoogleDirectionsAsync(MauiLocation origin, MauiLocation destination, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"origin={origin.Latitude.ToString(CultureInfo.InvariantCulture)},{origin.Longitude.ToString(CultureInfo.InvariantCulture)}",
            $"destination={destination.Latitude.ToString(CultureInfo.InvariantCulture)},{destination.Longitude.ToString(CultureInfo.InvariantCulture)}",
            "mode=driving",
            "language=vi",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        var url = $"https://maps.googleapis.com/maps/api/directions/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("routes", out var routesNode) || routesNode.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var routeNode = routesNode.EnumerateArray().FirstOrDefault();
        if (!routeNode.TryGetProperty("overview_polyline", out var polyNode)
            || !polyNode.TryGetProperty("points", out var pointsNode))
        {
            return null;
        }

        var path = DecodeGooglePolyline(pointsNode.GetString() ?? string.Empty);
        if (path.Count < 2)
        {
            return null;
        }

        var distance = "--";
        var duration = "--";
        if (routeNode.TryGetProperty("legs", out var legsNode) && legsNode.ValueKind == JsonValueKind.Array)
        {
            var leg = legsNode.EnumerateArray().FirstOrDefault();
            if (leg.TryGetProperty("distance", out var dNode) && dNode.TryGetProperty("text", out var dText))
            {
                distance = dText.GetString() ?? distance;
            }

            if (leg.TryGetProperty("duration", out var tNode) && tNode.TryGetProperty("text", out var tText))
            {
                duration = tText.GetString() ?? duration;
            }
        }

        return new DirectionsResult
        {
            DistanceText = distance,
            DurationText = duration,
            Path = path
        };
    }

    private static List<MauiLocation> DecodeGooglePolyline(string encoded)
    {
        var points = new List<MauiLocation>();
        if (string.IsNullOrEmpty(encoded))
        {
            return points;
        }

        var index = 0;
        var lat = 0;
        var lng = 0;

        while (index < encoded.Length)
        {
            var result = 0;
            var shift = 0;
            int b;
            do
            {
                if (index >= encoded.Length) return points;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            lat += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);

            result = 0;
            shift = 0;
            do
            {
                if (index >= encoded.Length) return points;
                b = encoded[index++] - 63;
                result |= (b & 0x1f) << shift;
                shift += 5;
            } while (b >= 0x20);
            lng += (result & 1) != 0 ? ~(result >> 1) : (result >> 1);

            points.Add(new MauiLocation(lat / 1E5, lng / 1E5));
        }

        return points;
    }

    private void DrawRouteOnMap(DirectionsResult route)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ClearRoute();

            var polyline = new Polyline
            {
                StrokeColor = Color.FromArgb("#1D4ED8"),
                StrokeWidth = 6
            };

            foreach (var point in route.Path)
            {
                polyline.Geopath.Add(point);
            }

            PoiMap.MapElements.Add(polyline);
            _routePolyline = polyline;

            var south = route.Path.Min(p => p.Latitude);
            var north = route.Path.Max(p => p.Latitude);
            var west = route.Path.Min(p => p.Longitude);
            var east = route.Path.Max(p => p.Longitude);
            MoveMapToBounds(south, west, north, east);
        });
    }

    private void MoveMapToBounds(double south, double west, double north, double east)
    {
        var center = new MauiLocation((south + north) / 2d, (west + east) / 2d);
        var northEast = new MauiLocation(north, east);
        var southWest = new MauiLocation(south, west);

        var r1 = MauiLocation.CalculateDistance(center.Latitude, center.Longitude, northEast.Latitude, northEast.Longitude, DistanceUnits.Kilometers);
        var r2 = MauiLocation.CalculateDistance(center.Latitude, center.Longitude, southWest.Latitude, southWest.Longitude, DistanceUnits.Kilometers);
        var radius = Math.Max(0.8, Math.Max(r1, r2) * 1.25);

        PoiMap.MoveToRegion(MapSpan.FromCenterAndRadius(center, Distance.FromKilometers(radius)));
    }

    private void ClearRoute()
    {
        if (_routePolyline is null)
        {
            return;
        }

        if (PoiMap.MapElements.Contains(_routePolyline))
        {
            PoiMap.MapElements.Remove(_routePolyline);
        }

        _routePolyline = null;
    }
}
