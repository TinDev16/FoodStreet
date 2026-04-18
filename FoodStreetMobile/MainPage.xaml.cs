using FoodStreetMobile.ViewModels;
using FoodStreetMobile.Services;
using FoodStreetMobile.Models;
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
using System.Collections.ObjectModel;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;
#if ANDROID
using AndroidMarker = Android.Gms.Maps.Model.Marker;
using AndroidBitmapDescriptor = Android.Gms.Maps.Model.BitmapDescriptor;
using AndroidBitmapDescriptorFactory = Android.Gms.Maps.Model.BitmapDescriptorFactory;
#endif

namespace FoodStreetMobile;

public partial class MainPage : ContentPage
{
    private const string PlayEmoji = "▶️";
    private const string PauseEmoji = "⏸️";

    private enum PlaybackSourceKind
    {
        None = 0,
        AudioWeb = 1,
        TtsNative = 2,
        TtsWeb = 3
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
    private const double DefaultMapZoomKm = 0.1;
    private const double VinhKhanhLatitude = 10.759312;
    private const double VinhKhanhLongitude = 106.703836;
    private const double VinhKhanhZoneRadiusMeters = 320;
    private const double TtsSecondsPerWord = 0.33;
    private static readonly HttpClient HttpClient = new();

    private readonly MainViewModel _viewModel;
    private readonly PlaceSearchService _placeSearchService;
    private readonly DeepLinkService _deepLinkService;
    private readonly PoiViewHistoryService _poiViewHistoryService;
    private readonly ObservableCollection<SearchPlaceResult> _searchResults = new();

    private bool _isLocationSetupDone;
    private MauiLocation? _lastUserLocation;
    private SearchPlaceResult? _selectedSearchResult;
    private PoiViewModel? _selectedPoi;
    private CancellationTokenSource? _searchTypingCts;
    private string _lastSearchQuery = string.Empty;
    private readonly Dictionary<Pin, PoiViewModel> _poiPins = new();
#if ANDROID
    private readonly Dictionary<string, AndroidBitmapDescriptor> _androidPoiIconCache = new(StringComparer.Ordinal);
#endif
    private Pin? _activePoiPin;
    private Pin? _searchPin;
    private Polyline? _routePolyline;
    private string? _lastRouteSummary;
    private Circle? _foodZoneCircle;
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
    private string? _preferredTtsLocaleLanguage;
    private PoiViewModel? _currentPlaybackPoi;
    private PoiViewModel? _queuedPlaybackPoi;
    private PlaybackSourceKind _currentPlaybackSource = PlaybackSourceKind.None;
    private double _currentPlaybackElapsedSeconds;
    private double _currentPlaybackDurationSeconds = 1;
    private bool _isAdvancingQueue;
    private bool _isAutoPlaySubscriptionActive;
    private bool _isSearchSelectionActive;

    private bool _sheetInitialized;
    private double _sheetExpandedTranslation;
    private double _sheetPartialTranslation;
    private double _sheetHiddenTranslation;
    private double _sheetPanStartTranslation;
    private double _lastAppliedMapBottomMargin = -1;
    private long _lastMapMarginUpdateTicks;
    private const double MapMarginUpdateThresholdPx = 3;
    private const int MapMarginUpdateMinIntervalMs = 16;

    private readonly PoiSyncService _poiSyncService;

    public MainPage(MainViewModel viewModel, PlaceSearchService placeSearchService, DeepLinkService deepLinkService, PoiViewHistoryService poiViewHistoryService, PoiSyncService poiSyncService)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _placeSearchService = placeSearchService;
        _deepLinkService = deepLinkService;
        _poiViewHistoryService = poiViewHistoryService;
        _poiSyncService = poiSyncService;
        BindingContext = _viewModel;
        SearchResultsView.ItemsSource = _searchResults;
        PlaceSearchEntry.TextChanged += OnPlaceSearchTextChanged;

        _viewModel.PoisLoaded += OnPoisLoaded;
        _viewModel.ActivePoiChanged += OnActivePoiChanged;
        _viewModel.UserLocationChanged += OnUserLocationChanged;
        _deepLinkService.PendingPoiLinkQueued += OnPendingPoiLinkQueued;
        _deepLinkService.PendingPlaceSelectionQueued += OnPendingPlaceSelectionQueued;
        SizeChanged += OnPageSizeChanged;
        InitializeTtsPlayer();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        EnsureAutoPlaySubscription(isEnabled: true);
        try
        {
            EnsureBottomSheetLayout();
            await EnsureUserLocationEnabledAsync();
            await _viewModel.InitializeAsync();
            await TryOpenPendingDeepLinkAsync();
            await TryOpenPendingPlaceSelectionAsync();

            if (_viewModel.Pois.Count == 0)
            {
                await DisplayAlertAsync(
                    "Chua co POI",
                    "Khong co du lieu POI. Neu ban dung Android emulator, thu HOST = http://10.0.2.2:5187",
                    "OK");
            }
        }
        catch (Exception ex)
        {
            Services.CrashLogger.Write("MainPage.OnAppearing", ex);
            try
            {
                await DisplayAlertAsync("Loi", ex.Message, "OK");
            }
            catch
            {
            }
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
            _isSearchSelectionActive = false;
            _lastRouteSummary = null;
            PlayAudioButton.IsEnabled = false;
            ResetPlaybackQueueState();
            HideAudioPlayer();

            foreach (var poi in pois)
            {
                AddPoiRadiusCircle(poi);
                PoiMap.Pins.Add(CreatePoiPin(poi));
            }

#if ANDROID
            _ = ApplyAndroidPoiMarkerUiAsync();
#endif

            if (pois.Count > 0)
            {
                var south = pois.Min(p => p.Latitude);
                var north = pois.Max(p => p.Latitude);
                var west = pois.Min(p => p.Longitude);
                var east = pois.Max(p => p.Longitude);
                MoveMapToBounds(south, west, north, east);
            }
            else
            {
                MoveMapTo(DefaultLatitude, DefaultLongitude);
            }
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
                Label = string.IsNullOrWhiteSpace(active.Name)
                    ? "POI"
                    : active.Name.Trim(),
                Address = $"{active.Latitude.ToString(CultureInfo.InvariantCulture)}, {active.Longitude.ToString(CultureInfo.InvariantCulture)}",
                Type = PinType.Place,
                Location = new MauiLocation(active.Latitude, active.Longitude)
            };
            _activePoiPin.MarkerClicked += OnPoiPinClicked;
            _poiPins[_activePoiPin] = active;
            PoiMap.Pins.Add(_activePoiPin);

#if ANDROID
            _ = ApplyAndroidPoiMarkerUiAsync();
#endif
        });
    }

    private void OnUserLocationChanged(MauiLocation location)
    {
        _lastUserLocation = location;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            PoiMap.IsShowingUser = true;
        });
    }

    private void OnAutoPlayPoiRequested(PoiViewModel poi)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (ShouldDeferAutoPlaybackBecauseUserIsViewingSearch())
            {
                await RequestAutoPlaybackAsync(poi);
                return;
            }

            _isSearchSelectionActive = false;
            _selectedPoi = poi;
            _selectedSearchResult = null;
            _lastRouteSummary = null;
            await RecordPoiViewAsync(poi);
            PlayAudioButton.IsEnabled = HasPlayableAudio(poi);
            UpdateBottomSheetContent(poi, resetPlayerState: false);
            await ShowSheetPartialAsync();
            await RequestAutoPlaybackAsync(poi);
        });
    }

    private async Task RecordPoiViewAsync(PoiViewModel poi)
    {
        try
        {
            await _poiViewHistoryService.RecordViewedAsync(PoiViewHistoryService.GuestUserId, poi.Id, poi.Name, poi.ImageUrl);
            #pragma warning disable CS4014
            _poiSyncService.TrackActivityAsync("view_poi", poi.Id, poi.Language);
            #pragma warning restore CS4014
        }
        catch
        {
        }
    }

    private void EnsureAutoPlaySubscription(bool isEnabled)
    {
        if (isEnabled)
        {
            if (_isAutoPlaySubscriptionActive)
            {
                return;
            }

            _viewModel.AutoPlayPoiRequested += OnAutoPlayPoiRequested;
            _isAutoPlaySubscriptionActive = true;
            return;
        }

        if (!_isAutoPlaySubscriptionActive)
        {
            return;
        }

        _viewModel.AutoPlayPoiRequested -= OnAutoPlayPoiRequested;
        _isAutoPlaySubscriptionActive = false;
    }

    private async Task TryOpenPendingDeepLinkAsync()
    {
        if (!_deepLinkService.TryTakePendingPoiLink(out var pending) || pending is null)
        {
            return;
        }

        PoiViewModel? poi = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            poi = _viewModel.Pois.FirstOrDefault(x => string.Equals(x.Id, pending.PoiId, StringComparison.OrdinalIgnoreCase));
            if (poi is not null)
            {
                break;
            }

            await _viewModel.RefreshFromServerAsync();
        }

        if (poi is null)
        {
            await DisplayAlertAsync("Thong bao", $"Khong tim thay POI id={pending.PoiId}.", "OK");
            return;
        }

        _isSearchSelectionActive = false;
        _selectedPoi = poi;
        _selectedSearchResult = null;
        _lastRouteSummary = null;
        ClearRoute();
        await RecordPoiViewAsync(poi);
        PlayAudioButton.IsEnabled = HasPlayableAudio(poi);
        UpdateBottomSheetContent(poi, resetPlayerState: false);
        MoveMapToPreserveZoom(poi.Latitude, poi.Longitude);
        await ShowSheetExpandedAsync();
    }

    private void OnPendingPoiLinkQueued()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await TryOpenPendingDeepLinkAsync();
            }
            catch
            {
            }
        });
    }

    private async Task TryOpenPendingPlaceSelectionAsync()
    {
        if (!_deepLinkService.TryTakePendingPlaceSelection(out var pending) || pending is null)
        {
            return;
        }

        _isSearchSelectionActive = true;
        if (!string.IsNullOrWhiteSpace(pending.PoiId))
        {
            var poi = _viewModel.Pois.FirstOrDefault(x => string.Equals(x.Id, pending.PoiId, StringComparison.OrdinalIgnoreCase));
            if (poi is not null)
            {
                _selectedPoi = poi;
                _selectedSearchResult = null;
                _lastRouteSummary = null;
                ClearRoute();
                await RecordPoiViewAsync(poi);
                PlayAudioButton.IsEnabled = HasPlayableAudio(poi);
                UpdateBottomSheetContent(poi, resetPlayerState: false);
                MoveMapToPreserveZoom(poi.Latitude, poi.Longitude);
                await ShowSheetExpandedAsync();
                return;
            }
        }

        var selected = new SearchPlaceResult
        {
            Name = pending.Name,
            Address = pending.Address,
            Latitude = pending.Latitude,
            Longitude = pending.Longitude,
            ImageUrl = pending.ImageUrl,
            PlaceId = pending.PlaceId,
            PoiId = pending.PoiId,
            Importance = 1
        };

        await SelectSearchResultAsync(selected);
    }

    private void OnPendingPlaceSelectionQueued()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await TryOpenPendingPlaceSelectionAsync();
            }
            catch
            {
            }
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
                await DisplayAlertAsync("Th?ng b?o", "C?n c?p quy?n v? tr? d? hi?n th? v? tr? hi?n t?i.", "OK");
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
            await SearchPlaceAsync();
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
            await DisplayAlertAsync("Chua co POI", "Khong co du lieu POI tu may chu.", "OK");
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
                await DisplayAlertAsync("Chua co POI", "Khong co du lieu POI tu may chu.", "OK");
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
            MoveMapTo(_lastUserLocation.Latitude, _lastUserLocation.Longitude);
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
                await DisplayAlertAsync("Th?ng b?o", "Chua l?y du?c v? tr? hi?n t?i.", "OK");
                return;
            }

            MoveMapTo(_lastUserLocation.Latitude, _lastUserLocation.Longitude);
        }
        catch
        {
            await DisplayAlertAsync("Th?ng b?o", "Kh?ng th? x?c d?nh v? tr? hi?n t?i.", "OK");
        }
        finally
        {
            if (locateButton is not null)
            {
                locateButton.IsEnabled = true;
            }
        }
    }


    private void OnPlaceSearchTextChanged(object? sender, Microsoft.Maui.Controls.TextChangedEventArgs e)
    {
        _ = HandlePlaceSearchTextChangedAsync(e.NewTextValue);
    }

    private async Task HandlePlaceSearchTextChangedAsync(string? newTextValue)
    {
        _searchTypingCts?.Cancel();
        _searchTypingCts?.Dispose();

        var query = newTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            HideSearchResults();
            SetSearchUiState(isLoading: false, errorText: null);
            return;
        }

        var cts = new CancellationTokenSource();
        _searchTypingCts = cts;
        SetSearchUiState(isLoading: true, errorText: null);

        try
        {
            await Task.Delay(220, cts.Token);
            var results = await SearchPlacesAsync(query, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            BindSearchResults(results, keepVisible: true);
            SetSearchUiState(isLoading: false, errorText: results.Count == 0 ? "Khong tim thay ket qua." : null);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation while typing.
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                CrashLogger.Write("MainPage.OnPlaceSearchTextChanged", ex);
                HideSearchResults();
                SetSearchUiState(isLoading: false, errorText: "Khong the tim kiem luc nay.");
            }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        EnsureAutoPlaySubscription(isEnabled: false);
    }

    private async void OnPlaceSearchCompleted(object? sender, EventArgs e)
    {
        await SearchPlaceAsync();
    }

    private async Task SearchPlaceAsync()
    {
        var query = PlaceSearchEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            await DisplayAlertAsync("Thông báo", "Hãy nhập địa điểm cần tìm.", "OK");
            return;
        }

        SetSearchUiState(isLoading: true, errorText: null);

        try
        {
            var results = await SearchPlacesAsync(query, CancellationToken.None);

            // Always show full result list — user selects from the list.
            BindSearchResults(results, keepVisible: true);
            SetSearchUiState(isLoading: false, errorText: results.Count == 0 ? "Không tìm thấy kết quả." : null);

            if (results.Count == 0)
            {
                await DisplayAlertAsync("Thông báo", "Không tìm thấy địa điểm.", "OK");
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("MainPage.SearchPlaceAsync", ex);
            SetSearchUiState(isLoading: false, errorText: "Không thể tìm kiếm lúc này.");
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
            await DisplayAlertAsync("Th?ng b?o", "Kh?ng th? l?y t?a d? cho d?a di?m n?y.", "OK");
            return;
        }

        _isSearchSelectionActive = true;
        _selectedSearchResult = resolved;
        _selectedPoi = null;
        _lastRouteSummary = null;
        ClearRoute();
        PlayAudioButton.IsEnabled = false;

        PlaceSearchEntry.Text = resolved.Name;
        PlaceSearchEntry.Unfocus();
        HideSearchResults();
        SetSearchUiState(isLoading: false, errorText: null);

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

        return await _placeSearchService.ResolveAsync(result, _lastUserLocation, CancellationToken.None);
    }

    private async Task<List<SearchPlaceResult>> SearchPlacesAsync(string query, CancellationToken cancellationToken)
    {
        var poiCandidates = _viewModel.Pois
            .Select(p => new PoiSearchCandidate
            {
                PoiId = p.Id,
                Name = p.Name,
                Address = p.Description,
                Latitude = p.Latitude,
                Longitude = p.Longitude,
                ImageUrl = NormalizeRemoteImageUrl(p.ImageUrl)
            })
            .ToList();

        var list = await _placeSearchService.SearchAsync(
            query,
            _lastUserLocation,
            maxResults: 6,
            poiCandidates,
            cancellationToken);

        _lastSearchQuery = query;
        return list;
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
    private void BindSearchResults(List<SearchPlaceResult> results, bool keepVisible)
    {
        try
        {
            _searchResults.Clear();
            foreach (var item in results)
            {
                _searchResults.Add(item);
            }

            SearchResultsView.IsVisible = keepVisible && _searchResults.Count > 0;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("MainPage.BindSearchResults", ex);
            _searchResults.Clear();
            SearchResultsView.IsVisible = false;
        }
    }

    private void HideSearchResults() => SearchResultsView.IsVisible = false;

    private void SetSearchUiState(bool isLoading, string? errorText)
    {
        SearchStatusLayout.IsVisible = false;
        SearchLoadingIndicator.IsRunning = false;
        SearchErrorLabel.Text = string.Empty;
        SearchErrorLabel.IsVisible = false;
    }

    private static string? NormalizeRemoteImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }

    private static ImageSource? BuildSafeImageSource(string? raw)
    {
        var normalized = NormalizeRemoteImageUrl(raw);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return ImageSource.FromUri(new Uri(normalized));
    }

    private Task DrawSearchResultAsync(SearchPlaceResult destination)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            RemovePin(_searchPin);
            _searchPin = new Pin
            {
                Label = destination.Name,
                Address = destination.Address,
                Type = PinType.Place,
                Location = new MauiLocation(destination.Latitude, destination.Longitude)
            };
            PoiMap.Pins.Add(_searchPin);
            CenterSearchPinInVisibleMap(destination);
        });

        return Task.CompletedTask;
    }

    private void CenterSearchPinInVisibleMap(SearchPlaceResult destination)
    {
        UpdateMapMarginBySheet(force: true);
        MoveMapTo(destination.Latitude, destination.Longitude);
    }

    private void MoveMapTo(double latitude, double longitude)
    {
        PoiMap.MoveToRegion(MapSpan.FromCenterAndRadius(
            new MauiLocation(latitude, longitude),
            Distance.FromKilometers(DefaultMapZoomKm)));
    }

    private void MoveMapToPreserveZoom(double latitude, double longitude)
    {
        MoveMapTo(latitude, longitude);
    }

    private Pin CreatePoiPin(PoiViewModel poi)
    {
        var pin = new Pin
        {
            Label = string.IsNullOrWhiteSpace(poi.Name) ? "POI" : poi.Name.Trim(),
            Address = $"B?n k?nh {Math.Round(poi.RadiusMeters)} m",
            Type = PinType.Place,
            Location = new MauiLocation(poi.Latitude, poi.Longitude)
        };
        pin.MarkerClicked += OnPoiPinClicked;
        _poiPins[pin] = poi;
        return pin;
    }

#if ANDROID
    private async Task ApplyAndroidPoiMarkerUiAsync()
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            var allReady = true;
            var pinPairs = _poiPins.ToArray();
            if (pinPairs.Length == 0)
            {
                return;
            }

            foreach (var pair in pinPairs)
            {
                var pin = pair.Key;
                if (pin.MarkerId is null)
                {
                    allReady = false;
                    continue;
                }

                var label = BuildCompactPoiName(pin.Label);
                var isActivePoiPin = ReferenceEquals(pin, _activePoiPin);
                var iconCacheKey = isActivePoiPin ? $"active::{label}" : $"normal::{label}";
                if (!_androidPoiIconCache.TryGetValue(iconCacheKey, out var icon))
                {
                    icon = BuildAndroidPoiMarkerIcon(label, isActivePoiPin);
                    _androidPoiIconCache[iconCacheKey] = icon;
                }

                var iconApplied = TryApplyAndroidMarkerIcon(pin.MarkerId, icon);
                var titleApplied = TryApplyAndroidMarkerTitle(pin.MarkerId, pin.Label);
                if (!iconApplied)
                {
                    allReady = false;
                }

                if (!titleApplied)
                {
                    allReady = false;
                }
            }

            if (allReady)
            {
                return;
            }

            await Task.Delay(180);
        }
    }

    private static bool TryApplyAndroidMarkerIcon(object markerId, AndroidBitmapDescriptor icon)
    {
        try
        {
            if (markerId is AndroidMarker marker)
            {
                marker.SetIcon(icon);
                marker.SetAnchor(0.1f, 0.5f);
                return true;
            }

            var type = markerId.GetType();
            var setIcon = type.GetMethod("SetIcon", new[] { icon.GetType() })
                ?? type.GetMethod("SetIcon");
            var setAnchor = type.GetMethod("SetAnchor", new[] { typeof(float), typeof(float) });
            if (setIcon is null || setAnchor is null)
            {
                return false;
            }

            setIcon.Invoke(markerId, new object[] { icon });
            setAnchor.Invoke(markerId, new object[] { 0.5f, 1f });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryApplyAndroidMarkerTitle(object markerId, string? title)
    {
        try
        {
            if (markerId is AndroidMarker marker)
            {
                marker.Title = title ?? "POI";
                marker.HideInfoWindow();
                marker.ShowInfoWindow();
                return true;
            }

            var type = markerId.GetType();
            var setTitle = type.GetMethod("SetTitle", new[] { typeof(string) });
            if (setTitle is null)
            {
                return false;
            }

            setTitle.Invoke(markerId, new object[] { title ?? "POI" });
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildCompactPoiName(string? name)
    {
        var trimmed = string.IsNullOrWhiteSpace(name) ? "POI" : name.Trim();
        return trimmed.Length <= 20 ? trimmed : $"{trimmed[..19]}?";
    }

    private static AndroidBitmapDescriptor BuildAndroidPoiMarkerIcon(string label, bool isHighlighted)
    {
        const float textSizePx = 24f;
        const float paddingHorizontalPx = 14f;
        const float paddingVerticalPx = 8f;
        const float gapPx = 8f;
        const float shopSizePx = 24f;

        var textPaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.ParseColor("#1F2937"),
            TextSize = textSizePx
        };
        var fm = textPaint.GetFontMetrics() ?? new Android.Graphics.Paint.FontMetrics();

        var textWidth = Math.Max(1f, textPaint.MeasureText(label));
        var textHeight = fm.Bottom - fm.Top;
        var bubbleWidth = (int)Math.Ceiling(textWidth + (paddingHorizontalPx * 2f));
        var bubbleHeight = (int)Math.Ceiling(textHeight + (paddingVerticalPx * 2f));
        var totalHeight = (int)Math.Ceiling(bubbleHeight + gapPx + shopSizePx);
        var totalWidth = Math.Max(bubbleWidth, (int)Math.Ceiling(shopSizePx + 8f));

        var bitmap = Android.Graphics.Bitmap.CreateBitmap(totalWidth, totalHeight, Android.Graphics.Bitmap.Config.Argb8888!);
        using var canvas = new Android.Graphics.Canvas(bitmap);

        var bubbleLeft = (totalWidth - bubbleWidth) / 2f;
        var bubbleTop = 0f;
        var bubbleRight = bubbleLeft + bubbleWidth;
        var bubbleBottom = bubbleTop + bubbleHeight;

        var bubblePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias) { Color = Android.Graphics.Color.ParseColor("#FFF7ED") };
        var strokePaint = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = isHighlighted
                ? Android.Graphics.Color.ParseColor("#2563EB")
                : Android.Graphics.Color.ParseColor("#FDBA74"),
            StrokeWidth = isHighlighted ? 3f : 2f
        };
        strokePaint.SetStyle(Android.Graphics.Paint.Style.Stroke);

        var rect = new Android.Graphics.RectF(bubbleLeft, bubbleTop, bubbleRight, bubbleBottom);
        canvas.DrawRoundRect(rect, 18f, 18f, bubblePaint);
        canvas.DrawRoundRect(rect, 18f, 18f, strokePaint);

        var textBaseline = bubbleTop + paddingVerticalPx - fm.Top;
        canvas.DrawText(label, bubbleLeft + paddingHorizontalPx, textBaseline, textPaint);

        var iconLeft = (totalWidth - shopSizePx) / 2f;
        var iconTop = bubbleBottom + gapPx;
        var iconRight = iconLeft + shopSizePx;
        var iconBottom = iconTop + shopSizePx;
        var iconRect = new Android.Graphics.RectF(iconLeft, iconTop, iconRight, iconBottom);

        var iconBackground = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.ParseColor("#EA580C")
        };
        canvas.DrawRoundRect(iconRect, 6f, 6f, iconBackground);

        var iconStroke = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.White,
            StrokeWidth = 2f
        };
        iconStroke.SetStyle(Android.Graphics.Paint.Style.Stroke);
        canvas.DrawRoundRect(iconRect, 6f, 6f, iconStroke);

        if (isHighlighted)
        {
            var activeIconStroke = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
            {
                Color = Android.Graphics.Color.ParseColor("#2563EB"),
                StrokeWidth = 3f
            };
            activeIconStroke.SetStyle(Android.Graphics.Paint.Style.Stroke);
            var activeIconRect = new Android.Graphics.RectF(iconLeft - 2f, iconTop - 2f, iconRight + 2f, iconBottom + 2f);
            canvas.DrawRoundRect(activeIconRect, 8f, 8f, activeIconStroke);
        }

        var shopLine = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.White,
            StrokeWidth = 1.8f
        };
        shopLine.SetStyle(Android.Graphics.Paint.Style.Stroke);

        var shopFill = new Android.Graphics.Paint(Android.Graphics.PaintFlags.AntiAlias)
        {
            Color = Android.Graphics.Color.White
        };

        var awningTop = iconTop + 5f;
        var awningBottom = awningTop + 4.5f;
        var awningRect = new Android.Graphics.RectF(iconLeft + 4.5f, awningTop, iconRight - 4.5f, awningBottom);
        canvas.DrawRect(awningRect, shopFill);

        var bodyTop = awningBottom + 2.2f;
        var bodyBottom = iconBottom - 4f;
        var bodyRect = new Android.Graphics.RectF(iconLeft + 6f, bodyTop, iconRight - 6f, bodyBottom);
        canvas.DrawRect(bodyRect, shopLine);

        var doorWidth = 3.8f;
        var doorRect = new Android.Graphics.RectF(
            (iconLeft + iconRight - doorWidth) / 2f,
            bodyBottom - 6f,
            (iconLeft + iconRight + doorWidth) / 2f,
            bodyBottom);
        canvas.DrawRect(doorRect, shopFill);

        return AndroidBitmapDescriptorFactory.FromBitmap(bitmap);
    }
#endif

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
        _isSearchSelectionActive = false;
        _selectedPoi = poi;
        _selectedSearchResult = null;
        _lastRouteSummary = null;
        ClearRoute();

        await RecordPoiViewAsync(poi);
        UpdateBottomSheetContent(poi);
        await ShowSheetPartialAsync();
        MoveMapToPreserveZoom(poi.Latitude, poi.Longitude);
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

    private async Task ShowSheetExpandedAsync()
    {
        EnsureBottomSheetLayout();
        await AnimateBottomSheetToAsync(_sheetExpandedTranslation, 220, Easing.CubicOut);
    }

    private async Task AnimateBottomSheetToAsync(double targetTranslation, uint duration, Easing easing)
    {
        targetTranslation = Math.Clamp(targetTranslation, _sheetExpandedTranslation, _sheetHiddenTranslation);
        await SearchBottomSheet.TranslateToAsync(0, targetTranslation, duration, easing);
        UpdateMapMarginBySheet(force: true);
    }

    private void UpdateMapMarginBySheet(bool force = false)
    {
        if (!_sheetInitialized)
        {
            if (force || _lastAppliedMapBottomMargin != 0)
            {
                PoiMap.Margin = new Thickness(0);
                _lastAppliedMapBottomMargin = 0;
            }

            return;
        }

        var visibleHeight = Math.Max(0, SearchBottomSheet.Height - SearchBottomSheet.TranslationY);
        if (!force)
        {
            var delta = Math.Abs(visibleHeight - _lastAppliedMapBottomMargin);
            var nowTicks = DateTime.UtcNow.Ticks;
            var minTicks = TimeSpan.FromMilliseconds(MapMarginUpdateMinIntervalMs).Ticks;
            if (delta < MapMarginUpdateThresholdPx || (nowTicks - _lastMapMarginUpdateTicks) < minTicks)
            {
                return;
            }

            _lastMapMarginUpdateTicks = nowTicks;
        }

        PoiMap.Margin = new Thickness(0, 0, 0, visibleHeight);
        _lastAppliedMapBottomMargin = visibleHeight;
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
            var queued = _queuedPlaybackPoi;
            var shouldKeepPartialForQueuedPlayback = _isSearchSelectionActive
                && _currentPlaybackPoi is null
                && queued is not null;

            _isSearchSelectionActive = false;
            _selectedPoi = null;
            _selectedSearchResult = null;
            PlayAudioButton.IsEnabled = false;
            if (_currentPlaybackPoi is null)
            {
                HideAudioPlayer(resetPlaybackQueueState: queued is null);
            }

            if (shouldKeepPartialForQueuedPlayback)
            {
                var queuedPoi = queued!;
                _selectedPoi = queuedPoi;
                PlayAudioButton.IsEnabled = HasPlayableAudio(queuedPoi);
                UpdateBottomSheetContent(queuedPoi, resetPlayerState: false);
                await AnimateBottomSheetToAsync(_sheetPartialTranslation, 170, Easing.CubicOut);
                await TryStartQueuedPlaybackIfIdleAsync();
                return;
            }

            await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 170, Easing.CubicIn);
            await TryStartQueuedPlaybackIfIdleAsync();
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
        PremiumInfoBorder.IsVisible = false;
        ResetBottomSheetPlayerUiForSelection();
        SheetTitleLabel.Text = result.Name;
        SheetAddressLabel.Text = string.IsNullOrWhiteSpace(_lastRouteSummary)
            ? result.Address
            : $"{result.Address}\n{_lastRouteSummary}";
        SheetImage.Source = string.IsNullOrWhiteSpace(result.ImageUrl)
            ? ImageSource.FromFile("dotnet_bot.png")
            : BuildSafeImageSource(result.ImageUrl) ?? "dotnet_bot.png";
    }

    private void UpdateBottomSheetContent(PoiViewModel poi, bool resetPlayerState = true)
    {
        var hasNarration = !string.IsNullOrWhiteSpace(ResolveNarrationForPlayback(poi));
        var hasAudioContent = !string.IsNullOrWhiteSpace(poi.AudioUrl) || hasNarration;
        PlayAudioButton.IsEnabled = hasAudioContent;
        PremiumInfoBorder.IsVisible = false;
        PremiumBadgeLabel.Text = "Premium";
        PremiumPriceLabel.Text = poi.Price <= 0 ? "Miễn phí" : $"{Math.Round(poi.Price).ToString("N0", CultureInfo.GetCultureInfo("vi-VN"))} đ";
        UnlockPoiButton.IsVisible = false;
        if (resetPlayerState)
        {
            if (IsSamePoi(poi, _currentPlaybackPoi))
            {
                RestoreBottomSheetPlayerUiForCurrentPlayback();
            }
            else
            {
                ResetBottomSheetPlayerUiForSelection();
            }
        }
        SheetTitleLabel.Text = poi.Name;
        var description = string.IsNullOrWhiteSpace(poi.Description)
            ? string.Format(
                CultureInfo.CurrentCulture,
                AppResources.ResourceManager.GetString("Main_RadiusFallbackMeters", CultureInfo.CurrentUICulture) ?? "Radius: {0} m",
                Math.Round(poi.RadiusMeters))
            : poi.Description;

        var distanceText = poi.DistanceMeters > 0
            ? "\n" + string.Format(
                CultureInfo.CurrentCulture,
                AppResources.ResourceManager.GetString("Main_DistanceMetersFormat", CultureInfo.CurrentUICulture) ?? "Distance: {0} m",
                Math.Round(poi.DistanceMeters))
            : string.Empty;
        SheetAddressLabel.Text = description + distanceText;
        SheetImage.Source = string.IsNullOrWhiteSpace(poi.ImageUrl)
            ? ImageSource.FromFile("dotnet_bot.png")
            : BuildSafeImageSource(poi.ImageUrl) ?? "dotnet_bot.png";
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
            await DisplayAlertAsync("Th?ng b?o", "??a di?m n?y chua c? t?a d? h?p l?.", "OK");
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

        if (!HasPlayableAudio(_selectedPoi))
        {
            await DisplayAlertAsync("Thong bao", "POI nay chua co audio hoac noi dung thuyet minh.", "OK");
            return;
        }
        await StartPoiPlaybackAsync(_selectedPoi, allowInterrupt: true);
    }

    private async void OnUnlockPoiClicked(object? sender, EventArgs e)
    {
        try
        {
            if (_selectedPoi is null)
            {
                return;
            }

            if (!_selectedPoi.IsPremiumLocked)
            {
                await DisplayAlertAsync("Thông báo", "POI đã được mở khóa.", "OK");
                return;
            }

            var unlockResult = await _viewModel.UnlockPoiAsync(_selectedPoi.Id);
            if (!unlockResult.Ok)
            {
                await DisplayAlertAsync("Lỗi", unlockResult.Message, "OK");
                return;
            }

            var refreshedPoi = await RefreshSelectedPoiStateAsync(_selectedPoi.Id);
            if (refreshedPoi is not null)
            {
                _selectedPoi = refreshedPoi;
                PlayAudioButton.IsEnabled = HasPlayableAudio(refreshedPoi);
                UpdateBottomSheetContent(refreshedPoi, resetPlayerState: false);
            }

            await DisplayAlertAsync("Thành công", unlockResult.Message, "OK");
        }
        catch (Exception ex)
        {
            await DisplayAlertAsync("Lỗi", $"Mở khóa thất bại: {ex.Message}", "OK");
        }
    }

    private async Task<PoiViewModel?> RefreshSelectedPoiStateAsync(string poiId)
    {
        if (string.IsNullOrWhiteSpace(poiId))
        {
            return null;
        }

        try
        {
            await _viewModel.RefreshFromServerAsync();
        }
        catch
        {
            // Keep silent here; playback flow already handles non-playable state gracefully.
        }

        var refreshed = _viewModel.Pois.FirstOrDefault(x => string.Equals(x.Id, poiId, StringComparison.Ordinal));
        if (refreshed is not null)
        {
            _selectedPoi = refreshed;
            PlayAudioButton.IsEnabled = HasPlayableAudio(refreshed);
            UpdateBottomSheetContent(refreshed, resetPlayerState: false);
        }

        return refreshed;
    }


    private IEnumerable<string> GetConfiguredBackendBaseUrls()
    {
        var raw = _viewModel.GetConfiguredBaseUrls();
        if (string.IsNullOrWhiteSpace(raw))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var parts = raw.Split([';', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out var uri))
            {
                continue;
            }

            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            {
                continue;
            }

            var normalized = uri.ToString().TrimEnd('/');
            if (seen.Add(normalized))
            {
                yield return normalized;
            }
        }
    }

    private async Task RequestAutoPlaybackAsync(PoiViewModel poi)
    {
        if (!HasPlayableAudio(poi))
        {
            return;
        }

        if (ShouldDeferAutoPlaybackBecauseUserIsViewingSearch() && _currentPlaybackPoi is null)
        {
            _queuedPlaybackPoi = poi;
            RefreshPlaybackQueuePanel();
            return;
        }

        if (IsSamePoi(_currentPlaybackPoi, poi))
        {
            RefreshPlaybackQueuePanel();
            return;
        }

        if (_currentPlaybackPoi is not null)
        {
            _queuedPlaybackPoi = poi;
            RefreshPlaybackQueuePanel();
            return;
        }

        await StartPoiPlaybackAsync(poi, allowInterrupt: false);
    }

    private bool ShouldDeferAutoPlaybackBecauseUserIsViewingSearch()
    {
        if (!_isSearchSelectionActive)
        {
            return false;
        }

        if (!_sheetInitialized)
        {
            return true;
        }

        return SearchBottomSheet.TranslationY < (_sheetHiddenTranslation - 1);
    }

    private async Task TryStartQueuedPlaybackIfIdleAsync()
    {
        if (_currentPlaybackPoi is not null || _queuedPlaybackPoi is null)
        {
            return;
        }

        var nextPoi = _queuedPlaybackPoi;
        _queuedPlaybackPoi = null;
        await StartPoiPlaybackAsync(nextPoi, allowInterrupt: true);
    }

    private async Task StartPoiPlaybackAsync(PoiViewModel poi, bool allowInterrupt)
    {
        if (!HasPlayableAudio(poi))
        {
            return;
        }

        var previousPlaybackPoi = _currentPlaybackPoi;
        if (allowInterrupt && _currentPlaybackPoi is not null && !IsSamePoi(_currentPlaybackPoi, poi))
        {
            StopCurrentPlaybackOnly();
            _queuedPlaybackPoi = null;
        }

        _currentPlaybackPoi = poi;
        _currentPlaybackElapsedSeconds = 0;
        _currentPlaybackDurationSeconds = 1;
        RefreshPlaybackQueuePanel();
        SyncBottomSheetToCurrentPlayback(previousPlaybackPoi, poi);

        #pragma warning disable CS4014
        _poiSyncService.TrackActivityAsync("play_audio", poi.Id, poi.Language);
        #pragma warning restore CS4014

        if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            if (!Uri.TryCreate(poi.AudioUrl, UriKind.Absolute, out var audioUri))
            {
                if (allowInterrupt)
                {
                    await DisplayAlertAsync("Thong bao", "Audio URL cua POI khong hop le.", "OK");
                }

                await HandleCurrentPlaybackCompletedAsync();
                return;
            }

            _currentPlaybackSource = PlaybackSourceKind.AudioWeb;
            ShowAudioPlayerHtml(BuildAudioPlayerHtml(audioUri.ToString()));
            return;
        }

        var narration = ResolveNarrationForPlayback(poi);
        if (string.IsNullOrWhiteSpace(narration))
        {
            await HandleCurrentPlaybackCompletedAsync();
            return;
        }

        _currentPlaybackSource = PlaybackSourceKind.TtsNative;
        await StartTtsPlayerAsync(narration);
    }

    private async Task HandleCurrentPlaybackCompletedAsync()
    {
        if (_isAdvancingQueue)
        {
            return;
        }

        _isAdvancingQueue = true;
        try
        {
            var finishedPoi = _currentPlaybackPoi;
            var wasViewingSearchPoi = finishedPoi is not null
                && _isSearchSelectionActive
                && _selectedSearchResult is null
                && IsSamePoi(_selectedPoi, finishedPoi);

            if (_queuedPlaybackPoi is null)
            {
                _currentPlaybackPoi = null;
                _currentPlaybackSource = PlaybackSourceKind.None;
                _currentPlaybackElapsedSeconds = 0;
                _currentPlaybackDurationSeconds = 1;
                RefreshPlaybackQueuePanel();

                if (wasViewingSearchPoi)
                {
                    _isSearchSelectionActive = false;
                    _selectedPoi = null;
                    _selectedSearchResult = null;
                    PlayAudioButton.IsEnabled = false;
                    await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 140, Easing.CubicIn);
                }
                return;
            }

            var nextPoi = _queuedPlaybackPoi;
            _queuedPlaybackPoi = null;
            RefreshPlaybackQueuePanel();

            if (wasViewingSearchPoi)
            {
                _isSearchSelectionActive = false;
                _selectedPoi = null;
                _selectedSearchResult = null;
                PlayAudioButton.IsEnabled = false;
                await AnimateBottomSheetToAsync(_sheetHiddenTranslation, 140, Easing.CubicIn);

                _selectedPoi = nextPoi;
                _selectedSearchResult = null;
                _lastRouteSummary = null;
                ClearRoute();
                PlayAudioButton.IsEnabled = HasPlayableAudio(nextPoi);
                UpdateBottomSheetContent(nextPoi, resetPlayerState: false);
                await ShowSheetPartialAsync();
            }
            await StartPoiPlaybackAsync(nextPoi, allowInterrupt: true);
        }
        finally
        {
            _isAdvancingQueue = false;
        }
    }

    private void SyncBottomSheetToCurrentPlayback(PoiViewModel? previousPlaybackPoi, PoiViewModel currentPlaybackPoi)
    {
        if (_selectedSearchResult is not null)
        {
            return;
        }

        if (_isSearchSelectionActive)
        {
            return;
        }

        if (!_sheetInitialized)
        {
            return;
        }

        var isSheetVisible = SearchBottomSheet.TranslationY < (_sheetHiddenTranslation - 1);
        if (!isSheetVisible)
        {
            return;
        }

        if (_selectedPoi is null)
        {
            return;
        }

        if (previousPlaybackPoi is null || !IsSamePoi(_selectedPoi, previousPlaybackPoi))
        {
            return;
        }

        if (IsSamePoi(_selectedPoi, currentPlaybackPoi))
        {
            return;
        }

        _selectedPoi = currentPlaybackPoi;
        PlayAudioButton.IsEnabled = HasPlayableAudio(currentPlaybackPoi);
        UpdateBottomSheetContent(currentPlaybackPoi, resetPlayerState: false);
    }

    private void ResetPlaybackQueueState()
    {
        _currentPlaybackPoi = null;
        _queuedPlaybackPoi = null;
        _currentPlaybackSource = PlaybackSourceKind.None;
        _currentPlaybackElapsedSeconds = 0;
        _currentPlaybackDurationSeconds = 1;
        RefreshPlaybackQueuePanel();
    }

    private void StopCurrentPlaybackOnly()
    {
        HideAudioPlayer(resetPlaybackQueueState: false, stopPlayback: true, clearWebSource: true);
    }

    private static bool HasPlayableAudio(PoiViewModel poi)
    {
        var narration = ResolveNarrationForPlayback(poi);
        return !string.IsNullOrWhiteSpace(poi.AudioUrl) || !string.IsNullOrWhiteSpace(narration);
    }

    private static bool IsSamePoi(PoiViewModel? left, PoiViewModel? right)
    {
        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.Id, right.Id, StringComparison.Ordinal);
    }

    private void RefreshPlaybackQueuePanel()
    {
        var displayPoi = _currentPlaybackPoi ?? _queuedPlaybackPoi;
        PlaybackQueuePanel.IsVisible = displayPoi is not null;
        if (displayPoi is null)
        {
            QueueInfoBorder.IsVisible = false;
            CurrentPlayingNameLabel.Text = "--";
            CurrentPlayingProgressBar.Progress = 0;
            CurrentPlayingTimeLabel.Text = "00:00";
            CurrentPlayingDurationLabel.Text = "00:00";
            return;
        }

        CurrentPlayingNameLabel.Text = displayPoi.Name;
        if (_currentPlaybackPoi is null)
        {
            CurrentPlayingProgressBar.Progress = 0;
            CurrentPlayingTimeLabel.Text = "00:00";
            CurrentPlayingDurationLabel.Text = "00:00";
        }
        else
        {
            var safeDuration = Math.Max(1, _currentPlaybackDurationSeconds);
            var progress = Math.Clamp(_currentPlaybackElapsedSeconds / safeDuration, 0, 1);
            CurrentPlayingProgressBar.Progress = progress;
            CurrentPlayingTimeLabel.Text = FormatTime(_currentPlaybackElapsedSeconds);
            CurrentPlayingDurationLabel.Text = FormatTime(safeDuration);
        }

        var hasNext = _currentPlaybackPoi is not null && _queuedPlaybackPoi is not null;
        QueueInfoBorder.IsVisible = hasNext;
        if (!hasNext)
        {
            QueuedPoiNameLabel.Text = "--";
            QueuedPoiDistanceLabel.IsVisible = false;
            QueuedPoiDistanceLabel.Text = string.Empty;
            return;
        }

        QueuedPoiNameLabel.Text = _queuedPlaybackPoi?.Name ?? string.Empty;
        if (_queuedPlaybackPoi?.DistanceMeters > 0)
        {
            QueuedPoiDistanceLabel.Text = $"{Math.Round(_queuedPlaybackPoi.DistanceMeters)} m";
            QueuedPoiDistanceLabel.IsVisible = true;
        }
        else
        {
            QueuedPoiDistanceLabel.IsVisible = false;
            QueuedPoiDistanceLabel.Text = string.Empty;
        }
    }

    private void UpdatePlaybackProgress(double elapsedSeconds, double durationSeconds)
    {
        _currentPlaybackElapsedSeconds = Math.Max(0, elapsedSeconds);
        _currentPlaybackDurationSeconds = Math.Max(1, durationSeconds);
        RefreshPlaybackQueuePanel();
    }

    private void ResetBottomSheetPlayerUiForSelection()
    {
        if (_currentPlaybackPoi is null)
        {
            HideAudioPlayer(resetPlaybackQueueState: false, stopPlayback: true, clearWebSource: true);
            return;
        }

        // There is active playback in background: clear current sheet player UI only
        // so a newly selected POI starts from a clean "not started" visual state.
        HideAudioPlayer(resetPlaybackQueueState: false, stopPlayback: false, clearWebSource: false);
    }

    private void RestoreBottomSheetPlayerUiForCurrentPlayback()
    {
        if (_currentPlaybackPoi is null)
        {
            ResetBottomSheetPlayerUiForSelection();
            return;
        }

        switch (_currentPlaybackSource)
        {
            case PlaybackSourceKind.TtsNative:
                AudioPlayerContainer.IsVisible = false;
                TtsPlayerContainer.IsVisible = true;
                TtsPlayPauseButton.Text = _ttsIsPlaying ? PauseEmoji : PlayEmoji;
                RefreshTtsUi();
                break;
            case PlaybackSourceKind.AudioWeb:
            case PlaybackSourceKind.TtsWeb:
                TtsPlayerContainer.IsVisible = false;
                AudioPlayerContainer.IsVisible = true;
                break;
            default:
                ResetBottomSheetPlayerUiForSelection();
                break;
        }
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

    private void HideAudioPlayer(
        bool resetPlaybackQueueState = false,
        bool stopPlayback = true,
        bool clearWebSource = true)
    {
        if (stopPlayback)
        {
            StopTtsPlayback();
        }
        TtsPlayerContainer.IsVisible = false;
        if (stopPlayback)
        {
            TtsPlayPauseButton.Text = PlayEmoji;
            TtsProgressSlider.Value = 0;
            TtsProgressSlider.Maximum = 1;
            TtsCurrentTimeLabel.Text = "00:00";
            TtsDurationLabel.Text = "00:00";
        }

        AudioPlayerContainer.IsVisible = false;
        if (clearWebSource)
        {
            AudioPlayerWebView.Source = null;
        }

        if (resetPlaybackQueueState)
        {
            ResetPlaybackQueueState();
        }
    }

    private void OnAudioPlayerWebViewNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.Url) || !e.Url.StartsWith("foodstreet://player", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        HandlePlayerWebCallback(e.Url);
    }

    private void HandlePlayerWebCallback(string rawUrl)
    {
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
        {
            return;
        }

        var values = ParseQuery(uri.Query);
        values.TryGetValue("event", out var eventName);
        var current = ParseDouble(values, "current");
        var duration = ParseDouble(values, "duration");

        if (_currentPlaybackSource is PlaybackSourceKind.AudioWeb or PlaybackSourceKind.TtsWeb)
        {
            UpdatePlaybackProgress(current, duration);
        }

        if (string.Equals(eventName, "ended", StringComparison.OrdinalIgnoreCase))
        {
            _ = HandleCurrentPlaybackCompletedAsync();
        }
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var span = query.AsSpan();
        if (span.Length > 0 && span[0] == '?')
        {
            span = span[1..];
        }

        foreach (var segment in span.ToString().Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = segment.Split('=', 2);
            var key = Uri.UnescapeDataString(pair[0]);
            var value = pair.Length == 2 ? Uri.UnescapeDataString(pair[1]) : string.Empty;
            if (!string.IsNullOrWhiteSpace(key))
            {
                result[key] = value;
            }
        }

        return result;
    }

    private static double ParseDouble(IReadOnlyDictionary<string, string> values, string key)
    {
        if (!values.TryGetValue(key, out var raw)
            || !double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            || double.IsNaN(parsed)
            || double.IsInfinity(parsed))
        {
            return 0;
        }

        return parsed;
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

    private async Task StartTtsPlayerAsync(string narration)
    {
        HideAudioPlayer(resetPlaybackQueueState: false);
        BuildTtsSegments(narration);

        _ttsWordIndex = 0;
        _ttsSegmentIndex = 0;
        _ttsElapsedSeconds = 0;
        var totalUnits = CountTtsUnits(narration);
        TtsProgressSlider.Maximum = Math.Max(1, Math.Max(_ttsWords.Count, totalUnits) * TtsSecondsPerWord);
        TtsProgressSlider.Value = 0;
        TtsPlayerContainer.IsVisible = true;
        TtsVolumeSlider.Value = Math.Clamp(TtsVolumeSlider.Value, 0, 1);
        RefreshTtsUi();
        if (_ttsWords.Count == 0)
        {
            try
            {
                var locale = await ResolvePreferredTtsLocaleAsync();
                var options = new SpeechOptions
                {
                    Volume = (float)Math.Clamp(TtsVolumeSlider.Value, 0, 1),
                    Pitch = 1.0f,
                    Rate = 1.05f,
                    Locale = locale
                };
                _ttsIsPlaying = true;
                TtsPlayPauseButton.Text = PauseEmoji;
                await TextToSpeech.Default.SpeakAsync(narration, options, CancellationToken.None);
                _ttsElapsedSeconds = TtsProgressSlider.Maximum;
                RefreshTtsUi();
                _ttsIsPlaying = false;
                TtsPlayPauseButton.Text = PlayEmoji;
                await HandleCurrentPlaybackCompletedAsync();
                return;
            }
            catch
            {
                _currentPlaybackSource = PlaybackSourceKind.TtsWeb;
                ShowAudioPlayerHtml(BuildTtsPlayerHtml(narration, ResolvePreferredTtsLanguageTag()));
                return;
            }
        }

        try
        {
            await ResumeTtsPlaybackAsync();
        }
        catch
        {
            try
            {
                var locale = await ResolvePreferredTtsLocaleAsync();
                var options = new SpeechOptions
                {
                    Volume = (float)Math.Clamp(TtsVolumeSlider.Value, 0, 1),
                    Pitch = 1.0f,
                    Rate = 1.05f,
                    Locale = locale
                };
                await TextToSpeech.Default.SpeakAsync(narration, options, CancellationToken.None);
                _ttsElapsedSeconds = TtsProgressSlider.Maximum;
                RefreshTtsUi();
                await HandleCurrentPlaybackCompletedAsync();
            }
            catch
            {
                // Final fallback for environments with broken native TTS.
                _currentPlaybackSource = PlaybackSourceKind.TtsWeb;
                ShowAudioPlayerHtml(BuildTtsPlayerHtml(narration, ResolvePreferredTtsLanguageTag()));
            }
        }
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
        TtsPlayPauseButton.Text = PauseEmoji;
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
            TtsPlayPauseButton.Text = PlayEmoji;
            if (_ttsWordIndex >= _ttsWords.Count)
            {
                _ttsElapsedSeconds = TtsProgressSlider.Maximum;
                RefreshTtsUi();
                _ = HandleCurrentPlaybackCompletedAsync();
            }
        }
    }

    private static string ResolveNarrationForPlayback(PoiViewModel poi)
    {
        if (!string.IsNullOrWhiteSpace(poi.Narration))
        {
            return poi.Narration;
        }

        return poi.Description ?? string.Empty;
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
        if (_currentPlaybackSource == PlaybackSourceKind.TtsNative)
        {
            UpdatePlaybackProgress(_ttsElapsedSeconds, TtsProgressSlider.Maximum);
        }
    }

    private void BuildTtsSegments(string narration)
    {
        _ttsWords.Clear();
        _ttsSegments.Clear();

        if (string.IsNullOrWhiteSpace(narration))
        {
            return;
        }

        _ttsWords.AddRange(SplitTtsUnits(narration));

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
        return ch == '.'
               || ch == '!'
               || ch == '?'
               || ch == ';'
               || ch == ':'
               || ch == '?'
               || ch == '!'
               || ch == '?'
               || ch == ';'
               || ch == '\n'
               || ch == '\r';
    }

    private bool TryAddTtsSegment(string raw, ref int runningWordCount)
    {
        var text = raw.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var units = CountTtsUnits(text);
        if (units == 0)
        {
            return false;
        }

        runningWordCount += units;
        _ttsSegments.Add(new TtsSegment
        {
            Text = text,
            WordCount = units,
            EndWordIndex = runningWordCount
        });
        return true;
    }

    private static List<string> SplitTtsUnits(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [];
        }

        if (ContainsCjkCharacter(normalized) && !normalized.Any(char.IsWhiteSpace))
        {
            return normalized
                .Where(ch => !char.IsWhiteSpace(ch))
                .Select(ch => ch.ToString())
                .ToList();
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private static int CountTtsUnits(string text) => SplitTtsUnits(text).Count;

    private static bool ContainsCjkCharacter(string text)
    {
        foreach (var ch in text)
        {
            if ((ch >= '\u4E00' && ch <= '\u9FFF')
                || (ch >= '\u3400' && ch <= '\u4DBF')
                || (ch >= '\uF900' && ch <= '\uFAFF')
                || (ch >= '\u3040' && ch <= '\u30FF'))
            {
                return true;
            }
        }

        return false;
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
        var language = AppLanguageService.NormalizeLanguageCode(_viewModel.CurrentLanguage) ?? "en";
        if (_preferredTtsLocaleLanguage is not null
            && _preferredTtsLocale is not null
            && string.Equals(_preferredTtsLocaleLanguage, language, StringComparison.OrdinalIgnoreCase))
        {
            return _preferredTtsLocale;
        }

        _preferredTtsLocaleLanguage = language;

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();

            var country = language switch
            {
                "vi" => "VN",
                "en" => "US",
                _ => null
            };

            Locale? best = null;
            if (!string.IsNullOrWhiteSpace(country))
            {
                best = locales.FirstOrDefault(l =>
                    string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase));
            }

            best ??= locales.FirstOrDefault(l => string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase));

            // Device does not support requested language -> fallback to English.
            best ??= locales.FirstOrDefault(l =>
                        string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase)
                        && string.Equals(l.Country, "US", StringComparison.OrdinalIgnoreCase))
                    ?? locales.FirstOrDefault(l => string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase))
                    ?? locales.FirstOrDefault();

            _preferredTtsLocale = best;
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
    body {{ margin:0; padding:0; font-family: Arial, sans-serif; background:#FFF7ED; color:#7C2D12; }}
    .controls {{ display:grid; grid-template-columns: 1fr 1fr 1fr; gap:8px; margin-bottom:8px; }}
    button {{ border:0; border-radius:10px; padding:8px 6px; font-size:12px; background:#FDE7D7; color:#7C2D12; }}
    .play {{ background:#E07A5F; color:#fff; font-weight:700; }}
    .row {{ display:grid; grid-template-columns: 42px 1fr 42px; gap:8px; align-items:center; font-size:11px; }}
    input[type=range] {{ width:100%; }}
    .vol {{ display:grid; grid-template-columns: 70px 1fr; gap:8px; align-items:center; margin-top:6px; font-size:12px; }}
  </style>
</head>
<body>
  <div class="controls">
    <button onclick="skip(-10)">Back 10s</button>
    <button id="playPause" class="play" onclick="toggle()">▶️</button>
    <button onclick="skip(10)">Forward 10s</button>
  </div>
  <audio id="audio" preload="metadata" src="__AUDIO_URL__"></audio>
  <div class="row">
    <span id="cur">00:00</span>
    <input id="progress" type="range" min="0" max="1" step="0.1" value="0" />
    <span id="dur">00:00</span>
  </div>
  <div class="vol">
    <span>Volume</span>
    <input id="volume" type="range" min="0" max="1" step="0.01" value="0.8" />
  </div>
  <script>
    const a = document.getElementById('audio');
    const btn = document.getElementById('playPause');
    const cur = document.getElementById('cur');
    const dur = document.getElementById('dur');
    const progress = document.getElementById('progress');
    const volume = document.getElementById('volume');
    let lastSentAt = 0;

    const fmt = (s) => {{
      if (!isFinite(s)) return '00:00';
      const m = Math.floor(s / 60);
      const ss = Math.floor(s % 60);
      return String(m).padStart(2, '0') + ':' + String(ss).padStart(2, '0');
    }};

    function notify(eventName) {{
      const now = Date.now();
      if (eventName === 'timeupdate' && now - lastSentAt < 250) return;
      if (eventName === 'timeupdate') lastSentAt = now;
      const current = Number.isFinite(a.currentTime) ? a.currentTime : 0;
      const duration = Number.isFinite(a.duration) ? a.duration : 0;
      window.location.href = `foodstreet://player?event=${{encodeURIComponent(eventName)}}&current=${{encodeURIComponent(current)}}&duration=${{encodeURIComponent(duration)}}`;
    }}

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
       notify('loadedmetadata');
      a.play().catch(() => {{ }});
    }});

    a.addEventListener('play', () => {{ btn.textContent = '⏸️'; notify('play'); }});
    a.addEventListener('pause', () => {{ btn.textContent = '▶️'; notify('pause'); }});
    a.addEventListener('ended', () => {{ btn.textContent = '▶️'; notify('ended'); }});
    a.addEventListener('timeupdate', () => {{
      progress.value = a.currentTime || 0;
      cur.textContent = fmt(a.currentTime || 0);
      notify('timeupdate');
    }});

    progress.addEventListener('input', () => {{
      a.currentTime = parseFloat(progress.value);
      cur.textContent = fmt(a.currentTime || 0);
      notify('seek');
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

    private string ResolvePreferredTtsLanguageTag()
    {
        var language = AppLanguageService.NormalizeLanguageCode(_viewModel.CurrentLanguage) ?? "en";
        return language switch
        {
            "vi" => "vi-VN",
            "en" => "en-US",
            "zh" => "zh-CN",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "ru" => "ru-RU",
            _ => "en-US"
        };
    }

    private static string BuildTtsPlayerHtml(string narration, string languageTag)
    {
        var safeJsonText = JsonSerializer.Serialize(narration);
        var safeLanguageTag = JsonSerializer.Serialize(string.IsNullOrWhiteSpace(languageTag) ? "en-US" : languageTag);
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
    <button onclick="skip(-10)">Back 10s</button>
    <button id="playPause" class="play" onclick="toggle()">▶️</button>
    <button onclick="skip(10)">Forward 10s</button>
  </div>
  <div class="row">
    <span id="cur">00:00</span>
    <input id="progress" type="range" min="0" max="1" step="0.1" value="0" />
    <span id="dur">00:00</span>
  </div>
  <div class="vol">
    <span>Volume</span>
    <input id="volume" type="range" min="0" max="1" step="0.01" value="0.8" />
  </div>
  <div class="note">TTS player (m? ph?ng ti?n tr?nh)</div>
  <script>
    const fullText = __TTS_TEXT_JSON__;
    const languageTag = __LANG_TAG_JSON__;
    const tokenize = (text) => {
      const normalized = (text || '').trim();
      if (!normalized) return [];
      if (/\s/.test(normalized)) return normalized.split(/\s+/).filter(Boolean);
      return Array.from(normalized).filter(ch => !/\s/.test(ch));
    };
    const words = tokenize(fullText);
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

    function notify(eventName) {
      const now = Date.now();
      if (eventName === 'timeupdate' && now - lastSentAt < 250) return;
      if (eventName === 'timeupdate') lastSentAt = now;
      const seconds = Math.min(totalSeconds, currentWord * secPerWord);
      window.location.href = `foodstreet://player?event=${encodeURIComponent(eventName)}&current=${encodeURIComponent(seconds)}&duration=${encodeURIComponent(totalSeconds)}`;
    }

    function refreshUi() {
      const seconds = Math.min(totalSeconds, currentWord * secPerWord);
      progress.max = totalSeconds;
      progress.value = seconds;
      cur.textContent = fmt(seconds);
      dur.textContent = fmt(totalSeconds);
      btn.textContent = paused || !playing ? '▶️' : '⏸️';
      notify('timeupdate');
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
      utterance.lang = languageTag || 'en-US';
      utterance.volume = parseFloat(volume.value || '0.8');
      segmentStartWord = currentWord;
      utterance.onend = () => { playing = false; paused = false; currentWord = words.length; stopTicker(); refreshUi(); notify('ended'); };
      playing = true;
      paused = false;
      speechSynthesis.speak(utterance);
      startTicker();
      refreshUi();
      notify('play');
    }

    function toggle() {
      if (!('speechSynthesis' in window) || words.length === 0) return;
      if (!playing) { playFromCurrent(); return; }
      if (!paused) { speechSynthesis.pause(); paused = true; refreshUi(); notify('pause'); return; }
      speechSynthesis.resume(); paused = false; refreshUi(); notify('play');
    }

    function skip(sec) {
      const step = Math.round(Math.abs(sec) / secPerWord);
      currentWord = sec < 0 ? Math.max(0, currentWord - step) : Math.min(words.length, currentWord + step);
      if (playing) { playFromCurrent(); } else { refreshUi(); notify('seek'); }
    }

    progress.addEventListener('input', () => {
      const sec = parseFloat(progress.value || '0');
      currentWord = Math.min(words.length, Math.max(0, Math.round(sec / secPerWord)));
      if (playing) { playFromCurrent(); } else { refreshUi(); notify('seek'); }
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
        return html
            .Replace("__TTS_TEXT_JSON__", safeJsonText)
            .Replace("__LANG_TAG_JSON__", safeLanguageTag);
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
            Title = "Chia s? d?a di?m",
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
            $"language={Uri.EscapeDataString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)}",
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
        MoveMapTo(center.Latitude, center.Longitude);
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


