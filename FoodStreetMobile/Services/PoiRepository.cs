using FoodStreetMobile.Models;

namespace FoodStreetMobile.Services;

public sealed class PoiRepository
{
    private readonly AppDatabase _database;

    public PoiRepository(AppDatabase database)
    {
        _database = database;
    }

    public async Task<IReadOnlyList<Poi>> GetPoisAsync(string languageCode)
    {
        var connection = await _database.GetConnectionAsync();
        var pois = await connection.Table<PoiEntity>()
            .Where(x => x.IsActive)
            .OrderByDescending(x => x.Priority)
            .ToListAsync();

        var translations = await connection.Table<PoiTranslationEntity>()
            .Where(x => x.LangCode == languageCode || x.LangCode == "vi")
            .ToListAsync();

        var byPoi = translations
            .GroupBy(x => x.PoiId)
            .ToDictionary(
                x => x.Key,
                x => x.OrderByDescending(t => t.LangCode == languageCode).First());

        var result = new List<Poi>(pois.Count);
        foreach (var entity in pois)
        {
            byPoi.TryGetValue(entity.Id, out var translation);

            result.Add(new Poi
            {
                Id = entity.Id,
                Name = translation?.Name ?? entity.Id,
                Description = translation?.Description ?? string.Empty,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                RadiusMeters = entity.RadiusMeters,
                Priority = entity.Priority,
                Narration = translation?.TtsText ?? string.Empty,
                ImageUrl = entity.ImageUrl,
                MapLink = entity.MapLink,
                AudioUrl = entity.AudioUrl,
                Language = translation?.LangCode ?? "vi"
            });
        }

        return result;
    }

    public async Task<string> GetCurrentLanguageAsync()
    {
        var connection = await _database.GetConnectionAsync();
        var setting = await connection.FindAsync<AppSettingEntity>("current_language");
        return string.IsNullOrWhiteSpace(setting?.Value) ? "vi" : setting.Value;
    }

    public async Task SetCurrentLanguageAsync(string languageCode)
    {
        var normalized = string.IsNullOrWhiteSpace(languageCode) ? "vi" : languageCode.Trim().ToLowerInvariant();
        var connection = await _database.GetConnectionAsync();
        await connection.InsertOrReplaceAsync(new AppSettingEntity
        {
            Key = "current_language",
            Value = normalized
        });
    }
}
