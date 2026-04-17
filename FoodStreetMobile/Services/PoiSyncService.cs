using System.Net.Http.Json;
using FoodStreetMobile.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace FoodStreetMobile.Services;

public sealed class PoiSyncService
{

    private const string BaseUrlsPreferenceKey = "admin_base_urls";
    private static readonly SemaphoreSlim _syncLock = new(1, 1);
    private readonly AppDatabase _database;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string? _lastSuccessfulBaseUrl;
    public string? LastError { get; private set; }
    public string? LastSuccessfulBaseUrl => _lastSuccessfulBaseUrl;

    public PoiSyncService(AppDatabase database)
    {
        _database = database;
    }

    public string GetConfiguredBaseUrls() => Preferences.Get(BaseUrlsPreferenceKey, string.Empty);

    public void SetConfiguredBaseUrls(string? rawValue)
    {
        var normalized = string.IsNullOrWhiteSpace(rawValue) ? string.Empty : rawValue.Trim();
        Preferences.Set(BaseUrlsPreferenceKey, normalized);
    }

    public IReadOnlyList<string> GetPreferredBaseUrlsSnapshot()
        => GetPreferredBaseUrls().ToList();

    public async Task<bool> TrySyncAsync(string? languageCode = null)
    {
        await _syncLock.WaitAsync();
        try
        {
            LastError = null;
            var requestedLang = NormalizeAppLanguageCode(languageCode) ?? "vi";
            var errors = new List<string>();
            foreach (var baseUrl in GetPreferredBaseUrls())
            {
                try
                {
                    var pois = await TryFetchPoisAsync(baseUrl, requestedLang);
                    if (pois is null)
                    {
                        errors.Add($"{baseUrl}: empty response");
                        continue;
                    }

                    await ApplyRemoteDataAsync(baseUrl, requestedLang, pois);
                    _lastSuccessfulBaseUrl = baseUrl;
                    LastError = null;
                    return true;
                }
                catch (Exception ex)
                {
                    errors.Add($"{baseUrl}: {ex.Message}");
                }
            }

            if (errors.Count > 0)
            {
                LastError = string.Join(" | ", errors);
            }
            else
            {
                LastError = "No backend endpoint candidate.";
            }

            var canReadAdminDbFile =
                OperatingSystem.IsWindows()
                || OperatingSystem.IsLinux()
                || OperatingSystem.IsMacOS();

            if (canReadAdminDbFile)
            {
                var syncedFromFile = await TrySyncFromAdminDbFileAsync(requestedLang, errors);
                if (syncedFromFile)
                {
                    LastError = null;
                    return true;
                }

                if (errors.Count > 0)
                {
                    LastError = string.Join(" | ", errors);
                }
            }

            return false;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    private async Task<List<PoiSyncDto>?> TryFetchPoisAsync(string baseUrl, string requestedLang)
    {
        var endpoint = $"{baseUrl}/api/pois?lang={Uri.EscapeDataString(requestedLang)}";
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, endpoint);
            using var res = await _httpClient.SendAsync(req);
            if (res.IsSuccessStatusCode)
            {
                var pois = await res.Content.ReadFromJsonAsync<List<PoiSyncDto>>();
                if (pois is not null)
                {
                    return pois;
                }
            }
        }
        catch
        {
            // Ignore and try legacy endpoint below.
        }

        var legacyEndpoint = $"{baseUrl}/api/shops?lang={Uri.EscapeDataString(requestedLang)}";
        using var legacyReq = new HttpRequestMessage(HttpMethod.Get, legacyEndpoint);
        using var legacyRes = await _httpClient.SendAsync(legacyReq);
        if (!legacyRes.IsSuccessStatusCode)
        {
            return null;
        }

        var legacy = await legacyRes.Content.ReadFromJsonAsync<List<ShopSyncDto>>();
        if (legacy is null)
        {
            return null;
        }

        return legacy.Select(x => new PoiSyncDto
        {
            Id = x.Id,
            LangCode = requestedLang,
            Name = x.ShopName,
            Latitude = x.Latitude,
            Longitude = x.Longitude,
            RadiusMeters = x.RadiusMeters,
            Priority = 0,
            MapLink = $"https://maps.google.com/?q={x.Latitude},{x.Longitude}",
            Description = x.Description,
            ImageUrl = x.ImageUrl,
            AudioUrl = x.AudioUrl,
            TtsText = x.TtsText
        }).ToList();
    }

    private static string? NormalizeAppLanguageCode(string? languageCode)
        => AppLanguageService.NormalizeLanguageCode(languageCode);

    public async Task<bool> UpsertRemoteAsync(ShopUpsertRequest request)
    {
        LastError = null;
        var errors = new List<string>();
        foreach (var baseUrl in GetPreferredBaseUrls())
        {
            try
            {
                var endpoint = $"{baseUrl}/api/shops/upsert";
                var payload = new ShopUpsertPayload
                {
                    Id = request.Id,
                    ShopName = request.ShopName,
                    Latitude = request.Latitude,
                    Longitude = request.Longitude,
                    RadiusMeters = request.RadiusMeters,
                    Description = request.Description,
                    TtsText = request.TtsText
                };

                var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
                if (!response.IsSuccessStatusCode)
                {
                    errors.Add($"{baseUrl}: {(int)response.StatusCode} {response.ReasonPhrase}");
                    continue;
                }

                _lastSuccessfulBaseUrl = baseUrl;
                LastError = null;
                return true;
            }
            catch (Exception ex)
            {
                errors.Add($"{baseUrl}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            LastError = string.Join(" | ", errors);
        }

        return false;
    }

    public async Task<bool> DeleteRemoteAsync(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        LastError = null;
        var errors = new List<string>();
        foreach (var baseUrl in GetPreferredBaseUrls())
        {
            try
            {
                var endpoint = $"{baseUrl}/api/shops/{Uri.EscapeDataString(id)}";
                var response = await _httpClient.DeleteAsync(endpoint);
                if (response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    var connection = await _database.GetConnectionAsync();
                    await connection.ExecuteAsync("UPDATE pois SET is_active = 0 WHERE id = ?;", id);
                    _lastSuccessfulBaseUrl = baseUrl;
                    LastError = null;
                    return true;
                }

                errors.Add($"{baseUrl}: {(int)response.StatusCode} {response.ReasonPhrase}");
            }
            catch (Exception ex)
            {
                errors.Add($"{baseUrl}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            LastError = string.Join(" | ", errors);
        }

        return false;
    }

    private async Task ApplyRemoteDataAsync(string baseUrl, string requestedLang, IReadOnlyList<PoiSyncDto> pois)
    {
        var connection = await _database.GetConnectionAsync();

        await connection.RunInTransactionAsync(conn =>
        {
            // Deactivate all first (This handles deletions on server)
            conn.Execute("UPDATE pois SET is_active = 0;");

            foreach (var poi in pois)
            {
                var normalizedName = string.IsNullOrWhiteSpace(poi.Name)
                    ? poi.Id
                    : poi.Name.Trim();
                var normalizedDescription = poi.Description?.Trim() ?? string.Empty;
                var normalizedAudioUrl = NormalizeAssetUrl(baseUrl, poi.AudioUrl);
                var normalizedTtsText = poi.TtsText?.Trim() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(normalizedTtsText) && string.IsNullOrWhiteSpace(normalizedAudioUrl) && !string.IsNullOrWhiteSpace(normalizedDescription))
                {
                    normalizedTtsText = normalizedDescription;
                }

                conn.InsertOrReplace(new PoiEntity
                {
                    Id = poi.Id,
                    Latitude = poi.Latitude,
                    Longitude = poi.Longitude,
                    RadiusMeters = poi.RadiusMeters,
                    Priority = poi.Priority,
                    MapLink = !string.IsNullOrWhiteSpace(poi.MapLink)
                        ? poi.MapLink
                        : $"https://maps.google.com/?q={poi.Latitude},{poi.Longitude}",
                    ImageUrl = NormalizeAssetUrl(baseUrl, poi.ImageUrl),
                    AudioUrl = normalizedAudioUrl,
                    Price = poi.Price > 0 ? poi.Price : 0,
                    IsPaid = poi.IsPaid,
                    IsActive = true
                });

                const string upsertTranslationSql = """
                    INSERT INTO poi_translations (poi_id, lang_code, name, description, tts_text)
                    VALUES (?, ?, ?, ?, ?)
                    ON CONFLICT(poi_id, lang_code) DO UPDATE SET
                        name = excluded.name,
                        description = excluded.description,
                        tts_text = excluded.tts_text;
                    """;
                conn.Execute(
                    upsertTranslationSql,
                    poi.Id,
                    requestedLang,
                    normalizedName,
                    normalizedDescription,
                    normalizedTtsText);
            }
        });
    }

    private IEnumerable<string> GetPreferredBaseUrls()
    {
        var list = new List<string>();
        list.Add("https://foodstreet-ry06.onrender.com");

        var configured = GetConfiguredBaseUrls();
        var configuredUrls = new List<string>();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var item in configured
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                configuredUrls.Add(NormalizeBaseUrl(item));
            }
        }

        if (!string.IsNullOrWhiteSpace(_lastSuccessfulBaseUrl))
        {
            var normalizedLastSuccess = NormalizeBaseUrl(_lastSuccessfulBaseUrl);
            if (configuredUrls.Count == 0 || configuredUrls.Contains(normalizedLastSuccess, StringComparer.OrdinalIgnoreCase))
            {
                list.Add(normalizedLastSuccess);
            }
        }

        if (configuredUrls.Count > 0)
        {
            list.AddRange(configuredUrls);
            return list
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        var configuredRaw = Environment.GetEnvironmentVariable("FOODSTREET_ADMIN_BASE_URLS");
        if (!string.IsNullOrWhiteSpace(configuredRaw))
        {
            foreach (var item in configuredRaw
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(NormalizeBaseUrl(item));
            }
        }

        var defaultPorts = new[] { 5187, 5000, 5001 };
#if ANDROID
        foreach (var port in defaultPorts)
        {
            list.Add($"http://10.0.2.2:{port}");
            list.Add($"http://10.0.3.2:{port}");
        }
#endif
        foreach (var port in defaultPorts)
        {
            list.Add($"http://localhost:{port}");
        }

        return list
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private async Task<bool> TrySyncFromAdminDbFileAsync(string requestedLang, List<string> errors)
    {
        var dbPath = ResolveAdminDbPath();
        if (string.IsNullOrWhiteSpace(dbPath))
        {
            errors.Add("db-file: not found (set FOODSTREET_ADMIN_DB_PATH if needed)");
            return false;
        }

        try
        {
            var sourceConnection = new SQLiteAsyncConnection(
                dbPath,
                SQLiteOpenFlags.ReadOnly | SQLiteOpenFlags.SharedCache);

            var sourcePois = await sourceConnection.Table<PoiEntity>()
                .Where(x => x.IsActive)
                .ToListAsync();

            var sourceTranslations = await sourceConnection.Table<PoiTranslationEntity>()
                .Where(x => x.LangCode == requestedLang || x.LangCode == "vi")
                .ToListAsync();

            var byPoiId = sourceTranslations
                .GroupBy(x => x.PoiId)
                .ToDictionary(x => x.Key, x => SelectBestTranslation(x.ToList(), requestedLang));

            var pois = new List<PoiSyncDto>(sourcePois.Count);
            foreach (var poi in sourcePois)
            {
                byPoiId.TryGetValue(poi.Id, out var translation);
                pois.Add(new PoiSyncDto
                {
                    Id = poi.Id,
                    LangCode = requestedLang,
                    Name = translation?.Name ?? poi.Id,
                    Latitude = poi.Latitude,
                    Longitude = poi.Longitude,
                    RadiusMeters = poi.RadiusMeters,
                    Priority = poi.Priority,
                    MapLink = poi.MapLink,
                    ImageUrl = poi.ImageUrl,
                    AudioUrl = poi.AudioUrl,
                    Price = poi.Price,
                    IsPaid = poi.IsPaid,
                    Description = translation?.Description ?? string.Empty,
                    TtsText = !string.IsNullOrWhiteSpace(translation?.TtsText)
                        ? translation!.TtsText
                        : translation?.Description ?? string.Empty
                });
            }

            await ApplyRemoteDataAsync("http://localhost:5187", requestedLang, pois);
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"db-file({dbPath}): {ex.Message}");
            return false;
        }
    }

    private static string? ResolveAdminDbPath()
    {
        var configured = Environment.GetEnvironmentVariable("FOODSTREET_ADMIN_DB_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return configured.Trim();
        }

        var searchRoots = new[]
        {
            AppContext.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var root in searchRoots.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            var current = root;
            for (var i = 0; i < 12 && !string.IsNullOrWhiteSpace(current); i++)
            {
                var candidate = Path.Combine(current, "FoodStreetPoiAdmin", "App_Data", "poi-admin.db3");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var parent = Directory.GetParent(current);
                if (parent is null)
                {
                    break;
                }

                current = parent.FullName;
            }
        }

        return null;
    }

    private static string NormalizeBaseUrl(string baseUrl)
        => baseUrl.Trim().TrimEnd('/');

    private static string NormalizeAssetUrl(string baseUrl, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return $"{baseUrl}{value}";
    }

    public string GetSessionId()
    {
        var sid = Preferences.Get("session_id", string.Empty);
        if (string.IsNullOrWhiteSpace(sid))
        {
            sid = $"app_{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}_{Guid.NewGuid():N}";
            Preferences.Set("session_id", sid);
        }
        return sid;
    }

    public async Task TrackActivityAsync(string action, string? poiId = null, string langCode = "vi", int? duration = null)
    {
        var sid = GetSessionId();
        foreach (var baseUrl in GetPreferredBaseUrls())
        {
            try
            {
                var endpoint = $"{baseUrl}/api/public/pois/track-activity";
                var payload = new
                {
                    action = action,
                    platform = "app",
                    sessionId = sid,
                    language = langCode,
                    poiId = poiId,
                    deviceType = "mobile",
                    duration = duration
                };
                var response = await _httpClient.PostAsJsonAsync(endpoint, payload);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch
            {
                // Ignore and try next
            }
        }
    }

    public sealed class ShopUpsertRequest
    {
        public string? Id { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; } = 40;
        public string Description { get; set; } = string.Empty;
        public string TtsText { get; set; } = string.Empty;
    }

    private sealed class ShopUpsertPayload
    {
        public string? Id { get; set; }
        public string ShopName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        public string Description { get; set; } = string.Empty;
        public string TtsText { get; set; } = string.Empty;
    }

    private sealed class PoiSyncDto
    {
        public string Id { get; set; } = string.Empty;
        public string LangCode { get; set; } = "vi";
        public string Name { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        public int Priority { get; set; }
        public string? MapLink { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? AudioUrl { get; set; }
        public string? TtsText { get; set; }
        public double Price { get; set; }
        public bool IsPaid { get; set; }
    }

    private sealed class ShopSyncDto
    {
        public string Id { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public string? AudioUrl { get; set; }
        public string? TtsText { get; set; }
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


