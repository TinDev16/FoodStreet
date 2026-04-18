using FoodStreetMobile.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace FoodStreetMobile.ViewModels;

public sealed class ProfileViewModel : INotifyPropertyChanged
{
    private readonly AppLanguageService _languageService;
    private readonly IServiceProvider _services;
    private AppLanguageOption? _selectedLanguage;


    public ProfileViewModel(AppLanguageService languageService, IServiceProvider services)
    {
        _languageService = languageService;
        _services = services;

        Languages = new ObservableCollection<AppLanguageOption>(_languageService.SupportedLanguages);
        OpenPoiHistoryCommand = new Command(async () => await OpenPoiHistoryAsync());

        _selectedLanguage =
            Languages.FirstOrDefault(x => string.Equals(x.Code, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(x => x.Code == "en")
            ?? new AppLanguageOption { Code = "en", Label = "English (en)" };

        _languageService.LanguageChanged += code =>
        {
            var option = Languages.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                SelectedLanguage = option;
            }
        };
    }

    public ObservableCollection<AppLanguageOption> Languages { get; }

    public ICommand OpenPoiHistoryCommand { get; }



    public AppLanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            if (value is null)
            {
                return;
            }

            if (Equals(_selectedLanguage, value) || string.Equals(_selectedLanguage?.Code, value.Code, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _selectedLanguage = value;
            OnPropertyChanged();
            _languageService.SetLanguage(value.Code);
        }
    }

    public Task OnAppearingAsync() => Task.CompletedTask;

    private async Task OpenPoiHistoryAsync()
    {
        try
        {
            var page = _services.GetRequiredService<PoiViewHistoryPage>();
            if (Shell.Current is not null)
            {
                await Shell.Current.Navigation.PushAsync(page);
            }
        }
        catch (Exception)
        {
            // Error logging could be added here if needed, 
            // but the StatusMessage is no longer displayed on the Profile page.
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
