using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Net.Http.Json;

static string NormalizeSupabaseBaseUrl(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        throw new InvalidOperationException("Missing SUPABASE_URL.");
    }

    if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException($"Invalid SUPABASE_URL: {raw}");
    }

    var normalized = new UriBuilder(uri)
    {
        Path = string.Empty,
        Query = string.Empty,
        Fragment = string.Empty
    }.Uri.ToString().TrimEnd('/');

    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new InvalidOperationException($"Invalid SUPABASE_URL after normalization: {raw}");
    }

    return normalized;
}

static string RequireEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name)?.Trim();
    if (string.IsNullOrWhiteSpace(value))
    {
        throw new InvalidOperationException($"Missing env var: {name}");
    }
    return value;
}

var supabaseUrl = NormalizeSupabaseBaseUrl(RequireEnv("SUPABASE_URL"));
var supabaseKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY")?.Trim()
                  ?? Environment.GetEnvironmentVariable("SUPABASE_KEY")?.Trim();
if (string.IsNullOrWhiteSpace(supabaseKey))
{
    throw new InvalidOperationException("Missing SUPABASE_SERVICE_ROLE_KEY.");
}

var sqlite = new SqliteConnection("Data Source=poi-admin.db3");
sqlite.Open();

using var http = new HttpClient();
http.DefaultRequestHeaders.Remove("apikey");
http.DefaultRequestHeaders.Remove("Authorization");
http.DefaultRequestHeaders.Add("apikey", supabaseKey);
http.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseKey}");

var restBase = $"{supabaseUrl}/rest/v1";

async Task Migrate(string tableName)
{
    using var cmd = sqlite.CreateCommand();
    cmd.CommandText = $"SELECT * FROM {tableName}";

    using var reader = cmd.ExecuteReader();
    var list = new List<Dictionary<string, object?>>();

    while (reader.Read())
    {
        var row = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < reader.FieldCount; i++)
        {
            var name = reader.GetName(i);
            var value = reader.GetValue(i);
            row[name] = value == DBNull.Value ? null : value;
        }

        list.Add(row);
    }

    Console.WriteLine($"Uploading {tableName} ({list.Count.ToString(CultureInfo.InvariantCulture)} records)...");
    using var res = await http.PostAsJsonAsync($"{restBase}/{tableName}", list);
    if (!res.IsSuccessStatusCode)
    {
        var body = await res.Content.ReadAsStringAsync();
        throw new InvalidOperationException($"Supabase upload failed for {tableName}: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
    }
    Console.WriteLine($"Done {tableName}");
}

// Run in FK-safe order
await Migrate("pois");
await Migrate("admin_accounts");
await Migrate("poi_translations");
await Migrate("active_sessions");
await Migrate("user_activity_events");
await Migrate("audio_tts_queue");
await Migrate("poi_audio_cache");

Console.WriteLine("ALL DONE");
