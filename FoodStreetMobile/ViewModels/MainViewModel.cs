using FoodStreetMobile.ViewModels;
using FoodStreetMobile.Services;
using FoodStreetMobile.Models;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Dispatching;

namespace FoodStreetMobile.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PoiRepository _poiRepository;
    private readonly GeofenceEngine _geofenceEngine;
    private readonly NarrationEngine _narrationEngine;
    private readonly LocationTracker _locationTracker;
    private readonly PoiSyncService _poiSyncService;
    private readonly AppLanguageService _languageService;

    private bool _isTracking;
    private string _statusText = "Sẵn sàng.";
    private PoiViewModel? _activePoi;
    private CancellationTokenSource? _narrationCts;
    private readonly SemaphoreSlim _autoNarrationLock = new(1, 1);
    private string? _currentAutoNarrationPoiId;
    private bool _initialized;
    private string _currentLanguage = "vi";
    private DateTimeOffset _lastAutoSyncAt = DateTimeOffset.MinValue;
    private readonly SemaphoreSlim _autoSyncLock = new(1, 1);
    private static readonly TimeSpan AutoSyncInterval = TimeSpan.FromSeconds(12);
    private IDispatcherTimer? _heartbeatTimer;
    private readonly ICommand _setVietnameseCommand;
    private readonly ICommand _setEnglishCommand;
    private readonly ICommand _syncNowCommand;

    public MainViewModel(
        PoiRepository poiRepository,
        PoiSyncService poiSyncService,
        GeofenceEngine geofenceEngine,
        NarrationEngine narrationEngine,
        LocationTracker locationTracker,
        AppLanguageService languageService)
    {
        _poiRepository = poiRepository;
        _poiSyncService = poiSyncService;
        _geofenceEngine = geofenceEngine;
        _narrationEngine = narrationEngine;
        _locationTracker = locationTracker;
        _languageService = languageService;

        Pois = new ObservableCollection<PoiViewModel>();
        ToggleTrackingCommand = new Command(async () => await ToggleTrackingAsync());
        _setVietnameseCommand = new Command(async () => await SetLanguageAsync("vi"));
        _setEnglishCommand = new Command(async () => await SetLanguageAsync("en"));
        _syncNowCommand = new Command(async () => await RefreshFromServerAsync());

        _languageService.LanguageChanged += language =>
        {
            MainThread.BeginInvokeOnMainThread(() => _ = ApplyLanguageAsync(language));
        };
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

    public string CurrentLanguage => _currentLanguage;

    public event Action<IReadOnlyList<PoiViewModel>, bool>? PoisLoaded;
    public event Action<PoiViewModel?>? ActivePoiChanged;
    public event Action<Location>? UserLocationChanged;
    public event Action<PoiViewModel>? AutoPlayPoiRequested;

    public async Task EnsureDataInitializedAsync()
    {
        if (!_initialized)
        {
            _currentLanguage = AppLanguageService.NormalizeLanguageCode(_languageService.CurrentLanguage) ?? "vi";
            await _poiRepository.SetCurrentLanguageAsync(_currentLanguage);
            _initialized = true;
        }

        await RefreshFromServerAsync();
    }

    public async Task InitializeAsync()
    {
        await EnsureDataInitializedAsync();

        if (!IsTracking)
        {
            await StartTrackingAsync();
        }

        if (_heartbeatTimer == null && Application.Current?.Dispatcher != null)
        {
            _heartbeatTimer = Application.Current.Dispatcher.CreateTimer();
            _heartbeatTimer.Interval = TimeSpan.FromSeconds(5);
            _heartbeatTimer.Tick += (s, e) => _ = _poiSyncService.TrackActivityAsync("ping", null, _currentLanguage);
        }
        _heartbeatTimer?.Start();
    }

    public async Task SetLanguageAsync(string languageCode)
    {
        _languageService.SetLanguage(languageCode);
    }

    private async Task ApplyLanguageAsync(string languageCode)
    {
        _currentLanguage = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
        await _poiRepository.SetCurrentLanguageAsync(_currentLanguage);
        OnPropertyChanged(nameof(CurrentLanguage));

        // Ensure we sync the new language data before reloading the UI
        StatusText = "Downloading translations...";
        await _poiSyncService.TrySyncAsync(_currentLanguage);
        
        await ReloadPoisAsync(_currentLanguage, isSilentSync: false);
    }

    public async Task RefreshFromServerAsync()
    {
        var synced = await _poiSyncService.TrySyncAsync(_currentLanguage);
        await ReloadPoisAsync(_currentLanguage, isSilentSync: false);
        if (synced)
        {
            var source = string.IsNullOrWhiteSpace(_poiSyncService.LastSuccessfulBaseUrl)
                ? "web admin"
                : _poiSyncService.LastSuccessfulBaseUrl;
            StatusText = $"Da dong bo POI tu {source}.";
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
        _narrationCts?.Dispose();
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

    public async Task<(bool Ok, string Message)> UnlockPoiAsync(string poiId)
    {
        _ = poiId;
        await Task.CompletedTask;
        return (true, "Ung dung hien khong con yeu cau dang nhap hay mo khoa POI.");
    }

    private static string BuildDeterministicPoiId(string shopName, double latitude, double longitude)
    {
        var normalizedName = string.IsNullOrWhiteSpace(shopName)
            ? "poi"
            : new string(shopName.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

        var lat = latitude.ToString("F6", CultureInfo.InvariantCulture);
        var lon = longitude.ToString("F6", CultureInfo.InvariantCulture);
        return $"{normalizedName}_{lat}_{lon}".Replace('.', '_');
    }

    private async Task ReloadPoisAsync(string languageCode, bool isSilentSync)
    {
        var sourcePois = await _poiRepository.GetPoisAsync(languageCode);

        await MainThread.InvokeOnMainThreadAsync(() =>
        {
            var currentPoisDict = Pois.ToDictionary(x => x.Id);
            var sourcePoisDict = sourcePois.ToDictionary(x => x.Id);

            // 1. Remove items no longer present
            var toRemove = Pois.Where(p => !sourcePoisDict.ContainsKey(p.Id)).ToList();
            foreach (var item in toRemove)
            {
                Pois.Remove(item);
            }

            // 2. Update or Add
            foreach (var source in sourcePois)
            {
                if (currentPoisDict.TryGetValue(source.Id, out var existing))
                {
                    // Update existing instance to avoid pin recreation
                    existing.UpdateFrom(source);
                }
                else
                {
                    // Add new
                    Pois.Add(new PoiViewModel(source));
                }
            }

            // Sort if priority changed significantly or just to maintain order
            // (Optional, omitted for simplicity unless requested)

            PoisLoaded?.Invoke(Pois, isSilentSync);
        });
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
            _locationTracker.LocationUpdated -= OnLocationUpdated;
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
        _narrationCts?.Cancel();
        _narrationCts?.Dispose();
        _narrationCts = null;
        _currentAutoNarrationPoiId = null;
        IsTracking = false;
        StatusText = "Da tam dung theo doi.";
        SetActivePoi(null);
    }

    private async void OnLocationUpdated(object? sender, Location location)
    {
        UserLocationChanged?.Invoke(location);

        await TryAutoSyncOnTrackingAsync();

        var newActive = _geofenceEngine.SelectActive(location, Pois);
        SetActivePoi(newActive);

        if (newActive is null)
        {
            _currentAutoNarrationPoiId = null;
            StatusText = "Chua co gian hang nao trong pham vi.";
            return;
        }

            StatusText = $"Dang gan: {newActive.Name}.";
        if (string.Equals(_currentAutoNarrationPoiId, newActive.Id, StringComparison.Ordinal))
        {
            return;
        }

        _currentAutoNarrationPoiId = newActive.Id;
        _ = TryAutoNarrationAsync(newActive);
    }

    private async Task TryAutoSyncOnTrackingAsync()
    {
        var now = DateTimeOffset.UtcNow;
        if (now - _lastAutoSyncAt < AutoSyncInterval)
        {
            return;
        }

        if (!await _autoSyncLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            now = DateTimeOffset.UtcNow;
            if (now - _lastAutoSyncAt < AutoSyncInterval)
            {
                return;
            }

            _lastAutoSyncAt = now;
            var synced = await _poiSyncService.TrySyncAsync(_currentLanguage);
            if (!synced)
            {
                return;
            }

            await ReloadPoisAsync(_currentLanguage, isSilentSync: true);
        }
        finally
        {
            _autoSyncLock.Release();
        }
    }

    private bool HasPoiCollectionChanged(IReadOnlyList<Poi> newPois)
    {
        // This is now less critical since we diff in ReloadPoisAsync, 
        // but we can keep it as a fast path for "nothing changed" if needed.
        if (Pois.Count != newPois.Count)
        {
            return true;
        }

        for (var i = 0; i < newPois.Count; i++)
        {
            var current = Pois[i];
            var next = newPois[i];

            if (!string.Equals(current.Id, next.Id, StringComparison.Ordinal)
                || Math.Abs(current.Latitude - next.Latitude) > 0.0000001
                || Math.Abs(current.Longitude - next.Longitude) > 0.0000001
                || Math.Abs(current.RadiusMeters - next.RadiusMeters) > 0.01
                || current.Priority != next.Priority
                || !string.Equals(current.Name, next.Name, StringComparison.Ordinal)
                || !string.Equals(current.Description, next.Description, StringComparison.Ordinal)
                || !string.Equals(current.Narration, next.Narration, StringComparison.Ordinal)
                || !string.Equals(current.AudioUrl, next.AudioUrl, StringComparison.Ordinal)
                || !string.Equals(current.Language, next.Language, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

    private async Task TryAutoNarrationAsync(PoiViewModel poi)
    {
        await _autoNarrationLock.WaitAsync();

        try
        {
            if (!string.Equals(_currentAutoNarrationPoiId, poi.Id, StringComparison.Ordinal))
            {
                return;
            }

            _narrationCts?.Cancel();
            _narrationCts?.Dispose();
            _narrationCts = new CancellationTokenSource();

            // Drive auto playback through the page UI so the POI info bar/bottom-sheet
            // and the in-app player state are always synchronized with GPS-triggered POI.
            AutoPlayPoiRequested?.Invoke(poi);
        }
        catch (OperationCanceledException)
        {
            // Ignore cancellation when user moves between POIs quickly.
        }
        catch
        {
            // Ignore narration errors.
        }
        finally
        {
            _autoNarrationLock.Release();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
