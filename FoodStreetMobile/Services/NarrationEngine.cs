using FoodStreetMobile.Models;
using FoodStreetMobile.ViewModels;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Media;

namespace FoodStreetMobile.Services;

public sealed class NarrationEngine
{
    private readonly AppDatabase _database;
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private Locale? _preferredTtsLocale;

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
            var locale = await ResolvePreferredTtsLocaleAsync();
            var options = new SpeechOptions
            {
                Locale = locale,
                Pitch = 1.0f,
                Rate = 1.08f
            };
            await TextToSpeech.Default.SpeakAsync(poi.Narration, options, cancellationToken);
        }
    }

    private async Task<Locale?> ResolvePreferredTtsLocaleAsync()
    {
        if (_preferredTtsLocale is not null)
        {
            return _preferredTtsLocale;
        }

        try
        {
            var locales = await TextToSpeech.Default.GetLocalesAsync();
            _preferredTtsLocale = locales
                .FirstOrDefault(l => string.Equals(l.Language, "vi", StringComparison.OrdinalIgnoreCase)
                                     && string.Equals(l.Country, "VN", StringComparison.OrdinalIgnoreCase))
                ?? locales.FirstOrDefault(l => string.Equals(l.Language, "vi", StringComparison.OrdinalIgnoreCase))
                ?? locales.FirstOrDefault();
        }
        catch
        {
            _preferredTtsLocale = null;
        }

        return _preferredTtsLocale;
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
