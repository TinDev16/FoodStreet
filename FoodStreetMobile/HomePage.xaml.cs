using FoodStreetMobile.Localization;
using FoodStreetMobile.Models;
using FoodStreetMobile.Services;
using FoodStreetMobile.ViewModels;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using MauiLocation = Microsoft.Maui.Devices.Sensors.Location;

namespace FoodStreetMobile;

public partial class HomePage : ContentPage
{
    private static readonly HttpClient HttpClient = new();
    private static readonly string FeaturedCacheFilePath =
        Path.Combine(FileSystem.AppDataDirectory, "featured_places_cache.json");

    private readonly AppLanguageService _languageService;
    private readonly MainViewModel _viewModel;
    private readonly PlaceSearchService _placeSearchService;
    private readonly DeepLinkService _deepLinkService;
    private readonly PoiSyncService _poiSyncService;
    private readonly ObservableCollection<FeaturedPlaceCard> _featuredPlaces = new();
    private readonly ObservableCollection<SearchPlaceResult> _searchResults = new();
    private CancellationTokenSource? _searchTypingCts;
    private MauiLocation? _lastUserLocation;
    private string _lastSearchQuery = string.Empty;
    private bool _hasInitializedPoiData;

    public HomePage(
        AppLanguageService languageService,
        MainViewModel viewModel,
        PlaceSearchService placeSearchService,
        DeepLinkService deepLinkService,
        PoiSyncService poiSyncService)
    {
        InitializeComponent();
        _languageService = languageService;
        _viewModel = viewModel;
        _placeSearchService = placeSearchService;
        _deepLinkService = deepLinkService;
        _poiSyncService = poiSyncService;
        FeaturedPlacesCollection.ItemsSource = _featuredPlaces;
        HomeSearchResultsView.ItemsSource = _searchResults;
        HomeSearchEntry.TextChanged += OnHomeSearchTextChanged;

        _languageService.LanguageChanged += OnLanguageChanged;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _searchTypingCts?.Cancel();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        try
        {
            if (!_hasInitializedPoiData)
            {
                await _viewModel.EnsureDataInitializedAsync();
                _hasInitializedPoiData = true;
            }

            await RefreshFeaturedPlacesAsync();
        }
        catch (Exception ex)
        {
            CrashLogger.Write("HomePage.OnAppearing", ex);
        }
    }

    private void OnLanguageChanged(string _)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await RefreshFeaturedPlacesAsync();
            }
            catch (Exception ex)
            {
                CrashLogger.Write("HomePage.OnLanguageChanged", ex);
            }
        });
    }

    private async Task RefreshFeaturedPlacesAsync()
    {
        await TryEnsureUserLocationAsync();
        var featured = await TryLoadFeaturedPlacesFromStatsAsync();
        _featuredPlaces.Clear();
        foreach (var item in featured)
        {
            _featuredPlaces.Add(item);
        }
        FeaturedPlacesContainer.IsVisible = _featuredPlaces.Count > 0;
    }

    private static string NormalizeAssetUrl(string baseUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return value;

        var normalizedBase = baseUrl.TrimEnd('/');
        var normalizedValue = value.StartsWith("/") ? value : "/" + value;
        return $"{normalizedBase}{normalizedValue}";
    }

    private async Task<List<FeaturedPlaceCard>> TryLoadFeaturedPlacesFromStatsAsync()
    {
        var requestedLang = AppLanguageService.NormalizeLanguageCode(_languageService.CurrentLanguage) ?? "vi";
        foreach (var baseUrl in _poiSyncService.GetPreferredBaseUrlsSnapshot())
        {
            try
            {
                using var response = await HttpClient.GetAsync(
                    $"{baseUrl}/api/public/featured-pois?lang={Uri.EscapeDataString(requestedLang)}&limit=4");
                if (!response.IsSuccessStatusCode)
                {
                    continue;
                }

                var items = await response.Content.ReadFromJsonAsync<List<FeaturedPoiDto>>() ?? [];
                var cards = items
                    .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                    .Select(x => BuildFeaturedCard(x, baseUrl, isFromCache: false))
                    .ToList();

                // Save to cache for offline use
                _ = SaveFeaturedCacheAsync(items, baseUrl);
                return cards;
            }
            catch
            {
                // Ignore endpoint errors, try next URL.
            }
        }

        // All endpoints failed — try offline cache
        return await LoadFeaturedCacheAsync();
    }

    private static async Task SaveFeaturedCacheAsync(List<FeaturedPoiDto> items, string baseUrl)
    {
        try
        {
            var payload = new FeaturedCachePayload { BaseUrl = baseUrl, Items = items };
            var json = JsonSerializer.Serialize(payload);
            await File.WriteAllTextAsync(FeaturedCacheFilePath, json).ConfigureAwait(false);
        }
        catch
        {
            // Non-critical: ignore cache write errors.
        }
    }

    private async Task<List<FeaturedPlaceCard>> LoadFeaturedCacheAsync()
    {
        try
        {
            if (!File.Exists(FeaturedCacheFilePath))
                return [];

            var json = await File.ReadAllTextAsync(FeaturedCacheFilePath).ConfigureAwait(false);
            var payload = JsonSerializer.Deserialize<FeaturedCachePayload>(json);
            if (payload is null || payload.Items is null || payload.Items.Count == 0)
                return [];

            return payload.Items
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .Select(x => BuildFeaturedCard(x, payload.BaseUrl, isFromCache: true))
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private async void OnHomeSearchCompleted(object? sender, EventArgs e)
    {
        await SearchAsync();
    }

    private async void OnHomeSearchTapped(object? sender, TappedEventArgs e)
    {
        await SearchAsync();
    }

    private void OnHomeSearchTextChanged(object? sender, TextChangedEventArgs e)
    {
        _ = HandleHomeSearchTextChangedAsync(e.NewTextValue);
    }

    private async Task HandleHomeSearchTextChangedAsync(string? newTextValue)
    {
        _searchTypingCts?.Cancel();
        // Do NOT Dispose the CTS here. Task.Delay might be unwinding concurrently.

        var query = newTextValue?.Trim() ?? string.Empty;
        if (query.Length < 2)
        {
            ClearHomeSearchUi();
            return;
        }

        var cts = new CancellationTokenSource();
        _searchTypingCts = cts;
        SetSearchStatus(isLoading: true, errorText: null);

        try
        {
            await Task.Delay(220, cts.Token);
            await TryEnsureUserLocationAsync();
            var results = await SearchPlacesAsync(query, cts.Token);
            if (cts.IsCancellationRequested)
            {
                return;
            }

            BindSearchResults(results);
            SetSearchStatus(isLoading: false, errorText: results.Count == 0 ? "Khong tim thay ket qua." : null);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation while typing.
        }
        catch (Exception ex)
        {
            if (!cts.IsCancellationRequested)
            {
                CrashLogger.Write("HomePage.OnHomeSearchTextChanged", ex);
                HomeSearchResultsView.IsVisible = false;
                SetSearchStatus(isLoading: false, errorText: "Khong the tim kiem luc nay.");
            }
        }
    }

    private async Task SearchAsync()
    {
        var query = HomeSearchEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(query))
        {
            await DisplayAlertAsync("Thong bao", "Hay nhap dia diem can tim.", "OK");
            return;
        }

        _searchTypingCts?.Cancel();
        SetSearchStatus(isLoading: true, errorText: null);

        try
        {
            await TryEnsureUserLocationAsync();
            var results = await SearchPlacesAsync(query, CancellationToken.None);

            BindSearchResults(results);
            SetSearchStatus(isLoading: false, errorText: results.Count == 0 ? "Khong tim thay ket qua." : null);

            if (results.Count == 0)
            {
                await DisplayAlertAsync("Thong bao", "Khong tim thay dia diem.", "OK");
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Write("HomePage.SearchAsync", ex);
            SetSearchStatus(isLoading: false, errorText: "Khong the tim kiem luc nay.");
            HomeSearchResultsView.IsVisible = false;
        }
    }

    private async void OnHomeSearchResultSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not SearchPlaceResult selected)
        {
            return;
        }

        if (sender is CollectionView collectionView)
        {
            collectionView.SelectedItem = null;
        }

        await SelectSearchResultAsync(selected);
    }

    private async Task SelectSearchResultAsync(SearchPlaceResult result)
    {
        var resolved = await ResolveSearchResultAsync(result);
        if (!resolved.HasCoordinates)
        {
            await DisplayAlertAsync("Thong bao", "Khong the lay toa do cho dia diem nay.", "OK");
            return;
        }

        HomeSearchEntry.Text = resolved.Name;
        HomeSearchEntry.Unfocus();
        HomeSearchResultsView.IsVisible = false;
        SetSearchStatus(isLoading: false, errorText: null);
        await NavigateToMapWithSelectionAsync(resolved);
    }

    private async Task<SearchPlaceResult> ResolveSearchResultAsync(SearchPlaceResult result)
    {
        if (result.HasCoordinates)
        {
            return result;
        }

        await TryEnsureUserLocationAsync();
        return await _placeSearchService.ResolveAsync(result, _lastUserLocation, CancellationToken.None);
    }

    private async Task NavigateToMapWithSelectionAsync(SearchPlaceResult selected)
    {
        _deepLinkService.QueuePendingPlaceSelection(new PendingPlaceSelection
        {
            Name = selected.Name,
            Address = selected.Address,
            Latitude = selected.Latitude,
            Longitude = selected.Longitude,
            ImageUrl = selected.ImageUrl,
            PlaceId = selected.PlaceId,
            PoiId = selected.PoiId
        });

        if (Shell.Current is AppShell shell)
        {
            shell.NavigateToMainTabsTab(1);
        }
    }

    private async Task<List<SearchPlaceResult>> SearchPlacesAsync(string query, CancellationToken cancellationToken)
    {
        var poiCandidates = _viewModel.Pois
            .Select(p => new PoiSearchCandidate
            {
                PoiId = p.Id,
                Name = p.Name ?? string.Empty,
                Address = p.Description ?? string.Empty,
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

    private async Task TryEnsureUserLocationAsync()
    {
        if (_lastUserLocation is not null)
        {
            return;
        }

        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Medium, TimeSpan.FromSeconds(10));
            var location = await Geolocation.GetLocationAsync(request);
            if (location is not null)
            {
                _lastUserLocation = location;
            }
        }
        catch
        {
            // Keep search usable even when location is unavailable.
        }
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

    private static string BuildFeaturedMetaText(long playCount)
    {
        var culture = CultureInfo.CurrentUICulture;
        if (playCount > 0)
        {
            var format = AppResources.ResourceManager.GetString("Home_InterestCountFormat", culture) ?? "{0} interested";
            return string.Format(culture, format, playCount);
        }

        return AppResources.ResourceManager.GetString("Home_Popular", culture) ?? "Popular";
    }

    private FeaturedPlaceCard BuildFeaturedCard(FeaturedPoiDto item, string baseUrl, bool isFromCache)
    {
        var poi = _viewModel.Pois.FirstOrDefault(p => string.Equals(p.Id, item.Id, StringComparison.Ordinal));
        return new FeaturedPlaceCard(
            item.Id,
            item.Name.Trim(),
            BuildFeaturedMetaText(item.PlayCount),
            BuildFeaturedSummaryText(poi?.Description),
            BuildFeaturedDistanceText(poi),
            poi?.PriceText ?? string.Empty,
            NormalizeAssetUrl(baseUrl, item.ImageUrl),
            isFromCache);
    }

    private static string BuildFeaturedSummaryText(string? description)
    {
        if (string.IsNullOrWhiteSpace(description))
        {
            return string.Empty;
        }

        var value = description.Trim();
        return value.Length > 90 ? value[..90] + "..." : value;
    }

    private string BuildFeaturedDistanceText(PoiViewModel? poi)
    {
        if (poi is null || _lastUserLocation is null)
        {
            return string.Empty;
        }

        var poiLocation = new MauiLocation(poi.Latitude, poi.Longitude);
        var kilometers = MauiLocation.CalculateDistance(_lastUserLocation, poiLocation, DistanceUnits.Kilometers);
        var meters = kilometers * 1000d;
        if (meters <= 0)
        {
            return string.Empty;
        }

        var culture = CultureInfo.CurrentUICulture;
        if (meters < 1000)
        {
            return $"{Math.Round(meters)} m";
        }

        return $"{Math.Round(kilometers, 1).ToString("0.0", culture)} km";
    }

    public void OnFeaturedPlaceTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is FeaturedPlaceCard card && !string.IsNullOrWhiteSpace(card.Id))
        {
            _deepLinkService.QueuePendingPlaceSelection(new PendingPlaceSelection
            {
                Name = card.Name,
                Address = string.IsNullOrWhiteSpace(card.SummaryText) ? card.MetaText : card.SummaryText,
                PoiId = card.Id,
                ImageUrl = card.ImageUrl
            });

            if (Shell.Current is AppShell shell)
            {
                shell.NavigateToMainTabsTab(1);
            }
        }
    }

    private sealed class FeaturedPlaceCard
    {
        public FeaturedPlaceCard(
            string id,
            string name,
            string metaText,
            string summaryText,
            string distanceText,
            string priceTag,
            string? imageUrl = null,
            bool isFromCache = false)
        {
            Id = id;
            Name = name;
            MetaText = metaText;
            SummaryText = summaryText;
            DistanceText = distanceText;
            PriceTag = priceTag;
            ImageUrl = imageUrl ?? string.Empty;
            IsFromCache = isFromCache;
        }

        public string Id { get; }
        public string Name { get; }
        public string MetaText { get; }
        public string SummaryText { get; }
        public string DistanceText { get; }
        public string PriceTag { get; }
        public string ImageUrl { get; }
        public bool IsFromCache { get; }
        public bool HasImage => !string.IsNullOrWhiteSpace(ImageUrl);
        public bool ShowPlaceholder => string.IsNullOrWhiteSpace(ImageUrl);
        public bool HasSummary => !string.IsNullOrWhiteSpace(SummaryText);
        public bool HasDistance => !string.IsNullOrWhiteSpace(DistanceText);
        public bool HasPriceTag => !string.IsNullOrWhiteSpace(PriceTag);
    }

    private sealed class FeaturedPoiDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ImageUrl { get; set; } = string.Empty;
        public long PlayCount { get; set; }
    }

    private sealed class FeaturedCachePayload
    {
        public string BaseUrl { get; set; } = string.Empty;
        public List<FeaturedPoiDto> Items { get; set; } = [];
    }

    private void BindSearchResults(IReadOnlyList<SearchPlaceResult> results)
    {
        try
        {
            _searchResults.Clear();
            foreach (var item in results)
            {
                _searchResults.Add(item);
            }

            HomeSearchResultsView.IsVisible = _searchResults.Count > 0;
        }
        catch (Exception ex)
        {
            CrashLogger.Write("HomePage.BindSearchResults", ex);
            _searchResults.Clear();
            HomeSearchResultsView.IsVisible = false;
        }
    }

    private void SetSearchStatus(bool isLoading, string? errorText)
    {
        HomeSearchStatusLayout.IsVisible = false;
        HomeSearchLoadingIndicator.IsRunning = false;

        HomeSearchErrorLabel.Text = string.Empty;
        HomeSearchErrorLabel.IsVisible = false;
    }

    private void ClearHomeSearchUi()
    {
        _lastSearchQuery = string.Empty;
        _searchResults.Clear();
        HomeSearchResultsView.IsVisible = false;
        SetSearchStatus(isLoading: false, errorText: null);
    }
}
