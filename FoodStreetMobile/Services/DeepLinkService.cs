namespace FoodStreetMobile.Services;

public sealed class DeepLinkService
{
    private readonly object _sync = new();
    private PendingPoiLink? _pendingPoiLink;
    public event Action? PendingPoiLinkQueued;

    public bool TryQueueFromUri(Uri uri)
    {
        if (uri is null)
        {
            return false;
        }

        if (!string.Equals(uri.Scheme, "foodstreet", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.Equals(uri.Host, "open-poi", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var values = ParseQuery(uri.Query);
        if (!values.TryGetValue("id", out var poiId) || string.IsNullOrWhiteSpace(poiId))
        {
            return false;
        }

        var lang = values.TryGetValue("lang", out var langRaw)
            ? NormalizeLanguageCode(langRaw)
            : string.Empty;

        lock (_sync)
        {
            _pendingPoiLink = new PendingPoiLink
            {
                PoiId = poiId.Trim(),
                LangCode = lang
            };
        }

        PendingPoiLinkQueued?.Invoke();
        return true;
    }

    public bool TryTakePendingPoiLink(out PendingPoiLink? link)
    {
        lock (_sync)
        {
            link = _pendingPoiLink;
            _pendingPoiLink = null;
            return link is not null;
        }
    }

    private static Dictionary<string, string> ParseQuery(string? query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var normalized = query[0] == '?' ? query[1..] : query;
        foreach (var pair in normalized.Split('&', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pivot = pair.IndexOf('=');
            if (pivot <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..pivot]);
            var value = Uri.UnescapeDataString(pair[(pivot + 1)..]);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            result[key] = value;
        }

        return result;
    }

    private static string NormalizeLanguageCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var cleaned = raw.Trim().ToLowerInvariant().Replace('_', '-');
        var parts = cleaned.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length == 0 ? string.Empty : parts[0];
    }
}

public sealed class PendingPoiLink
{
    public required string PoiId { get; init; }
    public string LangCode { get; init; } = string.Empty;
}
