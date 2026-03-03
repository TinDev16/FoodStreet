using Microsoft.Data.Sqlite;
using System.Globalization;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

var hasExplicitUrlsArg = args.Any(x => x.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) && !hasExplicitUrlsArg)
{
    // Allow emulator/physical devices on same LAN to reach the admin API.
    builder.WebHost.UseUrls("http://0.0.0.0:5187");
}

var app = builder.Build();

var dataDirectory = Path.Combine(app.Environment.ContentRootPath, "App_Data");
var uploadDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(uploadDirectory);

var dbPath = Path.Combine(dataDirectory, "poi-admin.db3");
var connectionString = $"Data Source={dbPath}";
var adbReverseSync = new object();
var lastAdbReverseAttemptUtc = DateTimeOffset.MinValue;

await InitializeDatabaseAsync(connectionString);

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/shops", async (HttpContext context) =>
{
    await TryEnsureAdbReverseAsync();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            p.id, p.latitude, p.longitude, p.radius_meters, p.image_url, p.audio_url,
            t.name, t.description, t.tts_text
        FROM pois p
        LEFT JOIN poi_translations t ON p.id = t.poi_id AND t.lang_code = 'vi'
        ORDER BY p.priority DESC, p.id ASC;
        """;

    var result = new List<ShopDto>();
    await using var command = new SqliteCommand(sql, connection);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var imageUrl = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
        var audioUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
        result.Add(new ShopDto
        {
            Id = reader.GetString(0),
            Latitude = reader.GetDouble(1),
            Longitude = reader.GetDouble(2),
            RadiusMeters = reader.GetDouble(3),
            ImageUrl = imageUrl,
            AudioUrl = audioUrl,
            ShopName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            Description = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            TtsText = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
        });
    }

    return Results.Ok(result);
});

app.MapGet("/api/shops/{id}", async (string id) =>
{
    await TryEnsureAdbReverseAsync();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        SELECT
            p.id, p.latitude, p.longitude, p.radius_meters, p.image_url, p.audio_url,
            t.name, t.description, t.tts_text
        FROM pois p
        LEFT JOIN poi_translations t ON p.id = t.poi_id AND t.lang_code = 'vi'
        WHERE p.id = $id;
        """;

    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$id", id);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return Results.NotFound();
    }

    var imageUrl = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);
    var audioUrl = reader.IsDBNull(5) ? string.Empty : reader.GetString(5);
    var item = new ShopDto
    {
        Id = reader.GetString(0),
        Latitude = reader.GetDouble(1),
        Longitude = reader.GetDouble(2),
        RadiusMeters = reader.GetDouble(3),
        ImageUrl = imageUrl,
        AudioUrl = audioUrl,
        ShopName = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        Description = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        TtsText = reader.IsDBNull(8) ? string.Empty : reader.GetString(8)
    };
    return Results.Ok(item);
});

app.MapPost("/api/shops", async (HttpRequest request) =>
{
    await TryEnsureAdbReverseAsync();
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Request must be multipart/form-data." });
    }

    var form = await request.ReadFormAsync();

    var id = form["id"].ToString().Trim();
    var shopName = form["shopName"].ToString().Trim();
    var gpsRaw = form["gps"].ToString().Trim();
    var description = form["description"].ToString().Trim();
    var ttsText = form["ttsText"].ToString().Trim();
    var imageUrlInput = form["imageUrl"].ToString().Trim();
    var clearAudioRequested = ParseBooleanForm(form["clearAudio"]);
    var clearTtsRequested = ParseBooleanForm(form["clearTts"]);
    var clearImageRequested = ParseBooleanForm(form["clearImage"]);

    if (string.IsNullOrWhiteSpace(shopName))
    {
        return Results.BadRequest(new { error = "Ten shop bat buoc." });
    }

    if (!TryParseGps(gpsRaw, out var latitude, out var longitude))
    {
        return Results.BadRequest(new { error = "GPS khong hop le. Dung dang 'lat, lon'." });
    }

    if (!double.TryParse(form["radiusMeters"], NumberStyles.Float, CultureInfo.InvariantCulture, out var radiusMeters) || radiusMeters <= 0)
    {
        return Results.BadRequest(new { error = "Radius (m) phai lon hon 0." });
    }

    var audioFile = form.Files.GetFile("audioFile");
    var imageFile = form.Files.GetFile("imageFile");
    string existingAudioUrl = string.Empty;
    string existingImageUrl = string.Empty;
    string existingTtsText = string.Empty;

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    if (!string.IsNullOrWhiteSpace(id))
    {
        const string sqlCurrent = """
            SELECT p.audio_url, p.image_url, t.tts_text
            FROM pois p
            LEFT JOIN poi_translations t ON p.id = t.poi_id AND t.lang_code = 'vi'
            WHERE p.id = $id;
            """;
        await using var currentCommand = new SqliteCommand(sqlCurrent, connection);
        currentCommand.Parameters.AddWithValue("$id", id);
        await using var currentReader = await currentCommand.ExecuteReaderAsync();
        if (await currentReader.ReadAsync())
        {
            existingAudioUrl = currentReader.IsDBNull(0) ? string.Empty : currentReader.GetString(0);
            existingImageUrl = currentReader.IsDBNull(1) ? string.Empty : currentReader.GetString(1);
            existingTtsText = currentReader.IsDBNull(2) ? string.Empty : currentReader.GetString(2);
        }
    }

    if (!string.IsNullOrWhiteSpace(imageUrlInput) && imageFile is not null && imageFile.Length > 0)
    {
        return Results.BadRequest(new { error = "Chi duoc chon 1 trong 2: link anh hoac upload anh." });
    }

    var finalAudioUrl = clearAudioRequested ? string.Empty : existingAudioUrl;
    var finalTtsText = clearTtsRequested ? string.Empty : existingTtsText;
    var finalImageUrl = clearImageRequested ? string.Empty : existingImageUrl;

    if (audioFile is not null && audioFile.Length > 0)
    {
        if (!string.IsNullOrWhiteSpace(ttsText) || !string.IsNullOrWhiteSpace(finalTtsText))
        {
            return Results.BadRequest(new { error = "Audio va TTS chi duoc chon 1. Hay xoa TTS truoc." });
        }

        var ext = Path.GetExtension(audioFile.FileName);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadDirectory, fileName);
        await using var fs = File.Create(fullPath);
        await audioFile.CopyToAsync(fs);
        if (!string.IsNullOrWhiteSpace(existingAudioUrl) && !string.Equals(existingAudioUrl, $"/uploads/{fileName}", StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteUploadedFile(uploadDirectory, existingAudioUrl);
        }

        finalAudioUrl = $"/uploads/{fileName}";
        finalTtsText = string.Empty;
    }
    else if (!string.IsNullOrWhiteSpace(ttsText))
    {
        if (!string.IsNullOrWhiteSpace(finalAudioUrl))
        {
            return Results.BadRequest(new { error = "Audio va TTS chi duoc chon 1. Hay xoa audio truoc." });
        }

        finalTtsText = ttsText;
    }

    if (clearAudioRequested && !string.IsNullOrWhiteSpace(existingAudioUrl))
    {
        TryDeleteUploadedFile(uploadDirectory, existingAudioUrl);
    }

    if (imageFile is not null && imageFile.Length > 0)
    {
        var ext = Path.GetExtension(imageFile.FileName);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(uploadDirectory, fileName);
        await using var fs = File.Create(fullPath);
        await imageFile.CopyToAsync(fs);
        if (!string.IsNullOrWhiteSpace(existingImageUrl) && !string.Equals(existingImageUrl, $"/uploads/{fileName}", StringComparison.OrdinalIgnoreCase))
        {
            TryDeleteUploadedFile(uploadDirectory, existingImageUrl);
        }

        finalImageUrl = $"/uploads/{fileName}";
    }
    else if (!string.IsNullOrWhiteSpace(imageUrlInput))
    {
        finalImageUrl = imageUrlInput;
    }

    if (clearImageRequested && !string.IsNullOrWhiteSpace(existingImageUrl))
    {
        TryDeleteUploadedFile(uploadDirectory, existingImageUrl);
    }

    if (string.IsNullOrWhiteSpace(id))
    {
        id = Guid.NewGuid().ToString("N");
    }

    var mapLink = $"https://maps.google.com/?q={latitude},{longitude}";

    await using var transaction = await connection.BeginTransactionAsync();
    await UpsertPoiAsync(connection, transaction, id, latitude, longitude, radiusMeters, mapLink, finalImageUrl, finalAudioUrl);
    await UpsertTranslationAsync(connection, transaction, id, shopName, description, finalTtsText);
    await transaction.CommitAsync();

    return Results.Ok(new { id });
});

app.MapPost("/api/shops/upsert", async (ShopUpsertJsonRequest request) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(request.ShopName))
    {
        return Results.BadRequest(new { error = "Ten shop bat buoc." });
    }

    if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
    {
        return Results.BadRequest(new { error = "Latitude/Longitude khong hop le." });
    }

    if (request.RadiusMeters <= 0)
    {
        return Results.BadRequest(new { error = "Radius (m) phai lon hon 0." });
    }

    var id = string.IsNullOrWhiteSpace(request.Id)
        ? Guid.NewGuid().ToString("N")
        : request.Id.Trim();

    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    var currentAudioUrl = string.Empty;
    var currentImageUrl = string.Empty;
    await using (var oldAudioCommand = new SqliteCommand("SELECT audio_url, image_url FROM pois WHERE id = $id;", connection))
    {
        oldAudioCommand.Parameters.AddWithValue("$id", id);
        await using var reader = await oldAudioCommand.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            currentAudioUrl = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            currentImageUrl = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }
    }

    var finalTtsText = request.TtsText?.Trim() ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(currentAudioUrl) && !string.IsNullOrWhiteSpace(finalTtsText))
    {
        return Results.BadRequest(new { error = "POI dang co audio file. Hay xoa audio truoc khi nhap TTS." });
    }

    var mapLink = $"https://maps.google.com/?q={request.Latitude},{request.Longitude}";
    await using var transaction = await connection.BeginTransactionAsync();
    await UpsertPoiAsync(connection, transaction, id, request.Latitude, request.Longitude, request.RadiusMeters, mapLink, currentImageUrl, currentAudioUrl);
    await UpsertTranslationAsync(
        connection,
        transaction,
        id,
        request.ShopName.Trim(),
        request.Description?.Trim() ?? string.Empty,
        finalTtsText);
    await transaction.CommitAsync();

    return Results.Ok(new { id });
});

app.MapDelete("/api/shops/{id}", async (string id) =>
{
    await TryEnsureAdbReverseAsync();
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var transaction = await connection.BeginTransactionAsync();
    var oldAudioUrl = string.Empty;
    var oldImageUrl = string.Empty;

    await using (var loadAssets = new SqliteCommand("SELECT audio_url, image_url FROM pois WHERE id = $id;", connection))
    {
        loadAssets.Transaction = (SqliteTransaction)transaction;
        loadAssets.Parameters.AddWithValue("$id", id);
        await using var reader = await loadAssets.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            oldAudioUrl = reader.IsDBNull(0) ? string.Empty : reader.GetString(0);
            oldImageUrl = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
        }
    }

    await using (var deleteTranslations = new SqliteCommand("DELETE FROM poi_translations WHERE poi_id = $id;", connection))
    {
        deleteTranslations.Transaction = (SqliteTransaction)transaction;
        deleteTranslations.Parameters.AddWithValue("$id", id);
        await deleteTranslations.ExecuteNonQueryAsync();
    }

    int affected;
    await using (var deletePoi = new SqliteCommand("DELETE FROM pois WHERE id = $id;", connection))
    {
        deletePoi.Transaction = (SqliteTransaction)transaction;
        deletePoi.Parameters.AddWithValue("$id", id);
        affected = await deletePoi.ExecuteNonQueryAsync();
    }

    await transaction.CommitAsync();
    if (affected > 0)
    {
        TryDeleteUploadedFile(uploadDirectory, oldAudioUrl);
        TryDeleteUploadedFile(uploadDirectory, oldImageUrl);
    }

    return affected == 0 ? Results.NotFound() : Results.NoContent();
});

app.Run();

async Task TryEnsureAdbReverseAsync()
{
    var now = DateTimeOffset.UtcNow;
    lock (adbReverseSync)
    {
        if (now - lastAdbReverseAttemptUtc < TimeSpan.FromSeconds(20))
        {
            return;
        }

        lastAdbReverseAttemptUtc = now;
    }

    try
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = "reverse tcp:5187 tcp:5187",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
    }
    catch
    {
        // Best-effort only; do not block admin APIs when adb is unavailable.
    }
}

static bool TryParseGps(string raw, out double latitude, out double longitude)
{
    latitude = 0;
    longitude = 0;

    if (string.IsNullOrWhiteSpace(raw))
    {
        return false;
    }

    var parts = raw.Split(',', StringSplitOptions.TrimEntries);
    if (parts.Length != 2)
    {
        return false;
    }

    if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out latitude)
        || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out longitude))
    {
        return false;
    }

    if (latitude is < -90 or > 90 || longitude is < -180 or > 180)
    {
        return false;
    }

    return true;
}

static bool ParseBooleanForm(Microsoft.Extensions.Primitives.StringValues values)
{
    var raw = values.ToString();
    if (string.IsNullOrWhiteSpace(raw))
    {
        return false;
    }

    return raw.Equals("1", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("true", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("on", StringComparison.OrdinalIgnoreCase)
        || raw.Equals("yes", StringComparison.OrdinalIgnoreCase);
}

static void TryDeleteUploadedFile(string uploadDirectory, string? assetUrl)
{
    if (string.IsNullOrWhiteSpace(assetUrl))
    {
        return;
    }

    if (!assetUrl.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var fileName = Path.GetFileName(assetUrl);
    if (string.IsNullOrWhiteSpace(fileName))
    {
        return;
    }

    var fullPath = Path.Combine(uploadDirectory, fileName);
    if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(uploadDirectory), StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    try
    {
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }
    }
    catch
    {
        // Best-effort cleanup only.
    }
}

static async Task UpsertPoiAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string id, double latitude, double longitude, double radiusMeters, string mapLink, string imageUrl, string audioUrl)
{
    const string sql = """
        INSERT INTO pois (id, latitude, longitude, radius_meters, priority, map_link, image_url, audio_url, is_active)
        VALUES ($id, $latitude, $longitude, $radius, 0, $map_link, $image_url, $audio_url, 1)
        ON CONFLICT(id) DO UPDATE SET
            latitude = excluded.latitude,
            longitude = excluded.longitude,
            radius_meters = excluded.radius_meters,
            map_link = excluded.map_link,
            image_url = excluded.image_url,
            audio_url = excluded.audio_url;
        """;

    await using var command = new SqliteCommand(sql, connection);
    command.Transaction = (SqliteTransaction)transaction;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$latitude", latitude);
    command.Parameters.AddWithValue("$longitude", longitude);
    command.Parameters.AddWithValue("$radius", radiusMeters);
    command.Parameters.AddWithValue("$map_link", mapLink);
    command.Parameters.AddWithValue("$image_url", imageUrl ?? string.Empty);
    command.Parameters.AddWithValue("$audio_url", audioUrl ?? string.Empty);
    await command.ExecuteNonQueryAsync();
}

static async Task UpsertTranslationAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, string id, string shopName, string description, string ttsText)
{
    const string sql = """
        INSERT INTO poi_translations (poi_id, lang_code, name, description, tts_text)
        VALUES ($id, 'vi', $name, $description, $tts_text)
        ON CONFLICT(poi_id, lang_code) DO UPDATE SET
            name = excluded.name,
            description = excluded.description,
            tts_text = excluded.tts_text;
        """;

    await using var command = new SqliteCommand(sql, connection);
    command.Transaction = (SqliteTransaction)transaction;
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$name", shopName);
    command.Parameters.AddWithValue("$description", description ?? string.Empty);
    command.Parameters.AddWithValue("$tts_text", ttsText ?? string.Empty);
    await command.ExecuteNonQueryAsync();
}

static async Task InitializeDatabaseAsync(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();

    const string sql = """
        CREATE TABLE IF NOT EXISTS pois (
            id TEXT PRIMARY KEY,
            latitude REAL NOT NULL,
            longitude REAL NOT NULL,
            radius_meters REAL NOT NULL,
            priority INTEGER NOT NULL,
            map_link TEXT NOT NULL DEFAULT '',
            image_url TEXT NOT NULL DEFAULT '',
            audio_url TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS poi_translations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            poi_id TEXT NOT NULL,
            lang_code TEXT NOT NULL,
            name TEXT NOT NULL,
            description TEXT NOT NULL DEFAULT '',
            tts_text TEXT NOT NULL DEFAULT '',
            UNIQUE(poi_id, lang_code),
            FOREIGN KEY(poi_id) REFERENCES pois(id) ON DELETE CASCADE
        );
        """;

    await using (var command = new SqliteCommand(sql, connection))
    {
        await command.ExecuteNonQueryAsync();
    }
}

sealed class ShopDto
{
    public string Id { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; }
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string TtsText { get; set; } = string.Empty;
}

sealed class ShopUpsertJsonRequest
{
    public string? Id { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 40;
    public string? Description { get; set; }
    public string? TtsText { get; set; }
}
