using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Devices.Sensors;

namespace FoodStreetMobile.Services;

public sealed class LocationTracker
{
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private IDispatcherTimer? _timer;
    private EventHandler? _tickHandler;

    public event EventHandler<Location>? LocationUpdated;
    public bool IsRunning { get; private set; }
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(5);

    public async Task StartAsync()
    {
        if (IsRunning)
        {
            return;
        }

        var status = await Permissions.RequestAsync<Permissions.LocationWhenInUse>();
        if (status != PermissionStatus.Granted)
        {
            throw new InvalidOperationException("Location permission was not granted.");
        }

        IsRunning = true;
        _timer = Application.Current?.Dispatcher.CreateTimer();
        if (_timer is null)
        {
            throw new InvalidOperationException("Unable to start dispatcher timer.");
        }

        _timer.Interval = PollInterval;
        _tickHandler = async (_, _) => await UpdateLocationAsync();
        _timer.Tick += _tickHandler;
        _timer.Start();

        await UpdateLocationAsync();
    }

    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        IsRunning = false;
        if (_timer is not null)
        {
            _timer.Stop();
            if (_tickHandler is not null)
            {
                _timer.Tick -= _tickHandler;
            }

            _timer = null;
            _tickHandler = null;
        }
    }

    private async Task UpdateLocationAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        if (!await _updateLock.WaitAsync(0))
        {
            return;
        }

        try
        {
            var request = new GeolocationRequest(GeolocationAccuracy.Best, TimeSpan.FromSeconds(8));
            var location = await Geolocation.GetLocationAsync(request);

            if (location is not null)
            {
                LocationUpdated?.Invoke(this, location);
            }
        }
        catch
        {
            // Ignore transient sensor errors; status is surfaced by the view model.
        }
        finally
        {
            _updateLock.Release();
        }
    }
}
