using FoodStreetMobile.Models;
using Microsoft.Maui.Devices.Sensors;
using System.Globalization;
using System.Text.Json;

namespace FoodStreetMobile.Services;

public sealed class PlaceSearchService
{
    private const string GoogleMapsApiKey = "AIzaSyAg9cHLgybrf3Edkl8ZK9nuRuQpF9nzCNY";
    private static readonly HttpClient HttpClient = new();

    public async Task<List<SearchPlaceResult>> SearchAsync(
        string query,
        Location? userLocation,
        int maxResults,
        IReadOnlyList<PoiSearchCandidate>? poiCandidates,
        CancellationToken cancellationToken)
    {
        if (maxResults <= 0)
        {
            maxResults = 8;
        }

        if (TryParseCoordinateInput(query, out var coordinate))
        {
            return new List<SearchPlaceResult> { coordinate };
        }

        var remoteResults = await QueryGoogleAutocompleteAsync(query, maxResults, applyVnFilter: true, applyLocationBias: true, userLocation, cancellationToken);
        if (remoteResults.Count == 0)
        {
            remoteResults = await QueryGoogleAutocompleteAsync(query, maxResults, applyVnFilter: false, applyLocationBias: true, userLocation, cancellationToken);
        }

        if (remoteResults.Count == 0)
        {
            remoteResults = await QueryGoogleGeocodeAsync(query, maxResults, applyVnFilter: true, applyBoundedBias: true, userLocation, cancellationToken);
        }

        if (remoteResults.Count == 0)
        {
            remoteResults = await QueryGoogleGeocodeAsync(query, maxResults, applyVnFilter: false, applyBoundedBias: true, userLocation, cancellationToken);
        }

        if (remoteResults.Count == 0)
        {
            remoteResults = await QueryGoogleGeocodeAsync(query, maxResults, applyVnFilter: false, applyBoundedBias: false, userLocation, cancellationToken);
        }

        var merged = new List<SearchPlaceResult>();
        if (poiCandidates is not null)
        {
            foreach (var poi in poiCandidates)
            {
                merged.Add(new SearchPlaceResult
                {
                    PoiId = poi.PoiId,
                    Name = poi.Name,
                    Address = poi.Address,
                    Latitude = poi.Latitude,
                    Longitude = poi.Longitude,
                    ImageUrl = SanitizeImageUrl(poi.ImageUrl),
                    Importance = 1.15
                });
            }
        }

        merged.AddRange(remoteResults);
        return RankSearchResults(query, merged, userLocation, maxResults);
    }

    public async Task<SearchPlaceResult> ResolveAsync(
        SearchPlaceResult result,
        Location? userLocation,
        CancellationToken cancellationToken)
    {
        if (result.HasCoordinates || !string.IsNullOrWhiteSpace(result.PoiId))
        {
            return result;
        }

        if (!string.IsNullOrWhiteSpace(result.PlaceId))
        {
            var details = await QueryGooglePlaceDetailsAsync(result.PlaceId, cancellationToken);
            if (details is not null)
            {
                return details;
            }
        }

        var fallback = await QueryGoogleGeocodeAsync(result.Address, 1, applyVnFilter: false, applyBoundedBias: true, userLocation, cancellationToken);
        return fallback.FirstOrDefault() ?? result;
    }

    private async Task<List<SearchPlaceResult>> QueryGoogleAutocompleteAsync(
        string query,
        int limit,
        bool applyVnFilter,
        bool applyLocationBias,
        Location? userLocation,
        CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"input={Uri.EscapeDataString(query)}",
            $"language={Uri.EscapeDataString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)}",
            "types=establishment|geocode",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        if (applyVnFilter)
        {
            parameters.Add("components=country:vn");
        }

        if (applyLocationBias && userLocation is not null)
        {
            parameters.Add($"location={userLocation.Latitude.ToString(CultureInfo.InvariantCulture)},{userLocation.Longitude.ToString(CultureInfo.InvariantCulture)}");
            parameters.Add("radius=15000");
        }

        var url = $"https://maps.googleapis.com/maps/api/place/autocomplete/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<SearchPlaceResult>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal)
            && !string.Equals(status, "ZERO_RESULTS", StringComparison.Ordinal))
        {
            return new List<SearchPlaceResult>();
        }

        if (!document.RootElement.TryGetProperty("predictions", out var predictionsNode)
            || predictionsNode.ValueKind != JsonValueKind.Array)
        {
            return new List<SearchPlaceResult>();
        }

        var results = new List<SearchPlaceResult>();
        foreach (var item in predictionsNode.EnumerateArray())
        {
            if (!TryBuildAutocompleteResult(item, out var result))
            {
                continue;
            }

            results.Add(result);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<List<SearchPlaceResult>> QueryGoogleGeocodeAsync(
        string query,
        int limit,
        bool applyVnFilter,
        bool applyBoundedBias,
        Location? userLocation,
        CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"address={Uri.EscapeDataString(query)}",
            $"language={Uri.EscapeDataString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)}",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        if (applyVnFilter)
        {
            parameters.Add("components=country:VN");
            parameters.Add("region=vn");
        }

        if (applyBoundedBias && userLocation is not null)
        {
            const double delta = 0.08;
            var south = userLocation.Latitude - delta;
            var west = userLocation.Longitude - delta;
            var north = userLocation.Latitude + delta;
            var east = userLocation.Longitude + delta;
            var bounds = $"{south.ToString(CultureInfo.InvariantCulture)},{west.ToString(CultureInfo.InvariantCulture)}|{north.ToString(CultureInfo.InvariantCulture)},{east.ToString(CultureInfo.InvariantCulture)}";
            parameters.Add($"bounds={Uri.EscapeDataString(bounds)}");
        }

        var url = $"https://maps.googleapis.com/maps/api/geocode/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new List<SearchPlaceResult>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
        {
            return new List<SearchPlaceResult>();
        }

        if (!document.RootElement.TryGetProperty("results", out var resultsNode)
            || resultsNode.ValueKind != JsonValueKind.Array)
        {
            return new List<SearchPlaceResult>();
        }

        var results = new List<SearchPlaceResult>();
        foreach (var item in resultsNode.EnumerateArray())
        {
            if (!TryBuildGeocodeResult(item, out var result))
            {
                continue;
            }

            results.Add(result);
            if (results.Count >= limit)
            {
                break;
            }
        }

        return results;
    }

    private async Task<SearchPlaceResult?> QueryGooglePlaceDetailsAsync(string placeId, CancellationToken cancellationToken)
    {
        var parameters = new List<string>
        {
            $"place_id={Uri.EscapeDataString(placeId)}",
            $"language={Uri.EscapeDataString(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName)}",
            "fields=name,formatted_address,geometry,photos",
            $"key={Uri.EscapeDataString(GoogleMapsApiKey)}"
        };

        var url = $"https://maps.googleapis.com/maps/api/place/details/json?{string.Join("&", parameters)}";
        using var response = await HttpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var status = document.RootElement.TryGetProperty("status", out var statusNode) ? statusNode.GetString() : null;
        if (!string.Equals(status, "OK", StringComparison.Ordinal))
        {
            return null;
        }

        if (!document.RootElement.TryGetProperty("result", out var resultNode))
        {
            return null;
        }

        return TryBuildPlaceDetailsResult(resultNode, placeId, out var result) ? result : null;
    }

    private static bool TryBuildAutocompleteResult(JsonElement item, out SearchPlaceResult result)
    {
        result = null!;
        var placeId = item.TryGetProperty("place_id", out var placeIdNode) ? placeIdNode.GetString() : null;
        if (string.IsNullOrWhiteSpace(placeId))
        {
            return false;
        }

        var description = item.TryGetProperty("description", out var descriptionNode)
            ? descriptionNode.GetString() ?? string.Empty
            : string.Empty;
        var mainText = string.Empty;

        if (item.TryGetProperty("structured_formatting", out var formattingNode)
            && formattingNode.TryGetProperty("main_text", out var mainTextNode))
        {
            mainText = mainTextNode.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(mainText))
        {
            mainText = description.Split(',').FirstOrDefault()?.Trim() ?? "Dia diem";
        }

        result = new SearchPlaceResult
        {
            Name = mainText,
            Address = description,
            Latitude = double.NaN,
            Longitude = double.NaN,
            Importance = 1.0,
            PlaceId = placeId
        };

        return true;
    }

    private static bool TryBuildGeocodeResult(JsonElement item, out SearchPlaceResult result)
    {
        result = null!;
        if (!item.TryGetProperty("geometry", out var geometryNode)
            || !geometryNode.TryGetProperty("location", out var locationNode)
            || !locationNode.TryGetProperty("lat", out var latNode)
            || !locationNode.TryGetProperty("lng", out var lonNode))
        {
            return false;
        }

        var lat = latNode.GetDouble();
        var lon = lonNode.GetDouble();
        var address = item.TryGetProperty("formatted_address", out var addressNode)
            ? addressNode.GetString() ?? string.Empty
            : string.Empty;

        var name = address.Split(',').FirstOrDefault()?.Trim() ?? "Dia diem";
        result = new SearchPlaceResult
        {
            Name = name,
            Address = string.IsNullOrWhiteSpace(address) ? "Khong co dia chi" : address,
            Latitude = lat,
            Longitude = lon,
            Importance = 0.7,
            PlaceId = item.TryGetProperty("place_id", out var placeIdNode) ? placeIdNode.GetString() : null
        };
        return true;
    }

    private static bool TryBuildPlaceDetailsResult(JsonElement item, string placeId, out SearchPlaceResult result)
    {
        result = null!;
        if (!item.TryGetProperty("geometry", out var geometryNode)
            || !geometryNode.TryGetProperty("location", out var locationNode)
            || !locationNode.TryGetProperty("lat", out var latNode)
            || !locationNode.TryGetProperty("lng", out var lonNode))
        {
            return false;
        }

        var name = item.TryGetProperty("name", out var nameNode) ? nameNode.GetString() ?? "Dia diem" : "Dia diem";
        var address = item.TryGetProperty("formatted_address", out var addressNode) ? addressNode.GetString() ?? "Khong co dia chi" : "Khong co dia chi";

        string? photoUrl = null;
        if (item.TryGetProperty("photos", out var photosNode) && photosNode.ValueKind == JsonValueKind.Array)
        {
            var first = photosNode.EnumerateArray().FirstOrDefault();
            if (first.TryGetProperty("photo_reference", out var refNode))
            {
                var photoRef = refNode.GetString();
                if (!string.IsNullOrWhiteSpace(photoRef))
                {
                    photoUrl = $"https://maps.googleapis.com/maps/api/place/photo?maxwidth=900&photo_reference={Uri.EscapeDataString(photoRef)}&key={Uri.EscapeDataString(GoogleMapsApiKey)}";
                }
            }
        }

        result = new SearchPlaceResult
        {
            Name = name,
            Address = address,
            Latitude = latNode.GetDouble(),
            Longitude = lonNode.GetDouble(),
            Importance = 1.0,
            ImageUrl = SanitizeImageUrl(photoUrl),
            PlaceId = placeId
        };
        return true;
    }

    private static bool TryParseCoordinateInput(string query, out SearchPlaceResult result)
    {
        result = null!;
        var segments = query.Split(',', StringSplitOptions.TrimEntries);
        if (segments.Length != 2)
        {
            return false;
        }

        if (!double.TryParse(segments[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(segments[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var lon))
        {
            return false;
        }

        if (lat is < -90 or > 90 || lon is < -180 or > 180)
        {
            return false;
        }

        result = new SearchPlaceResult
        {
            Name = "Toa do",
            Address = $"{lat.ToString(CultureInfo.InvariantCulture)}, {lon.ToString(CultureInfo.InvariantCulture)}",
            Latitude = lat,
            Longitude = lon,
            Importance = 0.6
        };

        return true;
    }

    private static List<SearchPlaceResult> RankSearchResults(
        string query,
        IEnumerable<SearchPlaceResult> results,
        Location? userLocation,
        int maxResults)
    {
        var normalizedQuery = TextNormalizer.NormalizeForSearch(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return new List<SearchPlaceResult>();
        }

        var queryTokens = normalizedQuery
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var dedupe = new HashSet<string>(StringComparer.Ordinal);

        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.Name))
            .Select(r =>
            {
                var score = r.Importance;
                var normalizedName = TextNormalizer.NormalizeForSearch(r.Name);
                var normalizedAddress = TextNormalizer.NormalizeForSearch(r.Address);
                var nameMatch = GetPhraseMatchKind(normalizedName, normalizedQuery);
                var addressMatch = GetPhraseMatchKind(normalizedAddress, normalizedQuery);
                score += ScorePhraseMatch(nameMatch, forName: true);
                score += ScorePhraseMatch(addressMatch, forName: false);

                var nameCoverage = ComputeTokenCoverage(queryTokens, normalizedName, out var namePrefixCoverage);
                var addressCoverage = ComputeTokenCoverage(queryTokens, normalizedAddress, out var addressPrefixCoverage);
                score += nameCoverage * 2.6;
                score += addressCoverage * 1.0;
                score += namePrefixCoverage * 0.9;
                score += addressPrefixCoverage * 0.25;

                var nameIndex = normalizedName.IndexOf(normalizedQuery, StringComparison.Ordinal);
                if (nameIndex >= 0)
                {
                    score += Math.Max(0, 0.8 - (nameIndex * 0.03));
                }

                var isRelevant = nameMatch != TextMatchKind.None
                                 || addressMatch != TextMatchKind.None
                                 || nameCoverage > 0
                                 || addressCoverage > 0;
                if (!isRelevant)
                {
                    score -= 100;
                }

                if (userLocation is not null && r.HasCoordinates)
                {
                    var km = Location.CalculateDistance(userLocation.Latitude, userLocation.Longitude, r.Latitude, r.Longitude, DistanceUnits.Kilometers);
                    r.DistanceKm = km;
                    score += Math.Max(0, 1.0 - Math.Min(40, km) / 30d);
                }
                else
                {
                    r.DistanceKm = null;
                }

                if (!string.IsNullOrWhiteSpace(r.PoiId))
                {
                    score += 0.3;
                }

                var minCoverage = queryTokens.Length <= 1 ? 1.0 : 0.5;
                var hasStrongPhrase = nameMatch is TextMatchKind.Exact or TextMatchKind.StartsWith
                    || addressMatch is TextMatchKind.Exact or TextMatchKind.StartsWith;
                var hasEnoughCoverage = nameCoverage >= minCoverage || addressCoverage >= minCoverage;

                return new
                {
                    Result = r,
                    Score = score,
                    IsRelevant = isRelevant && (hasStrongPhrase || hasEnoughCoverage)
                };
            })
            .Where(x => x.IsRelevant)
            .Where(x => x.Score > 0.8)
            .OrderByDescending(x => x.Score)
            .ThenBy(x => x.Result.Name, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Result)
            .Where(r =>
            {
                var key = string.IsNullOrWhiteSpace(r.PoiId)
                    ? $"{TextNormalizer.NormalizeForSearch(r.Name)}|{TextNormalizer.NormalizeForSearch(r.Address)}"
                    : $"poi|{r.PoiId}";
                return dedupe.Add(key);
            })
            .Take(maxResults)
            .ToList();
    }

    private enum TextMatchKind
    {
        None = 0,
        Contains = 1,
        StartsWith = 2,
        Exact = 3
    }

    private static TextMatchKind GetPhraseMatchKind(string target, string query)
    {
        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(query))
        {
            return TextMatchKind.None;
        }

        if (string.Equals(target, query, StringComparison.Ordinal))
        {
            return TextMatchKind.Exact;
        }

        if (target.StartsWith(query, StringComparison.Ordinal))
        {
            return TextMatchKind.StartsWith;
        }

        return target.Contains(query, StringComparison.Ordinal)
            ? TextMatchKind.Contains
            : TextMatchKind.None;
    }

    private static double ScorePhraseMatch(TextMatchKind matchKind, bool forName)
    {
        if (forName)
        {
            return matchKind switch
            {
                TextMatchKind.Exact => 8.5,
                TextMatchKind.StartsWith => 5.0,
                TextMatchKind.Contains => 2.2,
                _ => 0
            };
        }

        return matchKind switch
        {
            TextMatchKind.Exact => 3.0,
            TextMatchKind.StartsWith => 1.6,
            TextMatchKind.Contains => 0.9,
            _ => 0
        };
    }

    private static double ComputeTokenCoverage(
        IReadOnlyList<string> queryTokens,
        string target,
        out double prefixCoverage)
    {
        prefixCoverage = 0;
        if (queryTokens.Count == 0 || string.IsNullOrWhiteSpace(target))
        {
            return 0;
        }

        var targetTokens = target
            .Split(' ', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var matched = 0;
        var prefixMatched = 0;

        foreach (var token in queryTokens)
        {
            var exactOrPrefix = targetTokens.Any(t => t.StartsWith(token, StringComparison.Ordinal));
            if (exactOrPrefix)
            {
                matched++;
                prefixMatched++;
                continue;
            }

            if (target.Contains(token, StringComparison.Ordinal))
            {
                matched++;
            }
        }

        prefixCoverage = prefixMatched / (double)queryTokens.Count;
        return matched / (double)queryTokens.Count;
    }

    private static string? SanitizeImageUrl(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var value = raw.Trim();
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return null;
        }

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return value;
    }
}

