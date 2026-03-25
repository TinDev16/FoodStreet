using System.ComponentModel;
using System.Globalization;
using System.Resources;

namespace FoodStreetMobile.Localization;

public sealed class LocalizationResourceManager : INotifyPropertyChanged
{
    private CultureInfo _culture = CultureInfo.CurrentUICulture;

    private LocalizationResourceManager()
    {
    }

    public static LocalizationResourceManager Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public CultureInfo Culture
    {
        get => _culture;
        private set
        {
            if (Equals(_culture, value))
            {
                return;
            }

            _culture = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
        }
    }

    public string this[string key]
    {
        get
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            return AppResources.ResourceManager.GetString(key, Culture) ?? key;
        }
    }

    public void SetCulture(CultureInfo culture)
    {
        Culture = culture;
    }
}

