using FoodStreetMobile.Models;
using FoodStreetMobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;

namespace FoodStreetMobile.Services;

public sealed class NarrationEngine
{
    private readonly AppDatabase _database;
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private readonly Dictionary<string, Locale?> _ttsLocaleCache = new(StringComparer.OrdinalIgnoreCase);

    public NarrationEngine(AppDatabase database)
    {
        _database = database;
    }

    public async Task<bool> TryPlayAsync(PoiViewModel poi, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(poi.Narration) && string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            return false;
        }

        var connection = await _database.GetConnectionAsync();
        if (!await CanPlayAsync(connection, poi.Id, poi.Language))
        {
            return false;
        }

        await _speakLock.WaitAsync(cancellationToken);
        try
        {
            if (!await CanPlayAsync(connection, poi.Id, poi.Language))
            {
                return false;
            }

            await PlayInternalAsync(poi, cancellationToken);
            await MarkPlayedAsync(connection, poi.Id, poi.Language);
            return true;
        }
        finally
        {
            _speakLock.Release();
        }
    }

    public async Task<bool> PlayOnDemandAsync(PoiViewModel poi, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(poi.Narration) && string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            return false;
        }

        var connection = await _database.GetConnectionAsync();
        await _speakLock.WaitAsync(cancellationToken);
        try
        {
            await PlayInternalAsync(poi, cancellationToken);
            await MarkPlayedAsync(connection, poi.Id, poi.Language);
            return true;
        }
        finally
        {
            _speakLock.Release();
        }
    }

    private async Task PlayInternalAsync(PoiViewModel poi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.IsNullOrWhiteSpace(poi.AudioUrl))
        {
            await Launcher.Default.OpenAsync(new Uri(poi.AudioUrl));
            return;
        }

        if (!string.IsNullOrWhiteSpace(poi.Narration))
        {
            var locale = await ResolveTtsLocaleAsync(poi.Language);
            var options = new SpeechOptions
            {
                Locale = locale,
                Pitch = 1.0f,
                Rate = 1.08f
            };
            await TextToSpeech.Default.SpeakAsync(poi.Narration, options, cancellationToken);
        }
    }

    private async Task<Locale?> ResolveTtsLocaleAsync(string? languageCode)
    {
        var resolvedLanguage = NormalizeLanguageCode(languageCode);
        if (string.IsNullOrWhiteSpace(resolvedLanguage))
        {
            return null;
        }

        if (_ttsLocaleCache.TryGetValue(resolvedLanguage, out var cached))
        {
            return cached;
        }

        Locale? resolvedLocale = null;
        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();

            var (language, country) = SplitLanguageTag(resolvedLanguage);
            resolvedLocale = PickBestLocale(locales, language, country);
        }
        catch
        {
            resolvedLocale = null;
        }

        _ttsLocaleCache[resolvedLanguage] = resolvedLocale;
        return resolvedLocale;
    }

    private static string? NormalizeLanguageCode(string? languageCode)
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

        var language = parts[0].ToLowerInvariant();
        if (parts.Length == 1)
        {
            return language;
        }

        var country = parts[1].ToUpperInvariant();
        return $"{language}-{country}";
    }

    private static (string Language, string? Country) SplitLanguageTag(string normalizedLanguageTag)
    {
        var parts = normalizedLanguageTag.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return ("", null);
        }

        var language = parts[0].ToLowerInvariant();
        var country = parts.Length >= 2 ? parts[1].ToUpperInvariant() : null;
        return (language, country);
    }

    private static Locale? PickBestLocale(IEnumerable<Locale> locales, string language, string? country)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return locales.FirstOrDefault();
        }

        if (!string.IsNullOrWhiteSpace(country))
        {
            var exact = locales.FirstOrDefault(l =>
                string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Country, country, StringComparison.OrdinalIgnoreCase));
            if (exact is not null)
            {
                return exact;
            }
        }

        if (string.Equals(language, "vi", StringComparison.OrdinalIgnoreCase))
        {
            var vietnam = locales.FirstOrDefault(l =>
                string.Equals(l.Language, "vi", StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Country, "VN", StringComparison.OrdinalIgnoreCase));
            if (vietnam is not null)
            {
                return vietnam;
            }
        }

        if (string.Equals(language, "en", StringComparison.OrdinalIgnoreCase))
        {
            var us = locales.FirstOrDefault(l =>
                string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase)
                && string.Equals(l.Country, "US", StringComparison.OrdinalIgnoreCase));
            if (us is not null)
            {
                return us;
            }
        }

        var match = locales.FirstOrDefault(l => string.Equals(l.Language, language, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            return match;
        }

        // Device does not support requested language -> fallback to English.
        var english = locales.FirstOrDefault(l =>
            string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase)
            && string.Equals(l.Country, "US", StringComparison.OrdinalIgnoreCase))
            ?? locales.FirstOrDefault(l => string.Equals(l.Language, "en", StringComparison.OrdinalIgnoreCase));
        return english ?? locales.FirstOrDefault();
    }

    private async Task<bool> CanPlayAsync(SQLite.SQLiteAsyncConnection connection, string poiId, string language)
    {
        var state = await connection.FindAsync<PlaybackStateEntity>(poiId);
        if (state is null)
        {
            return true;
        }

        var cooldown = await GetCooldownAsync(connection);
        var lastPlayedUtc = DateTimeOffset.FromUnixTimeSeconds(state.LastPlayedUtc);

        if (state.LastLanguage != language)
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastPlayedUtc >= cooldown;
    }

    private static async Task MarkPlayedAsync(SQLite.SQLiteAsyncConnection connection, string poiId, string language)
    {
        var state = await connection.FindAsync<PlaybackStateEntity>(poiId) ?? new PlaybackStateEntity { PoiId = poiId };
        state.LastPlayedUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        state.LastLanguage = language;
        state.PlayCount += 1;
        await connection.InsertOrReplaceAsync(state);
    }

    private static async Task<TimeSpan> GetCooldownAsync(SQLite.SQLiteAsyncConnection connection)
    {
        var setting = await connection.FindAsync<AppSettingEntity>("audio_cooldown_seconds");
        if (!int.TryParse(setting?.Value, out var seconds) || seconds < 0)
        {
            seconds = 90;
        }

        return TimeSpan.FromSeconds(seconds);
    }
}
