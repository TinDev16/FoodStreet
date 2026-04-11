using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var builder = WebApplication.CreateBuilder(args);

var hasExplicitUrlsArg = args.Any(x => x.StartsWith("--urls", StringComparison.OrdinalIgnoreCase));
if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ASPNETCORE_URLS")) && !hasExplicitUrlsArg)
{
    // Allow emulator/physical devices on same LAN to reach the admin API.
    builder.WebHost.UseUrls("http://0.0.0.0:5187");
}

var jwtSecret = Environment.GetEnvironmentVariable("FOODSTREET_JWT_SECRET")?.Trim();
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    jwtSecret = "FoodStreetDevJwtSecretKey32CharsMin!!";
}
var bootstrapSuperAdminUser = Environment.GetEnvironmentVariable("FOODSTREET_SUPERADMIN_USER")?.Trim();
if (string.IsNullOrWhiteSpace(bootstrapSuperAdminUser))
{
    bootstrapSuperAdminUser = "admin";
}
var bootstrapSuperAdminPassword = Environment.GetEnvironmentVariable("FOODSTREET_SUPERADMIN_PASSWORD")?.Trim();
if (string.IsNullOrWhiteSpace(bootstrapSuperAdminPassword))
{
    bootstrapSuperAdminPassword = "admin123";
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = "FoodStreetPoiAdmin",
            ValidateAudience = true,
            ValidAudience = "FoodStreetMobile",
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    });
builder.Services.AddAuthorization();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();
app.UseForwardedHeaders();

var dataDirectory = Path.Combine(app.Environment.ContentRootPath, "App_Data");
var uploadDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(uploadDirectory);

var dbPath = Path.Combine(dataDirectory, "poi-admin.db3");
var connectionString = $"Data Source={dbPath}";
var adbReverseSync = new object();
var lastAdbReverseAttemptUtc = DateTimeOffset.MinValue;

var supportedLanguages = SupportedLanguage.CreateDefaults();
var supportedLanguageSet = supportedLanguages.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
var configuredPublicBaseUrl = NormalizePublicBaseUrl(
    Environment.GetEnvironmentVariable("POI_PUBLIC_BASE_URL")
    ?? Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
    ?? app.Configuration["PublicBaseUrl"]);
var translationApiKey = Environment.GetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY")?.Trim();
if (string.IsNullOrWhiteSpace(translationApiKey))
{
    translationApiKey = "AIzaSyBe6oYZg8K70gk2HdDWo5n9UcqzIG2WqJo";
}

await InitializeDatabaseAsync(connectionString);
await EnsureBootstrapSuperAdminAsync(connectionString, bootstrapSuperAdminUser, bootstrapSuperAdminPassword);

app.UseDefaultFiles();
app.UseStaticFiles();

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (Exception ex)
    {
        try
        {
            var logPath = Path.Combine(dataDirectory, "server-errors.log");
            var entry = $"-----{Environment.NewLine}{DateTimeOffset.Now:O}{Environment.NewLine}{ex}{Environment.NewLine}";
            await File.AppendAllTextAsync(logPath, entry);
        }
        catch
        {
        }

        if (context.Response.HasStarted)
        {
            throw;
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json; charset=utf-8";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "Internal Server Error",
            detail = ex.Message
        });
    }
});

app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/api/admin/auth/login", async (AdminLoginRequest? req) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Thieu username hoac password." });
    }

    var admin = await FindAdminForLoginAsync(connectionString, req.Username.Trim(), req.Password);
    if (admin is null)
    {
        return Results.Unauthorized();
    }

    var token = CreateAdminJwt(admin.Value.Id, admin.Value.Username, admin.Value.Role, admin.Value.FullName, jwtSecret);
    return Results.Ok(new
    {
        token,
        user = new
        {
            id = admin.Value.Id,
            username = admin.Value.Username,
            role = admin.Value.Role,
            fullName = admin.Value.FullName
        }
    });
});

app.MapPost("/api/admin/auth/logout", () => Results.Ok(new { message = "Dang xuat phia client (xoa token)." }));

app.MapGet("/api/admin/auth/me", (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    return Results.Ok(new
    {
        id = actor.Id,
        username = actor.Username,
        role = actor.Role,
        fullName = actor.FullName
    });
}).RequireAuthorization();

app.MapGet("/api/admin/owners", async (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    var includeDeleted = string.Equals(context.Request.Query["includeDeleted"], "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(context.Request.Query["includeDeleted"], "true", StringComparison.OrdinalIgnoreCase);
    var owners = await GetOwnerAccountsAsync(connectionString, includeDeleted);
    return Results.Ok(owners);
}).RequireAuthorization();

app.MapPost("/api/admin/owners", async (HttpContext context, AdminCreateOwnerRequest? req) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Thieu username hoac password." });
    }

    if (req.Password.Trim().Length < 6)
    {
        return Results.BadRequest(new { error = "Password phai co it nhat 6 ky tu." });
    }

    try
    {
        var ownerId = await CreateOwnerAccountAsync(connectionString, req.Username.Trim(), req.Password.Trim(), req.FullName?.Trim() ?? string.Empty);
        return Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/admin/owners/{id}", async (HttpContext context, string id, AdminUpdateOwnerRequest? req) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    if (!long.TryParse((id ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerId) || ownerId <= 0)
    {
        return Results.BadRequest(new { error = "owner id khong hop le." });
    }

    if (req is null)
    {
        return Results.BadRequest(new { error = "Thieu du lieu cap nhat owner." });
    }

    if (!string.IsNullOrWhiteSpace(req.Password) && req.Password.Trim().Length < 6)
    {
        return Results.BadRequest(new { error = "Password phai co it nhat 6 ky tu." });
    }

    try
    {
        var ok = await UpdateOwnerAccountAsync(connectionString, ownerId, req.Username?.Trim(), req.FullName?.Trim(), req.Password?.Trim());
        return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapDelete("/api/admin/owners/{id}", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    if (!long.TryParse((id ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerId) || ownerId <= 0)
    {
        return Results.BadRequest(new { error = "owner id khong hop le." });
    }

    try
    {
        var ok = await DeleteOwnerAccountAsync(connectionString, ownerId);
        return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/admin/owners/{id}/restore", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    if (!long.TryParse((id ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerId) || ownerId <= 0)
    {
        return Results.BadRequest(new { error = "owner id khong hop le." });
    }

    var ok = await RestoreOwnerAccountAsync(connectionString, ownerId);
    return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture), restored = true }) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/admin/pois/{id}/assign-owner", async (HttpContext context, string id, AssignPoiOwnerRequest? req) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    long? ownerId = null;
    if (!string.IsNullOrWhiteSpace(req?.OwnerId))
    {
        if (!long.TryParse(req.OwnerId.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOwner) || parsedOwner <= 0)
        {
            return Results.BadRequest(new { error = "ownerId khong hop le." });
        }

        ownerId = parsedOwner;
    }

    try
    {
        var ok = await AssignOwnerToPoiAsync(connectionString, poiId, ownerId);
        return ok ? Results.Ok(new { id, ownerId = ownerId?.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/mobile/auth/register", async (MobileRegisterRequest? req) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Thieu username hoac password." });
    }

    if (string.IsNullOrWhiteSpace(req.FullName) || string.IsNullOrWhiteSpace(req.Phone))
    {
        return Results.BadRequest(new { error = "Thieu ho ten hoac so dien thoai." });
    }

    var username = req.Username.Trim();
    var phoneDigits = NormalizePhoneDigits(req.Phone);
    if (username.Length < 3)
    {
        return Results.BadRequest(new { error = "Username phai co it nhat 3 ky tu." });
    }

    if (phoneDigits.Length < 8)
    {
        return Results.BadRequest(new { error = "So dien thoai khong hop le." });
    }

    if (req.Password.Length < 6)
    {
        return Results.BadRequest(new { error = "Mat khau phai co it nhat 6 ky tu." });
    }

    try
    {
        var userId = await MobileRegisterUserAsync(connectionString, username, req.Password, req.FullName.Trim(), req.Phone.Trim(), phoneDigits);
        var token = CreateMobileJwt(userId, username, req.FullName.Trim(), req.Phone.Trim(), jwtSecret);
        return Results.Ok(new MobileAuthResponse
        {
            Token = token,
            UserId = userId,
            Username = username,
            FullName = req.FullName.Trim(),
            Phone = req.Phone.Trim()
        });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/mobile/auth/login", async (MobileLoginRequest? req) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.UsernameOrPhone) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Thieu thong tin dang nhap." });
    }

    var user = await MobileFindUserForLoginAsync(connectionString, req.UsernameOrPhone.Trim(), req.Password);
    if (user is null)
    {
        return Results.Unauthorized();
    }

    var token = CreateMobileJwt(user.Value.Id, user.Value.Username, user.Value.FullName, user.Value.Phone, jwtSecret);
    return Results.Ok(new MobileAuthResponse
    {
        Token = token,
        UserId = user.Value.Id,
        Username = user.Value.Username,
        FullName = user.Value.FullName,
        Phone = user.Value.Phone
    });
});

app.MapPost("/api/mobile/auth/logout", () => Results.Ok(new { message = "Dang xuat phia client (xoa token)." }));

app.MapGet("/api/mobile/auth/me", (ClaimsPrincipal user) =>
{
    var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!long.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
    {
        return Results.Unauthorized();
    }

    var username = user.FindFirstValue("username") ?? string.Empty;
    var fullName = user.FindFirstValue("full_name") ?? string.Empty;
    var phone = user.FindFirstValue("phone") ?? string.Empty;
    return Results.Ok(new { id = userId, username, fullName, phone });
}).RequireAuthorization();

app.MapPost("/api/mobile/auth/change-password", async (ClaimsPrincipal user, MobileChangePasswordRequest? req) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.CurrentPassword) || string.IsNullOrWhiteSpace(req.NewPassword))
    {
        return Results.BadRequest(new { error = "Thieu mat khau cu hoac mat khau moi." });
    }

    if (req.NewPassword.Length < 6)
    {
        return Results.BadRequest(new { error = "Mat khau moi phai co it nhat 6 ky tu." });
    }

    var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!long.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var userId))
    {
        return Results.Unauthorized();
    }

    var ok = await MobileChangePasswordAsync(connectionString, userId, req.CurrentPassword, req.NewPassword);
    if (!ok)
    {
        return Results.BadRequest(new { error = "Mat khau hien tai khong dung." });
    }

    return Results.Ok(new { message = "Doi mat khau thanh cong." });
}).RequireAuthorization();

app.MapGet("/api/languages", () => Results.Ok(supportedLanguages));

app.MapGet("/api/public/base-url", async (HttpContext context) =>
{
    var (baseUrl, error) = await ResolvePublicBaseUrlForRequestAsync(context);
    if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.BadRequest(new { error });
    }

    return Results.Ok(new { baseUrl });
});

app.MapPost("/api/uploads", async (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out _))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();

    if (!context.Request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    var kind = (context.Request.Query["kind"].ToString() ?? string.Empty).Trim().ToLowerInvariant();
    if (kind is not ("image" or "audio"))
    {
        return Results.BadRequest(new { error = "Invalid kind. Use kind=image or kind=audio." });
    }

    var lang = NormalizeAppLanguageCode(context.Request.Query["lang"].ToString());
    if (!string.IsNullOrWhiteSpace(lang) && !supportedLanguageSet.Contains(lang))
    {
        return Results.BadRequest(new { error = $"Unsupported lang: {lang}" });
    }

    var form = await context.Request.ReadFormAsync();
    var file = form.Files.GetFile("file") ?? form.Files.FirstOrDefault();
    if (file is null || file.Length <= 0)
    {
        return Results.BadRequest(new { error = "Missing file." });
    }

    var ext = Path.GetExtension(file.FileName ?? string.Empty);
    ext = string.IsNullOrWhiteSpace(ext) ? string.Empty : ext.Trim().ToLowerInvariant();

    var allowed = kind switch
    {
        "image" => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".gif", ".webp" },
        _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp3", ".wav", ".m4a", ".aac", ".ogg" }
    };

    if (!allowed.Contains(ext))
    {
        return Results.BadRequest(new { error = $"Unsupported file extension: {ext}" });
    }

    var safeLang = string.IsNullOrWhiteSpace(lang) ? "x" : lang;
    var fileName = $"{kind}_{safeLang}_{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}_{Guid.NewGuid():N}{ext}";
    var fullPath = Path.Combine(uploadDirectory, fileName);
    if (!Path.GetFullPath(fullPath).StartsWith(Path.GetFullPath(uploadDirectory), StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { error = "Invalid file path." });
    }

    await using (var stream = File.Create(fullPath))
    {
        await file.CopyToAsync(stream);
    }

    var url = $"/uploads/{fileName}";
    var contentType = file.ContentType ?? string.Empty;
    return Results.Ok(new { url, kind, lang = safeLang, contentType, size = file.Length });
}).RequireAuthorization();

app.MapGet("/api/pois", async (HttpContext context) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var items = await GetPoisForMobileAsync(connectionString, requestedLang);
    return Results.Ok(items);
});

// Admin list (includes inactive) with role-based ownership filter.
app.MapGet("/api/pois/admin", async (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    var items = await GetPoisForAdminListAsync(connectionString, actor);
    return Results.Ok(items);
}).RequireAuthorization();

app.MapGet("/api/admin/reports/audio-plays", async (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();

    var sort = NormalizeAudioPlaySort(context.Request.Query["sort"].ToString());
    var fromDate = ParseDateOnlyFilter(context.Request.Query["from"].ToString());
    var toDate = ParseDateOnlyFilter(context.Request.Query["to"].ToString());
    if (!string.IsNullOrWhiteSpace(context.Request.Query["from"]) && fromDate is null)
    {
        return Results.BadRequest(new { error = "Gia tri 'from' khong hop le. Dinh dang dung: yyyy-MM-dd." });
    }

    if (!string.IsNullOrWhiteSpace(context.Request.Query["to"]) && toDate is null)
    {
        return Results.BadRequest(new { error = "Gia tri 'to' khong hop le. Dinh dang dung: yyyy-MM-dd." });
    }

    var fromUtc = fromDate?.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O");
    var toExclusiveUtc = toDate?.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).ToString("O");
    var items = await GetPoiAudioPlayStatsAsync(connectionString, actor, fromUtc, toExclusiveUtc, sort);
    return Results.Ok(new
    {
        items,
        filter = new
        {
            from = fromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            to = toDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            sort
        }
    });
}).RequireAuthorization();

// Admin: load core + all translations with ownership filter.
app.MapGet("/api/pois/{id}", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var core = await GetPoiAdminAsync(connection, poiId, actor);
    return core is null ? Results.NotFound() : Results.Ok(core);
}).RequireAuthorization();

// Mobile: load localized view (fallback to Vietnamese when missing).
app.MapGet("/api/pois/{id}/localized", async (HttpContext context, string id) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    await using var connection = await OpenConnectionAsync(connectionString);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await GetPoiForMobileAsync(connection, poiId, requestedLang);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/public/featured-pois", async (HttpContext context) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var limit = 4;
    if (int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
    {
        limit = Math.Clamp(parsedLimit, 1, 20);
    }

    var items = await GetFeaturedPoisForPublicAsync(connectionString, requestedLang, limit);
    return Results.Ok(items);
});

app.MapGet("/api/public/pois/{id}", async (HttpContext context, string id) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    await using var connection = await OpenConnectionAsync(connectionString);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await GetPoiForPublicAsync(connection, poiId, requestedLang);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/pois/{id}/public-link", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    var core = await GetPoiAdminAsync(connection, poiId, actor);
    if (core is null)
    {
        return Results.NotFound();
    }

    var lang = NormalizeAppLanguageCode(context.Request.Query["lang"].ToString());
    var (baseUrl, error) = await ResolvePublicBaseUrlForRequestAsync(context);
    if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.BadRequest(new { error });
    }

    var publicUrl = BuildPublicPoiUrl(baseUrl, poiId, lang);
    return Results.Ok(new { id = poiId.ToString(CultureInfo.InvariantCulture), url = publicUrl, baseUrl });
}).RequireAuthorization();

app.MapGet("/api/pois/{id}/qr.png", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    await using var connection = await OpenConnectionAsync(connectionString);
    var core = await GetPoiAdminAsync(connection, poiId, actor);
    if (core is null)
    {
        return Results.NotFound();
    }

    var lang = NormalizeAppLanguageCode(context.Request.Query["lang"].ToString());
    var rawSize = context.Request.Query["size"].ToString();
    var size = 512;
    if (!string.IsNullOrWhiteSpace(rawSize) && int.TryParse(rawSize, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedSize))
    {
        size = Math.Clamp(parsedSize, 256, 2048);
    }

    var download = string.Equals(context.Request.Query["download"], "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(context.Request.Query["download"], "true", StringComparison.OrdinalIgnoreCase);

    var (baseUrl, error) = await ResolvePublicBaseUrlForRequestAsync(context);
    if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.BadRequest(new { error });
    }

    var publicUrl = BuildPublicPoiUrl(baseUrl, poiId, lang);
    var pngBytes = await RenderQrPngAsync(publicUrl, size, context.RequestAborted);

    if (download)
    {
        var fileName = $"poi-{poiId}.png";
        return Results.File(pngBytes, "image/png", fileName);
    }

    return Results.File(pngBytes, "image/png");
}).RequireAuthorization();

app.MapPost("/api/pois", async (HttpContext context, PoiAdminUpsertRequest request) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();

    if (request.Latitude is < -90 or > 90 || request.Longitude is < -180 or > 180)
    {
        return Results.BadRequest(new { error = "Latitude/Longitude khong hop le." });
    }

    if (request.RadiusMeters <= 0)
    {
        return Results.BadRequest(new { error = "Radius (m) phai lon hon 0." });
    }

    var translations = request.Translations ?? [];
    var normalizedTranslations = new List<PoiTranslationDto>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var translationSeedByLang = new Dictionary<string, PoiTranslationDto>(StringComparer.OrdinalIgnoreCase);

    foreach (var t in translations)
    {
        var normalizedCode = NormalizeAppLanguageCode(t.LangCode) ?? "vi";
        if (!supportedLanguageSet.Contains(normalizedCode))
        {
            return Results.BadRequest(new { error = $"Unsupported lang_code: {t.LangCode}" });
        }

        if (!seen.Add(normalizedCode))
        {
            return Results.BadRequest(new { error = $"Duplicate lang_code: {normalizedCode}" });
        }

        var normalized = new PoiTranslationDto
        {
            LangCode = normalizedCode,
            Name = (t.Name ?? string.Empty).Trim(),
            Description = (t.Description ?? string.Empty).Trim(),
            TtsText = (t.TtsText ?? string.Empty).Trim(),
            AudioUrl = (t.AudioUrl ?? string.Empty).Trim(),
        };
        translationSeedByLang[normalizedCode] = normalized;
        normalizedTranslations.Add(normalized);
    }

    var sourceLangCode = NormalizeAppLanguageCode(request.SourceLangCode);
    var sourceName = (request.SourceName ?? string.Empty).Trim();
    var sourceDescription = (request.SourceDescription ?? string.Empty).Trim();
    var sourceTtsText = (request.SourceTtsText ?? string.Empty).Trim();
    var hasSourcePayload = !string.IsNullOrWhiteSpace(sourceLangCode)
                           || !string.IsNullOrWhiteSpace(sourceName)
                           || !string.IsNullOrWhiteSpace(sourceDescription)
                           || !string.IsNullOrWhiteSpace(sourceTtsText);

    if (hasSourcePayload)
    {
        if (string.IsNullOrWhiteSpace(sourceLangCode) || !supportedLanguageSet.Contains(sourceLangCode))
        {
            return Results.BadRequest(new { error = $"Unsupported sourceLangCode: {request.SourceLangCode}" });
        }

        if (string.IsNullOrWhiteSpace(sourceName))
        {
            return Results.BadRequest(new { error = "Ten POI cua ngon ngu dau vao khong duoc de trong." });
        }

        var generated = new List<PoiTranslationDto>(supportedLanguages.Count);
        foreach (var lang in supportedLanguages)
        {
            var audioUrl = translationSeedByLang.TryGetValue(lang.Code, out var seed)
                ? (seed.AudioUrl ?? string.Empty).Trim()
                : string.Empty;

            if (string.Equals(lang.Code, sourceLangCode, StringComparison.OrdinalIgnoreCase))
            {
                generated.Add(new PoiTranslationDto
                {
                    LangCode = lang.Code,
                    Name = sourceName,
                    Description = sourceDescription,
                    TtsText = sourceTtsText,
                    AudioUrl = audioUrl
                });
                continue;
            }

            try
            {
                var translated = await TranslatePoiContentAsync(
                    translationApiKey ?? string.Empty,
                    sourceLangCode,
                    lang.Code,
                    sourceName,
                    sourceDescription,
                    sourceTtsText);

                generated.Add(new PoiTranslationDto
                {
                    LangCode = lang.Code,
                    Name = translated.Name,
                    Description = translated.Description,
                    TtsText = translated.TtsText,
                    AudioUrl = audioUrl
                });
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { error = $"Khong the dich sang '{lang.Code}'.", detail = ex.Message });
            }
        }

        normalizedTranslations = generated;
    }

    if (!normalizedTranslations.Any(t => !string.IsNullOrWhiteSpace(t.Name)))
    {
        return Results.BadRequest(new { error = "Khong duoc de trong ten POI o tat ca ngon ngu (khuyen nghi: vi)." });
    }

    long? poiId = null;
    if (!string.IsNullOrWhiteSpace(request.Id))
    {
        if (!TryParsePoiId(request.Id, out var parsed))
        {
            return Results.BadRequest(new { error = "Invalid id." });
        }

        poiId = parsed;
    }

    if (IsOwner(actor) && poiId is null)
    {
        return Results.Forbid();
    }

    var mapLink = string.IsNullOrWhiteSpace(request.MapLink)
        ? $"https://maps.google.com/?q={request.Latitude.ToString(CultureInfo.InvariantCulture)},{request.Longitude.ToString(CultureInfo.InvariantCulture)}"
        : request.MapLink.Trim();

    await using var connection = await OpenConnectionAsync(connectionString);

    if (poiId is not null)
    {
        var existsForActor = await HasPoiAccessAsync(connection, poiId.Value, actor);
        if (!existsForActor)
        {
            return Results.NotFound();
        }
    }

    await using var transaction = await connection.BeginTransactionAsync();

    var savedId = await UpsertPoiCoreAsync(connection, transaction, new PoiCoreUpsert
    {
        Id = poiId,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        RadiusMeters = request.RadiusMeters,
        Priority = request.Priority,
        MapLink = mapLink,
        ImageUrl = (request.ImageUrl ?? string.Empty).Trim(),
        AudioUrl = (request.AudioUrl ?? string.Empty).Trim(),
        IsActive = request.IsActive,
        OwnerAdminId = IsOwner(actor) ? actor.Id : null
    });

    foreach (var t in normalizedTranslations)
    {
        await UpsertTranslationAsync(
            connection,
            transaction,
            savedId,
            t.LangCode,
            t.Name,
            t.Description,
            t.TtsText,
            t.AudioUrl);
    }

    await transaction.CommitAsync();

    return Results.Ok(new { id = savedId.ToString(CultureInfo.InvariantCulture) });
}).RequireAuthorization();

app.MapDelete("/api/pois/{id}", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var result = await DeletePoiAsync(connectionString, uploadDirectory, poiId, actor);
    return result switch
    {
        DeletePoiResult.Deleted => Results.Ok(new { id }),
        DeletePoiResult.NotFound => Results.NotFound(),
        _ => Results.Problem("Delete failed.")
    };
}).RequireAuthorization();

app.MapPost("/api/pois/{id}/restore", async (HttpContext context, string id) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    if (!IsSuperAdmin(actor))
    {
        return Results.Forbid();
    }

    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var restored = await RestorePoiAsync(connectionString, poiId);
    return restored ? Results.Ok(new { id, restored = true }) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/pois/{id}/audio-play", async (string id) =>
{
    await TryEnsureAdbReverseAsync();
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var recorded = await RecordPoiAudioPlayAsync(connectionString, poiId);
    return recorded ? Results.Ok(new { id, recorded = true }) : Results.NotFound();
});

// Legacy endpoints for older mobile build.
app.MapGet("/api/shops", async (HttpContext context) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var items = await GetPoisForMobileAsync(connectionString, requestedLang);
    var legacy = items.Select(x => new ShopDto
    {
        Id = x.Id,
        LangCode = x.LangCode,
        Latitude = x.Latitude,
        Longitude = x.Longitude,
        RadiusMeters = x.RadiusMeters,
        ImageUrl = x.ImageUrl,
        AudioUrl = x.AudioUrl,
        ShopName = x.Name,
        Description = x.Description,
        TtsText = x.TtsText
    }).ToList();
    return Results.Ok(legacy);
});

app.MapGet("/api/shops/{id}", async (HttpContext context, string id) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    await using var connection = await OpenConnectionAsync(connectionString);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await GetPoiForMobileAsync(connection, poiId, requestedLang);
    if (item is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new ShopDto
    {
        Id = item.Id,
        LangCode = item.LangCode,
        Latitude = item.Latitude,
        Longitude = item.Longitude,
        RadiusMeters = item.RadiusMeters,
        ImageUrl = item.ImageUrl,
        AudioUrl = item.AudioUrl,
        ShopName = item.Name,
        Description = item.Description,
        TtsText = item.TtsText
    });
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

    long? poiId = null;
    if (!string.IsNullOrWhiteSpace(request.Id))
    {
        if (TryParsePoiId(request.Id, out var parsed))
        {
            poiId = parsed;
        }
    }

    var langCode = NormalizeAppLanguageCode(request.LangCode) ?? "vi";
    if (!supportedLanguageSet.Contains(langCode))
    {
        langCode = "vi";
    }

    await using var connection = await OpenConnectionAsync(connectionString);

    var currentAudioUrl = string.Empty;
    var currentImageUrl = string.Empty;
    if (poiId is not null)
    {
        await using var oldAudioCommand = new SqliteCommand("SELECT audio_url, image_url FROM pois WHERE id = $id;", connection);
        oldAudioCommand.Parameters.AddWithValue("$id", poiId.Value);
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

    var mapLink = $"https://maps.google.com/?q={request.Latitude.ToString(CultureInfo.InvariantCulture)},{request.Longitude.ToString(CultureInfo.InvariantCulture)}";
    await using var transaction = await connection.BeginTransactionAsync();
    var savedId = await UpsertPoiCoreAsync(connection, transaction, new PoiCoreUpsert
    {
        Id = poiId,
        Latitude = request.Latitude,
        Longitude = request.Longitude,
        RadiusMeters = request.RadiusMeters,
        Priority = 0,
        MapLink = mapLink,
        ImageUrl = currentImageUrl,
        AudioUrl = currentAudioUrl,
        IsActive = true,
        OwnerAdminId = null
    });
    await UpsertTranslationAsync(
        connection,
        transaction,
        savedId,
        langCode,
        request.ShopName.Trim(),
        request.Description?.Trim() ?? string.Empty,
        finalTtsText,
        audioUrl: string.Empty);
    await transaction.CommitAsync();

    return Results.Ok(new { id = savedId.ToString(CultureInfo.InvariantCulture) });
});

app.MapDelete("/api/shops/{id}", async (string id) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var result = await DeletePoiAsync(connectionString, uploadDirectory, poiId, new AdminActor(0, "system", "superadmin", "System"));
    return result switch
    {
        DeletePoiResult.Deleted => Results.Ok(new { id }),
        DeletePoiResult.NotFound => Results.NotFound(),
        _ => Results.Problem("Delete failed.")
    };
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

static string NormalizeLanguageOrFallback(string? requested, HashSet<string> supportedLanguageSet)
{
    var normalized = NormalizeAppLanguageCode(requested) ?? "vi";
    return supportedLanguageSet.Contains(normalized) ? normalized : "vi";
}

static bool TryParsePoiId(string? raw, out long poiId)
{
    poiId = 0;
    if (string.IsNullOrWhiteSpace(raw))
    {
        return false;
    }

    return long.TryParse(raw.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out poiId)
           && poiId > 0;
}

static DateOnly? ParseDateOnlyFilter(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    return DateOnly.TryParseExact(raw.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var value)
        ? value
        : null;
}

static string NormalizeAudioPlaySort(string? raw)
    => string.Equals(raw, "asc", StringComparison.OrdinalIgnoreCase) ? "asc" : "desc";

async Task<(string? BaseUrl, string? Error)> ResolvePublicBaseUrlForRequestAsync(HttpContext context)
{
    var error = default(string);
    var requestedBaseUrlRaw = context.Request.Query["baseUrl"].ToString();
    if (!string.IsNullOrWhiteSpace(requestedBaseUrlRaw))
    {
        var requestedBaseUrl = NormalizePublicBaseUrl(requestedBaseUrlRaw);
        if (string.IsNullOrWhiteSpace(requestedBaseUrl))
        {
            error = "Invalid baseUrl. Use full http(s) URL, for example: https://example.com";
            return (null, error);
        }

        return (requestedBaseUrl, null);
    }

    if (!string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
    {
        return (configuredPublicBaseUrl, null);
    }

    var fallback = $"{context.Request.Scheme}://{context.Request.Host.ToUriComponent()}{context.Request.PathBase.ToUriComponent()}".TrimEnd('/');
    if (IsLocalUrl(fallback))
    {
        error = "Public URL dang la localhost. Vui long nhap Public base URL trong hop thoai QR hoac cau hinh bien moi truong POI_PUBLIC_BASE_URL.";
        return (null, error);
    }

    return (fallback, null);
}

static string? NormalizePublicBaseUrl(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return null;
    }

    if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
    {
        return null;
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        return null;
    }

    var builder = new UriBuilder(uri)
    {
        Query = string.Empty,
        Fragment = string.Empty
    };

    var normalized = builder.Uri.ToString().TrimEnd('/');
    return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
}

static string BuildPublicPoiUrl(string publicBaseUrl, long poiId, string? langCode)
{
    var safeBase = (publicBaseUrl ?? string.Empty).TrimEnd('/');
    if (string.IsNullOrWhiteSpace(safeBase))
    {
        throw new InvalidOperationException("Public base URL is empty.");
    }

    var queryParts = new List<string>
    {
        $"id={Uri.EscapeDataString(poiId.ToString(CultureInfo.InvariantCulture))}"
    };
    if (!string.IsNullOrWhiteSpace(langCode))
    {
        queryParts.Add($"lang={Uri.EscapeDataString(langCode)}");
    }

    return $"{safeBase}/poi.html?{string.Join("&", queryParts)}";
}

static bool IsLocalUrl(string? rawUrl)
{
    if (string.IsNullOrWhiteSpace(rawUrl))
    {
        return false;
    }

    if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
    {
        return false;
    }

    var host = (uri.Host ?? string.Empty).Trim().ToLowerInvariant();
    return host is "localhost" or "127.0.0.1" or "::1";
}

static async Task<byte[]> RenderQrPngAsync(string content, int size, CancellationToken cancellationToken)
{
    if (string.IsNullOrWhiteSpace(content))
    {
        throw new InvalidOperationException("QR content is empty.");
    }

    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(25)
    };

    var endpoint = $"https://quickchart.io/qr?format=png&ecLevel=M&margin=2&size={size.ToString(CultureInfo.InvariantCulture)}&text={Uri.EscapeDataString(content)}";
    using var response = await httpClient.GetAsync(endpoint, cancellationToken);
    if (!response.IsSuccessStatusCode)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new InvalidOperationException($"QR generation failed ({(int)response.StatusCode}): {body}");
    }

    return await response.Content.ReadAsByteArrayAsync(cancellationToken);
}

static async Task InitializeDatabaseAsync(string connectionString)
{
    await using var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using (var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
    {
        await pragma.ExecuteNonQueryAsync();
    }

    const string sql = @"
        CREATE TABLE IF NOT EXISTS pois (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            latitude REAL,
            longitude REAL,
            radius_meters REAL,
            priority INTEGER,
            map_link TEXT,
            image_url TEXT,
            audio_url TEXT,
            is_active INTEGER,
            owner_admin_id INTEGER,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT,
            delete_status TEXT NOT NULL DEFAULT 'ACTIVE',
            updated_at TEXT,
            FOREIGN KEY(owner_admin_id) REFERENCES admin_accounts(id)
        );

        CREATE TABLE IF NOT EXISTS poi_translations (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            poi_id INTEGER,
            lang_code TEXT,
            name TEXT,
            description TEXT,
            tts_text TEXT,
            audio_url TEXT,
            FOREIGN KEY(poi_id) REFERENCES pois(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS admin_accounts (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL COLLATE NOCASE,
            password_hash TEXT NOT NULL,
            role TEXT NOT NULL,
            full_name TEXT NOT NULL DEFAULT '',
            is_active INTEGER NOT NULL DEFAULT 1,
            is_deleted INTEGER NOT NULL DEFAULT 0,
            deleted_at TEXT,
            delete_status TEXT NOT NULL DEFAULT 'ACTIVE',
            updated_at TEXT,
            created_at TEXT NOT NULL
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_admin_accounts_username ON admin_accounts(username);
        ";

    await using (var command = new SqliteCommand(sql, connection))
    {
        await command.ExecuteNonQueryAsync();
    }

    // Best-effort migration for existing databases.
    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE poi_translations ADD COLUMN audio_url TEXT NOT NULL DEFAULT '';", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN is_deleted INTEGER NOT NULL DEFAULT 0;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN deleted_at TEXT;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN delete_status TEXT NOT NULL DEFAULT 'ACTIVE';", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN owner_admin_id INTEGER;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN updated_at TEXT;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch
    {
        // Ignore when the column already exists.
    }

    await using (var createOwnerIndex = new SqliteCommand("CREATE INDEX IF NOT EXISTS ix_pois_owner_admin_id ON pois(owner_admin_id);", connection))
    {
        await createOwnerIndex.ExecuteNonQueryAsync();
    }

    // Backfill status based on is_deleted for old rows.
    await using (var backfillDeleteStatus = new SqliteCommand("""
        UPDATE pois
        SET delete_status = CASE WHEN COALESCE(is_deleted, 0) = 1 THEN 'DELETED' ELSE 'ACTIVE' END
        WHERE delete_status IS NULL OR delete_status = '';
        """, connection))
    {
        await backfillDeleteStatus.ExecuteNonQueryAsync();
    }

    // Ensure unique per poi/lang for upsert behavior (dedupe first if needed).
    try
    {
        await using (var dedupe = new SqliteCommand("""
            DELETE FROM poi_translations
            WHERE id NOT IN (
                SELECT MIN(id)
                FROM poi_translations
                GROUP BY poi_id, lang_code
            );
            """, connection))
        {
            await dedupe.ExecuteNonQueryAsync();
        }

        await using (var createIndex = new SqliteCommand("CREATE UNIQUE INDEX IF NOT EXISTS ux_poi_translations_poi_lang ON poi_translations(poi_id, lang_code);", connection))
        {
            await createIndex.ExecuteNonQueryAsync();
        }
    }
    catch
    {
        // Best-effort only. If index cannot be created, translation upserts may fail.
    }

    await using (var createUsers = new SqliteCommand("""
        CREATE TABLE IF NOT EXISTS app_users (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            username TEXT NOT NULL COLLATE NOCASE,
            password_hash TEXT NOT NULL,
            full_name TEXT NOT NULL DEFAULT '',
            phone TEXT NOT NULL,
            phone_digits TEXT NOT NULL,
            created_at TEXT NOT NULL
        );
        CREATE TABLE IF NOT EXISTS poi_audio_play_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            poi_id INTEGER NOT NULL,
            created_at TEXT NOT NULL,
            FOREIGN KEY(poi_id) REFERENCES pois(id) ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_username ON app_users(username);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_app_users_phone_digits ON app_users(phone_digits);
        CREATE INDEX IF NOT EXISTS ix_poi_audio_play_events_poi_created_at ON poi_audio_play_events(poi_id, created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_poi_audio_play_events_created_at ON poi_audio_play_events(created_at DESC);
        """, connection))
    {
        await createUsers.ExecuteNonQueryAsync();
    }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE admin_accounts ADD COLUMN is_deleted INTEGER NOT NULL DEFAULT 0;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch { }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE admin_accounts ADD COLUMN deleted_at TEXT;", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch { }

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE admin_accounts ADD COLUMN delete_status TEXT NOT NULL DEFAULT 'ACTIVE';", connection);
        await migrate.ExecuteNonQueryAsync();
    }
    catch { }

    await using (var backfillAdminDeleteStatus = new SqliteCommand("""
        UPDATE admin_accounts
        SET delete_status = CASE WHEN COALESCE(is_deleted, 0) = 1 THEN 'DELETED' ELSE 'ACTIVE' END
        WHERE delete_status IS NULL OR delete_status = '';
        """, connection))
    {
        await backfillAdminDeleteStatus.ExecuteNonQueryAsync();
    }
}

static async Task<SqliteConnection> OpenConnectionAsync(string connectionString)
{
    var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection);
    await pragma.ExecuteNonQueryAsync();
    return connection;
}

static bool TryGetAdminActor(ClaimsPrincipal user, out AdminActor actor)
{
    actor = default!;
    var idStr = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue(JwtRegisteredClaimNames.Sub);
    if (!long.TryParse(idStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out var adminId) || adminId <= 0)
    {
        return false;
    }

    var username = user.FindFirstValue("admin_username") ?? string.Empty;
    var role = user.FindFirstValue("admin_role") ?? string.Empty;
    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
    {
        return false;
    }

    actor = new AdminActor(
        adminId,
        username,
        role.Trim().ToLowerInvariant(),
        user.FindFirstValue("admin_full_name") ?? string.Empty);
    return true;
}

static bool IsSuperAdmin(AdminActor actor) => string.Equals(actor.Role, "superadmin", StringComparison.OrdinalIgnoreCase);
static bool IsOwner(AdminActor actor) => string.Equals(actor.Role, "owner", StringComparison.OrdinalIgnoreCase);

static async Task EnsureBootstrapSuperAdminAsync(string connectionString, string username, string password)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var check = new SqliteCommand("SELECT COUNT(1) FROM admin_accounts WHERE role = 'superadmin';", connection);
    var countRaw = await check.ExecuteScalarAsync();
    var count = Convert.ToInt32(countRaw ?? 0, CultureInfo.InvariantCulture);
    if (count > 0)
    {
        return;
    }

    var hash = BCrypt.Net.BCrypt.HashPassword(password);
    await using var insert = new SqliteCommand("""
        INSERT INTO admin_accounts (username, password_hash, role, full_name, is_active, created_at)
        VALUES ($u, $h, 'superadmin', 'Super Admin', 1, $createdAt);
        """, connection);
    insert.Parameters.AddWithValue("$u", username);
    insert.Parameters.AddWithValue("$h", hash);
    insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
    await insert.ExecuteNonQueryAsync();
}

static string CreateAdminJwt(long adminId, string username, string role, string fullName, string secret)
{
    var claims = new List<Claim>
    {
        new(JwtRegisteredClaimNames.Sub, adminId.ToString(CultureInfo.InvariantCulture)),
        new(ClaimTypes.NameIdentifier, adminId.ToString(CultureInfo.InvariantCulture)),
        new("admin_username", username),
        new("admin_role", role),
        new("admin_full_name", fullName ?? string.Empty),
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: "FoodStreetPoiAdmin",
        audience: "FoodStreetMobile",
        claims: claims,
        expires: DateTime.UtcNow.AddDays(7),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task<(long Id, string Username, string Role, string FullName)?> FindAdminForLoginAsync(string connectionString, string username, string password)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var cmd = new SqliteCommand("""
        SELECT id, username, password_hash, role, full_name, is_active, is_deleted
        FROM admin_accounts
        WHERE lower(username) = lower($u)
        LIMIT 1;
        """, connection);
    cmd.Parameters.AddWithValue("$u", username);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var isActive = reader.IsDBNull(5) || reader.GetInt32(5) != 0;
    var isDeleted = !reader.IsDBNull(6) && reader.GetInt32(6) != 0;
    if (!isActive || isDeleted)
    {
        return null;
    }

    var hash = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
    if (string.IsNullOrWhiteSpace(hash) || !BCrypt.Net.BCrypt.Verify(password, hash))
    {
        return null;
    }

    return (
        reader.GetInt64(0),
        reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
        reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
        reader.IsDBNull(4) ? string.Empty : reader.GetString(4));
}

static async Task<List<OwnerAccountDto>> GetOwnerAccountsAsync(string connectionString, bool includeDeleted = false)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    var sql = """
        SELECT id, username, full_name, COALESCE(is_deleted, 0), deleted_at, COALESCE(delete_status, 'ACTIVE')
        FROM admin_accounts
        WHERE role = 'owner'
        ORDER BY username ASC;
        """;
    if (!includeDeleted)
    {
        sql = sql.Replace("ORDER BY", "AND COALESCE(is_deleted, 0) = 0 ORDER BY", StringComparison.Ordinal);
    }
    await using var cmd = new SqliteCommand(sql, connection);
    await using var reader = await cmd.ExecuteReaderAsync();
    var result = new List<OwnerAccountDto>();
    while (await reader.ReadAsync())
    {
        result.Add(new OwnerAccountDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            Username = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            FullName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            IsDeleted = !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
            DeletedAt = reader.IsDBNull(4) ? null : reader.GetString(4),
            DeleteStatus = reader.IsDBNull(5) ? "ACTIVE" : reader.GetString(5),
        });
    }

    return result;
}

static async Task<long> CreateOwnerAccountAsync(string connectionString, string username, string password, string fullName)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var exists = new SqliteCommand("SELECT 1 FROM admin_accounts WHERE lower(username) = lower($u) LIMIT 1;", connection);
    exists.Parameters.AddWithValue("$u", username);
    if (await exists.ExecuteScalarAsync() is not null)
    {
        throw new InvalidOperationException("Username da ton tai.");
    }

    var hash = BCrypt.Net.BCrypt.HashPassword(password);
    await using var insert = new SqliteCommand("""
        INSERT INTO admin_accounts (username, password_hash, role, full_name, is_active, created_at)
        VALUES ($u, $h, 'owner', $fullName, 1, $createdAt);
        SELECT last_insert_rowid();
        """, connection);
    insert.Parameters.AddWithValue("$u", username);
    insert.Parameters.AddWithValue("$h", hash);
    insert.Parameters.AddWithValue("$fullName", fullName ?? string.Empty);
    insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
    var raw = await insert.ExecuteScalarAsync();
    return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
}

static async Task<bool> UpdateOwnerAccountAsync(string connectionString, long ownerId, string? username, string? fullName, string? password)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var exists = new SqliteCommand("SELECT 1 FROM admin_accounts WHERE id = $id AND role = 'owner' AND COALESCE(is_deleted, 0) = 0 LIMIT 1;", connection);
    exists.Parameters.AddWithValue("$id", ownerId);
    if (await exists.ExecuteScalarAsync() is null)
    {
        return false;
    }

    if (!string.IsNullOrWhiteSpace(username))
    {
        await using var checkUser = new SqliteCommand("SELECT 1 FROM admin_accounts WHERE lower(username) = lower($u) AND id <> $id LIMIT 1;", connection);
        checkUser.Parameters.AddWithValue("$u", username);
        checkUser.Parameters.AddWithValue("$id", ownerId);
        if (await checkUser.ExecuteScalarAsync() is not null)
        {
            throw new InvalidOperationException("Username da ton tai.");
        }
    }

    var setSql = new List<string>();
    await using var cmd = new SqliteCommand { Connection = connection };
    cmd.Parameters.AddWithValue("$id", ownerId);

    if (!string.IsNullOrWhiteSpace(username))
    {
        setSql.Add("username = $u");
        cmd.Parameters.AddWithValue("$u", username);
    }

    if (fullName is not null)
    {
        setSql.Add("full_name = $fullName");
        cmd.Parameters.AddWithValue("$fullName", fullName);
    }

    if (!string.IsNullOrWhiteSpace(password))
    {
        setSql.Add("password_hash = $passwordHash");
        cmd.Parameters.AddWithValue("$passwordHash", BCrypt.Net.BCrypt.HashPassword(password));
    }

    if (setSql.Count == 0)
    {
        return true;
    }

    cmd.CommandText = $"UPDATE admin_accounts SET {string.Join(", ", setSql)} WHERE id = $id AND role = 'owner' AND COALESCE(is_deleted, 0) = 0;";
    var affected = await cmd.ExecuteNonQueryAsync();
    return affected > 0;
}

static async Task<bool> DeleteOwnerAccountAsync(string connectionString, long ownerId)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var exists = new SqliteCommand("SELECT 1 FROM admin_accounts WHERE id = $id AND role = 'owner' AND COALESCE(is_deleted, 0) = 0 LIMIT 1;", connection);
    exists.Parameters.AddWithValue("$id", ownerId);
    if (await exists.ExecuteScalarAsync() is null)
    {
        return false;
    }

    // Unassign POIs before deleting owner.
    await using (var unassign = new SqliteCommand("UPDATE pois SET owner_admin_id = NULL WHERE owner_admin_id = $id;", connection))
    {
        unassign.Parameters.AddWithValue("$id", ownerId);
        await unassign.ExecuteNonQueryAsync();
    }

    await using var softDelete = new SqliteCommand("""
        UPDATE admin_accounts
        SET is_deleted = 1,
            is_active = 0,
            deleted_at = $deletedAt,
            delete_status = 'DELETED'
        WHERE id = $id AND role = 'owner' AND COALESCE(is_deleted, 0) = 0;
        """, connection);
    softDelete.Parameters.AddWithValue("$id", ownerId);
    softDelete.Parameters.AddWithValue("$deletedAt", DateTimeOffset.UtcNow.ToString("O"));
    var affected = await softDelete.ExecuteNonQueryAsync();
    return affected > 0;
}

static async Task<bool> RestoreOwnerAccountAsync(string connectionString, long ownerId)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var restore = new SqliteCommand("""
        UPDATE admin_accounts
        SET is_deleted = 0,
            is_active = 1,
            deleted_at = NULL,
            delete_status = 'ACTIVE'
        WHERE id = $id AND role = 'owner' AND COALESCE(is_deleted, 0) = 1;
        """, connection);
    restore.Parameters.AddWithValue("$id", ownerId);
    var affected = await restore.ExecuteNonQueryAsync();
    return affected > 0;
}

static async Task<bool> HasPoiAccessAsync(SqliteConnection connection, long poiId, AdminActor actor)
{
    if (IsSuperAdmin(actor))
    {
        await using var exists = new SqliteCommand("SELECT 1 FROM pois WHERE id = $id LIMIT 1;", connection);
        exists.Parameters.AddWithValue("$id", poiId);
        return await exists.ExecuteScalarAsync() is not null;
    }

    await using var ownerCheck = new SqliteCommand("SELECT 1 FROM pois WHERE id = $id AND owner_admin_id = $ownerId LIMIT 1;", connection);
    ownerCheck.Parameters.AddWithValue("$id", poiId);
    ownerCheck.Parameters.AddWithValue("$ownerId", actor.Id);
    return await ownerCheck.ExecuteScalarAsync() is not null;
}

static async Task<bool> RecordPoiAudioPlayAsync(string connectionString, long poiId)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    await using var existsCommand = new SqliteCommand("""
        SELECT 1
        FROM pois
        WHERE id = $id
          AND COALESCE(is_deleted, 0) = 0
        LIMIT 1;
        """, connection);
    existsCommand.Parameters.AddWithValue("$id", poiId);
    if (await existsCommand.ExecuteScalarAsync() is null)
    {
        return false;
    }

    await using var insert = new SqliteCommand("""
        INSERT INTO poi_audio_play_events (poi_id, created_at)
        VALUES ($poiId, $createdAt);
        """, connection);
    insert.Parameters.AddWithValue("$poiId", poiId);
    insert.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));
    await insert.ExecuteNonQueryAsync();
    return true;
}

static async Task<bool> AssignOwnerToPoiAsync(string connectionString, long poiId, long? ownerId)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    if (ownerId is not null)
    {
        await using var ownerExists = new SqliteCommand("""
            SELECT 1 FROM admin_accounts
            WHERE id = $ownerId AND role = 'owner' AND COALESCE(is_active, 1) = 1
            LIMIT 1;
            """, connection);
        ownerExists.Parameters.AddWithValue("$ownerId", ownerId.Value);
        if (await ownerExists.ExecuteScalarAsync() is null)
        {
            throw new InvalidOperationException("Owner khong ton tai hoac da bi khoa.");
        }
    }

    await using var update = new SqliteCommand("UPDATE pois SET owner_admin_id = $ownerId WHERE id = $id;", connection);
    update.Parameters.AddWithValue("$ownerId", ownerId.HasValue ? ownerId.Value : DBNull.Value);
    update.Parameters.AddWithValue("$id", poiId);
    var affected = await update.ExecuteNonQueryAsync();
    return affected > 0;
}

static async Task<List<PoiMobileDto>> GetPoisForMobileAsync(string connectionString, string requestedLang)
{
    await using var connection = await OpenConnectionAsync(connectionString);

    const string sql = @"
        SELECT
            p.id,
            p.latitude,
            p.longitude,
            p.radius_meters,
            p.priority,
            p.map_link,
            p.image_url,
            p.audio_url,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            COALESCE(NULLIF(t_req.description, ''), t_vi.description, '') AS description,
            COALESCE(NULLIF(t_req.tts_text, ''), NULLIF(t_req.description, ''), NULLIF(t_vi.tts_text, ''), t_vi.description, '') AS tts_text,
            COALESCE(NULLIF(t_req.audio_url, ''), NULLIF(t_vi.audio_url, ''), '') AS audio_lang
        FROM pois p
        LEFT JOIN poi_translations t_req ON p.id = t_req.poi_id AND t_req.lang_code = $lang_code
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        WHERE p.is_active = 1 AND COALESCE(p.is_deleted, 0) = 0
        ORDER BY p.priority DESC, p.id ASC;
        ";

    var result = new List<PoiMobileDto>();
    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$lang_code", requestedLang);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        var coreAudioUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
        var translatedAudioUrl = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
        result.Add(new PoiMobileDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            LangCode = requestedLang,
            Latitude = reader.GetDouble(1),
            Longitude = reader.GetDouble(2),
            RadiusMeters = reader.GetDouble(3),
            Priority = reader.GetInt32(4),
            MapLink = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            ImageUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            AudioUrl = !string.IsNullOrWhiteSpace(translatedAudioUrl) ? translatedAudioUrl : coreAudioUrl,
            Name = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            Description = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            TtsText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
        });
    }

    return result;
}

static async Task<List<PoiAudioPlayStatDto>> GetPoiAudioPlayStatsAsync(
    string connectionString,
    AdminActor actor,
    string? fromUtc,
    string? toExclusiveUtc,
    string sort)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    var countOrder = string.Equals(sort, "asc", StringComparison.OrdinalIgnoreCase) ? "ASC" : "DESC";
    var sql = $"""
        SELECT
            p.id,
            COALESCE(NULLIF(t_vi.name, ''), '') AS name_vi,
            COUNT(e.id) AS play_count,
            MAX(e.created_at) AS last_played_at
        FROM pois p
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        LEFT JOIN poi_audio_play_events e ON e.poi_id = p.id
            AND ($fromUtc IS NULL OR e.created_at >= $fromUtc)
            AND ($toExclusiveUtc IS NULL OR e.created_at < $toExclusiveUtc)
        WHERE COALESCE(p.is_deleted, 0) = 0
        {(IsOwner(actor) ? "AND p.owner_admin_id = $ownerId" : string.Empty)}
        GROUP BY p.id, name_vi
        ORDER BY play_count {countOrder}, p.id ASC;
        """;

    var result = new List<PoiAudioPlayStatDto>();
    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$fromUtc", string.IsNullOrWhiteSpace(fromUtc) ? DBNull.Value : fromUtc);
    command.Parameters.AddWithValue("$toExclusiveUtc", string.IsNullOrWhiteSpace(toExclusiveUtc) ? DBNull.Value : toExclusiveUtc);
    if (IsOwner(actor))
    {
        command.Parameters.AddWithValue("$ownerId", actor.Id);
    }

    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new PoiAudioPlayStatDto
        {
            PoiId = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            PoiName = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            PlayCount = reader.IsDBNull(2) ? 0 : reader.GetInt64(2),
            LastPlayedAt = reader.IsDBNull(3) ? null : reader.GetString(3)
        });
    }

    return result;
}

static async Task<List<FeaturedPoiDto>> GetFeaturedPoisForPublicAsync(string connectionString, string requestedLang, int limit)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    const string sql = """
        SELECT
            p.id,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            p.image_url,
            COUNT(e.id) AS play_count
        FROM pois p
        LEFT JOIN poi_translations t_req ON p.id = t_req.poi_id AND t_req.lang_code = $lang_code
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        LEFT JOIN poi_audio_play_events e ON e.poi_id = p.id
        WHERE p.is_active = 1 AND COALESCE(p.is_deleted, 0) = 0
        GROUP BY p.id, COALESCE(NULLIF(t_req.name, ''), t_vi.name, ''), p.image_url, p.priority
        ORDER BY play_count DESC, p.priority DESC, p.id ASC
        LIMIT $limit;
        """;

    var result = new List<FeaturedPoiDto>();
    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$lang_code", requestedLang);
    command.Parameters.AddWithValue("$limit", limit);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new FeaturedPoiDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            ImageUrl = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            PlayCount = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
        });
    }

    return result;
}

static async Task<List<PoiAdminListItemDto>> GetPoisForAdminListAsync(string connectionString, AdminActor actor)
{
    await using var connection = await OpenConnectionAsync(connectionString);

    var sql = @"
        SELECT
            p.id,
            p.latitude,
            p.longitude,
            p.radius_meters,
            p.priority,
            p.map_link,
            p.image_url,
            p.audio_url,
            p.is_active,
            COALESCE(NULLIF(t_vi.name, ''), '') AS name_vi,
            COALESCE(p.is_deleted, 0) AS is_deleted,
            p.deleted_at,
            COALESCE(p.delete_status, CASE WHEN COALESCE(p.is_deleted, 0) = 1 THEN 'DELETED' ELSE 'ACTIVE' END) AS delete_status,
            p.owner_admin_id,
            COALESCE(a.username, '') AS owner_username,
            COALESCE(a.full_name, '') AS owner_full_name
        FROM pois p
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        LEFT JOIN admin_accounts a ON p.owner_admin_id = a.id
        ORDER BY p.priority DESC, p.id ASC;
        ";

    if (IsOwner(actor))
    {
        sql = sql.Replace("ORDER BY", "WHERE p.owner_admin_id = $ownerId ORDER BY", StringComparison.Ordinal);
    }

    var result = new List<PoiAdminListItemDto>();
    await using var command = new SqliteCommand(sql, connection);
    if (IsOwner(actor))
    {
        command.Parameters.AddWithValue("$ownerId", actor.Id);
    }
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new PoiAdminListItemDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            Latitude = reader.GetDouble(1),
            Longitude = reader.GetDouble(2),
            RadiusMeters = reader.GetDouble(3),
            Priority = reader.GetInt32(4),
            MapLink = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            ImageUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            AudioUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            IsActive = reader.GetInt32(8) != 0,
            NameVi = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
            IsDeleted = !reader.IsDBNull(10) && reader.GetInt32(10) != 0,
            DeletedAt = reader.IsDBNull(11) ? null : reader.GetString(11),
            DeleteStatus = reader.IsDBNull(12) ? "ACTIVE" : reader.GetString(12),
            OwnerAdminId = reader.IsDBNull(13) ? null : reader.GetInt64(13).ToString(CultureInfo.InvariantCulture),
            OwnerUsername = reader.IsDBNull(14) ? string.Empty : reader.GetString(14),
            OwnerFullName = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
        });
    }

    return result;
}

static async Task<PoiMobileDto?> GetPoiForMobileAsync(SqliteConnection connection, long id, string requestedLang)
{
    const string sql = @"
        SELECT
            p.id,
            p.latitude,
            p.longitude,
            p.radius_meters,
            p.priority,
            p.map_link,
            p.image_url,
            p.audio_url,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            COALESCE(NULLIF(t_req.description, ''), t_vi.description, '') AS description,
            COALESCE(NULLIF(t_req.tts_text, ''), NULLIF(t_req.description, ''), NULLIF(t_vi.tts_text, ''), t_vi.description, '') AS tts_text,
            COALESCE(NULLIF(t_req.audio_url, ''), NULLIF(t_vi.audio_url, ''), '') AS audio_lang
        FROM pois p
        LEFT JOIN poi_translations t_req ON p.id = t_req.poi_id AND t_req.lang_code = $lang_code
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        WHERE p.id = $id AND COALESCE(p.is_deleted, 0) = 0;
        ";

    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$lang_code", requestedLang);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var coreAudioUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
    var translatedAudioUrl = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
    return new PoiMobileDto
    {
        Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
        LangCode = requestedLang,
        Latitude = reader.GetDouble(1),
        Longitude = reader.GetDouble(2),
        RadiusMeters = reader.GetDouble(3),
        Priority = reader.GetInt32(4),
        MapLink = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
        ImageUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        AudioUrl = !string.IsNullOrWhiteSpace(translatedAudioUrl) ? translatedAudioUrl : coreAudioUrl,
        Name = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
        Description = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
        TtsText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
    };
}

static async Task<PoiMobileDto?> GetPoiForPublicAsync(SqliteConnection connection, long id, string requestedLang)
{
    const string sql = @"
        SELECT
            p.id,
            p.latitude,
            p.longitude,
            p.radius_meters,
            p.priority,
            p.map_link,
            p.image_url,
            p.audio_url,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            COALESCE(NULLIF(t_req.description, ''), t_vi.description, '') AS description,
            COALESCE(NULLIF(t_req.tts_text, ''), NULLIF(t_req.description, ''), NULLIF(t_vi.tts_text, ''), t_vi.description, '') AS tts_text,
            COALESCE(NULLIF(t_req.audio_url, ''), NULLIF(t_vi.audio_url, ''), '') AS audio_lang
        FROM pois p
        LEFT JOIN poi_translations t_req ON p.id = t_req.poi_id AND t_req.lang_code = $lang_code
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        WHERE p.id = $id AND p.is_active = 1 AND COALESCE(p.is_deleted, 0) = 0;
        ";

    await using var command = new SqliteCommand(sql, connection);
    command.Parameters.AddWithValue("$id", id);
    command.Parameters.AddWithValue("$lang_code", requestedLang);
    await using var reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var coreAudioUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7);
    var translatedAudioUrl = reader.IsDBNull(11) ? string.Empty : reader.GetString(11);
    return new PoiMobileDto
    {
        Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
        LangCode = requestedLang,
        Latitude = reader.GetDouble(1),
        Longitude = reader.GetDouble(2),
        RadiusMeters = reader.GetDouble(3),
        Priority = reader.GetInt32(4),
        MapLink = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
        ImageUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        AudioUrl = !string.IsNullOrWhiteSpace(translatedAudioUrl) ? translatedAudioUrl : coreAudioUrl,
        Name = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
        Description = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
        TtsText = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
    };
}

static async Task<PoiAdminDto?> GetPoiAdminAsync(SqliteConnection connection, long id, AdminActor actor)
{
    const string coreSql = @"
        SELECT id, latitude, longitude, radius_meters, priority, map_link, image_url, audio_url, is_active, owner_admin_id
        FROM pois
        WHERE id = $id;
        ";

    PoiAdminDto? core = null;
    await using (var command = new SqliteCommand(coreSql, connection))
    {
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return null;
        }
        var ownerAdminId = reader.IsDBNull(9) ? (long?)null : reader.GetInt64(9);
        if (IsOwner(actor) && ownerAdminId != actor.Id)
        {
            return null;
        }

        core = new PoiAdminDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            Latitude = reader.GetDouble(1),
            Longitude = reader.GetDouble(2),
            RadiusMeters = reader.GetDouble(3),
            Priority = reader.GetInt32(4),
            MapLink = reader.IsDBNull(5) ? string.Empty : reader.GetString(5),
            ImageUrl = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            AudioUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            IsActive = reader.GetInt32(8) != 0,
            OwnerAdminId = ownerAdminId?.ToString(CultureInfo.InvariantCulture),
        };
    }

    const string translationSql = @"
        SELECT lang_code, name, description, tts_text, audio_url
        FROM poi_translations
        WHERE poi_id = $id
        ORDER BY lang_code ASC;
        ";

    var translations = new List<PoiTranslationDto>();
    await using (var command = new SqliteCommand(translationSql, connection))
    {
        command.Parameters.AddWithValue("$id", id);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            translations.Add(new PoiTranslationDto
            {
                LangCode = reader.IsDBNull(0) ? "vi" : reader.GetString(0),
                Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
                Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                TtsText = reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
                AudioUrl = reader.IsDBNull(4) ? string.Empty : reader.GetString(4),
            });
        }
    }

    core.Translations = translations;
    return core;
}

static async Task<long> UpsertPoiCoreAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, PoiCoreUpsert request)
{
    if (request.Id is null)
    {
        const string insertSql = @"
            INSERT INTO pois (latitude, longitude, radius_meters, priority, map_link, image_url, audio_url, is_active, owner_admin_id, is_deleted, deleted_at, delete_status)
            VALUES ($latitude, $longitude, $radius, $priority, $map_link, $image_url, $audio_url, $is_active, $owner_admin_id, 0, NULL, 'ACTIVE');
            SELECT last_insert_rowid();
            ";

        await using var insert = new SqliteCommand(insertSql, connection);
        insert.Transaction = (SqliteTransaction)transaction;
        insert.Parameters.AddWithValue("$latitude", request.Latitude);
        insert.Parameters.AddWithValue("$longitude", request.Longitude);
        insert.Parameters.AddWithValue("$radius", request.RadiusMeters);
        insert.Parameters.AddWithValue("$priority", request.Priority);
        insert.Parameters.AddWithValue("$map_link", request.MapLink);
        insert.Parameters.AddWithValue("$image_url", request.ImageUrl ?? string.Empty);
        insert.Parameters.AddWithValue("$audio_url", request.AudioUrl ?? string.Empty);
        insert.Parameters.AddWithValue("$is_active", request.IsActive ? 1 : 0);
        insert.Parameters.AddWithValue("$owner_admin_id", request.OwnerAdminId.HasValue ? request.OwnerAdminId.Value : DBNull.Value);
        var raw = await insert.ExecuteScalarAsync();
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    const string upsertSql = @"
        INSERT INTO pois (id, latitude, longitude, radius_meters, priority, map_link, image_url, audio_url, is_active, owner_admin_id, is_deleted, deleted_at, delete_status)
        VALUES ($id, $latitude, $longitude, $radius, $priority, $map_link, $image_url, $audio_url, $is_active, $owner_admin_id, 0, NULL, 'ACTIVE')
        ON CONFLICT(id) DO UPDATE SET
            latitude = excluded.latitude,
            longitude = excluded.longitude,
            radius_meters = excluded.radius_meters,
            priority = excluded.priority,
            map_link = excluded.map_link,
            image_url = excluded.image_url,
            audio_url = excluded.audio_url,
            is_active = excluded.is_active,
            owner_admin_id = COALESCE(pois.owner_admin_id, excluded.owner_admin_id),
            is_deleted = 0,
            deleted_at = NULL,
            delete_status = 'ACTIVE';
        ";

    await using var command = new SqliteCommand(upsertSql, connection);
    command.Transaction = (SqliteTransaction)transaction;
    command.Parameters.AddWithValue("$id", request.Id.Value);
    command.Parameters.AddWithValue("$latitude", request.Latitude);
    command.Parameters.AddWithValue("$longitude", request.Longitude);
    command.Parameters.AddWithValue("$radius", request.RadiusMeters);
    command.Parameters.AddWithValue("$priority", request.Priority);
    command.Parameters.AddWithValue("$map_link", request.MapLink);
    command.Parameters.AddWithValue("$image_url", request.ImageUrl ?? string.Empty);
    command.Parameters.AddWithValue("$audio_url", request.AudioUrl ?? string.Empty);
    command.Parameters.AddWithValue("$is_active", request.IsActive ? 1 : 0);
    command.Parameters.AddWithValue("$owner_admin_id", request.OwnerAdminId.HasValue ? request.OwnerAdminId.Value : DBNull.Value);
    await command.ExecuteNonQueryAsync();
    return request.Id.Value;
}

static async Task UpsertTranslationAsync(
    SqliteConnection connection,
    System.Data.Common.DbTransaction transaction,
    long poiId,
    string langCode,
    string name,
    string description,
    string ttsText,
    string audioUrl)
{
    const string sql = @"
        INSERT INTO poi_translations (poi_id, lang_code, name, description, tts_text, audio_url)
        VALUES ($id, $lang_code, $name, $description, $tts_text, $audio_url)
        ON CONFLICT(poi_id, lang_code) DO UPDATE SET
            name = excluded.name,
            description = excluded.description,
            tts_text = excluded.tts_text,
            audio_url = excluded.audio_url;
        ";

    await using var command = new SqliteCommand(sql, connection);
    command.Transaction = (SqliteTransaction)transaction;
    command.Parameters.AddWithValue("$id", poiId);
    command.Parameters.AddWithValue("$lang_code", string.IsNullOrWhiteSpace(langCode) ? "vi" : langCode.Trim());
    command.Parameters.AddWithValue("$name", name ?? string.Empty);
    command.Parameters.AddWithValue("$description", description ?? string.Empty);
    command.Parameters.AddWithValue("$tts_text", ttsText ?? string.Empty);
    command.Parameters.AddWithValue("$audio_url", audioUrl ?? string.Empty);
    await command.ExecuteNonQueryAsync();
}

static async Task<DeletePoiResult> DeletePoiAsync(string connectionString, string uploadDirectory, long id, AdminActor actor)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    if (!await HasPoiAccessAsync(connection, id, actor))
    {
        return DeletePoiResult.NotFound;
    }

    const string softDeleteSql = """
        UPDATE pois
        SET is_deleted = 1,
            deleted_at = $deletedAt,
            delete_status = 'DELETED'
        WHERE id = $id AND COALESCE(is_deleted, 0) = 0;
        """;
    await using var cmd = new SqliteCommand(softDeleteSql, connection);
    cmd.Parameters.AddWithValue("$id", id);
    cmd.Parameters.AddWithValue("$deletedAt", DateTimeOffset.UtcNow.ToString("O"));
    var affected = await cmd.ExecuteNonQueryAsync();
    if (affected <= 0)
    {
        return DeletePoiResult.NotFound;
    }

    return DeletePoiResult.Deleted;
}

static async Task<bool> RestorePoiAsync(string connectionString, long id)
{
    await using var connection = await OpenConnectionAsync(connectionString);
    const string sql = """
        UPDATE pois
        SET is_deleted = 0,
            deleted_at = NULL,
            delete_status = 'ACTIVE'
        WHERE id = $id AND COALESCE(is_deleted, 0) = 1;
        """;
    await using var cmd = new SqliteCommand(sql, connection);
    cmd.Parameters.AddWithValue("$id", id);
    var affected = await cmd.ExecuteNonQueryAsync();
    return affected > 0;
}

static string? NormalizeAppLanguageCode(string? languageCode)
{
    if (string.IsNullOrWhiteSpace(languageCode))
    {
        return null;
    }

    var trimmed = languageCode.Trim().Replace('_', '-');
    var parts = trimmed.Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    if (parts.Length == 0)
    {
        return null;
    }

    return parts[0].ToLowerInvariant();
}

static async Task<(string Name, string Description, string TtsText)> TranslatePoiContentAsync(
    string apiKey,
    string sourceLangCode,
    string targetLangCode,
    string name,
    string description,
    string ttsText)
{
    var sourceValues = new[] { name ?? string.Empty, description ?? string.Empty, ttsText ?? string.Empty };
    var sourceIndexes = new List<int>();
    var textsToTranslate = new List<string>();

    for (var i = 0; i < sourceValues.Length; i++)
    {
        if (!string.IsNullOrWhiteSpace(sourceValues[i]))
        {
            sourceIndexes.Add(i);
            textsToTranslate.Add(sourceValues[i]);
        }
    }

    if (textsToTranslate.Count == 0)
    {
        return (string.Empty, string.Empty, string.Empty);
    }

    var translatedTexts = await TranslateTextsAsync(apiKey, sourceLangCode, targetLangCode, textsToTranslate);
    if (translatedTexts.Count != textsToTranslate.Count)
    {
        throw new InvalidOperationException($"Cloud Translation response mismatch for target '{targetLangCode}'.");
    }

    var result = new[] { string.Empty, string.Empty, string.Empty };
    for (var i = 0; i < sourceIndexes.Count; i++)
    {
        result[sourceIndexes[i]] = translatedTexts[i];
    }

    return (result[0], result[1], result[2]);
}

static async Task<List<string>> TranslateTextsAsync(
    string apiKey,
    string sourceLangCode,
    string targetLangCode,
    IReadOnlyList<string> texts)
{
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException("Google Cloud Translation API key is missing.");
    }

    if (texts.Count == 0)
    {
        return [];
    }

    var formFields = new List<KeyValuePair<string, string>>
    {
        new("source", sourceLangCode),
        new("target", targetLangCode),
        new("format", "text")
    };
    foreach (var text in texts)
    {
        formFields.Add(new KeyValuePair<string, string>("q", text));
    }

    var endpoint = $"https://translation.googleapis.com/language/translate/v2?key={Uri.EscapeDataString(apiKey)}";
    using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
    {
        Content = new FormUrlEncodedContent(formFields)
    };
    using var httpClient = new HttpClient();
    using var response = await httpClient.SendAsync(request);
    var body = await response.Content.ReadAsStringAsync();
    if (!response.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Cloud Translation API error ({(int)response.StatusCode}): {body}");
    }

    using var doc = JsonDocument.Parse(body);
    if (!doc.RootElement.TryGetProperty("data", out var data)
        || !data.TryGetProperty("translations", out var translationsElement)
        || translationsElement.ValueKind != JsonValueKind.Array)
    {
        throw new InvalidOperationException("Cloud Translation API response format is invalid.");
    }

    var translated = new List<string>(translationsElement.GetArrayLength());
    foreach (var item in translationsElement.EnumerateArray())
    {
        var text = item.TryGetProperty("translatedText", out var translatedTextElement)
            ? translatedTextElement.GetString() ?? string.Empty
            : string.Empty;
        translated.Add(WebUtility.HtmlDecode(text));
    }

    return translated;
}

static string NormalizePhoneDigits(string? raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        return string.Empty;
    }

    var digits = Regex.Replace(raw.Trim(), @"\D", "");
    if (digits.StartsWith("84", StringComparison.Ordinal) && digits.Length >= 10)
    {
        digits = "0" + digits[2..];
    }

    return digits;
}

static string CreateMobileJwt(long userId, string username, string fullName, string phone, string secret)
{
    var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, userId.ToString(CultureInfo.InvariantCulture)),
        new Claim(ClaimTypes.NameIdentifier, userId.ToString(CultureInfo.InvariantCulture)),
        new Claim("username", username),
        new Claim("full_name", fullName),
        new Claim("phone", phone),
    };
    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new JwtSecurityToken(
        issuer: "FoodStreetPoiAdmin",
        audience: "FoodStreetMobile",
        claims: claims,
        expires: DateTime.UtcNow.AddDays(30),
        signingCredentials: creds);
    return new JwtSecurityTokenHandler().WriteToken(token);
}

static async Task<long> MobileRegisterUserAsync(string connectionString, string username, string password, string fullName, string phoneDisplay, string phoneDigits)
{
    await using var conn = await OpenConnectionAsync(connectionString);
    await using var checkUser = new SqliteCommand("SELECT 1 FROM app_users WHERE lower(username) = lower($u) LIMIT 1;", conn);
    checkUser.Parameters.AddWithValue("$u", username);
    var exists = await checkUser.ExecuteScalarAsync();
    if (exists is not null)
    {
        throw new InvalidOperationException("Username da ton tai.");
    }

    await using var checkPhone = new SqliteCommand("SELECT 1 FROM app_users WHERE phone_digits = $p LIMIT 1;", conn);
    checkPhone.Parameters.AddWithValue("$p", phoneDigits);
    var existsPhone = await checkPhone.ExecuteScalarAsync();
    if (existsPhone is not null)
    {
        throw new InvalidOperationException("So dien thoai da duoc dang ky.");
    }

    var hash = BCrypt.Net.BCrypt.HashPassword(password);
    await using var insert = new SqliteCommand("""
        INSERT INTO app_users (username, password_hash, full_name, phone, phone_digits, created_at)
        VALUES ($u, $h, $fn, $ph, $pd, $ca);
        SELECT last_insert_rowid();
        """, conn);
    insert.Parameters.AddWithValue("$u", username);
    insert.Parameters.AddWithValue("$h", hash);
    insert.Parameters.AddWithValue("$fn", fullName);
    insert.Parameters.AddWithValue("$ph", phoneDisplay);
    insert.Parameters.AddWithValue("$pd", phoneDigits);
    insert.Parameters.AddWithValue("$ca", DateTimeOffset.UtcNow.ToString("O"));
    var raw = await insert.ExecuteScalarAsync();
    return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
}

static async Task<(long Id, string Username, string FullName, string Phone)?> MobileFindUserForLoginAsync(string connectionString, string usernameOrPhone, string password)
{
    await using var conn = await OpenConnectionAsync(connectionString);
    var trimmed = usernameOrPhone.Trim();
    var digits = NormalizePhoneDigits(trimmed);
    var phoneMatch = digits.Length >= 8 ? digits : "___no_phone_match___";

    const string sql = """
        SELECT id, username, password_hash, full_name, phone
        FROM app_users
        WHERE lower(username) = lower($u) OR phone_digits = $pd
        LIMIT 1;
        """;
    await using var cmd = new SqliteCommand(sql, conn);
    cmd.Parameters.AddWithValue("$u", trimmed);
    cmd.Parameters.AddWithValue("$pd", phoneMatch);
    await using var reader = await cmd.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
    {
        return null;
    }

    var id = reader.GetInt64(0);
    var username = reader.GetString(1);
    var hash = reader.GetString(2);
    var fullName = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
    var phone = reader.IsDBNull(4) ? string.Empty : reader.GetString(4);

    if (!BCrypt.Net.BCrypt.Verify(password, hash))
    {
        return null;
    }

    return (id, username, fullName, phone);
}

static async Task<bool> MobileChangePasswordAsync(string connectionString, long userId, string currentPassword, string newPassword)
{
    await using var conn = await OpenConnectionAsync(connectionString);
    await using var select = new SqliteCommand("SELECT password_hash FROM app_users WHERE id = $id LIMIT 1;", conn);
    select.Parameters.AddWithValue("$id", userId);
    var scalar = await select.ExecuteScalarAsync();
    if (scalar is null)
    {
        return false;
    }

    var hash = Convert.ToString(scalar, CultureInfo.InvariantCulture) ?? string.Empty;
    if (string.IsNullOrEmpty(hash) || !BCrypt.Net.BCrypt.Verify(currentPassword, hash))
    {
        return false;
    }

    var newHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
    await using var update = new SqliteCommand("UPDATE app_users SET password_hash = $h WHERE id = $id;", conn);
    update.Parameters.AddWithValue("$h", newHash);
    update.Parameters.AddWithValue("$id", userId);
    var rows = await update.ExecuteNonQueryAsync();
    return rows > 0;
}

sealed class MobileRegisterRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
    public string? Phone { get; set; }
}

sealed class MobileLoginRequest
{
    public string? UsernameOrPhone { get; set; }
    public string? Password { get; set; }
}

sealed class MobileChangePasswordRequest
{
    public string? CurrentPassword { get; set; }
    public string? NewPassword { get; set; }
}

sealed class MobileAuthResponse
{
    public string Token { get; set; } = string.Empty;
    public long UserId { get; set; }
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
}

enum DeletePoiResult
{
    Unknown = 0,
    NotFound = 1,
    Deleted = 2
}

readonly record struct AdminActor(long Id, string Username, string Role, string FullName);

sealed class SupportedLanguage
{
    public required string Code { get; init; }
    public required string Label { get; init; }

    public static IReadOnlyList<SupportedLanguage> CreateDefaults()
        =>
        [
            new SupportedLanguage { Code = "vi", Label = "Tiếng Việt (vi)" },
            new SupportedLanguage { Code = "en", Label = "English (en)" },
            new SupportedLanguage { Code = "zh", Label = "中文 (zh)" },
            new SupportedLanguage { Code = "ja", Label = "日本語 (ja)" },
            new SupportedLanguage { Code = "ru", Label = "Русский (ru)" },
            new SupportedLanguage { Code = "ko", Label = "한국어 (ko)" },
        ];
}

sealed class PoiMobileDto
{
    public string Id { get; set; } = string.Empty;
    public string LangCode { get; set; } = "vi";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TtsText { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; }
    public int Priority { get; set; }
    public string MapLink { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
}

sealed class PoiAdminListItemDto
{
    public string Id { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; }
    public int Priority { get; set; }
    public string MapLink { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public string NameVi { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public string? DeletedAt { get; set; }
    public string DeleteStatus { get; set; } = "ACTIVE";
    public string? OwnerAdminId { get; set; }
    public string OwnerUsername { get; set; } = string.Empty;
    public string OwnerFullName { get; set; } = string.Empty;
}

sealed class PoiAudioPlayStatDto
{
    public string PoiId { get; set; } = string.Empty;
    public string PoiName { get; set; } = string.Empty;
    public long PlayCount { get; set; }
    public string? LastPlayedAt { get; set; }
}

sealed class FeaturedPoiDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public long PlayCount { get; set; }
}

sealed class PoiTranslationDto
{
    public string LangCode { get; set; } = "vi";
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string TtsText { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
}

sealed class PoiAdminDto
{
    public string Id { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 40;
    public int Priority { get; set; }
    public string MapLink { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? OwnerAdminId { get; set; }
    public List<PoiTranslationDto> Translations { get; set; } = [];
}

sealed class OwnerAccountDto
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public string? DeletedAt { get; set; }
    public string DeleteStatus { get; set; } = "ACTIVE";
}

sealed class AdminLoginRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
}

sealed class AdminCreateOwnerRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
}

sealed class AdminUpdateOwnerRequest
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public string? FullName { get; set; }
}

sealed class AssignPoiOwnerRequest
{
    public string? OwnerId { get; set; }
}

sealed class PoiAdminUpsertRequest
{
    public string? Id { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 40;
    public int Priority { get; set; }
    public string? MapLink { get; set; }
    public string? ImageUrl { get; set; }
    public string? AudioUrl { get; set; }
    public bool IsActive { get; set; } = true;
    public string? SourceLangCode { get; set; }
    public string? SourceName { get; set; }
    public string? SourceDescription { get; set; }
    public string? SourceTtsText { get; set; }
    public List<PoiTranslationDto>? Translations { get; set; }
}

sealed class PoiCoreUpsert
{
    public required long? Id { get; init; }
    public required double Latitude { get; init; }
    public required double Longitude { get; init; }
    public required double RadiusMeters { get; init; }
    public required int Priority { get; init; }
    public required string MapLink { get; init; }
    public required string ImageUrl { get; init; }
    public required string AudioUrl { get; init; }
    public required bool IsActive { get; init; }
    public required long? OwnerAdminId { get; init; }
}

sealed class ShopDto
{
    public string Id { get; set; } = string.Empty;
    public string LangCode { get; set; } = "vi";
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
    public string? LangCode { get; set; }
    public string ShopName { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 40;
    public string? Description { get; set; }
    public string? TtsText { get; set; }
}
