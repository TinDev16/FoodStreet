using FoodStreetMobile.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FoodStreetMobile.ViewModels;

public sealed class PoiViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private double _distanceMeters;

    public PoiViewModel(Poi poi)
    {
        Id = poi.Id;
        Name = poi.Name;
        Latitude = poi.Latitude;
        Longitude = poi.Longitude;
        RadiusMeters = poi.RadiusMeters;
        Priority = poi.Priority;
        Narration = poi.Narration;
    }

    public string Id { get; }
    public string Name { get; }
    public double Latitude { get; }
    public double Longitude { get; }
    public double RadiusMeters { get; }
    public int Priority { get; }
    public string Narration { get; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
            {
                return;
            }

            _isActive = value;
            OnPropertyChanged();
        }
    }

    public double DistanceMeters
    {
        get => _distanceMeters;
        set
        {
            if (Math.Abs(_distanceMeters - value) < 0.1)
            {
                return;
            }

            _distanceMeters = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DistanceText));
        }
    }

    public string DistanceText => _distanceMeters <= 0 ? "--" : $"{Math.Round(_distanceMeters)} m";

    public string NarrationPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Narration))
            {
                return string.Empty;
            }

            return Narration.Length > 90 ? Narration[..90] + "..." : Narration;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
