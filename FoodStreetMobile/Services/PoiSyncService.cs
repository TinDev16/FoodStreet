using System.Net.Http.Json;
using FoodStreetMobile.Models;
using Microsoft.Maui.Storage;
using SQLite;

namespace FoodStreetMobile.Services;

public sealed class PoiSyncService
{
    private const string BaseUrlsPreferenceKey = "admin_base_urls";
    private readonly AppDatabase _database;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private string? _lastSuccessfulBaseUrl;
    public string? LastError { get; private set; }

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

    public async Task<bool> TrySyncAsync()
    {
        LastError = null;
        var errors = new List<string>();
        foreach (var baseUrl in GetPreferredBaseUrls())
        {
            try
            {
                var endpoint = $"{baseUrl}/api/shops";
                var shops = await _httpClient.GetFromJsonAsync<List<ShopSyncDto>>(endpoint);
                if (shops is null)
                {
                    errors.Add($"{baseUrl}: empty response");
                    continue;
                }

                await ApplyRemoteDataAsync(baseUrl, shops);
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
            var syncedFromFile = await TrySyncFromAdminDbFileAsync(errors);
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

    private async Task ApplyRemoteDataAsync(string baseUrl, IReadOnlyList<ShopSyncDto> shops)
    {
        var connection = await _database.GetConnectionAsync();
        await connection.ExecuteAsync("UPDATE pois SET is_active = 0;");

        foreach (var shop in shops)
        {
            await connection.InsertOrReplaceAsync(new PoiEntity
            {
                Id = shop.Id,
                Latitude = shop.Latitude,
                Longitude = shop.Longitude,
                RadiusMeters = shop.RadiusMeters,
                Priority = 0,
                MapLink = $"https://maps.google.com/?q={shop.Latitude},{shop.Longitude}",
                ImageUrl = string.Empty,
                AudioUrl = NormalizeAudioUrl(baseUrl, shop.AudioUrl),
                IsActive = true
            });

            await connection.InsertOrReplaceAsync(new PoiTranslationEntity
            {
                PoiId = shop.Id,
                LangCode = "vi",
                Name = shop.ShopName,
                Description = shop.Description ?? string.Empty,
                TtsText = shop.TtsText ?? string.Empty
            });
        }
    }

    private IEnumerable<string> GetPreferredBaseUrls()
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(_lastSuccessfulBaseUrl))
        {
            list.Add(NormalizeBaseUrl(_lastSuccessfulBaseUrl));
        }

        var configured = GetConfiguredBaseUrls();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            foreach (var item in configured
                .Split(new[] { ',', ';', ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                list.Add(NormalizeBaseUrl(item));
            }
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

    private async Task<bool> TrySyncFromAdminDbFileAsync(List<string> errors)
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
                .Where(x => x.LangCode == "vi")
                .ToListAsync();

            var byPoiId = sourceTranslations
                .GroupBy(x => x.PoiId)
                .ToDictionary(x => x.Key, x => x.First());

            var shops = new List<ShopSyncDto>(sourcePois.Count);
            foreach (var poi in sourcePois)
            {
                byPoiId.TryGetValue(poi.Id, out var translation);
                shops.Add(new ShopSyncDto
                {
                    Id = poi.Id,
                    ShopName = translation?.Name ?? poi.Id,
                    Latitude = poi.Latitude,
                    Longitude = poi.Longitude,
                    RadiusMeters = poi.RadiusMeters,
                    Description = translation?.Description ?? string.Empty,
                    AudioUrl = poi.AudioUrl,
                    TtsText = translation?.TtsText ?? string.Empty
                });
            }

            await ApplyRemoteDataAsync("http://localhost:5187", shops);
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

    private static string NormalizeAudioUrl(string baseUrl, string? value)
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

    private sealed class ShopSyncDto
    {
        public string Id { get; set; } = string.Empty;
        public string ShopName { get; set; } = string.Empty;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public double RadiusMeters { get; set; }
        public string? Description { get; set; }
        public string? AudioUrl { get; set; }
        public string? TtsText { get; set; }
    }
}
