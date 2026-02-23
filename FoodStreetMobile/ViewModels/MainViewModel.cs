using FoodStreetMobile.Services;
using Microsoft.Maui.Devices.Sensors;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FoodStreetMobile.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly PoiRepository _poiRepository;
    private readonly GeofenceEngine _geofenceEngine;
    private readonly NarrationEngine _narrationEngine;
    private readonly LocationTracker _locationTracker;

    private bool _isTracking;
    private string _statusText = "San sang.";
    private PoiViewModel? _activePoi;
    private CancellationTokenSource? _narrationCts;
    private bool _initialized;

    public MainViewModel(
        PoiRepository poiRepository,
        GeofenceEngine geofenceEngine,
        NarrationEngine narrationEngine,
        LocationTracker locationTracker)
    {
        _poiRepository = poiRepository;
        _geofenceEngine = geofenceEngine;
        _narrationEngine = narrationEngine;
        _locationTracker = locationTracker;

        Pois = new ObservableCollection<PoiViewModel>();
        ToggleTrackingCommand = new Command(async () => await ToggleTrackingAsync());
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

    public event Action<IReadOnlyList<PoiViewModel>>? PoisLoaded;
    public event Action<PoiViewModel?>? ActivePoiChanged;
    public event Action<Location>? UserLocationChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        foreach (var poi in _poiRepository.GetPois())
        {
            Pois.Add(new PoiViewModel(poi));
        }

        _initialized = true;
        PoisLoaded?.Invoke(Pois);
        await Task.CompletedTask;
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
