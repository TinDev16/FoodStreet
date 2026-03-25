using FoodStreetMobile.Localization;
using Microsoft.Maui.Storage;
using System.Globalization;

namespace FoodStreetMobile.Services;

public sealed class AppLanguageService
{
    private const string PreferenceKey = "app_language";
    private readonly IReadOnlyList<AppLanguageOption> _supported = AppLanguageOption.CreateDefaults();
    private string _currentLanguage;

    public AppLanguageService()
    {
        _currentLanguage = NormalizeLanguageCode(Preferences.Get(PreferenceKey, "vi")) ?? "vi";
    }

    public event Action<string>? LanguageChanged;

    public IReadOnlyList<AppLanguageOption> SupportedLanguages => _supported;

    public string CurrentLanguage => _currentLanguage;

    public void Initialize()
    {
        ApplyCulture(_currentLanguage);
    }

    public void SetLanguage(string languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode) ?? "vi";
        if (string.Equals(_currentLanguage, normalized, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentLanguage = normalized;
        Preferences.Set(PreferenceKey, normalized);
        ApplyCulture(normalized);
        LanguageChanged?.Invoke(normalized);
    }

    private static void ApplyCulture(string languageCode)
    {
        CultureInfo culture;
        try
        {
            culture = new CultureInfo(languageCode);
        }
        catch
        {
            culture = new CultureInfo("en");
        }

        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        LocalizationResourceManager.Instance.SetCulture(culture);
    }

    public static string? NormalizeLanguageCode(string? languageCode)
    {
        if (string.IsNullOrWhiteSpace(languageCode))
        {
            return null;
        }

        var trimmed = languageCode.Trim().Replace('_', '-');
        var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        return parts[0].ToLowerInvariant();
    }
}

public sealed class AppLanguageOption
{
    public required string Code { get; init; }
    public required string Label { get; init; }

    public static IReadOnlyList<AppLanguageOption> CreateDefaults()
        =>
        [
            new AppLanguageOption { Code = "vi", Label = "Tiếng Việt (vi)" },
            new AppLanguageOption { Code = "en", Label = "English (en)" },
            new AppLanguageOption { Code = "zh", Label = "中文 (zh)" },
            new AppLanguageOption { Code = "ja", Label = "日本語 (ja)" },
            new AppLanguageOption { Code = "ru", Label = "Русский (ru)" },
            new AppLanguageOption { Code = "ko", Label = "한국어 (ko)" },
        ];
}

