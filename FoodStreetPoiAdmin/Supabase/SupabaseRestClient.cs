using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace FoodStreetPoiAdmin.Supabase;

public sealed class SupabaseRestClient
{
    private readonly HttpClient _http;
    private readonly JsonSerializerOptions _jsonOptions;

    public SupabaseRestClient(HttpClient http)
    {
        _http = http;
        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    }

    public async Task<T> GetAsync<T>(string pathAndQuery, CancellationToken cancellationToken = default)
    {
        using var res = await _http.GetAsync(pathAndQuery, cancellationToken);
        await EnsureSuccessAsync(res, pathAndQuery);
        var value = await res.Content.ReadFromJsonAsync<T>(_jsonOptions, cancellationToken);
        if (value is null)
        {
            throw new InvalidOperationException($"Supabase returned empty JSON for GET {pathAndQuery}.");
        }

        return value;
    }

    public async Task<IReadOnlyList<T>> GetListAsync<T>(string pathAndQuery, CancellationToken cancellationToken = default)
        => await GetAsync<List<T>>(pathAndQuery, cancellationToken);

    public async Task<TResponse?> PostAsync<TRequest, TResponse>(
        string pathAndQuery,
        TRequest body,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery)
        {
            Content = JsonContent.Create(body)
        };
        ApplyHeaders(req, headers);

        using var res = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(res, $"{req.Method} {pathAndQuery}");
        if (res.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await res.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    public async Task PostAsync<TRequest>(
        string pathAndQuery,
        TRequest body,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, pathAndQuery)
        {
            Content = JsonContent.Create(body)
        };
        ApplyHeaders(req, headers);

        using var res = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(res, $"{req.Method} {pathAndQuery}");
    }

    public async Task<TResponse?> PatchAsync<TRequest, TResponse>(
        string pathAndQuery,
        TRequest body,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), pathAndQuery)
        {
            Content = JsonContent.Create(body)
        };
        ApplyHeaders(req, headers);

        using var res = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(res, $"{req.Method} {pathAndQuery}");
        if (res.StatusCode == HttpStatusCode.NoContent)
        {
            return default;
        }

        return await res.Content.ReadFromJsonAsync<TResponse>(_jsonOptions, cancellationToken);
    }

    public async Task PatchAsync<TRequest>(
        string pathAndQuery,
        TRequest body,
        IReadOnlyDictionary<string, string>? headers = null,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(new HttpMethod("PATCH"), pathAndQuery)
        {
            Content = JsonContent.Create(body)
        };
        ApplyHeaders(req, headers);

        using var res = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(res, $"{req.Method} {pathAndQuery}");
    }

    public async Task DeleteAsync(string pathAndQuery, CancellationToken cancellationToken = default)
    {
        using var res = await _http.DeleteAsync(pathAndQuery, cancellationToken);
        await EnsureSuccessAsync(res, $"DELETE {pathAndQuery}");
    }

    public async Task<(IReadOnlyList<T> Items, long? TotalCount)> GetListWithCountAsync<T>(
        string pathAndQuery,
        CancellationToken cancellationToken = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, pathAndQuery);
        req.Headers.TryAddWithoutValidation("Prefer", "count=exact");

        using var res = await _http.SendAsync(req, cancellationToken);
        await EnsureSuccessAsync(res, $"GET {pathAndQuery}");
        var items = await res.Content.ReadFromJsonAsync<List<T>>(_jsonOptions, cancellationToken) ?? [];
        var total = TryParseTotalCount(res);
        return (items, total);
    }

    private static void ApplyHeaders(HttpRequestMessage request, IReadOnlyDictionary<string, string>? headers)
    {
        if (headers is null) return;
        foreach (var kv in headers)
        {
            request.Headers.Remove(kv.Key);
            request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
        }
    }

    private static long? TryParseTotalCount(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Content-Range", out var values))
        {
            return null;
        }

        // Example: "0-49/123"
        var raw = values.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var slash = raw.LastIndexOf('/');
        if (slash < 0 || slash == raw.Length - 1) return null;
        var totalPart = raw[(slash + 1)..].Trim();
        if (totalPart == "*") return null;
        return long.TryParse(totalPart, out var total) ? total : null;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage res, string op)
    {
        if (res.IsSuccessStatusCode) return;

        string body;
        try
        {
            body = await res.Content.ReadAsStringAsync();
        }
        catch
        {
            body = "<no body>";
        }

        throw new HttpRequestException($"Supabase request failed: {op} => {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
    }
}

