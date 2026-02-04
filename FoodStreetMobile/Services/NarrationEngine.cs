using FoodStreetMobile.ViewModels;
using Microsoft.Maui.Media;

namespace FoodStreetMobile.Services;

public sealed class NarrationEngine
{
    private readonly SemaphoreSlim _speakLock = new(1, 1);
    private readonly Dictionary<string, DateTimeOffset> _lastPlayed = new();

    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(5);

    public async Task<bool> TryPlayAsync(PoiViewModel poi, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(poi.Narration))
        {
            return false;
        }

        if (!CanPlay(poi.Id))
        {
            return false;
        }

        await _speakLock.WaitAsync(cancellationToken);
        try
        {
            if (!CanPlay(poi.Id))
            {
                return false;
            }

            await TextToSpeech.Default.SpeakAsync(poi.Narration);
            _lastPlayed[poi.Id] = DateTimeOffset.UtcNow;
            return true;
        }
        finally
        {
            _speakLock.Release();
        }
    }

    private bool CanPlay(string poiId)
    {
        if (!_lastPlayed.TryGetValue(poiId, out var lastPlayed))
        {
            return true;
        }

        return DateTimeOffset.UtcNow - lastPlayed >= Cooldown;
    }
}
