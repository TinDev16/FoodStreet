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
            .ToDictionary(x => x.Key, x => SelectBestTranslation(x.ToList(), languageCode));

        var result = new List<Poi>(pois.Count);
        foreach (var entity in pois)
        {
            byPoi.TryGetValue(entity.Id, out var translation);

            var resolvedName = !string.IsNullOrWhiteSpace(translation?.Name)
                ? translation!.Name
                : entity.Id;

            if (!string.Equals(languageCode, "vi", StringComparison.OrdinalIgnoreCase))
            {
                resolvedName = TextNormalizer.RemoveDiacritics(resolvedName);
            }
            var resolvedNarration = !string.IsNullOrWhiteSpace(translation?.TtsText)
                ? translation!.TtsText
                : translation?.Description ?? string.Empty;

            result.Add(new Poi
            {
                Id = entity.Id,
                Name = resolvedName,
                Description = translation?.Description ?? string.Empty,
                Latitude = entity.Latitude,
                Longitude = entity.Longitude,
                RadiusMeters = entity.RadiusMeters,
                Priority = entity.Priority,
                Narration = resolvedNarration,
                ImageUrl = entity.ImageUrl,
                MapLink = entity.MapLink,
                AudioUrl = entity.AudioUrl,
                Price = entity.Price,
                IsPaid = entity.IsPaid,
                Language = languageCode
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

    private static PoiTranslationEntity? SelectBestTranslation(IReadOnlyList<PoiTranslationEntity> candidates, string languageCode)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        bool HasUsefulContent(PoiTranslationEntity t)
            => !string.IsNullOrWhiteSpace(t.Name)
               || !string.IsNullOrWhiteSpace(t.TtsText)
               || !string.IsNullOrWhiteSpace(t.Description);

        var preferredLangWithContent = candidates.FirstOrDefault(t =>
            string.Equals(t.LangCode, languageCode, StringComparison.OrdinalIgnoreCase) && HasUsefulContent(t));
        if (preferredLangWithContent is not null)
        {
            return preferredLangWithContent;
        }

        var vietnameseWithContent = candidates.FirstOrDefault(t =>
            string.Equals(t.LangCode, "vi", StringComparison.OrdinalIgnoreCase) && HasUsefulContent(t));
        if (vietnameseWithContent is not null)
        {
            return vietnameseWithContent;
        }

        var preferredLang = candidates.FirstOrDefault(t => string.Equals(t.LangCode, languageCode, StringComparison.OrdinalIgnoreCase));
        if (preferredLang is not null)
        {
            return preferredLang;
        }

        var vietnamese = candidates.FirstOrDefault(t => string.Equals(t.LangCode, "vi", StringComparison.OrdinalIgnoreCase));
        if (vietnamese is not null)
        {
            return vietnamese;
        }

        return candidates.FirstOrDefault();
    }
}
