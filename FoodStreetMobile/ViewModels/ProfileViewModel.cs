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
    private readonly AuthService _authService;
    private readonly IServiceProvider _services;
    private AppLanguageOption? _selectedLanguage;
    private bool _isLoggedIn;
    private bool _isBusy;
    private string _statusMessage = string.Empty;
    private string _profileUsername = string.Empty;
    private string _profileFullName = string.Empty;
    private string _profilePhone = string.Empty;
    private long _profileUserId;
    private string _currentPassword = string.Empty;
    private string _newPassword = string.Empty;
    private string _confirmPassword = string.Empty;

    public ProfileViewModel(AppLanguageService languageService, AuthService authService, IServiceProvider services)
    {
        _languageService = languageService;
        _authService = authService;
        _services = services;

        Languages = new ObservableCollection<AppLanguageOption>(_languageService.SupportedLanguages);
        OpenPoiHistoryCommand = new Command(async () => await OpenPoiHistoryAsync(), () => !IsBusy);
        LogoutCommand = new Command(async () => await LogoutAsync(), () => !IsBusy && IsLoggedIn);
        ChangePasswordCommand = new Command(async () => await ChangePasswordAsync(), () => !IsBusy && IsLoggedIn);
        GoToLoginCommand = new Command(async () => await GoToLoginAsync(), () => !IsBusy);

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

        _ = LoadSessionAsync();
    }

    public ObservableCollection<AppLanguageOption> Languages { get; }

    public ICommand OpenPoiHistoryCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand ChangePasswordCommand { get; }
    public ICommand GoToLoginCommand { get; }

    public bool IsLoggedIn
    {
        get => _isLoggedIn;
        private set
        {
            if (_isLoggedIn == value)
            {
                return;
            }

            _isLoggedIn = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNotLoggedIn));
            RefreshCommandCanExecute();
        }
    }

    public bool IsNotLoggedIn => !IsLoggedIn;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
            {
                return;
            }

            _isBusy = value;
            OnPropertyChanged();
            RefreshCommandCanExecute();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public string ProfileUsername
    {
        get => _profileUsername;
        private set
        {
            if (_profileUsername == value)
            {
                return;
            }

            _profileUsername = value;
            OnPropertyChanged();
        }
    }

    public string ProfileFullName
    {
        get => _profileFullName;
        private set
        {
            if (_profileFullName == value)
            {
                return;
            }

            _profileFullName = value;
            OnPropertyChanged();
        }
    }

    public string ProfilePhone
    {
        get => _profilePhone;
        private set
        {
            if (_profilePhone == value)
            {
                return;
            }

            _profilePhone = value;
            OnPropertyChanged();
        }
    }

    public long ProfileUserId
    {
        get => _profileUserId;
        private set
        {
            if (_profileUserId == value)
            {
                return;
            }

            _profileUserId = value;
            OnPropertyChanged();
        }
    }

    public string CurrentPassword
    {
        get => _currentPassword;
        set
        {
            if (_currentPassword == value)
            {
                return;
            }

            _currentPassword = value;
            OnPropertyChanged();
        }
    }

    public string NewPassword
    {
        get => _newPassword;
        set
        {
            if (_newPassword == value)
            {
                return;
            }

            _newPassword = value;
            OnPropertyChanged();
        }
    }

    public string ConfirmPassword
    {
        get => _confirmPassword;
        set
        {
            if (_confirmPassword == value)
            {
                return;
            }

            _confirmPassword = value;
            OnPropertyChanged();
        }
    }

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

    public async Task OnAppearingAsync()
    {
        await LoadSessionAsync();
        if (!IsLoggedIn)
        {
            return;
        }

        await _authService.TryRefreshProfileFromServerAsync();
        await ApplyCachedProfileAsync();
    }

    private async Task LoadSessionAsync()
    {
        try
        {
            IsLoggedIn = _authService.IsLoggedIn;
            await ApplyCachedProfileAsync();
        }
        catch
        {
            // ignore
        }
    }

    private async Task ApplyCachedProfileAsync()
    {
        var cached = await _authService.LoadCachedProfileAsync();
        if (cached is null)
        {
            return;
        }

        ProfileUserId = cached.ServerUserId;
        ProfileUsername = cached.Username;
        ProfileFullName = cached.FullName;
        ProfilePhone = cached.Phone;
    }

    private async Task ChangePasswordAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = string.Empty;
        try
        {
            var (ok, msg) = await _authService.ChangePasswordAsync(CurrentPassword, NewPassword, ConfirmPassword);
            StatusMessage = msg;
            if (ok)
            {
                CurrentPassword = string.Empty;
                NewPassword = string.Empty;
                ConfirmPassword = string.Empty;
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LogoutAsync()
    {
        _authService.Logout();
        IsLoggedIn = false;
        ProfileUserId = 0;
        ProfileUsername = string.Empty;
        ProfileFullName = string.Empty;
        ProfilePhone = string.Empty;
        CurrentPassword = string.Empty;
        NewPassword = string.Empty;
        ConfirmPassword = string.Empty;
        StatusMessage = string.Empty;

        if (Shell.Current is not null)
        {
            try
            {
                await Shell.Current.GoToAsync("//AuthPage");
            }
            catch
            {
            }
        }
    }

    private async Task GoToLoginAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        try
        {
            await Shell.Current.GoToAsync("//AuthPage");
        }
        catch
        {
        }
    }

    private void RefreshCommandCanExecute()
    {
        if (OpenPoiHistoryCommand is Command oh)
        {
            oh.ChangeCanExecute();
        }

        if (LogoutCommand is Command lo)
        {
            lo.ChangeCanExecute();
        }

        if (ChangePasswordCommand is Command cp)
        {
            cp.ChangeCanExecute();
        }

        if (GoToLoginCommand is Command gl)
        {
            gl.ChangeCanExecute();
        }
    }

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
        catch
        {
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
