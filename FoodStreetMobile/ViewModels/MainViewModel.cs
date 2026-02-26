using FoodStreetMobile.Services;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FoodStreetMobile.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PoiRepository _poiRepository;
    private readonly GeofenceEngine _geofenceEngine;
    private readonly NarrationEngine _narrationEngine;
    private readonly LocationTracker _locationTracker;
    private readonly PoiSyncService _poiSyncService;

    private bool _isTracking;
    private string _statusText = "San sang.";
    private PoiViewModel? _activePoi;
    private CancellationTokenSource? _narrationCts;
    private bool _initialized;
    private string _currentLanguage = "vi";
    private readonly ICommand _setVietnameseCommand;
    private readonly ICommand _setEnglishCommand;
    private readonly ICommand _syncNowCommand;

    public MainViewModel(
        PoiRepository poiRepository,
        PoiSyncService poiSyncService,
        GeofenceEngine geofenceEngine,
        NarrationEngine narrationEngine,
        LocationTracker locationTracker)
    {
        _poiRepository = poiRepository;
        _poiSyncService = poiSyncService;
        _geofenceEngine = geofenceEngine;
        _narrationEngine = narrationEngine;
        _locationTracker = locationTracker;

        Pois = new ObservableCollection<PoiViewModel>();
        ToggleTrackingCommand = new Command(async () => await ToggleTrackingAsync());
        _setVietnameseCommand = new Command(async () => await SetLanguageAsync("vi"));
        _setEnglishCommand = new Command(async () => await SetLanguageAsync("en"));
        _syncNowCommand = new Command(async () => await RefreshFromServerAsync());
    }

    public ObservableCollection<PoiViewModel> Pois { get; }

    public bool IsTracking
    {
        get => _isTracking;
        private set
        {
            if (_isTracking == value)
            {
                return;
            }

            _isTracking = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TrackingButtonText));
        }
    }

    public string TrackingButtonText => IsTracking ? "Dung theo doi" : "Bat dau theo doi";

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public PoiViewModel? ActivePoi
    {
        get => _activePoi;
        private set
        {
            if (_activePoi == value)
            {
                return;
            }

            _activePoi = value;
            OnPropertyChanged();
            ActivePoiChanged?.Invoke(value);
        }
    }

    public ICommand ToggleTrackingCommand { get; }
    public ICommand SetVietnameseCommand => _setVietnameseCommand;
    public ICommand SetEnglishCommand => _setEnglishCommand;
    public ICommand SyncNowCommand => _syncNowCommand;

    public event Action<IReadOnlyList<PoiViewModel>>? PoisLoaded;
    public event Action<PoiViewModel?>? ActivePoiChanged;
    public event Action<Location>? UserLocationChanged;

    public async Task InitializeAsync()
    {
        if (!_initialized)
        {
            _currentLanguage = await _poiRepository.GetCurrentLanguageAsync();
            _initialized = true;
        }

        await RefreshFromServerAsync();
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
        await _poiRepository.SetCurrentLanguageAsync(_currentLanguage);
        await ReloadPoisAsync(_currentLanguage);
        StatusText = _currentLanguage == "en"
            ? "Language switched to English."
            : "Da chuyen ngon ngu sang tieng Viet.";
    }

    public async Task RefreshFromServerAsync()
    {
        var synced = await _poiSyncService.TrySyncAsync();
        await ReloadPoisAsync(_currentLanguage);
        if (synced)
        {
            StatusText = "Da dong bo POI tu web admin.";
            return;
        }

        var detail = string.IsNullOrWhiteSpace(_poiSyncService.LastError)
            ? "Khong ket noi duoc web admin."
            : _poiSyncService.LastError!;
        StatusText = $"Khong dong bo duoc POI: {detail}";
    }

    public async Task<bool> SaveShopFromMapAsync(string shopName, double latitude, double longitude, string description, string? poiId = null)
    {
        var resolvedPoiId = string.IsNullOrWhiteSpace(poiId)
            ? BuildDeterministicPoiId(shopName, latitude, longitude)
            : poiId;

        var request = new PoiSyncService.ShopUpsertRequest
        {
            Id = resolvedPoiId,
            ShopName = shopName,
            Latitude = latitude,
            Longitude = longitude,
            RadiusMeters = 40,
            Description = description,
            TtsText = description
        };

        var pushed = await _poiSyncService.UpsertRemoteAsync(request);
        if (!pushed)
        {
            return false;
        }

        await RefreshFromServerAsync();
        return true;
    }

    public async Task<bool> DeleteShopFromMapAsync(string shopName, double latitude, double longitude, string? poiId = null)
    {
        var resolvedPoiId = string.IsNullOrWhiteSpace(poiId)
            ? BuildDeterministicPoiId(shopName, latitude, longitude)
            : poiId;

        var deleted = await _poiSyncService.DeleteRemoteAsync(resolvedPoiId);
        if (!deleted)
        {
            return false;
        }

        await RefreshFromServerAsync();
        return true;
    }

    public async Task<bool> PlayPoiAudioAsync(PoiViewModel poi)
    {
        _narrationCts?.Cancel();
        _narrationCts = new CancellationTokenSource();

        try
        {
            return await _narrationEngine.PlayOnDemandAsync(poi, _narrationCts.Token);
        }
        catch
        {
            return false;
        }
    }

    public string GetConfiguredBaseUrls() => _poiSyncService.GetConfiguredBaseUrls();

    public void SetConfiguredBaseUrls(string? rawValue) => _poiSyncService.SetConfiguredBaseUrls(rawValue);

    private static string BuildDeterministicPoiId(string shopName, double latitude, double longitude)
    {
        var normalizedName = string.IsNullOrWhiteSpace(shopName)
            ? "poi"
            : new string(shopName.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        var lat = latitude.ToString("F6", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("F6", CultureInfo.InvariantCulture);
        return $"{normalizedName}_{lat}_{lon}".Replace('.', '_');
    }

    private async Task ReloadPoisAsync(string languageCode)
    {
        var pois = await _poiRepository.GetPoisAsync(languageCode);
        Pois.Clear();
        foreach (var poi in pois)
        {
            Pois.Add(new PoiViewModel(poi));
        }

        SetActivePoi(null);
        PoisLoaded?.Invoke(Pois);
    }

    private async Task ToggleTrackingAsync()
    {
        if (IsTracking)
        {
            StopTracking();
            return;
        }

        await StartTrackingAsync();
    }

    private async Task StartTrackingAsync()
    {
        try
        {
            _locationTracker.LocationUpdated += OnLocationUpdated;
            await _locationTracker.StartAsync();
            IsTracking = true;
            StatusText = "Dang theo doi vi tri...";
        }
        catch (Exception ex)
        {
            IsTracking = false;
            StatusText = $"Khong the bat dinh vi: {ex.Message}";
        }
    }

    private void StopTracking()
    {
        _locationTracker.LocationUpdated -= OnLocationUpdated;
        _locationTracker.Stop();
        IsTracking = false;
        StatusText = "Da tam dung theo doi.";
        SetActivePoi(null);
    }

    private async void OnLocationUpdated(object? sender, Location location)
    {
        UserLocationChanged?.Invoke(location);

        var newActive = _geofenceEngine.SelectActive(location, Pois);
        SetActivePoi(newActive);

        if (newActive is not null)
        {
            StatusText = $"Dang gan: {newActive.Name}.";
            await TryNarrationAsync(newActive);
        }
        else
        {
            StatusText = "Chua co gian hang nao trong pham vi.";
        }
    }

    private void SetActivePoi(PoiViewModel? poi)
    {
        if (ActivePoi == poi)
        {
            return;
        }

        if (ActivePoi is not null)
        {
            ActivePoi.IsActive = false;
        }

        ActivePoi = poi;

        if (ActivePoi is not null)
        {
            ActivePoi.IsActive = true;
        }
    }

    private async Task TryNarrationAsync(PoiViewModel poi)
    {
        _narrationCts?.Cancel();
        _narrationCts = new CancellationTokenSource();

        try
        {
            await _narrationEngine.TryPlayAsync(poi, _narrationCts.Token);
        }
        catch
        {
            // Ignore narration errors.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
