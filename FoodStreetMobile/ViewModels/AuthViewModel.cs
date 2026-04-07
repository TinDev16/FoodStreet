using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FoodStreetMobile.Services;
using FoodStreetMobile;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;

namespace FoodStreetMobile.ViewModels;

public sealed class AuthViewModel : INotifyPropertyChanged
{
    private readonly AuthService _authService;
    private readonly ToastNotificationService _toast;
    private readonly AppLanguageService _languageService;
    private AppLanguageOption? _selectedLanguage;
    private bool _isBusy;
    private string _serverBaseUrl = string.Empty;
    private string _loginIdentifier = string.Empty;
    private string _loginPassword = string.Empty;
    private string _regUsername = string.Empty;
    private string _regPassword = string.Empty;
    private string _regFullName = string.Empty;
    private string _regPhone = string.Empty;

    public AuthViewModel(AuthService authService, ToastNotificationService toast, AppLanguageService languageService)
    {
        _authService = authService;
        _toast = toast;
        _languageService = languageService;

        var suggested = _authService.GetApiBaseUrl();
        _serverBaseUrl = !string.IsNullOrEmpty(suggested)
            ? suggested
            : DeviceInfo.Platform == DevicePlatform.Android
                ? "http://10.0.2.2:5187"
                : "http://localhost:5187";

        Languages = new ObservableCollection<AppLanguageOption>(_languageService.SupportedLanguages);
        _selectedLanguage =
            Languages.FirstOrDefault(x => string.Equals(x.Code, _languageService.CurrentLanguage, StringComparison.OrdinalIgnoreCase))
            ?? Languages.FirstOrDefault(x => x.Code == "en")
            ?? Languages.FirstOrDefault();

        _languageService.LanguageChanged += code =>
        {
            var option = Languages.FirstOrDefault(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            if (option is not null)
            {
                SelectedLanguage = option;
            }
        };

        LoginCommand = new Command(async () => await LoginAsync(), () => !IsBusy);
        RegisterCommand = new Command(async () => await RegisterAsync(), () => !IsBusy);
        GoToRegisterCommand = new Command(async () => await GoToRegisterAsync(), () => !IsBusy);
        GoToLoginCommand = new Command(async () => await GoToLoginAsync(), () => !IsBusy);
    }

    public ObservableCollection<AppLanguageOption> Languages { get; }

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

    public ICommand LoginCommand { get; }
    public ICommand RegisterCommand { get; }
    public ICommand GoToRegisterCommand { get; }
    public ICommand GoToLoginCommand { get; }

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

    public string ServerBaseUrl
    {
        get => _serverBaseUrl;
        set
        {
            if (_serverBaseUrl == value)
            {
                return;
            }

            _serverBaseUrl = value;
            OnPropertyChanged();
        }
    }

    public string LoginIdentifier
    {
        get => _loginIdentifier;
        set
        {
            if (_loginIdentifier == value)
            {
                return;
            }

            _loginIdentifier = value;
            OnPropertyChanged();
        }
    }

    public string LoginPassword
    {
        get => _loginPassword;
        set
        {
            if (_loginPassword == value)
            {
                return;
            }

            _loginPassword = value;
            OnPropertyChanged();
        }
    }

    public string RegUsername
    {
        get => _regUsername;
        set
        {
            if (_regUsername == value)
            {
                return;
            }

            _regUsername = value;
            OnPropertyChanged();
        }
    }

    public string RegPassword
    {
        get => _regPassword;
        set
        {
            if (_regPassword == value)
            {
                return;
            }

            _regPassword = value;
            OnPropertyChanged();
        }
    }

    public string RegFullName
    {
        get => _regFullName;
        set
        {
            if (_regFullName == value)
            {
                return;
            }

            _regFullName = value;
            OnPropertyChanged();
        }
    }

    public string RegPhone
    {
        get => _regPhone;
        set
        {
            if (_regPhone == value)
            {
                return;
            }

            _regPhone = value;
            OnPropertyChanged();
        }
    }

    private async Task GoToRegisterAsync()
    {
        if (Shell.Current is null)
        {
            return;
        }

        try
        {
            await Shell.Current.GoToAsync("//RegisterPage");
        }
        catch
        {
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

    private async Task LoginAsync()
    {
        if (IsBusy || Shell.Current is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var (ok, msg) = await _authService.LoginAsync(ServerBaseUrl, LoginIdentifier, LoginPassword);
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                try
                {
                    if (ok)
                    {
                        LoginPassword = string.Empty;
                        if (Shell.Current is AppShell shell)
                        {
                            shell.NavigateToMainTabsTab(0);
                        }

                        await _toast.ShowAsync(msg, false);
                    }
                    else
                    {
                        await _toast.ShowAsync(msg, true);
                    }
                }
                catch
                {
                    // Tránh crash nếu Shell/Toast lỗi trên một số thiết bị.
                }
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RegisterAsync()
    {
        if (IsBusy || Shell.Current is null)
        {
            return;
        }

        IsBusy = true;
        try
        {
            var (ok, msg) = await _authService.RegisterAsync(
                ServerBaseUrl,
                RegUsername,
                RegPassword,
                RegFullName,
                RegPhone);

            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await _toast.ShowAsync(msg, !ok);
                if (ok)
                {
                    var usernameForLogin = RegUsername;
                    RegPassword = string.Empty;
                    RegFullName = string.Empty;
                    RegPhone = string.Empty;
                    RegUsername = string.Empty;
                    LoginIdentifier = usernameForLogin;

                    if (Shell.Current is not null)
                    {
                        await Shell.Current.GoToAsync("//AuthPage");
                    }
                }
            });
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RefreshCommandCanExecute()
    {
        if (LoginCommand is Command lc)
        {
            lc.ChangeCanExecute();
        }

        if (RegisterCommand is Command rc)
        {
            rc.ChangeCanExecute();
        }

        if (GoToRegisterCommand is Command gr)
        {
            gr.ChangeCanExecute();
        }

        if (GoToLoginCommand is Command gl)
        {
            gl.ChangeCanExecute();
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
