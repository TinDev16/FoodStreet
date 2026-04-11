using FoodStreetMobile.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FoodStreetMobile.ViewModels;

public sealed class PoiViewModel : INotifyPropertyChanged
{
    private bool _isActive;
    private double _distanceMeters;
    private string _name = string.Empty;
    private string _description = string.Empty;
    private double _latitude;
    private double _longitude;
    private double _radiusMeters;
    private int _priority;
    private string _narration = string.Empty;
    private string _imageUrl = string.Empty;
    private string _mapLink = string.Empty;
    private string _audioUrl = string.Empty;
    private string _language = string.Empty;

    public PoiViewModel(Poi poi)
    {
        Id = poi.Id;
        UpdateFrom(poi);
    }

    public void UpdateFrom(Poi poi)
    {
        Name = poi.Name;
        Description = poi.Description;
        Latitude = poi.Latitude;
        Longitude = poi.Longitude;
        RadiusMeters = poi.RadiusMeters;
        Priority = poi.Priority;
        Narration = poi.Narration;
        ImageUrl = poi.ImageUrl;
        MapLink = poi.MapLink;
        AudioUrl = poi.AudioUrl;
        Language = poi.Language;
    }

    public string Id { get; init; } = string.Empty;

    public string Name
    {
        get => _name;
        private set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    public string Description
    {
        get => _description;
        private set { if (_description != value) { _description = value; OnPropertyChanged(); } }
    }

    public double Latitude
    {
        get => _latitude;
        private set { if (Math.Abs(_latitude - value) > 0.0000001) { _latitude = value; OnPropertyChanged(); } }
    }

    public double Longitude
    {
        get => _longitude;
        private set { if (Math.Abs(_longitude - value) > 0.0000001) { _longitude = value; OnPropertyChanged(); } }
    }

    public double RadiusMeters
    {
        get => _radiusMeters;
        private set { if (Math.Abs(_radiusMeters - value) > 0.01) { _radiusMeters = value; OnPropertyChanged(); } }
    }

    public int Priority
    {
        get => _priority;
        private set { if (_priority != value) { _priority = value; OnPropertyChanged(); } }
    }

    public string Narration
    {
        get => _narration;
        private set { if (_narration != value) { _narration = value; OnPropertyChanged(); OnPropertyChanged(nameof(NarrationPreview)); } }
    }

    public string ImageUrl
    {
        get => _imageUrl;
        private set { if (_imageUrl != value) { _imageUrl = value; OnPropertyChanged(); } }
    }

    public string MapLink
    {
        get => _mapLink;
        private set { if (_mapLink != value) { _mapLink = value; OnPropertyChanged(); } }
    }

    public string AudioUrl
    {
        get => _audioUrl;
        private set { if (_audioUrl != value) { _audioUrl = value; OnPropertyChanged(); } }
    }

    public string Language
    {
        get => _language;
        private set { if (_language != value) { _language = value; OnPropertyChanged(); } }
    }

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
