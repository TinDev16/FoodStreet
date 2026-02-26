using FoodStreetMobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Maps;
using Microsoft.Maui.Media;
using System.Globalization;
using System.ComponentModel;
using System.Net;
using System.Net.Http;
using System.Text;
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

    private sealed class TtsSegment
    {
        public required string Text { get; init; }
        public required int WordCount { get; init; }
        public required int EndWordIndex { get; init; }
    }

    private const string GoogleMapsApiKey = "AIzaSyAg9cHLgybrf3Edkl8ZK9nuRuQpF9nzCNY";
    private const double DefaultLatitude = 10.762011;
    private const double DefaultLongitude =  106.703465;
    private const double DefaultZoomRadiusKm = 0.08;
    private const double VinhKhanhLatitude = 10.759312;
    private const double VinhKhanhLongitude = 106.703836;
    private const double VinhKhanhZoomRadiusKm = 0.55;
    private const double VinhKhanhZoneRadiusMeters = 320;
    private const double TtsSecondsPerWord = 0.33;
    private static readonly HttpClient HttpClient = new();

    private readonly MainViewModel _viewModel;
    private readonly List<SearchPlaceResult> _searchResults = new();

    private bool _hasCenteredOnUser;
    private bool _isLocationSetupDone;
    private MauiLocation? _lastUserLocation;
    private SearchPlaceResult? _selectedSearchResult;
    private PoiViewModel? _selectedPoi;
    private CancellationTokenSource? _searchTypingCts;
    private string _lastSearchQuery = string.Empty;
    private readonly Dictionary<Pin, PoiViewModel> _poiPins = new();
    private Pin? _activePoiPin;
    private Pin? _searchPin;
    private Polyline? _routePolyline;
    private string? _lastRouteSummary;
    private Circle? _foodZoneCircle;
    private CancellationTokenSource? _statusBannerCts;
    private readonly List<Circle> _poiRadiusCircles = new();
    private readonly List<string> _ttsWords = new();
    private readonly List<TtsSegment> _ttsSegments = new();
    private CancellationTokenSource? _ttsCts;
    private IDispatcherTimer? _ttsTimer;
    private int _ttsWordIndex;
    private int _ttsSegmentIndex;
    private double _ttsElapsedSeconds;
    private bool _ttsIsPlaying;
    private bool _isTtsSeeking;
    private Locale? _preferredTtsLocale;

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
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged += OnPageSizeChanged;
        InitializeTtsPlayer();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureBottomSheetLayout();
        await EnsureUserLocationEnabledAsync();
        await _viewModel.InitializeAsync();
        _ = ShowStatusBannerForOneSecondAsync();

        if (_viewModel.Pois.Count == 0)
        {
            await DisplayAlertAsync(
                "Chua co POI",
                $"{_viewModel.StatusText}\nNeu ban dung Android emulator, thu HOST = http://10.0.2.2:5187",
                "OK");
        }
    }

    private void OnPoisLoaded(IReadOnlyList<PoiViewModel> pois)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            DetachPoiPinEvents();
            PoiMap.IsShowingUser = true;
            PoiMap.Pins.Clear();
            ClearRoute();
            ClearPoiRadiusCircles();
            if (_foodZoneCircle is not null && PoiMap.MapElements.Contains(_foodZoneCircle))
            {
                PoiMap.MapElements.Remove(_foodZoneCircle);
                _foodZoneCircle = null;
            }

            _activePoiPin = null;
            _searchPin = null;
            _selectedPoi = null;
            _selectedSearchResult = null;
            _lastRouteSummary = null;
            PlayAudioButton.IsEnabled = false;
            HideAudioPlayer();

            foreach (var poi in pois)
            {
                AddPoiRadiusCircle(poi);
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
                Label = $"{active.Name} (Đang gần)",
                Address = $"{active.Latitude.ToString(CultureInfo.InvariantCulture)}, {active.Longitude.ToString(CultureInfo.InvariantCulture)}",
                Type = PinType.Place,
                Location = new MauiLocation(active.Latitude, active.Longitude)
            };
            _activePoiPin.MarkerClicked += OnPoiPinClicked;
            _poiPins[_activePoiPin] = active;
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
                await DisplayAlertAsync("Thông báo", "Cần cấp quyền vị trí để hiển thị vị trí hiện tại.", "OK");
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
        var clickableElement = sender as VisualElement;
        if (clickableElement is not null)
        {
            clickableElement.IsEnabled = false;
        }

        try
        {
            await SearchPlaceAsync(forceSelection: true);
        }
        finally
        {
            if (clickableElement is not null)
            {
                clickableElement.IsEnabled = true;
            }
        }
    }

    private async void OnConfigureHostClicked(object? sender, EventArgs e)
    {
        var current = _viewModel.GetConfiguredBaseUrls();
        var input = await DisplayPromptAsync(
            "Backend HOST",
            "Nhap 1 hoac nhieu URL, cach nhau boi dau ';'. Vi du: http://10.0.2.2:5187;http://localhost:5187",
            "Luu",
            "Huy",
            "http://10.0.2.2:5187",
            -1,
            Keyboard.Url,
            current);

        if (input is null)
        {
            return;
        }

        _viewModel.SetConfiguredBaseUrls(input);
        await _viewModel.RefreshFromServerAsync();

        if (_viewModel.Pois.Count == 0)
        {
            await DisplayAlertAsync("Chua co POI", _viewModel.StatusText, "OK");
        }
    }

    private async void OnGoToVinhKhanhClicked(object? sender, EventArgs e)
    {
        if (sender is Button jumpButton)
        {
            jumpButton.IsEnabled = false;
        }

        try
        {
            await _viewModel.RefreshFromServerAsync();
            if (_viewModel.Pois.Count == 0)
            {
                await DisplayAlertAsync("Chua co POI", _viewModel.StatusText, "OK");
            }

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

            _selectedPoi = null;
            _selectedSearchResult = null;
            _lastRouteSummary = null;
            ClearRoute();
            await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 140, Easing.CubicIn);
            MoveMapTo(_lastUserLocation.Latitude, _lastUserLocation.Longitude, 0.1);
        }
        finally
        {
            if (sender is Button button)
            {
                button.IsEnabled = true;
            }
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
                await DisplayAlertAsync("Thông báo", "Chưa lấy được vị trí hiện tại.", "OK");
                return;
            }

            MoveMapTo(_lastUserLocation.Latitude, _lastUserLocation.Longitude, 0.1);
        }
        catch
        {
            await DisplayAlertAsync("Thông báo", "Không thể xác định vị trí hiện tại.", "OK");
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
            await DisplayAlertAsync("Thông báo", "Hãy nhập địa điểm cần tìm.", "OK");
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
                await DisplayAlertAsync("Thông báo", "Không tìm thấy địa điểm.", "OK");
                return;
            }

            if (forceSelection)
            {
                await SelectSearchResultAsync(results[0]);
            }
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Không thể tìm địa điểm: {ex.Message}", "OK");
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
            await DisplayAlertAsync("Thông báo", "Không thể lấy tọa độ cho địa điểm này.", "OK");
            return;
        }

        _selectedSearchResult = resolved;
        _selectedPoi = null;
        _lastRouteSummary = null;
        ClearRoute();
        PlayAudioButton.IsEnabled = false;

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
            mainText = description.Split(',').FirstOrDefault()?.Trim() ?? "Địa điểm";
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

        var name = address.Split(',').FirstOrDefault()?.Trim() ?? "Địa điểm";
        result = new SearchPlaceResult
        {
            Name = name,
            Address = string.IsNullOrWhiteSpace(address) ? "Không có địa chỉ" : address,
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

        var name = item.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "Địa điểm" : "Địa điểm";
        var address = item.TryGetProperty("formatted_address", out var addressNode) ? addressNode.GetString() ?? "Không có địa chỉ" : "Không có địa chỉ";

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

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.StatusText))
        {
            _ = ShowStatusBannerForOneSecondAsync();
        }
    }

    private async Task ShowStatusBannerForOneSecondAsync()
    {
        _statusBannerCts?.Cancel();
        _statusBannerCts?.Dispose();

        var cts = new CancellationTokenSource();
        _statusBannerCts = cts;

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            StatusBanner.IsVisible = !string.IsNullOrWhiteSpace(_viewModel.StatusText);
            StatusBanner.Opacity = 1;
        });

        try
        {
            await Task.Delay(1000, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await StatusBanner.FadeToAsync(0, 150, Easing.CubicOut);
                StatusBanner.IsVisible = false;
                StatusBanner.Opacity = 1;
            });
        }
        catch (TaskCanceledException)
        {
            // Ignore cancellation when status updates rapidly.
        }
    }

    private void DrawVinhKhanhFoodZone()
    {
        if (_foodZoneCircle is not null && PoiMap.MapElements.Contains(_foodZoneCircle))
        {
            PoiMap.MapElements.Remove(_foodZoneCircle);
        }

        _foodZoneCircle = new Circle
        {
            Center = new MauiLocation(VinhKhanhLatitude, VinhKhanhLongitude),
            Radius = new Distance(VinhKhanhZoneRadiusMeters),
            StrokeColor = Color.FromArgb("#EA580C"),
            StrokeWidth = 2,
            FillColor = Color.FromArgb("#33FB923C")
        };

        PoiMap.MapElements.Add(_foodZoneCircle);
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
            Name = "Tọa độ",
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

    private void MoveMapToPreserveZoom(double latitude, double longitude, double fallbackRadiusKm = 0.1)
    {
        var currentRadiusKm = PoiMap.VisibleRegion?.Radius.Kilometers;
        var radiusKm = currentRadiusKm.HasValue && currentRadiusKm.Value > 0
            ? currentRadiusKm.Value
            : fallbackRadiusKm;
        MoveMapTo(latitude, longitude, radiusKm);
    }

    private Pin CreatePoiPin(PoiViewModel poi)
    {
        var compactLabel = BuildCompactPoiLabel(poi.Name);
        var pin = new Pin
        {
            Label = compactLabel,
            Address = $"Bán kính {Math.Round(poi.RadiusMeters)} m",
            Type = PinType.SavedPin,
            Location = new MauiLocation(poi.Latitude, poi.Longitude)
        };
        pin.MarkerClicked += OnPoiPinClicked;
        _poiPins[pin] = poi;
        return pin;
    }

    private static string BuildCompactPoiLabel(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "POI";
        }

        var trimmed = name.Trim();
        return trimmed.Length <= 16 ? trimmed : $"{trimmed[..15]}…";
    }

    private void AddPoiRadiusCircle(PoiViewModel poi)
    {
        var circle = new Circle
        {
            Center = new MauiLocation(poi.Latitude, poi.Longitude),
            Radius = new Distance(Math.Max(8, poi.RadiusMeters)),
            StrokeColor = Color.FromArgb("#EA580C"),
            StrokeWidth = 1.5f,
            FillColor = Color.FromArgb("#1AF97316")
        };

        PoiMap.MapElements.Add(circle);
        _poiRadiusCircles.Add(circle);
    }

    private void ClearPoiRadiusCircles()
    {
        foreach (var circle in _poiRadiusCircles)
        {
            if (PoiMap.MapElements.Contains(circle))
            {
                PoiMap.MapElements.Remove(circle);
            }
        }

        _poiRadiusCircles.Clear();
    }

    private void DetachPoiPinEvents()
    {
        foreach (var pair in _poiPins)
        {
            pair.Key.MarkerClicked -= OnPoiPinClicked;
        }

        _poiPins.Clear();
    }

    private async void OnPoiPinClicked(object? sender, PinClickedEventArgs e)
    {
        if (sender is not Pin pin || !_poiPins.TryGetValue(pin, out var poi))
        {
            return;
        }

        e.HideInfoWindow = true;
        _selectedPoi = poi;
        _selectedSearchResult = null;
        _lastRouteSummary = null;
        ClearRoute();

        UpdateBottomSheetContent(poi);
        await ShowSheetPartialAsync();
        MoveMapToPreserveZoom(poi.Latitude, poi.Longitude, 0.6);
    }

    private void RemovePin(Pin? pin)
    {
        if (pin is null)
        {
            return;
        }

        pin.MarkerClicked -= OnPoiPinClicked;
        _poiPins.Remove(pin);

        if (PoiMap.Pins.Contains(pin))
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
            return;
        }

        if (_selectedPoi is not null)
        {
            MoveMapToPreserveZoom(_selectedPoi.Latitude, _selectedPoi.Longitude, 0.1);
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
            _selectedPoi = null;
            _selectedSearchResult = null;
            PlayAudioButton.IsEnabled = false;
            HideAudioPlayer();
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
        PlayAudioButton.IsEnabled = false;
        HideAudioPlayer();
        SheetTitleLabel.Text = result.Name;
        SheetAddressLabel.Text = string.IsNullOrWhiteSpace(_lastRouteSummary)
            ? result.Address
            : $"{result.Address}\n{_lastRouteSummary}";
        SheetImage.Source = string.IsNullOrWhiteSpace(result.ImageUrl)
            ? ImageSource.FromFile("dotnet_bot.png")
            : ImageSource.FromUri(new Uri(result.ImageUrl));
    }

    private void UpdateBottomSheetContent(PoiViewModel poi)
    {
        PlayAudioButton.IsEnabled = !string.IsNullOrWhiteSpace(poi.AudioUrl) || !string.IsNullOrWhiteSpace(poi.Narration);
        HideAudioPlayer();
        SheetTitleLabel.Text = poi.Name;
        var description = string.IsNullOrWhiteSpace(poi.Description) ? $"Ban kinh {Math.Round(poi.RadiusMeters)} m" : poi.Description;
        var distanceText = poi.DistanceMeters > 0 ? $"\nKhoang cach: {Math.Round(poi.DistanceMeters)} m" : string.Empty;
        SheetAddressLabel.Text = description + distanceText;
        SheetImage.Source = string.IsNullOrWhiteSpace(poi.ImageUrl)
            ? ImageSource.FromFile("dotnet_bot.png")
            : ImageSource.FromUri(new Uri(poi.ImageUrl));
    }

    private async void OnDirectionsClicked(object? sender, EventArgs e)
    {
        if (_selectedPoi is not null)
        {
            var poiDestination = new MauiLocation(_selectedPoi.Latitude, _selectedPoi.Longitude);
            if (_lastUserLocation is not null)
            {
                var route = await QueryGoogleDirectionsAsync(_lastUserLocation, poiDestination, CancellationToken.None);
                if (route is not null && route.Path.Count > 1)
                {
                    DrawRouteOnMap(route);
                    _lastRouteSummary = $"{route.DistanceText} - {route.DurationText}";
                    UpdateBottomSheetContent(_selectedPoi);
                }
            }

            var poiLat = _selectedPoi.Latitude.ToString(CultureInfo.InvariantCulture);
            var poiLon = _selectedPoi.Longitude.ToString(CultureInfo.InvariantCulture);
            var poiMapsUrl = _lastUserLocation is null
                ? $"https://www.google.com/maps/dir/?api=1&destination={poiLat},{poiLon}&travelmode=driving"
                : $"https://www.google.com/maps/dir/?api=1&origin={_lastUserLocation.Latitude.ToString(CultureInfo.InvariantCulture)},{_lastUserLocation.Longitude.ToString(CultureInfo.InvariantCulture)}&destination={poiLat},{poiLon}&travelmode=driving";

            await Launcher.Default.OpenAsync(poiMapsUrl);
            return;
        }

        if (_selectedSearchResult is null)
        {
            return;
        }

        if (!_selectedSearchResult.HasCoordinates)
        {
            await DisplayAlertAsync("Thông báo", "Địa điểm này chưa có tọa độ hợp lệ.", "OK");
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
        if (_selectedPoi is not null)
        {
            var poiAction = await DisplayActionSheetAsync("POI", "Huy", null, "Luu/Cap nhat", "Xoa");
            if (poiAction == "Luu/Cap nhat")
            {
                var savedPoi = await _viewModel.SaveShopFromMapAsync(
                    _selectedPoi.Name,
                    _selectedPoi.Latitude,
                    _selectedPoi.Longitude,
                    _selectedPoi.Description,
                    _selectedPoi.Id);

                if (savedPoi)
                {
                    await DisplayAlertAsync("Da luu", $"Da dong bo {_selectedPoi.Name} len web va app.", "OK");
                    return;
                }

                await DisplayAlertAsync("Loi", "Khong ket noi duoc web admin de luu POI.", "OK");
                return;
            }

            if (poiAction == "Xoa")
            {
                var deletedPoi = await _viewModel.DeleteShopFromMapAsync(
                    _selectedPoi.Name,
                    _selectedPoi.Latitude,
                    _selectedPoi.Longitude,
                    _selectedPoi.Id);

                if (deletedPoi)
                {
                    await DisplayAlertAsync("Da xoa", $"Da dong bo xoa {_selectedPoi.Name}.", "OK");
                    _selectedPoi = null;
                    PlayAudioButton.IsEnabled = false;
                    await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 140, Easing.CubicIn);
                    return;
                }

                await DisplayAlertAsync("Loi", "Khong ket noi duoc web admin de xoa POI.", "OK");
            }

            return;
        }

        if (_selectedSearchResult is null)
        {
            return;
        }

        var action = await DisplayActionSheetAsync("POI", "Huy", null, "Luu/Cap nhat", "Xoa");
        if (action == "Luu/Cap nhat")
        {
            var saved = await _viewModel.SaveShopFromMapAsync(
                _selectedSearchResult.Name,
                _selectedSearchResult.Latitude,
                _selectedSearchResult.Longitude,
                _selectedSearchResult.Address);

            if (saved)
            {
                await DisplayAlertAsync("Da luu", $"Da dong bo {_selectedSearchResult.Name} len web va app.", "OK");
                return;
            }

            await DisplayAlertAsync("Loi", "Khong ket noi duoc web admin de luu POI.", "OK");
            return;
        }

        if (action == "Xoa")
        {
            var deleted = await _viewModel.DeleteShopFromMapAsync(
                _selectedSearchResult.Name,
                _selectedSearchResult.Latitude,
                _selectedSearchResult.Longitude);

            if (deleted)
            {
                await DisplayAlertAsync("Da xoa", $"Da dong bo xoa {_selectedSearchResult.Name}.", "OK");
                return;
            }

            await DisplayAlertAsync("Loi", "Khong ket noi duoc web admin de xoa POI.", "OK");
        }
    }

    private async void OnPlayAudioClicked(object? sender, EventArgs e)
    {
        if (_selectedPoi is null)
        {
            await DisplayAlertAsync("Thong bao", "Hay chon POI de phat audio.", "OK");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_selectedPoi.AudioUrl))
        {
            if (!Uri.TryCreate(_selectedPoi.AudioUrl, UriKind.Absolute, out var audioUri))
            {
                await DisplayAlertAsync("Thong bao", "Audio URL cua POI khong hop le.", "OK");
                return;
            }

            ShowAudioPlayerHtml(BuildAudioPlayerHtml(audioUri.ToString()));
            return;
        }

        if (!string.IsNullOrWhiteSpace(_selectedPoi.Narration))
        {
            StartTtsPlayer(_selectedPoi.Narration);
            return;
        }

        await DisplayAlertAsync("Thong bao", "POI nay chua co audio hoac noi dung thuyet minh.", "OK");
    }

    private void ShowAudioPlayerHtml(string html)
    {
        StopTtsPlayback();
        TtsPlayerContainer.IsVisible = false;
        AudioPlayerContainer.IsVisible = true;
        AudioPlayerWebView.Source = new HtmlWebViewSource
        {
            Html = html
        };
    }

    private void HideAudioPlayer()
    {
        StopTtsPlayback();
        TtsPlayerContainer.IsVisible = false;
        TtsPlayPauseButton.Text = "▶";
        TtsProgressSlider.Value = 0;
        TtsProgressSlider.Maximum = 1;
        TtsCurrentTimeLabel.Text = "00:00";
        TtsDurationLabel.Text = "00:00";

        AudioPlayerContainer.IsVisible = false;
        AudioPlayerWebView.Source = null;
    }

    private void InitializeTtsPlayer()
    {
        _ttsTimer = Dispatcher.CreateTimer();
        _ttsTimer.Interval = TimeSpan.FromMilliseconds(300);
        _ttsTimer.Tick += (_, _) =>
        {
            if (!_ttsIsPlaying || _isTtsSeeking)
            {
                return;
            }

            _ttsElapsedSeconds = Math.Min(TtsProgressSlider.Maximum, _ttsElapsedSeconds + 0.3);
            RefreshTtsUi();
        };
    }

    private void StartTtsPlayer(string narration)
    {
        HideAudioPlayer();
        BuildTtsSegments(narration);

        _ttsWordIndex = 0;
        _ttsSegmentIndex = 0;
        _ttsElapsedSeconds = 0;
        TtsProgressSlider.Maximum = Math.Max(1, _ttsWords.Count * TtsSecondsPerWord);
        TtsProgressSlider.Value = 0;
        TtsPlayerContainer.IsVisible = true;
        TtsVolumeSlider.Value = Math.Clamp(TtsVolumeSlider.Value, 0, 1);
        RefreshTtsUi();
        _ = ResumeTtsPlaybackAsync();
    }

    private async Task ResumeTtsPlaybackAsync()
    {
        if (_ttsIsPlaying || _ttsWords.Count == 0 || _ttsWordIndex >= _ttsWords.Count)
        {
            return;
        }

        _ttsCts = new CancellationTokenSource();
        var token = _ttsCts.Token;
        _ttsIsPlaying = true;
        TtsPlayPauseButton.Text = "⏸";
        _ttsTimer?.Start();
        var locale = await ResolvePreferredTtsLocaleAsync();

        try
        {
            RecalculateTtsSegmentIndex();
            while (_ttsSegmentIndex < _ttsSegments.Count && !token.IsCancellationRequested)
            {
                var segment = _ttsSegments[_ttsSegmentIndex];
                var options = new SpeechOptions
                {
                    Volume = (float)Math.Clamp(TtsVolumeSlider.Value, 0, 1),
                    Pitch = 1.0f,
                    Rate = 1.1f,
                    Locale = locale
                };

                await TextToSpeech.Default.SpeakAsync(segment.Text, options, token);
                _ttsWordIndex = segment.EndWordIndex;
                _ttsSegmentIndex++;
                _ttsElapsedSeconds = Math.Min(TtsProgressSlider.Maximum, _ttsWordIndex * TtsSecondsPerWord);
                RefreshTtsUi();
            }
        }
        catch (OperationCanceledException)
        {
            // Pause/seek/stop.
        }
        finally
        {
            _ttsIsPlaying = false;
            _ttsTimer?.Stop();
            TtsPlayPauseButton.Text = "▶";
            if (_ttsWordIndex >= _ttsWords.Count)
            {
                _ttsElapsedSeconds = TtsProgressSlider.Maximum;
                RefreshTtsUi();
            }
        }
    }

    private void StopTtsPlayback()
    {
        _ttsCts?.Cancel();
        _ttsCts?.Dispose();
        _ttsCts = null;
        _ttsIsPlaying = false;
        _ttsTimer?.Stop();
    }

    private void RefreshTtsUi()
    {
        if (!_isTtsSeeking)
        {
            TtsProgressSlider.Value = Math.Clamp(_ttsElapsedSeconds, 0, TtsProgressSlider.Maximum);
        }

        TtsCurrentTimeLabel.Text = FormatTime(_ttsElapsedSeconds);
        TtsDurationLabel.Text = FormatTime(TtsProgressSlider.Maximum);
    }

    private void BuildTtsSegments(string narration)
    {
        _ttsWords.Clear();
        _ttsSegments.Clear();

        if (string.IsNullOrWhiteSpace(narration))
        {
            return;
        }

        _ttsWords.AddRange(narration.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        var sb = new StringBuilder();
        var runningWordCount = 0;
        foreach (var ch in narration)
        {
            sb.Append(ch);
            if (!IsTtsBoundary(ch))
            {
                continue;
            }

            if (TryAddTtsSegment(sb.ToString(), ref runningWordCount))
            {
                sb.Clear();
            }
        }

        if (sb.Length > 0)
        {
            TryAddTtsSegment(sb.ToString(), ref runningWordCount);
        }
    }

    private static bool IsTtsBoundary(char ch)
    {
        return ch == '.' || ch == '\n' || ch == '\r';
    }

    private bool TryAddTtsSegment(string raw, ref int runningWordCount)
    {
        var text = raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0)
        {
            return false;
        }

        runningWordCount += words.Length;
        _ttsSegments.Add(new TtsSegment
        {
            Text = text,
            WordCount = words.Length,
            EndWordIndex = runningWordCount
        });
        return true;
    }

    private void RecalculateTtsSegmentIndex()
    {
        _ttsSegmentIndex = 0;
        while (_ttsSegmentIndex < _ttsSegments.Count && _ttsSegments[_ttsSegmentIndex].EndWordIndex <= _ttsWordIndex)
        {
            _ttsSegmentIndex++;
        }
    }

    private static string FormatTime(double seconds)
    {
        var total = Math.Max(0, (int)Math.Floor(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    private async Task<Locale?> ResolvePreferredTtsLocaleAsync()
    {
        if (_preferredTtsLocale is not null)
        {
            return _preferredTtsLocale;
        }

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            _preferredTtsLocale = locales
                .FirstOrDefault(l => string.Equals(l.Language, "vi", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(l.Country, "VN", StringComparison.OrdinalIgnoreCase))
                ?? locales.FirstOrDefault(l => string.Equals(l.Language, "vi", StringComparison.OrdinalIgnoreCase))
                ?? locales.FirstOrDefault();
        }
        catch
        {
            _preferredTtsLocale = null;
        }

        return _preferredTtsLocale;
    }

    private async void OnTtsPlayPauseClicked(object? sender, EventArgs e)
    {
        if (_ttsWords.Count == 0)
        {
            return;
        }

        if (_ttsIsPlaying)
        {
            StopTtsPlayback();
            return;
        }

        if (_ttsWordIndex >= _ttsWords.Count)
        {
            _ttsWordIndex = 0;
            _ttsElapsedSeconds = 0;
            RefreshTtsUi();
        }

        await ResumeTtsPlaybackAsync();
    }

    private async void OnTtsSkipBackwardClicked(object? sender, EventArgs e)
    {
        await SeekTtsByOffsetAsync(-10);
    }

    private async void OnTtsSkipForwardClicked(object? sender, EventArgs e)
    {
        await SeekTtsByOffsetAsync(10);
    }

    private async Task SeekTtsByOffsetAsync(double seconds)
    {
        if (_ttsWords.Count == 0)
        {
            return;
        }

        var nextSeconds = Math.Clamp(_ttsElapsedSeconds + seconds, 0, TtsProgressSlider.Maximum);
        _ttsElapsedSeconds = nextSeconds;
        _ttsWordIndex = Math.Clamp((int)Math.Round(nextSeconds / TtsSecondsPerWord), 0, _ttsWords.Count);
        RefreshTtsUi();

        if (_ttsIsPlaying)
        {
            StopTtsPlayback();
            await ResumeTtsPlaybackAsync();
        }
    }

    private void OnTtsProgressDragStarted(object? sender, EventArgs e)
    {
        _isTtsSeeking = true;
    }

    private async void OnTtsProgressDragCompleted(object? sender, EventArgs e)
    {
        _isTtsSeeking = false;
        _ttsElapsedSeconds = TtsProgressSlider.Value;
        _ttsWordIndex = Math.Clamp((int)Math.Round(_ttsElapsedSeconds / TtsSecondsPerWord), 0, _ttsWords.Count);
        RefreshTtsUi();

        if (_ttsIsPlaying)
        {
            StopTtsPlayback();
            await ResumeTtsPlaybackAsync();
        }
    }

    private void OnTtsProgressValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (!_isTtsSeeking)
        {
            return;
        }

        TtsCurrentTimeLabel.Text = FormatTime(e.NewValue);
    }

    private static string BuildAudioPlayerHtml(string audioUrl)
    {
        var safeUrl = WebUtility.HtmlEncode(audioUrl);
        var html = """
<!DOCTYPE html>
<html>
<head>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <style>
    body {{ margin:0; padding:10px; font-family: Arial, sans-serif; background:#FFF7ED; color:#7C2D12; }}
    .controls {{ display:grid; grid-template-columns: 1fr 1fr 1fr; gap:8px; margin-bottom:8px; }}
    button {{ border:0; border-radius:10px; padding:8px 6px; font-size:12px; background:#FDE7D7; color:#7C2D12; }}
    .play {{ background:#E07A5F; color:#fff; font-weight:700; }}
    .row {{ display:grid; grid-template-columns: 42px 1fr 42px; gap:8px; align-items:center; font-size:11px; }}
    input[type=range] {{ width:100%; }}
    .vol {{ display:grid; grid-template-columns: 70px 1fr; gap:8px; align-items:center; margin-top:6px; font-size:12px; }}
  </style>
</head>
<body>
  <audio id="audio" preload="metadata" src="__AUDIO_URL__"></audio>
  <div class="controls">
    <button onclick="skip(-10)">⏪ 10s</button>
    <button id="playPause" class="play" onclick="toggle()">▶</button>
    <button onclick="skip(10)">10s ⏩</button>
  </div>
  <div class="row">
    <span id="cur">00:00</span>
    <input id="progress" type="range" min="0" max="1" step="0.1" value="0" />
    <span id="dur">00:00</span>
  </div>
  <div class="vol">
    <span>Âm lượng</span>
    <input id="volume" type="range" min="0" max="1" step="0.01" value="0.8" />
  </div>
  <script>
    const a = document.getElementById('audio');
    const btn = document.getElementById('playPause');
    const cur = document.getElementById('cur');
    const dur = document.getElementById('dur');
    const progress = document.getElementById('progress');
    const volume = document.getElementById('volume');

    const fmt = (s) => {{
      if (!isFinite(s)) return '00:00';
      const m = Math.floor(s / 60);
      const ss = Math.floor(s % 60);
      return String(m).padStart(2, '0') + ':' + String(ss).padStart(2, '0');
    }};

    function toggle() {{
      if (a.paused) {{
        a.play();
      }} else {{
        a.pause();
      }}
    }}

    function skip(delta) {{
      a.currentTime = Math.max(0, Math.min((a.duration || 0), a.currentTime + delta));
    }}

    a.addEventListener('loadedmetadata', () => {{
      progress.max = Math.max(1, a.duration || 1);
      dur.textContent = fmt(a.duration || 0);
      a.volume = parseFloat(volume.value);
      a.play().catch(() => {{ }});
    }});

    a.addEventListener('play', () => btn.textContent = '⏸');
    a.addEventListener('pause', () => btn.textContent = '▶');
    a.addEventListener('ended', () => btn.textContent = '▶');
    a.addEventListener('timeupdate', () => {{
      progress.value = a.currentTime || 0;
      cur.textContent = fmt(a.currentTime || 0);
    }});

    progress.addEventListener('input', () => {{
      a.currentTime = parseFloat(progress.value);
      cur.textContent = fmt(a.currentTime || 0);
    }});

    volume.addEventListener('input', () => {{
      a.volume = parseFloat(volume.value);
    }});
  </script>
</body>
</html>
""";
        return html.Replace("__AUDIO_URL__", safeUrl);
    }

    private static string BuildTtsPlayerHtml(string narration)
    {
        var safeJsonText = JsonSerializer.Serialize(narration);
        var html = """
<!DOCTYPE html>
<html>
<head>
  <meta name="viewport" content="width=device-width, initial-scale=1.0"/>
  <style>
    body { margin:0; padding:10px; font-family: Arial, sans-serif; background:#FFF7ED; color:#7C2D12; }
    .controls { display:grid; grid-template-columns: 1fr 1fr 1fr; gap:8px; margin-bottom:8px; }
    button { border:0; border-radius:10px; padding:8px 6px; font-size:12px; background:#FDE7D7; color:#7C2D12; }
    .play { background:#E07A5F; color:#fff; font-weight:700; }
    .row { display:grid; grid-template-columns: 42px 1fr 42px; gap:8px; align-items:center; font-size:11px; }
    input[type=range] { width:100%; }
    .vol { display:grid; grid-template-columns: 70px 1fr; gap:8px; align-items:center; margin-top:6px; font-size:12px; }
    .note { margin-top:6px; font-size:10px; opacity:.75; }
  </style>
</head>
<body>
  <div class="controls">
    <button onclick="skip(-10)">⏪ 10s</button>
    <button id="playPause" class="play" onclick="toggle()">▶</button>
    <button onclick="skip(10)">10s ⏩</button>
  </div>
  <div class="row">
    <span id="cur">00:00</span>
    <input id="progress" type="range" min="0" max="1" step="0.1" value="0" />
    <span id="dur">00:00</span>
  </div>
  <div class="vol">
    <span>Âm lượng</span>
    <input id="volume" type="range" min="0" max="1" step="0.01" value="0.8" />
  </div>
  <div class="note">TTS player (mô phỏng tiến trình)</div>
  <script>
    const fullText = __TTS_TEXT_JSON__;
    const words = (fullText || '').trim().split(/\s+/).filter(Boolean);
    const secPerWord = 0.42;
    const skipWords = Math.round(10 / secPerWord);
    const totalSeconds = Math.max(1, words.length * secPerWord);

    const btn = document.getElementById('playPause');
    const cur = document.getElementById('cur');
    const dur = document.getElementById('dur');
    const progress = document.getElementById('progress');
    const volume = document.getElementById('volume');

    let utterance = null;
    let playing = false;
    let paused = false;
    let currentWord = 0;
    let segmentStartWord = 0;
    let ticker = null;

    const fmt = (s) => {
      const v = Math.max(0, Math.floor(s || 0));
      const m = Math.floor(v / 60);
      const ss = v % 60;
      return String(m).padStart(2, '0') + ':' + String(ss).padStart(2, '0');
    };

    function refreshUi() {
      const seconds = Math.min(totalSeconds, currentWord * secPerWord);
      progress.max = totalSeconds;
      progress.value = seconds;
      cur.textContent = fmt(seconds);
      dur.textContent = fmt(totalSeconds);
      btn.textContent = paused || !playing ? '▶' : '⏸';
    }

    function stopTicker() {
      if (ticker) { clearInterval(ticker); ticker = null; }
    }

    function startTicker() {
      stopTicker();
      ticker = setInterval(() => {
        if (!playing || paused) return;
        currentWord = Math.min(words.length, currentWord + 1);
        refreshUi();
      }, Math.max(120, secPerWord * 1000));
    }

    function playFromCurrent() {
      if (!('speechSynthesis' in window) || words.length === 0) return;
      speechSynthesis.cancel();
      const slice = words.slice(currentWord).join(' ');
      if (!slice) { playing = false; paused = false; refreshUi(); return; }
      utterance = new SpeechSynthesisUtterance(slice);
      utterance.lang = 'vi-VN';
      utterance.volume = parseFloat(volume.value || '0.8');
      segmentStartWord = currentWord;
      utterance.onend = () => { playing = false; paused = false; currentWord = words.length; stopTicker(); refreshUi(); };
      playing = true;
      paused = false;
      speechSynthesis.speak(utterance);
      startTicker();
      refreshUi();
    }

    function toggle() {
      if (!('speechSynthesis' in window) || words.length === 0) return;
      if (!playing) { playFromCurrent(); return; }
      if (!paused) { speechSynthesis.pause(); paused = true; refreshUi(); return; }
      speechSynthesis.resume(); paused = false; refreshUi();
    }

    function skip(sec) {
      const step = Math.round(Math.abs(sec) / secPerWord);
      currentWord = sec < 0 ? Math.max(0, currentWord - step) : Math.min(words.length, currentWord + step);
      if (playing) { playFromCurrent(); } else { refreshUi(); }
    }

    progress.addEventListener('input', () => {
      const sec = parseFloat(progress.value || '0');
      currentWord = Math.min(words.length, Math.max(0, Math.round(sec / secPerWord)));
      if (playing) { playFromCurrent(); } else { refreshUi(); }
    });

    volume.addEventListener('input', () => {
      if (utterance) {
        // Apply volume by restarting the current segment.
        if (playing) playFromCurrent();
      }
    });

    window.addEventListener('beforeunload', () => {
      stopTicker();
      if ('speechSynthesis' in window) speechSynthesis.cancel();
    });

    refreshUi();
    playFromCurrent();
  </script>
</body>
</html>
""";
        return html.Replace("__TTS_TEXT_JSON__", safeJsonText);
    }

    private async void OnShareClicked(object? sender, EventArgs e)
    {
        if (_selectedPoi is not null)
        {
            var poiLat = _selectedPoi.Latitude.ToString(CultureInfo.InvariantCulture);
            var poiLon = _selectedPoi.Longitude.ToString(CultureInfo.InvariantCulture);
            var poiText = $"{_selectedPoi.Name}\n{_selectedPoi.Description}\nhttps://www.google.com/maps/search/?api=1&query={poiLat},{poiLon}";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Title = "Chia se dia diem",
                Text = poiText
            });
            return;
        }

        if (_selectedSearchResult is null)
        {
            return;
        }

        var lat = _selectedSearchResult.Latitude.ToString(CultureInfo.InvariantCulture);
        var lon = _selectedSearchResult.Longitude.ToString(CultureInfo.InvariantCulture);
        var text = $"{_selectedSearchResult.Name}\n{_selectedSearchResult.Address}\nhttps://www.google.com/maps/search/?api=1&query={lat},{lon}";

        await Share.Default.RequestAsync(new ShareTextRequest
        {
            Title = "Chia sẻ địa điểm",
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
        var radius = Math.Max(0.1, Math.Max(r1, r2) * 1.25);

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





