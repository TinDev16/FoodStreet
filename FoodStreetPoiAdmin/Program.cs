using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using FoodStreetPoiAdmin.Supabase;
using Microsoft.IdentityModel.Tokens;
using System.Diagnostics;
using System.Globalization;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

// Fix for Render/Cloud deployment inotify limits
Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

var builder = WebApplication.CreateBuilder(args);

var urls = builder.Configuration["urls"] ?? Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
if (string.IsNullOrEmpty(urls) || urls.Contains("localhost") || urls.Contains("127.0.0.1"))
{
    builder.WebHost.ConfigureKestrel(options =>
    {
        // Bind to all interfaces on port 5187 if running locally, 
        // allowing physical devices on the same LAN to connect.
        options.ListenAnyIP(5187);
    });
}

var jwtSecret = (Environment.GetEnvironmentVariable("FOODSTREET_JWT_SECRET") ?? builder.Configuration["Jwt:Secret"])?.Trim();
if (string.IsNullOrWhiteSpace(jwtSecret) || jwtSecret.Length < 32)
{
    throw new InvalidOperationException("Missing/invalid FOODSTREET_JWT_SECRET (must be >= 32 chars). Set it as env var or in appsettings.json (Jwt:Secret).");
}

var bootstrapSuperAdminUser = (Environment.GetEnvironmentVariable("FOODSTREET_SUPERADMIN_USER") ?? builder.Configuration["SuperAdmin:User"])?.Trim();
var bootstrapSuperAdminPassword = (Environment.GetEnvironmentVariable("FOODSTREET_SUPERADMIN_PASSWORD") ?? builder.Configuration["SuperAdmin:Password"])?.Trim();
var enableBootstrapSuperAdmin = !string.IsNullOrWhiteSpace(bootstrapSuperAdminUser) || !string.IsNullOrWhiteSpace(bootstrapSuperAdminPassword);
if (enableBootstrapSuperAdmin && (string.IsNullOrWhiteSpace(bootstrapSuperAdminUser) || string.IsNullOrWhiteSpace(bootstrapSuperAdminPassword)))
{
    throw new InvalidOperationException("To bootstrap superadmin, set both FOODSTREET_SUPERADMIN_USER and FOODSTREET_SUPERADMIN_PASSWORD.");
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

builder.Services.AddHostedService<TtsQueueWorker>();
builder.Services.AddHttpClient();

var supabaseUrl = (Environment.GetEnvironmentVariable("SUPABASE_URL") ?? builder.Configuration["Supabase:Url"])?.Trim();
if (string.IsNullOrWhiteSpace(supabaseUrl))
{
    throw new InvalidOperationException("Missing SUPABASE_URL (or appsettings Supabase:Url).");
}
supabaseUrl = NormalizeSupabaseBaseUrl(supabaseUrl);

var supabaseServiceRoleKey = (Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") ?? builder.Configuration["Supabase:ServiceRoleKey"])?.Trim();
if (string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
{
    // Back-compat for older deployments. Prefer SUPABASE_SERVICE_ROLE_KEY.
    supabaseServiceRoleKey = (Environment.GetEnvironmentVariable("SUPABASE_KEY") ?? builder.Configuration["Supabase:Key"])?.Trim();
}
if (string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
{
    throw new InvalidOperationException("Missing SUPABASE_SERVICE_ROLE_KEY (or appsettings Supabase:ServiceRoleKey).");
}

builder.Services.AddHttpClient<SupabaseRestClient>(client =>
{
    client.BaseAddress = new Uri(supabaseUrl, UriKind.Absolute);
    client.DefaultRequestHeaders.Remove("apikey");
    client.DefaultRequestHeaders.Remove("Authorization");
    client.DefaultRequestHeaders.Add("apikey", supabaseServiceRoleKey);
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {supabaseServiceRoleKey}");
    client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
    client.Timeout = TimeSpan.FromSeconds(20);
});
builder.Services.AddScoped<IDataService, SupabaseDataService>();

var app = builder.Build();

app.UseForwardedHeaders();

var dataDirectory = Path.Combine(app.Environment.ContentRootPath, "App_Data");
var uploadDirectory = Path.Combine(app.Environment.WebRootPath ?? Path.Combine(app.Environment.ContentRootPath, "wwwroot"), "uploads");
Directory.CreateDirectory(dataDirectory);
Directory.CreateDirectory(uploadDirectory);
var adbReverseSync = new object();
var lastAdbReverseAttemptUtc = DateTimeOffset.MinValue;

var supportedLanguages = SupportedLanguage.CreateDefaults();
var supportedLanguageSet = supportedLanguages.Select(x => x.Code).ToHashSet(StringComparer.OrdinalIgnoreCase);
var configuredPublicBaseUrl = NormalizePublicBaseUrl(
    Environment.GetEnvironmentVariable("POI_PUBLIC_BASE_URL")
    ?? Environment.GetEnvironmentVariable("PUBLIC_BASE_URL")
    ?? app.Configuration["PublicBaseUrl"]);
var translationApiKey = (Environment.GetEnvironmentVariable("GOOGLE_TRANSLATE_API_KEY") ?? builder.Configuration["GoogleTranslate:ApiKey"])?.Trim();

if (enableBootstrapSuperAdmin)
{
    using var scope = app.Services.CreateScope();
    var dataService = scope.ServiceProvider.GetRequiredService<IDataService>();
    try
    {
        if (supabaseServiceRoleKey != "YOUR_SUPABASE_SERVICE_ROLE_KEY_HERE" && !string.IsNullOrWhiteSpace(supabaseServiceRoleKey))
        {
            await dataService.EnsureBootstrapSuperAdminAsync(bootstrapSuperAdminUser!, bootstrapSuperAdminPassword!);
        }
        else
        {
            Console.WriteLine("WARNING: Supabase Service Role Key is not configured. Skipping superadmin bootstrap.");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"WARNING: Failed to bootstrap superadmin (Supabase might be unreachable): {ex.Message}");
    }
}

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
            var entry = $"-----{Environment.NewLine}{DateTimeOffset.UtcNow:O}{Environment.NewLine}{ex}{Environment.NewLine}";
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

app.MapPost("/api/admin/auth/login", async (AdminLoginRequest? req, IDataService dataService) =>
{
    if (req is null || string.IsNullOrWhiteSpace(req.Username) || string.IsNullOrWhiteSpace(req.Password))
    {
        return Results.BadRequest(new { error = "Thieu username hoac password." });
    }

    var admin = await dataService.FindAdminForLoginAsync(req.Username.Trim(), req.Password);
    if (admin is null)
    {
        return Results.Unauthorized();
    }

    var token = CreateAdminJwt(admin.Id, admin.Username, admin.Role, admin.FullName, jwtSecret);
    return Results.Ok(new
    {
        token,
        user = new
        {
            id = admin.Id,
            username = admin.Username,
            role = admin.Role,
            fullName = admin.FullName
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
app.MapGet("/api/admin/owners", async (HttpContext context, IDataService dataService) =>
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
    var owners = await dataService.GetOwnerAccountsAsync(includeDeleted);
    return Results.Ok(owners);
}).RequireAuthorization();

app.MapPost("/api/admin/owners", async (HttpContext context, AdminCreateOwnerRequest? req, IDataService dataService) =>
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
        var ownerId = await dataService.CreateOwnerAccountAsync(req.Username.Trim(), req.Password.Trim(), req.FullName?.Trim() ?? string.Empty);
        return Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPut("/api/admin/owners/{id}", async (HttpContext context, string id, AdminUpdateOwnerRequest? req, IDataService dataService) =>
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
        var ok = await dataService.UpdateOwnerAccountAsync(ownerId, req.Username?.Trim(), req.FullName?.Trim(), req.Password?.Trim());
        return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapDelete("/api/admin/owners/{id}", async (HttpContext context, string id, IDataService dataService) =>
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
        var ok = await dataService.DeleteOwnerAccountAsync(ownerId);
        return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireAuthorization();

app.MapPost("/api/admin/owners/{id}/restore", async (HttpContext context, string id, IDataService dataService) =>
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

    var ok = await dataService.RestoreOwnerAccountAsync(ownerId);
    return ok ? Results.Ok(new { id = ownerId.ToString(CultureInfo.InvariantCulture), restored = true }) : Results.NotFound();
}).RequireAuthorization();

app.MapPost("/api/admin/pois/{id}/assign-owner", async (HttpContext context, string id, AssignPoiOwnerRequest? req, IDataService dataService) =>
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
        var ok = await dataService.AssignOwnerToPoiAsync(poiId, ownerId);
        return ok ? Results.Ok(new { id, ownerId = ownerId?.ToString(CultureInfo.InvariantCulture) }) : Results.NotFound();
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
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

app.MapGet("/api/pois", async (HttpContext context, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var items = await dataService.GetPoisForMobileAsync(requestedLang);
    return Results.Ok(items);
});

// Admin list (includes inactive) with role-based ownership filter.
app.MapGet("/api/pois/admin", async (HttpContext context, IDataService dataService) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    var items = await dataService.GetPoisForAdminListAsync(actor);
    return Results.Ok(items);
}).RequireAuthorization();

// Admin: load core + all translations with ownership filter.
app.MapGet("/api/pois/{id}", async (HttpContext context, string id, IDataService dataService) =>
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

    var core = await dataService.GetPoiAdminAsync(poiId, actor);
    return core is null ? Results.NotFound() : Results.Ok(core);
}).RequireAuthorization();

// Mobile: load localized view (fallback to Vietnamese when missing).
app.MapGet("/api/pois/{id}/localized", async (HttpContext context, string id, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await dataService.GetPoiForMobileByIdAsync(poiId, requestedLang);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/public/featured-pois", async (HttpContext context, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var limit = 4;
    if (int.TryParse(context.Request.Query["limit"].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedLimit))
    {
        limit = Math.Clamp(parsedLimit, 1, 20);
    }

    var items = await dataService.GetFeaturedPoisForPublicAsync(requestedLang, limit);
    return Results.Ok(items);
});

app.MapGet("/api/public/pois/{id}", async (HttpContext context, string id, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await dataService.GetPoiForPublicByIdAsync(poiId, requestedLang);
    return item is null ? Results.NotFound() : Results.Ok(item);
});

app.MapGet("/api/pois/{id}/public-link", async (HttpContext context, string id, IDataService dataService) =>
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

    var core = await dataService.GetPoiAdminAsync(poiId, actor);
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

app.MapGet("/api/pois/{id}/qr.png", async (HttpContext context, string id, IDataService dataService) =>
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

    var core = await dataService.GetPoiAdminAsync(poiId, actor);
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

    var publicUrl = BuildQrScanUrl(baseUrl, poiId);
    var pngBytes = await RenderQrPngAsync(publicUrl, size, context.RequestAborted);

    if (download)
    {
        var fileName = $"poi-{poiId}.png";
        return Results.File(pngBytes, "image/png", fileName);
    }

    return Results.File(pngBytes, "image/png");
}).RequireAuthorization();

app.MapGet("/api/admin/qr/master.png", async (HttpContext context) =>
{
    if (!TryGetAdminActor(context.User, out var actor))
    {
        return Results.Unauthorized();
    }

    await TryEnsureAdbReverseAsync();
    var (baseUrl, error) = await ResolvePublicBaseUrlForRequestAsync(context);
    if (!string.IsNullOrWhiteSpace(error) || string.IsNullOrWhiteSpace(baseUrl))
    {
        return Results.BadRequest(new { error });
    }

    var publicUrl = BuildQrScanUrl(baseUrl, 0);
    var rawSize = context.Request.Query["size"].ToString();
    var size = 512;
    if (!string.IsNullOrWhiteSpace(rawSize) && int.TryParse(rawSize, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsedSize))
    {
        size = Math.Clamp(parsedSize, 256, 2048);
    }

    var download = string.Equals(context.Request.Query["download"], "1", StringComparison.OrdinalIgnoreCase);
    var pngBytes = await RenderQrPngAsync(publicUrl, size, context.RequestAborted);

    if (download)
    {
        return Results.File(pngBytes, "image/png", "foodstreet-master-qr.png");
    }

    return Results.File(pngBytes, "image/png");
}).RequireAuthorization();

app.MapGet("/qr/scan", (HttpContext context) =>
{
    var code = context.Request.Query["code"].ToString();
    var html = $@"
<!DOCTYPE html>
<html>
<head>
    <title>Đang xác thực...</title>
    <meta name=""viewport"" content=""width=device-width, initial-scale=1"">
    <style>
        body {{ font-family: sans-serif; display: flex; justify-content: center; align-items: center; height: 100vh; margin: 0; background-color: #f8fafc; color: #334155; }}
        .loader {{ border: 4px solid #e2e8f0; border-top: 4px solid #4f46e5; border-radius: 50%; width: 40px; height: 40px; animation: spin 1s linear infinite; margin: 0 auto 20px; }}
        @keyframes spin {{ 0% {{ transform: rotate(0deg); }} 100% {{ transform: rotate(360deg); }} }}
    </style>
</head>
<body>
    <div style=""text-align: center;"">
        <div class=""loader""></div>
        <div>Loading...</div>
    </div>
    <script>
        function getOrCreateDeviceId() {{
            let id = localStorage.getItem(""device_id"");
            if (!id) {{
                try {{ id = crypto.randomUUID(); }} 
                catch(e) {{ id = ""dev_"" + Date.now() + ""_"" + Math.random().toString(36).substring(2); }}
                localStorage.setItem(""device_id"", id);
            }}
            return id;
        }}

        setTimeout(async () => {{
            const isTouch = (""ontouchstart"" in window) || (navigator.maxTouchPoints > 0);
            const screenWidth = window.screen.width;
            const screenInfo = JSON.stringify({{ w: window.screen.width, h: window.screen.height, dpr: window.devicePixelRatio || 1 }});
            const deviceId = getOrCreateDeviceId();
            let sid = localStorage.getItem(""session_id"");
            if (!sid) {{ sid = ""web_"" + Date.now() + ""_"" + Math.random().toString(36).substring(2); localStorage.setItem(""session_id"", sid); }}

            try {{
                const res = await fetch(""/api/public/qr/confirm"", {{
                    method: ""POST"",
                    headers: {{ ""Content-Type"": ""application/json"" }},
                    body: JSON.stringify({{ screenWidth, isTouch, code: ""{WebUtility.HtmlEncode(code)}"", sessionId: sid, deviceId, screenInfo }})
                }});
                const data = await res.json();
                if (data && data.url) window.location.replace(data.url);
                else window.location.replace(""/poi.html?id="" + encodeURIComponent(""{WebUtility.HtmlEncode(code)}""));
            }} catch(e) {{
                window.location.replace(""/poi.html?id="" + encodeURIComponent(""{WebUtility.HtmlEncode(code)}""));
            }}
        }}, 300);
    </script>
</body>
</html>";
    return Results.Content(html, "text/html");
});

app.MapPost("/api/public/qr/confirm", async (HttpContext context, QrConfirmRequest req, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    var code = req.Code ?? "";
    bool isReal = req.IsTouch && req.ScreenWidth <= 1024;
    var deviceType = (isReal || req.ScreenWidth <= 1024) ? "mobile" : "desktop";
    
    long? poiId = null;
    if (TryParsePoiId(code, out var p)) poiId = p;
    
    var lang = context.Request.Query["lang"].FirstOrDefault()
               ?? context.Request.Headers["Accept-Language"].ToString()?.Split(',').FirstOrDefault()?.Split(';').FirstOrDefault()
               ?? "vi";
    lang = NormalizeAppLanguageCode(lang);

    var sid = string.IsNullOrWhiteSpace(req.SessionId) ? ($"anon_{Guid.NewGuid():N}") : req.SessionId;
    var ua = context.Request.Headers["User-Agent"].ToString();
    var ip = context.Connection.RemoteIpAddress?.ToString();

    await dataService.RecordUserActivityAsync(
        sid, 
        "web", 
        "scan_qr", 
        lang, 
        deviceType, 
        poiId, 
        isReal ? 1 : 0, 
        null,
        req.DeviceId,
        ua,
        ip,
        req.ScreenInfo,
        req.Latitude,
        req.Longitude);
    
    var (baseUrl, _) = await ResolvePublicBaseUrlForRequestAsync(context);
    var url = BuildPublicPoiUrl(baseUrl ?? "", poiId ?? 0, lang);
    return Results.Ok(new { url });
});

app.MapPost("/api/public/pois/track-activity", async (HttpContext context, TrackActivityRequest request, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    
    if (string.IsNullOrWhiteSpace(request.Action) || string.IsNullOrWhiteSpace(request.SessionId) || string.IsNullOrWhiteSpace(request.Platform)) {
        return Results.BadRequest(new { error = "Thiếu dữ liệu bắt buộc (action, sessionId, platform)." });
    }
    
    long? poiId = null;
    if (!string.IsNullOrWhiteSpace(request.PoiId)) {
        if (TryParsePoiId(request.PoiId, out var p)) poiId = p;
    }

    var ua = context.Request.Headers["User-Agent"].ToString();
    var ip = context.Connection.RemoteIpAddress?.ToString();
    
    var recorded = await dataService.RecordUserActivityAsync(
        request.SessionId, 
        request.Platform, 
        request.Action, 
        request.Language, 
        request.DeviceType, 
        poiId, 
        null, 
        request.Duration,
        request.DeviceId,
        ua,
        ip,
        request.ScreenInfo,
        request.Latitude,
        request.Longitude);

    return recorded ? Results.Ok(new { recorded = true }) : Results.Problem("Cannot record activity.");
});

app.MapPost("/api/public/tts/request", async (TtsRequest req, IDataService dataService) =>
{
    if (string.IsNullOrWhiteSpace(req.Text) || string.IsNullOrWhiteSpace(req.PoiId))
    {
        return Results.BadRequest(new { error = "Missing text or poiId." });
    }

    var jobId = Guid.NewGuid().ToString();
    await dataService.EnqueueTtsJobAsync(jobId, req.PoiId.Trim(), req.Text.Trim());

    return Results.Ok(new { jobId });
});


// Store unlocked Master QR sessions/users in memory for simulated payment
var unlockedMasterSessions = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>();

app.MapGet("/api/public/master/info", (HttpContext context, IConfiguration config) =>
{
    var unlockFee = config.GetValue<long>("MasterQr:UnlockFee", 0);
    var sessionId = context.Request.Query["sessionId"].ToString() ?? "";
    var userId = context.Request.Query["userId"].ToString() ?? "";
    
    var isUnlocked = unlockFee <= 0 || 
                     (!string.IsNullOrEmpty(sessionId) && unlockedMasterSessions.ContainsKey(sessionId)) ||
                     (!string.IsNullOrEmpty(userId) && unlockedMasterSessions.ContainsKey(userId));

    return Results.Ok(new
    {
        unlockFee,
        isUnlocked
    });
});

app.MapPost("/api/public/master/unlock", async (HttpContext context) =>
{
    var req = await context.Request.ReadFromJsonAsync<System.Text.Json.Nodes.JsonObject>();
    var sessionId = req?["sessionId"]?.ToString() ?? "";
    var userId = req?["userId"]?.ToString() ?? "";

    if (!string.IsNullOrEmpty(sessionId))
    {
        unlockedMasterSessions.TryAdd(sessionId, true);
    }
    if (!string.IsNullOrEmpty(userId))
    {
        unlockedMasterSessions.TryAdd(userId, true);
    }

    return Results.Ok(new { success = true, isUnlocked = true });
});

#if false
app.MapGet("/api/admin/reports/user-activities", async (HttpContext context, 
    string? platform, string? period, string? from, string? to, string? poiSort, string? fields, string? action) =>
{
    if (!TryGetAdminActor(context.User, out var actor)) return Results.Unauthorized();
    await TryEnsureAdbReverseAsync();

    await using var conn = await OpenConnectionAsync(connectionString);

    bool isOwner = IsOwner(actor);
    string ownerWhere = isOwner ? "AND p.owner_admin_id = $ownerId" : "";

    // Normalize optional single-action filter for Hourly + Ranking charts.
    // Accept both short aliases (online/audio/qr/view) and raw action names.
    string? actionFilterValue = null;
    if (!string.IsNullOrWhiteSpace(action))
    {
        var a = action.Trim().ToLowerInvariant();
        actionFilterValue = a switch
        {
            "online" or "ping" => "ping",
            "audio" or "play_audio" => "play_audio",
            "qr" or "scan_qr" => "scan_qr",
            "view" or "view_poi" => "view_poi",
            _ => null
        };
    }
    // When a specific action is selected we lock both charts to that action only;
    // otherwise we keep the default (exclude 'ping' heartbeats so interactions are clean).
    string actionClauseSpecific = actionFilterValue is null ? "uae.action != 'ping'" : "uae.action = $actionFilter";

    // 1. Online now (last 20s based on UTC)
    long onlineNow = 0;
    string onlineSql;
    if (!isOwner)
    {
        onlineSql = "SELECT COUNT(DISTINCT session_id) FROM active_sessions WHERE datetime(last_ping_at) >= datetime('now', '-45 seconds')";
        if (!string.IsNullOrEmpty(platform) && platform != "all") onlineSql += " AND platform = $platform";
    }
    else
    {
        // For Owners: must check both session liveness and interaction with their POIs.
        onlineSql = @$"
            SELECT COUNT(DISTINCT s.session_id)
            FROM active_sessions s
            JOIN user_activity_events uae ON uae.session_id = s.session_id
            JOIN pois p ON p.id = uae.poi_id
            WHERE datetime(s.last_ping_at) >= datetime('now', '-45 seconds')
              AND datetime(uae.created_at) >= datetime('now', '-10 minutes')
              AND p.owner_admin_id = $ownerId
            ";
        if (!string.IsNullOrEmpty(platform) && platform != "all") onlineSql += " AND s.platform = $platform";
    }

    await using (var cmd = new SqliteCommand(onlineSql, conn))
    {
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (!string.IsNullOrEmpty(platform) && platform != "all") cmd.Parameters.AddWithValue("$platform", platform);
        var res = await cmd.ExecuteScalarAsync();
        if (res != null) onlineNow = Convert.ToInt64(res, CultureInfo.InvariantCulture);
    }

    if (fields == "onlineNow") {
        return Results.Ok(new { onlineNow });
    }
    
    var vnTz = GetVnTimeZone();
    var nowUtc = DateTimeOffset.UtcNow;
    var nowVn = TimeZoneInfo.ConvertTime(nowUtc, vnTz);

    // 2. Filter logic for historical data (Index-friendly UTC boundaries)
    DateTimeOffset startUtc = DateTimeOffset.MinValue;
    DateTimeOffset endUtc = DateTimeOffset.MaxValue;
    string startDateStr = "";
    string endDateStr = nowVn.ToString("yyyy-MM-dd");

    if (period == "today")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn));
        startUtc = startVn.ToUniversalTime();
        endUtc = startUtc.AddDays(1);
        startDateStr = endDateStr;
    }
    else if (period == "week")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-6);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "month")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-29);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "year")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-364);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "custom" && !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
    {
        var f = ParseDateOnlyFilter(from);
        var t = ParseDateOnlyFilter(to);
        if (f.HasValue && t.HasValue)
        {
            var startVn = new DateTimeOffset(f.Value.Year, f.Value.Month, f.Value.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn));
            startUtc = startVn.ToUniversalTime();
            var endVn = new DateTimeOffset(t.Value.Year, t.Value.Month, t.Value.Day, 23, 59, 59, 999, vnTz.GetUtcOffset(nowVn));
            endUtc = endVn.ToUniversalTime();
            startDateStr = f.Value.ToString("yyyy-MM-dd");
            endDateStr = t.Value.ToString("yyyy-MM-dd");
        }
    }


    var sqliteVnOffset = GetSqliteOffset(vnTz, nowUtc);

    
    string platformFilter = (!string.IsNullOrEmpty(platform) && platform != "all") ? " AND uae.platform = $platform" : "";
    
    // 3. Summary stats for select period
    long periodAudioPlays = 0;
    long periodQrScans = 0;
    long periodViews = 0;
    string summarySql = $@"
        SELECT uae.action, COUNT(1) 
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere}
        GROUP BY uae.action;";
    
    await using (var cmd = new SqliteCommand(summarySql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var act = reader.GetString(0);
            var count = reader.GetInt64(1);
            if (act == "play_audio") periodAudioPlays = count;
            else if (act == "scan_qr") periodQrScans = count;
            else if (act == "view_poi") periodViews = count;
        }
    }

    // 4. Chart Data (Grouped by Date in VN Time)
    var chartData = new List<object>();
    string chartSql = $@"
        SELECT date(uae.created_at, $sqliteOffset) as dt, uae.action, COUNT(1) as c, uae.platform
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere}
        GROUP BY dt, uae.action, uae.platform
        ORDER BY dt ASC;";
    await using (var cmd = new SqliteCommand(chartSql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$sqliteOffset", sqliteVnOffset);
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            chartData.Add(new { 
                date = reader.GetString(0), 
                action = reader.GetString(1), 
                count = reader.GetInt64(2),
                platform = reader.GetString(3) 
            });
        }
    }

    // 5. Hourly Activity (0-23 in VN Time). When a specific action is selected
    // (via ?action=audio|qr|view|online), only count that action. Otherwise
    // default to all interactions excluding 'ping' heartbeats.
    // Special case: for 'ping' (Online) we count DISTINCT sessions so the bar
    // reflects the number of unique online users per hour (not raw heartbeats).
    var hourlyData = new List<object>();
    string hourlyCountExpr = (actionFilterValue == "ping") ? "COUNT(DISTINCT uae.session_id)" : "COUNT(1)";
    string hourlySql = $@"
        SELECT CAST(strftime('%H', uae.created_at, $sqliteOffset) AS INTEGER) as hr, {hourlyCountExpr}
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE {actionClauseSpecific} AND uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere}
        GROUP BY hr ORDER BY hr ASC;";
    await using (var cmd = new SqliteCommand(hourlySql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$sqliteOffset", sqliteVnOffset);
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (actionFilterValue is not null) cmd.Parameters.AddWithValue("$actionFilter", actionFilterValue);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            hourlyData.Add(new { hour = reader.GetInt32(0).ToString("D2"), count = reader.GetInt64(1) });
        }
    }

    // 6. Top POI Ranking. Default view uses a weighted score (Scan=3, Audio=2, View=1).
    // When a specific action is selected, rank by raw count of that action only.
    // For 'ping' (Online) the ranking is not meaningful (heartbeats don't reflect
    // interest in a POI), so we intentionally return an empty list.
    var topPois = new List<object>();
    if (actionFilterValue != "ping")
    {
        string sortDir = (poiSort == "asc") ? "ASC" : "DESC"; // default DESC
        string poiRankLang = "vi";

        string poiScoreExpr = actionFilterValue is null
            ? @"SUM(CASE uae.action 
                       WHEN 'scan_qr' THEN 3 
                       WHEN 'play_audio' THEN 2 
                       WHEN 'view_poi' THEN 1 
                       ELSE 0 END)"
            : "COUNT(1)";

        string poiSql = $@"
            SELECT uae.poi_id, t.name, {poiScoreExpr} as score
            FROM user_activity_events uae
            LEFT JOIN pois p ON p.id = uae.poi_id
            LEFT JOIN (
                SELECT poi_id, name 
                FROM poi_translations 
                WHERE lang_code = $rankLang
                GROUP BY poi_id
            ) t ON uae.poi_id = t.poi_id
            WHERE uae.poi_id IS NOT NULL 
              AND {actionClauseSpecific} 
              AND uae.created_at >= $startUtc AND uae.created_at < $endUtc
              {platformFilter} {ownerWhere}
            GROUP BY uae.poi_id
            ORDER BY score {sortDir}
            LIMIT 50;";
        await using var cmd = new SqliteCommand(poiSql, conn);
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$rankLang", poiRankLang);
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (actionFilterValue is not null) cmd.Parameters.AddWithValue("$actionFilter", actionFilterValue);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);


        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            topPois.Add(new { 
                poiId = reader.IsDBNull(0) ? 0 : reader.GetInt64(0), 
                name = reader.IsDBNull(1) ? "Unknown POI" : reader.GetString(1),
                count = reader.GetInt64(2)
            });
        }
    }

    // 7. NEW: Unique Devices Count
    long totalUniqueDevices = 0;
    string uniqueDevicesSql = $@"
        SELECT COUNT(DISTINCT uae.device_id) 
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere};";
    await using (var cmd = new SqliteCommand(uniqueDevicesSql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);
        var res = await cmd.ExecuteScalarAsync();
        totalUniqueDevices = Convert.ToInt64(res);
    }

    // 8. NEW: Breakdown Stats (Language)
    var langStats = new List<object>();

    string langStatsSql = $@"
        SELECT COALESCE(NULLIF(uae.language, ''), 'unknown') as label, COUNT(1) as c 
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE {actionClauseSpecific} AND uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere}
        GROUP BY label ORDER BY c DESC;";
    await using (var cmd = new SqliteCommand(langStatsSql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (actionFilterValue is not null) cmd.Parameters.AddWithValue("$actionFilter", actionFilterValue);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);
        var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) langStats.Add(new { label = reader.GetString(0), count = reader.GetInt32(1) });
    }

    // 9. NEW: Paginated Detailed Logs
    int pageIndex = 0;
    int pageSize = 50;
    if (int.TryParse(context.Request.Query["page"], out var pIdx)) pageIndex = Math.Max(0, pIdx);
    if (int.TryParse(context.Request.Query["pageSize"], out var pSize)) pageSize = Math.Clamp(pSize, 10, 200);

    var recentLogs = new List<object>();
    int totalLogCount = 0;

    string logCountSql = $@"
        SELECT COUNT(1) FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        WHERE {actionClauseSpecific} AND uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere};";
    await using (var cmd = new SqliteCommand(logCountSql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (actionFilterValue is not null) cmd.Parameters.AddWithValue("$actionFilter", actionFilterValue);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);

        totalLogCount = Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    string logSql = $@"
        SELECT uae.id, uae.poi_id, t.name as poi_name, uae.action, uae.platform, uae.device_id, uae.browser_family, uae.os_family, uae.ip_address, uae.screen_info, uae.created_at
        FROM user_activity_events uae
        LEFT JOIN pois p ON p.id = uae.poi_id
        LEFT JOIN (
            SELECT poi_id, name FROM poi_translations WHERE lang_code = 'vi' GROUP BY poi_id
        ) t ON uae.poi_id = t.poi_id
        WHERE {actionClauseSpecific} AND uae.created_at >= $startUtc AND uae.created_at < $endUtc
          {platformFilter} {ownerWhere}
        ORDER BY uae.created_at DESC
        LIMIT $limit OFFSET $offset;";
    await using (var cmd = new SqliteCommand(logSql, conn))
    {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$limit", pageSize);
        cmd.Parameters.AddWithValue("$offset", pageIndex * pageSize);
        if (isOwner) cmd.Parameters.AddWithValue("$ownerId", actor.Id);
        if (actionFilterValue is not null) cmd.Parameters.AddWithValue("$actionFilter", actionFilterValue);
        if (!string.IsNullOrEmpty(platformFilter)) cmd.Parameters.AddWithValue("$platform", platform);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            recentLogs.Add(new {
                id = reader.GetInt64(0),
                poiId = reader.IsDBNull(1) ? null : (object)reader.GetInt64(1).ToString(),
                poiName = reader.IsDBNull(2) ? null : reader.GetString(2),
                action = reader.GetString(3),
                platform = reader.GetString(4),
                deviceId = reader.IsDBNull(5) ? null : reader.GetString(5),
                browser = reader.IsDBNull(6) ? null : reader.GetString(6),
                os = reader.IsDBNull(7) ? null : reader.GetString(7),
                ip = reader.IsDBNull(8) ? null : reader.GetString(8),
                screenInfo = reader.IsDBNull(9) ? null : reader.GetString(9),
                createdAt = reader.GetString(10)
            });
        }
    }

    // 10. Breakdown Stats (Browser & OS)
    var browserStats = new List<object>();
    var osStats = new List<object>();

    const string browserSql = "SELECT COALESCE(NULLIF(uae.browser_family, ''), 'unknown') as label, COUNT(1) as c FROM user_activity_events uae WHERE uae.created_at >= $startUtc AND uae.created_at < $endUtc GROUP BY label ORDER BY c DESC;";
    await using (var cmd = new SqliteCommand(browserSql, conn)) {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) browserStats.Add(new { label = reader.GetString(0), count = reader.GetInt32(1) });
    }

    const string osSql = "SELECT COALESCE(NULLIF(uae.os_family, ''), 'unknown') as label, COUNT(1) as c FROM user_activity_events uae WHERE uae.created_at >= $startUtc AND uae.created_at < $endUtc GROUP BY label ORDER BY c DESC;";
    await using (var cmd = new SqliteCommand(osSql, conn)) {
        cmd.Parameters.AddWithValue("$startUtc", startUtc.ToString("O"));
        cmd.Parameters.AddWithValue("$endUtc", endUtc.ToString("O"));
        var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) osStats.Add(new { label = reader.GetString(0), count = reader.GetInt32(1) });
    }

    // 11. TTS Queue Status
    var ttsQueue = new List<object>();
    const string ttsSql = "SELECT id, poi_id, text, status, created_at FROM audio_tts_queue WHERE status != 'done' ORDER BY created_at ASC LIMIT 20;";
    await using (var cmd = new SqliteCommand(ttsSql, conn)) {
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            ttsQueue.Add(new {
                id = reader.GetInt64(0).ToString(),
                poiId = reader.GetInt64(1).ToString(),
                text = reader.GetString(2),
                status = reader.GetString(3),
                createdAt = reader.GetString(4)
            });
        }
    }

    // 12. Advanced Online Visitors with "Pro" Proximity Logic
    var onlineVisitors = new List<object>();
    var allPois = new List<(long Id, string Name, double Lat, double Lon, double RadiusMeters)>();
    const string allPoisSql = "SELECT p.id, COALESCE(pt.name, 'POI ' || p.id), p.latitude, p.longitude, p.radius_meters FROM pois p LEFT JOIN poi_translations pt ON pt.poi_id = p.id AND pt.lang_code = 'vi' WHERE p.is_deleted = 0;";
    await using (var cmd = new SqliteCommand(allPoisSql, conn)) {
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) allPois.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));
    }

    string visitorsSql = "SELECT session_id, platform, latitude, longitude, last_ping_at, device_id, browser_family, os_family FROM active_sessions WHERE datetime(last_ping_at) >= datetime('now', '-45 seconds')";
    if (!string.IsNullOrEmpty(platform) && platform != "all") visitorsSql += " AND platform = $platform";
    await using (var cmd = new SqliteCommand(visitorsSql, conn)) {
        if (!string.IsNullOrEmpty(platform) && platform != "all") cmd.Parameters.AddWithValue("$platform", platform);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync()) {
            var sid = reader.GetString(0);
            var plt = reader.GetString(1);
            var lat = reader.IsDBNull(2) ? (double?)null : reader.GetDouble(2);
            var lon = reader.IsDBNull(3) ? (double?)null : reader.GetDouble(3);
            var deviceId = reader.IsDBNull(5) ? null : reader.GetString(5);
            var browserFamily = reader.IsDBNull(6) ? "N/A" : reader.GetString(6);
            var osFamily = reader.IsDBNull(7) ? "N/A" : reader.GetString(7);
            
            string proximityState = "Exploring";
            string proximityText = "Đang di chuyển";
            string atPoiName = "";
            var nearbyPois = new List<dynamic>();
            var proximityNearbyList = new List<object>();

            if (lat.HasValue && lon.HasValue) {
                foreach (var p in allPois) {
                    var dLat = (p.Lat - lat.Value) * Math.PI / 180;
                    var dLon = (p.Lon - lon.Value) * Math.PI / 180;
                    var a = Math.Sin(dLat/2) * Math.Sin(dLat/2) + Math.Cos(lat.Value*Math.PI/180) * Math.Cos(p.Lat*Math.PI/180) * Math.Sin(dLon/2) * Math.Sin(dLon/2);
                    var d = 6371000 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1-a));
                    if (d <= p.RadiusMeters + 25.0) nearbyPois.Add(new { p.Id, p.Name, Distance = d, p.RadiusMeters });
                }
                var sorted = nearbyPois.OrderBy(x => x.Distance).ToList();
                var buffer = 25.0;
                var insidePois = sorted.Where(p => p.Distance <= p.RadiusMeters).ToList();
                var bufferPois = sorted.Where(p => p.Distance <= p.RadiusMeters + buffer).ToList();

                if (insidePois.Count >= 2) {
                    proximityState = "Between";
                    proximityText = $"Giữa {insidePois[0].Name} và {insidePois[1].Name}";
                } else if (insidePois.Count == 1) {
                    proximityState = "At";
                    atPoiName = insidePois[0].Name;
                    proximityText = $"Tại {insidePois[0].Name}";
                } else if (bufferPois.Count >= 2) {
                    proximityState = "Between";
                    proximityText = $"Giữa {bufferPois[0].Name} và {bufferPois[1].Name}";
                } else if (bufferPois.Count == 1) {
                    proximityState = "Near";
                    proximityText = $"Tiến gần {bufferPois[0].Name} ({Math.Round(bufferPois[0].Distance)}m)";
                } else {
                    proximityState = "Exploring";
                    proximityText = "Đang di chuyển";
                }

                var displayNearby = (insidePois.Count > 0 ? insidePois : bufferPois);
                proximityNearbyList = displayNearby.Select(x => new { name = (string)x.Name, distance = Math.Round((double)x.Distance) }).ToList<object>();
            }

            onlineVisitors.Add(new { 
                sessionId = sid, 
                platform = plt, 
                deviceId,
                browser = browserFamily,
                os = osFamily,
                proximityState, 
                proximityText, 
                nearbyPois = proximityNearbyList,
                atPoiName,
                lat, 
                lon 
            });
        }
    }

    var allPoisSimple = allPois.Select(p => new { id = p.Id.ToString(), name = p.Name, lat = p.Lat, lon = p.Lon, radius = p.RadiusMeters }).ToList();

    return Results.Ok(new {
        onlineNow,
        periodAudioPlays,
        periodQrScans,
        periodViews,
        startDate = startDateStr,
        endDate = endDateStr,
        chartData,
        hourlyData,
        topPois,
        allPois = allPoisSimple, // Added for map rendering
        totalUniqueDevices,
        langStats,
        browserStats,
        osStats,
        recentLogs,
        totalLogCount,
        ttsQueue,
        onlineVisitors
    });
}).RequireAuthorization();
#endif

app.MapGet("/api/admin/reports/user-activities", async (
    HttpContext context,
    SupabaseRestClient supabase,
    string? platform,
    string? period,
    string? from,
    string? to,
    string? poiSort,
    string? fields,
    string? action) =>
{
    if (!TryGetAdminActor(context.User, out var actor)) return Results.Unauthorized();
    await TryEnsureAdbReverseAsync();

    var ct = context.RequestAborted;
    bool isOwner = IsOwner(actor);

    static string Esc(DateTimeOffset value) => Uri.EscapeDataString(value.ToString("O"));

    // Normalize optional single-action filter for Hourly + Ranking charts.
    // Accept both short aliases (online/audio/qr/view) and raw action names.
    string? actionFilterValue = null;
    if (!string.IsNullOrWhiteSpace(action))
    {
        var a = action.Trim().ToLowerInvariant();
        actionFilterValue = a switch
        {
            "online" or "ping" => "ping",
            "audio" or "play_audio" => "play_audio",
            "qr" or "scan_qr" => "scan_qr",
            "view" or "view_poi" => "view_poi",
            _ => null
        };
    }

    // 1) Online now (last 45s based on UTC)
    var onlineCutoffUtc = DateTimeOffset.UtcNow.AddSeconds(-45);
    var onlineQuery = $"/rest/v1/active_sessions?select=session_id,last_ping_at,platform&last_ping_at=gte.{Esc(onlineCutoffUtc)}";
    if (!string.IsNullOrEmpty(platform) && platform != "all")
    {
        onlineQuery += $"&platform=eq.{Uri.EscapeDataString(platform)}";
    }
    var onlineSessions = await supabase.GetListAsync<SupabaseActiveSessionRow>(onlineQuery, ct);
    var onlineSessionIds = onlineSessions
        .Select(x => (x.session_id ?? string.Empty).Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    long onlineNow;
    if (!isOwner)
    {
        onlineNow = onlineSessionIds.Count;
    }
    else
    {
        var ownerPoiIds = await supabase.GetListAsync<SupabasePoiIdRow>(
            $"/rest/v1/pois?select=id,owner_admin_id,is_deleted&owner_admin_id=eq.{actor.Id}",
            ct);
        var ownerPoiIdSet = ownerPoiIds
            .Where(x => x.id > 0 && !SupabasePoi.ParseBoolish(x.is_deleted, defaultValue: false))
            .Select(x => x.id)
            .ToHashSet();

        if (onlineSessionIds.Count == 0 || ownerPoiIdSet.Count == 0)
        {
            onlineNow = 0;
        }
        else
        {
            var ownerLookbackUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            var ownerActivityQuery = $"/rest/v1/user_activity_events?select=session_id,poi_id,created_at&created_at=gte.{Esc(ownerLookbackUtc)}&poi_id=in.({string.Join(",", ownerPoiIdSet)})";
            var ownerRecent = await supabase.GetListAsync<SupabaseOwnerSessionRow>(ownerActivityQuery, ct);
            onlineNow = ownerRecent
                .Select(x => (x.session_id ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Where(sid => onlineSessionIds.Contains(sid))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .LongCount();
        }
    }

    if (fields == "onlineNow")
    {
        return Results.Ok(new { onlineNow });
    }

    var vnTz = GetVnTimeZone();
    var nowUtc = DateTimeOffset.UtcNow;
    var nowVn = TimeZoneInfo.ConvertTime(nowUtc, vnTz);

    // 2) Filter logic for historical data (Index-friendly UTC boundaries)
    DateTimeOffset startUtc = DateTimeOffset.MinValue;
    DateTimeOffset endUtc = DateTimeOffset.MaxValue;
    string startDateStr = "";
    string endDateStr = nowVn.ToString("yyyy-MM-dd");

    if (period == "today")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn));
        startUtc = startVn.ToUniversalTime();
        endUtc = startUtc.AddDays(1);
        startDateStr = endDateStr;
    }
    else if (period == "week")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-6);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "month")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-29);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "year")
    {
        var startVn = new DateTimeOffset(nowVn.Year, nowVn.Month, nowVn.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn)).AddDays(-364);
        startUtc = startVn.ToUniversalTime();
        startDateStr = startVn.ToString("yyyy-MM-dd");
    }
    else if (period == "custom" && !string.IsNullOrEmpty(from) && !string.IsNullOrEmpty(to))
    {
        var f = ParseDateOnlyFilter(from);
        var t = ParseDateOnlyFilter(to);
        if (f.HasValue && t.HasValue)
        {
            var startVn = new DateTimeOffset(f.Value.Year, f.Value.Month, f.Value.Day, 0, 0, 0, vnTz.GetUtcOffset(nowVn));
            startUtc = startVn.ToUniversalTime();
            var endVn = new DateTimeOffset(t.Value.Year, t.Value.Month, t.Value.Day, 23, 59, 59, 999, vnTz.GetUtcOffset(nowVn));
            endUtc = endVn.ToUniversalTime();
            startDateStr = f.Value.ToString("yyyy-MM-dd");
            endDateStr = t.Value.ToString("yyyy-MM-dd");
        }
    }

    // When a specific action is selected we lock charts to that action only;
    // otherwise keep the default (exclude 'ping' heartbeats so interactions are clean).
    Func<SupabaseUserActivityReportRow, bool> actionPredicate = actionFilterValue is null
        ? (e => !string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase))
        : (e => string.Equals(e.action, actionFilterValue, StringComparison.OrdinalIgnoreCase));

    // Load POIs for naming + proximity map (Owners: only their POIs)
    var poisQuery = "/rest/v1/pois?select=id,latitude,longitude,radius_meters,is_deleted,owner_admin_id,poi_translations(lang_code,name)&order=id.asc";
    if (isOwner)
    {
        poisQuery += $"&owner_admin_id=eq.{actor.Id}";
    }
    var pois = await supabase.GetListAsync<SupabasePoiReportRow>(poisQuery, ct);
    var allPois = pois
        .Where(p => p.id > 0 && !SupabasePoi.ParseBoolish(p.is_deleted, defaultValue: false))
        .Select(p =>
        {
            var vi = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, "vi", StringComparison.OrdinalIgnoreCase));
            var name = (vi?.name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = $"POI {p.id.ToString(CultureInfo.InvariantCulture)}";
            }
            return (Id: p.id, Name: name, Lat: p.latitude, Lon: p.longitude, RadiusMeters: p.radius_meters);
        })
        .ToList();
    var poiNameById = allPois.ToDictionary(x => x.Id, x => x.Name);

    // 3) Fetch activity events in range (paged)
    static async Task<List<T>> FetchAllAsync<T>(SupabaseRestClient supabase, string baseQuery, CancellationToken ct)
    {
        const int batchSize = 1000;
        var all = new List<T>(capacity: batchSize);
        for (var offset = 0; offset <= 200_000; offset += batchSize)
        {
            var page = await supabase.GetListAsync<T>($"{baseQuery}&limit={batchSize}&offset={offset}", ct);
            if (page.Count == 0) break;
            all.AddRange(page);
            if (page.Count < batchSize) break;
        }

        return all;
    }

    var eventsQuery = "/rest/v1/user_activity_events?select=id,session_id,poi_id,action,platform,language,device_id,browser_family,os_family,ip_address,screen_info,created_at"
                      + $"&created_at=gte.{Esc(startUtc)}&created_at=lt.{Esc(endUtc)}"
                      + "&order=created_at.asc,id.asc";
    if (!string.IsNullOrEmpty(platform) && platform != "all")
    {
        eventsQuery += $"&platform=eq.{Uri.EscapeDataString(platform)}";
    }

    var events = await FetchAllAsync<SupabaseUserActivityReportRow>(supabase, eventsQuery, ct);

    // Fix created_at offset from Supabase if it was serialized as local time without Z
    foreach (var e in events)
    {
        if (e.created_at != default && e.created_at.Offset != TimeSpan.Zero)
        {
            e.created_at = new DateTimeOffset(e.created_at.DateTime, TimeSpan.Zero);
        }
    }

    if (isOwner)
    {
        var ownerPoiSet = allPois.Select(x => x.Id).ToHashSet();
        events = events
            .Where(e => e.poi_id.HasValue && ownerPoiSet.Contains(e.poi_id.Value))
            .ToList();
    }

    // 4) Summary stats for select period
    long periodAudioPlays = 0;
    long periodQrScans = 0;
    long periodViews = 0;
    foreach (var g in events.Where(e => !string.IsNullOrWhiteSpace(e.action)).GroupBy(e => e.action!, StringComparer.OrdinalIgnoreCase))
    {
        var count = g.LongCount();
        if (string.Equals(g.Key, "play_audio", StringComparison.OrdinalIgnoreCase)) periodAudioPlays = count;
        else if (string.Equals(g.Key, "scan_qr", StringComparison.OrdinalIgnoreCase)) periodQrScans = count;
        else if (string.Equals(g.Key, "view_poi", StringComparison.OrdinalIgnoreCase)) periodViews = count;
    }

    // 5) Chart Data (Grouped by Date in VN Time)
    var chartData = events
        .Where(e => e.created_at != default && !string.IsNullOrWhiteSpace(e.action))
        .GroupBy(e =>
        {
            var vn = TimeZoneInfo.ConvertTime(e.created_at, vnTz);
            return new
            {
                Dt = vn.ToString("yyyy-MM-dd"),
                Action = e.action!,
                Platform = (e.platform ?? string.Empty).Trim()
            };
        })
        .OrderBy(g => g.Key.Dt, StringComparer.Ordinal)
        .Select(g => new
        {
            date = g.Key.Dt,
            action = g.Key.Action,
            count = g.LongCount(),
            platform = string.IsNullOrWhiteSpace(g.Key.Platform) ? "unknown" : g.Key.Platform
        })
        .Cast<object>()
        .ToList();

    // 6) Hourly Activity (0-23 in VN Time)
    var hourlySource = events.Where(actionPredicate);
    var hourlyData = hourlySource
        .Where(e => e.created_at != default)
        .GroupBy(e => TimeZoneInfo.ConvertTime(e.created_at, vnTz).Hour)
        .OrderBy(g => g.Key)
        .Select(g =>
        {
            long c = actionFilterValue == "ping"
                ? g.Select(x => (x.session_id ?? string.Empty).Trim()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).LongCount()
                : g.LongCount();
            return new { hour = g.Key.ToString("D2"), count = c };
        })
        .Cast<object>()
        .ToList();

    // 7) Top POI Ranking
    var topPois = new List<object>();
    if (actionFilterValue != "ping")
    {
        var sortAsc = poiSort == "asc";
        if (actionFilterValue is null)
        {
            Func<SupabaseUserActivityReportRow, long> weight = e => (e.action ?? string.Empty).ToLowerInvariant() switch
            {
                "scan_qr" => 3,
                "play_audio" => 2,
                "view_poi" => 1,
                _ => 0
            };

            var ranked = events
                .Where(e => e.poi_id.HasValue && !string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.poi_id!.Value)
                .Select(g => new { PoiId = g.Key, Score = g.Sum(weight) })
                .OrderBy(x => sortAsc ? x.Score : -x.Score)
                .ThenBy(x => x.PoiId)
                .Take(50)
                .ToList();

            foreach (var r in ranked)
            {
                topPois.Add(new
                {
                    poiId = r.PoiId,
                    name = poiNameById.TryGetValue(r.PoiId, out var name) ? name : "Unknown POI",
                    count = r.Score
                });
            }
        }
        else
        {
            var ranked = events
                .Where(e => e.poi_id.HasValue && string.Equals(e.action, actionFilterValue, StringComparison.OrdinalIgnoreCase))
                .GroupBy(e => e.poi_id!.Value)
                .Select(g => new { PoiId = g.Key, Score = g.LongCount() })
                .OrderBy(x => sortAsc ? x.Score : -x.Score)
                .ThenBy(x => x.PoiId)
                .Take(50)
                .ToList();

            foreach (var r in ranked)
            {
                topPois.Add(new
                {
                    poiId = r.PoiId,
                    name = poiNameById.TryGetValue(r.PoiId, out var name) ? name : "Unknown POI",
                    count = r.Score
                });
            }
        }
    }

    // 8) Unique Devices Count
    long totalUniqueDevices = events
        .Select(e => (e.device_id ?? string.Empty).Trim())
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .LongCount();

    // 9) Breakdown Stats (Language) - Consolidated by session for meaningful demographics
    IEnumerable<SupabaseUserActivityReportRow> breakdownSource;
    if (actionFilterValue == "ping")
    {
        breakdownSource = events
            .Where(e => string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.session_id ?? string.Empty)
            .Select(g => g.First());
    }
    else if (actionFilterValue == null)
    {
        // Default: Count each session once, regardless of how many heartbeats or interactions it had.
        breakdownSource = events
            .GroupBy(e => e.session_id ?? string.Empty)
            .Select(g => g.First());
    }
    else
    {
        breakdownSource = events.Where(e => string.Equals(e.action, actionFilterValue, StringComparison.OrdinalIgnoreCase));
    }

    var langStats = breakdownSource
        .GroupBy(e => string.IsNullOrWhiteSpace(e.language) ? "unknown" : e.language!.Trim())
        .Select(g => new { label = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .Cast<object>()
        .ToList();

    // 10) Paginated Detailed Logs - Consolidate 'ping' entries to avoid cluttering.
    // For logs, we include pings but only one (the latest) per session.
    int pageIndex = 0;
    int pageSize = 50;
    if (int.TryParse(context.Request.Query["page"], out var pIdx)) pageIndex = Math.Max(0, pIdx);
    if (int.TryParse(context.Request.Query["pageSize"], out var pSize)) pageSize = Math.Clamp(pSize, 10, 200);

    IEnumerable<SupabaseUserActivityReportRow> filteredLogEvents;
    if (actionFilterValue == "ping")
    {
        // When strictly viewing pings, show one per session.
        filteredLogEvents = events
            .Where(e => string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.session_id ?? string.Empty)
            .Select(g => g.OrderByDescending(x => x.created_at).First());
    }
    else if (actionFilterValue == null)
    {
        // Default view: include interactions + one consolidated ping per session to show presence
        var interactions = events.Where(e => !string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase));
        var pings = events
            .Where(e => string.Equals(e.action, "ping", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.session_id ?? string.Empty)
            .Select(g => g.OrderByDescending(x => x.created_at).First());
        
        filteredLogEvents = interactions.Concat(pings);
    }
    else
    {
        // Other specific filters (audio, qr, view)
        filteredLogEvents = events.Where(e => string.Equals(e.action, actionFilterValue, StringComparison.OrdinalIgnoreCase));
    }

    var logSource = filteredLogEvents.OrderByDescending(e => e.created_at).ToList();
    var totalLogCount = logSource.Count;

    var recentLogs = logSource
        .Skip(pageIndex * pageSize)
        .Take(pageSize)
        .Select(e => new
        {
            id = e.id,
            poiId = e.poi_id.HasValue ? (object)e.poi_id.Value.ToString(CultureInfo.InvariantCulture) : null,
            poiName = e.poi_id.HasValue && poiNameById.TryGetValue(e.poi_id.Value, out var name) ? name : null,
            action = e.action ?? string.Empty,
            platform = e.platform ?? string.Empty,
            deviceId = e.device_id,
            browser = e.browser_family,
            os = e.os_family,
            ip = e.ip_address,
            screenInfo = e.screen_info,
            createdAt = e.created_at == default ? string.Empty : e.created_at.ToString("O")
        })
        .Cast<object>()
        .ToList();

    // 11) Breakdown Stats (Browser & OS) - Using the same consolidated source
    var browserStats = breakdownSource
        .GroupBy(e => string.IsNullOrWhiteSpace(e.browser_family) ? "unknown" : e.browser_family!.Trim())
        .Select(g => new { label = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .Cast<object>()
        .ToList();

    var osStats = breakdownSource
        .GroupBy(e => string.IsNullOrWhiteSpace(e.os_family) ? "unknown" : e.os_family!.Trim())
        .Select(g => new { label = g.Key, count = g.Count() })
        .OrderByDescending(x => x.count)
        .Cast<object>()
        .ToList();

    // 12) TTS Queue Status
    var ttsQueueRows = await supabase.GetListAsync<SupabaseTtsQueueReportRow>(
        "/rest/v1/audio_tts_queue?select=id,poi_id,text,status,created_at&status=neq.done&order=created_at.asc&limit=20",
        ct);
    var ttsQueue = ttsQueueRows
        .Select(r => new
        {
            id = (r.id ?? string.Empty).Trim(),
            poiId = r.poi_id.ToString(),
            text = r.text ?? string.Empty,
            status = r.status ?? string.Empty,
            createdAt = r.created_at ?? string.Empty
        })
        .Cast<object>()
        .ToList();

    // 13) Advanced Online Visitors with proximity logic
    var onlineVisitors = new List<object>();
    var visitorCutoffUtc = DateTimeOffset.UtcNow.AddSeconds(-45);
    var visitorsQuery = $"/rest/v1/active_sessions?select=session_id,platform,latitude,longitude,last_ping_at,device_id,browser_family,os_family&last_ping_at=gte.{Esc(visitorCutoffUtc)}";
    if (!string.IsNullOrEmpty(platform) && platform != "all")
    {
        visitorsQuery += $"&platform=eq.{Uri.EscapeDataString(platform)}";
    }
    var visitors = await supabase.GetListAsync<SupabaseActiveSessionDetailsRow>(visitorsQuery, ct);

    if (isOwner && visitors.Count > 0)
    {
        var ownerPoiSet = allPois.Select(x => x.Id).ToHashSet();
        if (ownerPoiSet.Count > 0)
        {
            var ownerLookbackUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
            var ownerActivityQuery = $"/rest/v1/user_activity_events?select=session_id,poi_id,created_at&created_at=gte.{Esc(ownerLookbackUtc)}&poi_id=in.({string.Join(",", ownerPoiSet)})";
            var ownerRecent = await supabase.GetListAsync<SupabaseOwnerSessionRow>(ownerActivityQuery, ct);
            var allowedSessions = ownerRecent
                .Select(x => (x.session_id ?? string.Empty).Trim())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            visitors = visitors.Where(v => !string.IsNullOrWhiteSpace(v.session_id) && allowedSessions.Contains(v.session_id.Trim())).ToList();
        }
        else
        {
            visitors = [];
        }
    }

    foreach (var v in visitors)
    {
        var sid = (v.session_id ?? string.Empty).Trim();
        var plt = (v.platform ?? string.Empty).Trim();
        double? lat = v.latitude;
        double? lon = v.longitude;
        var deviceId = v.device_id;
        var browserFamily = string.IsNullOrWhiteSpace(v.browser_family) ? "N/A" : v.browser_family!;
        var osFamily = string.IsNullOrWhiteSpace(v.os_family) ? "N/A" : v.os_family!;

        string proximityState = "Exploring";
        string proximityText = "Ä ang di chuyá»ƒn";
        string atPoiName = "";
        var nearbyPois = new List<dynamic>();
        var proximityNearbyList = new List<object>();

        if (lat.HasValue && lon.HasValue)
        {
            foreach (var p in allPois)
            {
                var dLat = (p.Lat - lat.Value) * Math.PI / 180;
                var dLon = (p.Lon - lon.Value) * Math.PI / 180;
                var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(lat.Value * Math.PI / 180) * Math.Cos(p.Lat * Math.PI / 180) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
                var d = 6371000 * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
                if (d <= p.RadiusMeters + 25.0) nearbyPois.Add(new { p.Id, p.Name, Distance = d, p.RadiusMeters });
            }
            var sorted = nearbyPois.OrderBy(x => x.Distance).ToList();
            var buffer = 25.0;
            var insidePois = sorted.Where(p => p.Distance <= p.RadiusMeters).ToList();
            var bufferPois = sorted.Where(p => p.Distance <= p.RadiusMeters + buffer).ToList();

            if (insidePois.Count >= 2)
            {
                proximityState = "Between";
                proximityText = $"Giá»¯a {insidePois[0].Name} vÃ  {insidePois[1].Name}";
            }
            else if (insidePois.Count == 1)
            {
                proximityState = "At";
                atPoiName = insidePois[0].Name;
                proximityText = $"Táº¡i {insidePois[0].Name}";
            }
            else if (bufferPois.Count >= 2)
            {
                proximityState = "Between";
                proximityText = $"Giá»¯a {bufferPois[0].Name} vÃ  {bufferPois[1].Name}";
            }
            else if (bufferPois.Count == 1)
            {
                proximityState = "Near";
                proximityText = $"Tiáº¿n gáº§n {bufferPois[0].Name} ({Math.Round(bufferPois[0].Distance)}m)";
            }
            else
            {
                proximityState = "Exploring";
                proximityText = "Ä ang di chuyá»ƒn";
            }

            var displayNearby = (insidePois.Count > 0 ? insidePois : bufferPois);
            proximityNearbyList = displayNearby.Select(x => new { name = (string)x.Name, distance = Math.Round((double)x.Distance) }).ToList<object>();
        }

        onlineVisitors.Add(new
        {
            sessionId = sid,
            platform = plt,
            deviceId,
            browser = browserFamily,
            os = osFamily,
            proximityState,
            proximityText,
            nearbyPois = proximityNearbyList,
            atPoiName,
            lat,
            lon
        });
    }

    var allPoisSimple = allPois.Select(p => new
    {
        id = p.Id.ToString(CultureInfo.InvariantCulture),
        name = p.Name,
        lat = p.Lat,
        lon = p.Lon,
        radius = p.RadiusMeters
    }).ToList();

    return Results.Ok(new
    {
        onlineNow,
        periodAudioPlays,
        periodQrScans,
        periodViews,
        startDate = startDateStr,
        endDate = endDateStr,
        chartData,
        hourlyData,
        topPois,
        allPois = allPoisSimple,
        totalUniqueDevices,
        langStats,
        browserStats,
        osStats,
        recentLogs,
        totalLogCount,
        ttsQueue,
        onlineVisitors
    });
}).RequireAuthorization();

app.MapPost("/api/pois", async (HttpContext context, PoiAdminUpsertRequest request, IDataService dataService) =>
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

    if (request.Price < 0)
    {
        return Results.BadRequest(new { error = "Gia mo khoa khong duoc am." });
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
                if (string.IsNullOrWhiteSpace(translationApiKey))
                {
                    // Fallback to source content if API key is missing
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

    var savedId = await dataService.UpsertPoiAdminAsync(
        poiId,
        request,
        actor,
        mapLink,
        normalizedTranslations,
        context.RequestAborted);
    if (savedId is null)
    {
        return Results.NotFound();
    }

    return Results.Ok(new { id = savedId.Value.ToString(CultureInfo.InvariantCulture) });
}).RequireAuthorization();

app.MapDelete("/api/pois/{id}", async (HttpContext context, string id, IDataService dataService) =>
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

    var result = await dataService.DeletePoiAsync(poiId, actor, context.RequestAborted);
    return result switch
    {
        DeletePoiResult.Deleted => Results.Ok(new { id }),
        DeletePoiResult.NotFound => Results.NotFound(),
        _ => Results.Problem("Delete failed.")
    };
}).RequireAuthorization();

app.MapPost("/api/pois/{id}/restore", async (HttpContext context, string id, IDataService dataService) =>
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

    var restored = await dataService.RestorePoiAsync(poiId, actor, context.RequestAborted);
    return restored ? Results.Ok(new { id, restored = true }) : Results.NotFound();
}).RequireAuthorization();

// Legacy endpoints for older mobile build.
app.MapGet("/api/shops", async (HttpContext context, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    var items = await dataService.GetPoisForMobileAsync(requestedLang);
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

app.MapGet("/api/shops/{id}", async (HttpContext context, string id, IDataService dataService) =>
{
    await TryEnsureAdbReverseAsync();
    if (string.IsNullOrWhiteSpace(id))
    {
        return Results.BadRequest(new { error = "Missing id." });
    }

    var requestedLang = NormalizeLanguageOrFallback(context.Request.Query["lang"].ToString(), supportedLanguageSet);
    if (!TryParsePoiId(id, out var poiId))
    {
        return Results.BadRequest(new { error = "Invalid id." });
    }

    var item = await dataService.GetPoiForMobileByIdAsync(poiId, requestedLang, context.RequestAborted);
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

app.MapPost("/api/shops/upsert", async (ShopUpsertJsonRequest request, IDataService dataService, HttpContext context) =>
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

    var mapLink = $"https://maps.google.com/?q={request.Latitude.ToString(CultureInfo.InvariantCulture)},{request.Longitude.ToString(CultureInfo.InvariantCulture)}";
    try
    {
        var savedId = await dataService.UpsertLegacyShopAsync(poiId, request, langCode, mapLink, context.RequestAborted);
        return Results.Ok(new { id = savedId.ToString(CultureInfo.InvariantCulture) });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/shops/{id}", async (string id, IDataService dataService, HttpContext context) =>
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

    var result = await dataService.DeletePoiAsync(poiId, new AdminActor(0, "system", "superadmin", "System"), context.RequestAborted);
    return result switch
    {
        DeletePoiResult.Deleted => Results.Ok(new { id }),
        DeletePoiResult.NotFound => Results.NotFound(),
        _ => Results.Problem("Delete failed.")
    };
});

app.Run();

static TimeZoneInfo GetVnTimeZone()
{
    try 
    {
        return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
    }
    catch (TimeZoneNotFoundException)
    {
        return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
    }
}

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

async Task<(string? BaseUrl, string? Error)> ResolvePublicBaseUrlForRequestAsync(HttpContext context)
{
    var error = default(string);

    // 1. Explicit Query Param (Force)
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

    // 2. ENV Priority (Render / Production)
    if (!string.IsNullOrWhiteSpace(configuredPublicBaseUrl))
    {
        return (configuredPublicBaseUrl, null);
    }

    // 3. Detect localhost request -> Fallback to LAN IP
    var host = context.Request.Host.Host;
    if (host == "localhost" || host == "127.0.0.1" || host == "::1")
    {
        var ip = GetLocalIpAddress();
        if (!string.IsNullOrEmpty(ip))
        {
            var port = context.Request.Host.Port;
            var scheme = context.Request.Scheme;
            var result = $"{scheme}://{ip}{(port.HasValue ? ":" + port.Value : "")}";
            Console.WriteLine($"[DEBUG] Resolved Local Public URL: {result}");
            return (result, null);
        }

        error = "Public URL đang là localhost và không thể tự động xác định IP LAN. Vui lòng cấu hình POI_PUBLIC_BASE_URL.";
        return (null, error);
    }

    // 4. Default Fallback (Production Domain from Host Header)
    var fallback = $"{context.Request.Scheme}://{context.Request.Host.ToUriComponent()}{context.Request.PathBase.ToUriComponent()}".TrimEnd('/');
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

static string NormalizeSupabaseBaseUrl(string raw)
{
    if (string.IsNullOrWhiteSpace(raw))
    {
        throw new InvalidOperationException("Supabase base URL is empty.");
    }

    if (!Uri.TryCreate(raw.Trim(), UriKind.Absolute, out var uri))
    {
        throw new InvalidOperationException($"Invalid Supabase URL: {raw}");
    }

    if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
        && !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
    {
        throw new InvalidOperationException($"Supabase URL must be http(s): {raw}");
    }

    var normalized = new UriBuilder(uri)
    {
        Path = string.Empty,
        Query = string.Empty,
        Fragment = string.Empty
    }.Uri.ToString().TrimEnd('/');

    if (string.IsNullOrWhiteSpace(normalized))
    {
        throw new InvalidOperationException($"Invalid Supabase URL after normalization: {raw}");
    }

    return normalized;
}

static string BuildQrScanUrl(string publicBaseUrl, long poiId)
{
    if (poiId <= 0)
    {
        return $"{publicBaseUrl.TrimEnd('/')}/qr/scan?code=master";
    }
    return $"{publicBaseUrl.TrimEnd('/')}/qr/scan?code={poiId}";
}

static string BuildPublicPoiUrl(string publicBaseUrl, long poiId, string? langCode)
{
    if (poiId <= 0)
    {
        return $"{publicBaseUrl.TrimEnd('/')}/list.html";
    }
    var langSuffix = string.IsNullOrWhiteSpace(langCode) ? "" : $"&lang={langCode}";
    return $"{publicBaseUrl.TrimEnd('/')}/poi.html?id={poiId}{langSuffix}";
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

static string? GetLocalIpAddress()
{
    try
    {
        var host = Dns.GetHostEntry(Dns.GetHostName());
        foreach (var ip in host.AddressList)
        {
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
            {
                return ip.ToString();
            }
        }
    }
    catch
    {
        try
        {
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0);
            socket.Connect("8.8.8.8", 65530);
            if (socket.LocalEndPoint is IPEndPoint endPoint)
            {
                return endPoint.Address.ToString();
            }
        }
        catch { }
    }
    return null;
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

#if false
static async Task AddColumnIfNotExists(SqliteConnection conn, string table, string column, string definition)
{
    await using var checkCmd = new SqliteCommand($"PRAGMA table_info({table});", conn);
    await using var reader = await checkCmd.ExecuteReaderAsync();
    bool exists = false;
    while (await reader.ReadAsync())
    {
        if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
        {
            exists = true;
            break;
        }
    }
    if (!exists)
    {
        await using var alterCmd = new SqliteCommand($"ALTER TABLE {table} ADD COLUMN {column} {definition};", conn);
        await alterCmd.ExecuteNonQueryAsync();
    }
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
            price REAL NOT NULL DEFAULT 0,
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

    try
    {
        await using var migrate = new SqliteCommand("ALTER TABLE pois ADD COLUMN price REAL NOT NULL DEFAULT 0;", connection);
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
        CREATE TABLE IF NOT EXISTS active_sessions (
            session_id TEXT PRIMARY KEY,
            last_ping_at TEXT NOT NULL,
            platform TEXT NOT NULL,
            device_id TEXT,
            browser_family TEXT,
            os_family TEXT,
            latitude REAL,
            longitude REAL
        );

        CREATE TABLE IF NOT EXISTS user_activity_events (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            poi_id INTEGER,
            session_id TEXT NOT NULL,
            device_id TEXT,
            platform TEXT NOT NULL,
            action TEXT NOT NULL,
            language TEXT,
            device_type TEXT,
            browser_family TEXT,
            os_family TEXT,
            ip_address TEXT,
            screen_info TEXT,
            is_real_scan INTEGER,
            duration INTEGER,
            created_at TEXT NOT NULL,
            latitude REAL,
            longitude REAL,
            FOREIGN KEY(poi_id) REFERENCES pois(id) ON DELETE CASCADE
        );

        CREATE TABLE IF NOT EXISTS audio_tts_queue (
            id TEXT PRIMARY KEY,
            poi_id TEXT,
            text TEXT,
            status TEXT NOT NULL DEFAULT 'waiting', -- waiting | processing | done | error
            created_at TEXT NOT NULL,
            updated_at TEXT
        );


        CREATE TABLE IF NOT EXISTS poi_audio_cache (
            poi_id INTEGER,
            lang_code TEXT,
            text_hash TEXT,
            audio_url TEXT,
            PRIMARY KEY(poi_id, lang_code, text_hash)
        );

        CREATE INDEX IF NOT EXISTS ix_user_activity_events_created_at ON user_activity_events(created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_session_id ON user_activity_events(session_id);
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_poi_id ON user_activity_events(poi_id);
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_action ON user_activity_events(action);
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_platform ON user_activity_events(platform);
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_action_created_at ON user_activity_events(action, created_at DESC);
        CREATE INDEX IF NOT EXISTS ix_active_sessions_ping ON active_sessions(last_ping_at);
        """, connection))
    {
        await createUsers.ExecuteNonQueryAsync();
    }

    // New Columns Migrations
    await AddColumnIfNotExists(connection, "active_sessions", "device_id", "TEXT");
    await AddColumnIfNotExists(connection, "active_sessions", "browser_family", "TEXT");
    await AddColumnIfNotExists(connection, "active_sessions", "os_family", "TEXT");
    await AddColumnIfNotExists(connection, "active_sessions", "latitude", "REAL");
    await AddColumnIfNotExists(connection, "active_sessions", "longitude", "REAL");
    await AddColumnIfNotExists(connection, "user_activity_events", "latitude", "REAL");
    await AddColumnIfNotExists(connection, "user_activity_events", "longitude", "REAL");
    await AddColumnIfNotExists(connection, "user_activity_events", "device_id", "TEXT");
    await AddColumnIfNotExists(connection, "user_activity_events", "browser_family", "TEXT");
    await AddColumnIfNotExists(connection, "user_activity_events", "os_family", "TEXT");
    await AddColumnIfNotExists(connection, "user_activity_events", "ip_address", "TEXT");
    await AddColumnIfNotExists(connection, "user_activity_events", "screen_info", "TEXT");

    // NEW Indices for migrations
    await using (var cmdIndices = new SqliteCommand("""
        CREATE INDEX IF NOT EXISTS ix_user_activity_events_device_id ON user_activity_events(device_id);
    """, connection))
    {
        await cmdIndices.ExecuteNonQueryAsync();
    }

    await CleanupOldLogsAsync(connection);

    await AddColumnIfNotExists(connection, "admin_accounts", "is_deleted", "INTEGER NOT NULL DEFAULT 0");
    await AddColumnIfNotExists(connection, "admin_accounts", "deleted_at", "TEXT");

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

    foreach (var table in new[] { "role_permissions", "user_roles", "permissions", "roles", "users", "poi_audio_play_events", "poi_images", "user_poi_access", "app_users" })
    {
        await using var dropCmd = new SqliteCommand($"DROP TABLE IF EXISTS {table};", connection);
        await dropCmd.ExecuteNonQueryAsync();
    }

    await RenameTableIfExists(connection, "audio_tts_queue", "audio_tts_queue_old");
}


static async Task<SqliteConnection> OpenConnectionAsync(string connectionString)
{
    var connection = new SqliteConnection(connectionString);
    await connection.OpenAsync();
    await using var pragma = new SqliteCommand("PRAGMA foreign_keys = ON;", connection);
    await pragma.ExecuteNonQueryAsync();
    return connection;
}

#endif

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

#if false
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
#endif

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







#if false
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




static async Task<List<FeaturedPoiDto>> GetFeaturedPoisForPublicAsync(string connectionString, string requestedLang, int limit)
{
    await using var connection = await OpenConnectionAsync(connectionString);

    // Popularity = total count of user interactions (scan_qr, play_audio, view_poi) in the last 7 days.
    // 'ping' heartbeat events are explicitly excluded. Falls back to manually set priority then ID.
    const string sql = """
        SELECT
            p.id,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            p.image_url,
            COALESCE(stats.score, 0) AS popularity_score
        FROM pois p
        LEFT JOIN (
            SELECT
                poi_id,
                COUNT(1) AS score
            FROM user_activity_events
            WHERE created_at >= $utcLookback
              AND action IN ('scan_qr', 'play_audio', 'view_poi')
            GROUP BY poi_id
        ) stats ON p.id = stats.poi_id
        LEFT JOIN poi_translations t_req ON p.id = t_req.poi_id AND t_req.lang_code = $lang_code
        LEFT JOIN poi_translations t_vi ON p.id = t_vi.poi_id AND t_vi.lang_code = 'vi'
        WHERE p.is_active = 1 AND COALESCE(p.is_deleted, 0) = 0
        GROUP BY p.id, COALESCE(NULLIF(t_req.name, ''), t_vi.name, ''), p.image_url, p.priority, stats.score
        ORDER BY popularity_score DESC, p.priority DESC, p.id ASC
        LIMIT $limit;
        """;

    var result = new List<FeaturedPoiDto>();
    await using var command = new SqliteCommand(sql, connection);
    var utcLookback = DateTimeOffset.UtcNow.AddDays(-7).ToString("O");
    command.Parameters.AddWithValue("$lang_code", requestedLang);
    command.Parameters.AddWithValue("$limit", limit);
    command.Parameters.AddWithValue("$utcLookback", utcLookback);
    await using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        result.Add(new FeaturedPoiDto
        {
            Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
            Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1),
            ImageUrl = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
            PopularityScore = reader.IsDBNull(3) ? 0 : reader.GetInt64(3)
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
            p.price,
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
            Price = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
            MapLink = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            ImageUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            AudioUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            IsActive = reader.GetInt32(9) != 0,
            NameVi = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
            IsDeleted = !reader.IsDBNull(11) && reader.GetInt32(11) != 0,
            DeletedAt = reader.IsDBNull(12) ? null : reader.GetString(12),
            DeleteStatus = reader.IsDBNull(13) ? "ACTIVE" : reader.GetString(13),
            OwnerAdminId = reader.IsDBNull(14) ? null : reader.GetInt64(14).ToString(CultureInfo.InvariantCulture),
            OwnerUsername = reader.IsDBNull(15) ? string.Empty : reader.GetString(15),
            OwnerFullName = reader.IsDBNull(16) ? string.Empty : reader.GetString(16),
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
            p.price,
            p.map_link,
            p.image_url,
            p.audio_url,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            COALESCE(NULLIF(t_req.description, ''), t_vi.description, '') AS description,
            COALESCE(NULLIF(t_req.tts_text, ''), NULLIF(t_req.description, ''), NULLIF(t_vi.tts_text, ''), t_vi.description, '') AS tts_text,
            COALESCE(NULLIF(t_req.audio_url, ''), NULLIF(t_vi.audio_url, ''), '') AS audio_lang,
            1 AS is_paid
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

    var coreAudioUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
    var translatedAudioUrl = reader.IsDBNull(12) ? string.Empty : reader.GetString(12);
    return new PoiMobileDto
    {
        Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
        LangCode = requestedLang,
        Latitude = reader.GetDouble(1),
        Longitude = reader.GetDouble(2),
        RadiusMeters = reader.GetDouble(3),
        Priority = reader.GetInt32(4),
        Price = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
        MapLink = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        ImageUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        AudioUrl = !string.IsNullOrWhiteSpace(translatedAudioUrl) ? translatedAudioUrl : coreAudioUrl,
        Name = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
        Description = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
        TtsText = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
        IsPaid = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
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
            p.price,
            p.map_link,
            p.image_url,
            p.audio_url,
            COALESCE(NULLIF(t_req.name, ''), t_vi.name, '') AS name,
            COALESCE(NULLIF(t_req.description, ''), t_vi.description, '') AS description,
            COALESCE(NULLIF(t_req.tts_text, ''), NULLIF(t_req.description, ''), NULLIF(t_vi.tts_text, ''), t_vi.description, '') AS tts_text,
            COALESCE(NULLIF(t_req.audio_url, ''), NULLIF(t_vi.audio_url, ''), '') AS audio_lang,
            1 AS is_paid
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

    var coreAudioUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8);
    var translatedAudioUrl = reader.IsDBNull(12) ? string.Empty : reader.GetString(12);
    return new PoiMobileDto
    {
        Id = reader.GetInt64(0).ToString(CultureInfo.InvariantCulture),
        LangCode = requestedLang,
        Latitude = reader.GetDouble(1),
        Longitude = reader.GetDouble(2),
        RadiusMeters = reader.GetDouble(3),
        Priority = reader.GetInt32(4),
        Price = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
        MapLink = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
        ImageUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
        AudioUrl = !string.IsNullOrWhiteSpace(translatedAudioUrl) ? translatedAudioUrl : coreAudioUrl,
        Name = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
        Description = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
        TtsText = reader.IsDBNull(11) ? string.Empty : reader.GetString(11),
        IsPaid = !reader.IsDBNull(13) && reader.GetInt32(13) != 0,
    };
}

static async Task<PoiAdminDto?> GetPoiAdminAsync(SqliteConnection connection, long id, AdminActor actor)
{
    const string coreSql = @"
        SELECT id, latitude, longitude, radius_meters, priority, price, map_link, image_url, audio_url, is_active, owner_admin_id
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
        var ownerAdminId = reader.IsDBNull(10) ? (long?)null : reader.GetInt64(10);
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
            Price = reader.IsDBNull(5) ? 0 : reader.GetDouble(5),
            MapLink = reader.IsDBNull(6) ? string.Empty : reader.GetString(6),
            ImageUrl = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
            AudioUrl = reader.IsDBNull(8) ? string.Empty : reader.GetString(8),
            IsActive = reader.GetInt32(9) != 0,
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
            INSERT INTO pois (latitude, longitude, radius_meters, priority, price, map_link, image_url, audio_url, is_active, owner_admin_id, is_deleted, deleted_at, delete_status)
            VALUES ($latitude, $longitude, $radius, $priority, $price, $map_link, $image_url, $audio_url, $is_active, $owner_admin_id, 0, NULL, 'ACTIVE');
            SELECT last_insert_rowid();
            ";

        await using var insert = new SqliteCommand(insertSql, connection);
        insert.Transaction = (SqliteTransaction)transaction;
        insert.Parameters.AddWithValue("$latitude", request.Latitude);
        insert.Parameters.AddWithValue("$longitude", request.Longitude);
        insert.Parameters.AddWithValue("$radius", request.RadiusMeters);
        insert.Parameters.AddWithValue("$priority", request.Priority);
        insert.Parameters.AddWithValue("$price", request.Price >= 0 ? request.Price : 0);
        insert.Parameters.AddWithValue("$map_link", request.MapLink);
        insert.Parameters.AddWithValue("$image_url", request.ImageUrl ?? string.Empty);
        insert.Parameters.AddWithValue("$audio_url", request.AudioUrl ?? string.Empty);
        insert.Parameters.AddWithValue("$is_active", request.IsActive ? 1 : 0);
        insert.Parameters.AddWithValue("$owner_admin_id", request.OwnerAdminId.HasValue ? request.OwnerAdminId.Value : DBNull.Value);
        var raw = await insert.ExecuteScalarAsync();
        return Convert.ToInt64(raw, CultureInfo.InvariantCulture);
    }

    const string upsertSql = @"
        INSERT INTO pois (id, latitude, longitude, radius_meters, priority, price, map_link, image_url, audio_url, is_active, owner_admin_id, is_deleted, deleted_at, delete_status)
        VALUES ($id, $latitude, $longitude, $radius, $priority, $price, $map_link, $image_url, $audio_url, $is_active, $owner_admin_id, 0, NULL, 'ACTIVE')
        ON CONFLICT(id) DO UPDATE SET
            latitude = excluded.latitude,
            longitude = excluded.longitude,
            radius_meters = excluded.radius_meters,
            priority = excluded.priority,
            price = excluded.price,
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
    command.Parameters.AddWithValue("$price", request.Price >= 0 ? request.Price : 0);
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

#endif
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


#if false
static async Task CleanupOldLogsAsync(SqliteConnection conn)
{
    try
    {
        // Retention: 90 days
        const string sql = "DELETE FROM user_activity_events WHERE datetime(created_at) < datetime('now', '-90 days');";
        await using var cmd = new SqliteCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }
    catch { }
}







static async Task RenameTableIfExists(SqliteConnection connection, string oldName, string newName)
{
    bool exists = false;
    await using (var cmd = new SqliteCommand("SELECT name FROM sqlite_master WHERE type='table' AND name=$name;", connection))
    {
        cmd.Parameters.AddWithValue("$name", oldName);
        var res = await cmd.ExecuteScalarAsync();
        exists = res != null;
    }

    if (exists)
    {
        // Simple check: if old table has 'user_id' (old schema), rename it
        bool hasUserId = false;
        await using (var cmd = new SqliteCommand($"PRAGMA table_info({oldName});", connection))
        {
            var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(1) == "user_id") hasUserId = true;
            }
        }

        if (hasUserId)
        {
            await using (var cmd = new SqliteCommand($"ALTER TABLE {oldName} RENAME TO {newName};", connection))
            {
                await cmd.ExecuteNonQueryAsync();
            }
        }
    }
}

#endif

public static class LocalHelpers
{
    public static (string Browser, string OS) ParseUserAgent(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent)) return ("Unknown", "Unknown");
        var ua = userAgent.ToLowerInvariant();
        string browser = "Unknown";
        if (ua.Contains("chrome") || ua.Contains("chromium")) browser = "Chrome";
        else if (ua.Contains("safari") && !ua.Contains("chrome")) browser = "Safari";
        else if (ua.Contains("firefox")) browser = "Firefox";
        else if (ua.Contains("edge")) browser = "Edge";

        string os = "Unknown";
        if (ua.Contains("windows")) os = "Windows";
        else if (ua.Contains("android")) os = "Android";
        else if (ua.Contains("iphone") || ua.Contains("ipad")) os = "iOS";
        else if (ua.Contains("mac os")) os = "macOS";
        else if (ua.Contains("linux")) os = "Linux";
        return (browser, os);
    }
}

public class TtsJob
{
    public required string Id { get; set; }
    public required string Text { get; set; }
}

sealed class SupabaseTtsQueueRow
{
    public string? id { get; set; }
    public string? text { get; set; }
    public string? status { get; set; }
}

public class TtsQueueWorker : BackgroundService
{
    private readonly SemaphoreSlim _semaphore = new(3);
    private static readonly IReadOnlyDictionary<string, string> PreferReturnRepresentation
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Prefer"] = "return=representation" };

    private readonly SupabaseRestClient _supabase;
    private readonly ILogger<TtsQueueWorker> _logger;

    public TtsQueueWorker(SupabaseRestClient supabase, ILogger<TtsQueueWorker> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }


    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("TTS Queue Worker started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var jobs = await ClaimWaitingJobsAsync(3, stoppingToken);
                foreach (var job in jobs)
                {
                    _ = ProcessJobAsync(job, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error polling TTS queue.");
            }

            await Task.Delay(2000, stoppingToken);
        }
    }

    private async Task<List<TtsJob>> ClaimWaitingJobsAsync(int limit, CancellationToken cancellationToken)
    {
        var jobs = new List<TtsJob>();
        var candidates = await _supabase.GetListAsync<SupabaseTtsQueueRow>(
            $"/rest/v1/audio_tts_queue?select=id,text,status&status=eq.waiting&order=created_at.asc&limit={Math.Clamp(limit * 3, 3, 30)}",
            cancellationToken);

        foreach (var c in candidates)
        {
            if (jobs.Count >= limit)
            {
                break;
            }

            var id = (c.id ?? string.Empty).Trim();
            var text = (c.text ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var claimPayload = new Dictionary<string, object?>
            {
                ["status"] = "processing",
                ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
            };

            // Claim is conditional on the row still being 'waiting'.
            var claimed = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseTtsQueueRow>>(
                $"/rest/v1/audio_tts_queue?id=eq.{Uri.EscapeDataString(id)}&status=eq.waiting",
                claimPayload,
                headers: PreferReturnRepresentation,
                cancellationToken: cancellationToken);
            if (claimed is null || claimed.Count == 0)
            {
                continue;
            }

            jobs.Add(new TtsJob { Id = id, Text = text });
        }

        return jobs;
    }

    private async Task ProcessJobAsync(TtsJob job, CancellationToken token)
    {
        await _semaphore.WaitAsync(token);
        _logger.LogInformation("Processing TTS Job {JobId}", job.Id);

        try
        {
            // Simulate TTS generation
            await Task.Delay(5000, token);

            await _supabase.PatchAsync(
                $"/rest/v1/audio_tts_queue?id=eq.{Uri.EscapeDataString(job.Id)}",
                new Dictionary<string, object?>
                {
                    ["status"] = "done",
                    ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
                },
                cancellationToken: token);
             
            _logger.LogInformation("TTS Job {JobId} completed.", job.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing TTS Job {JobId}", job.Id);
            try
            {
                await _supabase.PatchAsync(
                    $"/rest/v1/audio_tts_queue?id=eq.{Uri.EscapeDataString(job.Id)}",
                    new Dictionary<string, object?>
                    {
                        ["status"] = "error",
                        ["updated_at"] = DateTimeOffset.UtcNow.ToString("O")
                    },
                    cancellationToken: token);
            }
            catch { }
        }
        finally
        {
            _semaphore.Release();
        }
    }
}



enum DeletePoiResult
{
    Unknown = 0,
    NotFound = 1,
    Deleted = 2
}

public readonly record struct AdminActor(long Id, string Username, string Role, string FullName);

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

public sealed class PoiMobileDto
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
    public double Price { get; set; }
    public bool IsPaid { get; set; }
    public string MapLink { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public string AudioLang { get; set; } = string.Empty;
}

sealed class PoiAdminListItemDto
{
    public string Id { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; }
    public int Priority { get; set; }
    public double Price { get; set; }
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

sealed class FeaturedPoiDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public long PopularityScore { get; set; }
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
    public double Price { get; set; }
    public string MapLink { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string AudioUrl { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public string? OwnerAdminId { get; set; }
    public List<PoiTranslationDto> Translations { get; set; } = [];
}

public sealed class OwnerAccountDto
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
    public double Price { get; set; }
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
    public required double Price { get; init; }
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

sealed class TrackActivityRequest
{
    public string? Action { get; set; }
    public string? Platform { get; set; }
    public string? SessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? Language { get; set; }
    public string? PoiId { get; set; }
    public string? DeviceType { get; set; }
    public int? Duration { get; set; }
    public string? ScreenInfo { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

sealed class QrConfirmRequest
{
    public float ScreenWidth { get; set; }
    public bool IsTouch { get; set; }
    public string? Code { get; set; }
    public string? SessionId { get; set; }
    public string? DeviceId { get; set; }
    public string? ScreenInfo { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

sealed class TtsRequest
{
    public string? UserId { get; set; }
    public string? PoiId { get; set; }
    public string? Text { get; set; }
    public string? LangCode { get; set; }
}

sealed class UserActivityLogDto
{
    public long Id { get; set; }
    public string? PoiId { get; set; }
    public string? PoiName { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? DeviceId { get; set; }
    public string? Browser { get; set; }
    public string? OS { get; set; }
    public string? IP { get; set; }
    public string? ScreenInfo { get; set; }
    public string CreatedAt { get; set; } = string.Empty;
}

sealed class DashboardReportsResponse
{
    public long OnlineNow { get; set; }
    public long PeriodAudioPlays { get; set; }
    public long PeriodQrScans { get; set; }
    public long PeriodViews { get; set; }
    public string StartDate { get; set; } = string.Empty;
    public string EndDate { get; set; } = string.Empty;
    public List<ChartPointDto> ChartData { get; set; } = [];
    public List<HourlyPointDto> HourlyData { get; set; } = [];
    public List<PoiRankingDto> TopPois { get; set; } = [];
    
    // New analytics fields
    public long TotalUniqueDevices { get; set; }
    public List<StatBreakdownDto> LangStats { get; set; } = [];
    public List<StatBreakdownDto> BrowserStats { get; set; } = [];
    public List<StatBreakdownDto> OsStats { get; set; } = [];
    public List<UserActivityLogDto> RecentLogs { get; set; } = [];
    public int TotalLogCount { get; set; }
    
    public List<object> OnlineVisitors { get; set; } = [];
    public List<object> TtsQueue { get; set; } = [];
}

sealed class ChartPointDto { public string Date { get; set; } = ""; public string Action { get; set; } = ""; public int Count { get; set; } }
sealed class HourlyPointDto { public int Hour { get; set; } public int Count { get; set; } }
sealed class PoiRankingDto { public string PoiId { get; set; } = ""; public string Name { get; set; } = ""; public int Count { get; set; } }
sealed class StatBreakdownDto { public string Label { get; set; } = ""; public int Count { get; set; } }



interface IDataService
{
    Task EnsureBootstrapSuperAdminAsync(string username, string password, CancellationToken cancellationToken = default);
    Task<List<PoiMobileDto>> GetPoisForMobileAsync(string lang_code);
    Task<PoiMobileDto?> GetPoiForMobileByIdAsync(long poiId, string requestedLang, CancellationToken cancellationToken = default);
    Task<PoiMobileDto?> GetPoiForPublicByIdAsync(long poiId, string requestedLang, CancellationToken cancellationToken = default);
    Task<List<FeaturedPoiDto>> GetFeaturedPoisForPublicAsync(string requestedLang, int limit, CancellationToken cancellationToken = default);
    Task<List<PoiAdminListItemDto>> GetPoisForAdminListAsync(AdminActor actor, CancellationToken cancellationToken = default);
    Task<PoiAdminDto?> GetPoiAdminAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default);
    Task<long?> UpsertPoiAdminAsync(
        long? poiId,
        PoiAdminUpsertRequest request,
        AdminActor actor,
        string mapLink,
        IReadOnlyList<PoiTranslationDto> normalizedTranslations,
        CancellationToken cancellationToken = default);
    Task<DeletePoiResult> DeletePoiAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default);
    Task<bool> RestorePoiAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default);
    Task<AdminResult?> FindAdminForLoginAsync(string username, string password);
    Task<List<OwnerAccountDto>> GetOwnerAccountsAsync(bool includeDeleted = false);
    Task<long> CreateOwnerAccountAsync(string username, string password, string fullName, CancellationToken cancellationToken = default);
    Task<bool> UpdateOwnerAccountAsync(long ownerId, string? username, string? fullName, string? password, CancellationToken cancellationToken = default);
    Task<bool> DeleteOwnerAccountAsync(long ownerId, CancellationToken cancellationToken = default);
    Task<bool> RestoreOwnerAccountAsync(long ownerId, CancellationToken cancellationToken = default);
    Task<bool> AssignOwnerToPoiAsync(long poiId, long? ownerId, CancellationToken cancellationToken = default);
    Task<bool> RecordUserActivityAsync(
        string sessionId,
        string platform,
        string action,
        string? language,
        string? deviceType,
        long? poiId,
        int? isRealScan,
        int? duration,
        string? deviceId,
        string? userAgent,
        string? ipAddress,
        string? screenInfo,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default);
    Task EnqueueTtsJobAsync(string jobId, string poiId, string text, CancellationToken cancellationToken = default);
    Task<long> UpsertLegacyShopAsync(long? poiId, ShopUpsertJsonRequest request, string langCode, string mapLink, CancellationToken cancellationToken = default);
}

sealed class AdminResult
{
    public long Id { get; set; }
    public string Username { get; set; } = "";
    public string Role { get; set; } = "";
    public string FullName { get; set; } = "";
}

sealed class SupabaseDataService : IDataService
{
    private static readonly IReadOnlyDictionary<string, string> PreferReturnRepresentation
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Prefer"] = "return=representation" };

    private static readonly IReadOnlyDictionary<string, string> PreferUpsertReturnRepresentation
        = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Prefer"] = "resolution=merge-duplicates,return=representation" };

    private readonly SupabaseRestClient _supabase;
    private readonly ILogger<SupabaseDataService> _logger;

    public SupabaseDataService(SupabaseRestClient supabase, ILogger<SupabaseDataService> logger)
    {
        _supabase = supabase;
        _logger = logger;
    }

    public async Task EnsureBootstrapSuperAdminAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        var existing = await _supabase.GetListAsync<SupabaseAdminAccount>(
            "/rest/v1/admin_accounts?select=id&role=eq.superadmin&limit=1",
            cancellationToken);
        if (existing.Count > 0)
        {
            return;
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var payload = new Dictionary<string, object?>
        {
            ["username"] = username,
            ["password_hash"] = hash,
            ["role"] = "superadmin",
            ["full_name"] = "Super Admin",
            ["is_active"] = true,
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
        await _supabase.PostAsync("/rest/v1/admin_accounts", payload, headers: null, cancellationToken: cancellationToken);
    }

    public async Task<List<PoiMobileDto>> GetPoisForMobileAsync(string lang_code)
    {
        var pois = await _supabase.GetListAsync<SupabasePoi>(
            "/rest/v1/pois?select=id,latitude,longitude,radius_meters,priority,price,map_link,image_url,audio_url,is_active,is_deleted,poi_translations(lang_code,name,description,tts_text,audio_url)&order=priority.desc,id.asc");

        var result = new List<PoiMobileDto>();
        foreach (var p in pois)
        {
            if (!p.IsActive || p.IsDeleted)
            {
                continue;
            }

            var t_req = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, lang_code, StringComparison.OrdinalIgnoreCase));
            var t_vi = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, "vi", StringComparison.OrdinalIgnoreCase));
            result.Add(ToMobileDto(p, lang_code, t_req, t_vi));
        }
        return result;
    }

    public async Task<PoiMobileDto?> GetPoiForMobileByIdAsync(long poiId, string requestedLang, CancellationToken cancellationToken = default)
    {
        var pois = await _supabase.GetListAsync<SupabasePoi>(
            $"/rest/v1/pois?select=id,latitude,longitude,radius_meters,priority,price,map_link,image_url,audio_url,is_active,is_deleted,poi_translations(lang_code,name,description,tts_text,audio_url)&id=eq.{poiId}&limit=1",
            cancellationToken);
        var p = pois.FirstOrDefault();
        if (p is null || !p.IsActive || p.IsDeleted)
        {
            return null;
        }

        var t_req = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, requestedLang, StringComparison.OrdinalIgnoreCase));
        var t_vi = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, "vi", StringComparison.OrdinalIgnoreCase));
        return ToMobileDto(p, requestedLang, t_req, t_vi);
    }

    public async Task<PoiMobileDto?> GetPoiForPublicByIdAsync(long poiId, string requestedLang, CancellationToken cancellationToken = default)
        => await GetPoiForMobileByIdAsync(poiId, requestedLang, cancellationToken);

    public async Task<List<FeaturedPoiDto>> GetFeaturedPoisForPublicAsync(string requestedLang, int limit, CancellationToken cancellationToken = default)
    {
        var lookbackUtc = DateTimeOffset.UtcNow.AddDays(-7).ToString("O");
        var events = await _supabase.GetListAsync<SupabaseUserActivityEvent>(
            $"/rest/v1/user_activity_events?select=poi_id,action&created_at=gte.{Uri.EscapeDataString(lookbackUtc)}&action=in.(scan_qr,play_audio,view_poi)",
            cancellationToken);

        var scoreByPoi = events
            .Where(e => e.poi_id.HasValue)
            .GroupBy(e => e.poi_id!.Value)
            .ToDictionary(g => g.Key, g => g.LongCount());

        var pois = await _supabase.GetListAsync<SupabasePoi>(
            "/rest/v1/pois?select=id,priority,image_url,is_active,is_deleted,poi_translations(lang_code,name)&order=priority.desc,id.asc",
            cancellationToken);

        var featured = new List<FeaturedPoiDto>();
        foreach (var p in pois)
        {
            if (!p.IsActive || p.IsDeleted)
            {
                continue;
            }

            var t_req = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, requestedLang, StringComparison.OrdinalIgnoreCase));
            var t_vi = p.poi_translations.FirstOrDefault(t => string.Equals(t.lang_code, "vi", StringComparison.OrdinalIgnoreCase));
            var name = !string.IsNullOrWhiteSpace(t_req?.name) ? t_req!.name! : (t_vi?.name ?? string.Empty);
            scoreByPoi.TryGetValue(p.id, out var popularity);
            featured.Add(new FeaturedPoiDto
            {
                Id = p.id.ToString(CultureInfo.InvariantCulture),
                Name = name,
                ImageUrl = p.image_url ?? string.Empty,
                    PopularityScore = (int)Math.Min((long)int.MaxValue, popularity)
                });
            }

        return featured
            .OrderByDescending(x => x.PopularityScore)
            .ThenByDescending(x => pois.FirstOrDefault(p => p.id.ToString(CultureInfo.InvariantCulture) == x.Id)?.priority ?? 0)
            .ThenBy(x => long.TryParse(x.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var pid) ? pid : long.MaxValue)
            .Take(limit)
            .ToList();
    }

    public async Task<List<PoiAdminListItemDto>> GetPoisForAdminListAsync(AdminActor actor, CancellationToken cancellationToken = default)
    {
        var pois = await _supabase.GetListAsync<SupabasePoiAdminRow>(
            "/rest/v1/pois?select=id,latitude,longitude,radius_meters,priority,price,map_link,image_url,audio_url,is_active,is_deleted,deleted_at,delete_status,owner_admin_id&order=priority.desc,id.asc",
            cancellationToken);

        if (ActorIsOwner(actor))
        {
            pois = pois.Where(p => p.owner_admin_id.HasValue && p.owner_admin_id.Value == actor.Id).ToList();
        }

        var viTranslations = await _supabase.GetListAsync<SupabasePoiTranslationRow>(
            "/rest/v1/poi_translations?select=poi_id,lang_code,name&lang_code=eq.vi",
            cancellationToken);
        var nameViByPoi = viTranslations
            .GroupBy(t => t.poi_id)
            .ToDictionary(g => g.Key, g => (g.FirstOrDefault()?.name ?? string.Empty).Trim());

        var ownerIds = pois.Where(p => p.owner_admin_id.HasValue).Select(p => p.owner_admin_id!.Value).Distinct().ToList();
        var ownersById = new Dictionary<long, (string Username, string FullName)>();
        foreach (var chunk in ownerIds.Chunk(100))
        {
            var inClause = string.Join(',', chunk);
            var owners = await _supabase.GetListAsync<SupabaseAdminAccount>(
                $"/rest/v1/admin_accounts?select=id,username,full_name&id=in.({inClause})",
                cancellationToken);
            foreach (var o in owners)
            {
                ownersById[o.id] = (o.username ?? string.Empty, o.full_name ?? string.Empty);
            }
        }

        return pois.Select(p =>
        {
            ownersById.TryGetValue(p.owner_admin_id ?? -1, out var owner);
            nameViByPoi.TryGetValue(p.id, out var nameVi);
            var isDeleted = p.IsDeleted;
            var deleteStatus = string.IsNullOrWhiteSpace(p.delete_status)
                ? (isDeleted ? "DELETED" : "ACTIVE")
                : p.delete_status!;

            return new PoiAdminListItemDto
            {
                Id = p.id.ToString(CultureInfo.InvariantCulture),
                Latitude = p.latitude,
                Longitude = p.longitude,
                RadiusMeters = p.radius_meters,
                Priority = p.priority,
                Price = p.price,
                MapLink = p.map_link ?? string.Empty,
                ImageUrl = p.image_url ?? string.Empty,
                AudioUrl = p.audio_url ?? string.Empty,
                IsActive = p.IsActive,
                NameVi = nameVi ?? string.Empty,
                IsDeleted = isDeleted,
                DeletedAt = p.deleted_at,
                DeleteStatus = deleteStatus,
                OwnerAdminId = p.owner_admin_id?.ToString(CultureInfo.InvariantCulture),
                OwnerUsername = owner.Username ?? string.Empty,
                OwnerFullName = owner.FullName ?? string.Empty
            };
        }).ToList();
    }

    public async Task<PoiAdminDto?> GetPoiAdminAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default)
    {
        var rows = await _supabase.GetListAsync<SupabasePoiAdminRow>(
            $"/rest/v1/pois?select=id,latitude,longitude,radius_meters,priority,price,map_link,image_url,audio_url,is_active,owner_admin_id&id=eq.{poiId}&limit=1",
            cancellationToken);
        var row = rows.FirstOrDefault();
        if (row is null)
        {
            return null;
        }

        if (ActorIsOwner(actor) && row.owner_admin_id != actor.Id)
        {
            return null;
        }

        var translations = await _supabase.GetListAsync<SupabasePoiTranslationFullRow>(
            $"/rest/v1/poi_translations?select=lang_code,name,description,tts_text,audio_url&poi_id=eq.{poiId}&order=lang_code.asc",
            cancellationToken);

        return new PoiAdminDto
        {
            Id = row.id.ToString(CultureInfo.InvariantCulture),
            Latitude = row.latitude,
            Longitude = row.longitude,
            RadiusMeters = row.radius_meters,
            Priority = row.priority,
            Price = row.price,
            MapLink = row.map_link ?? string.Empty,
            ImageUrl = row.image_url ?? string.Empty,
            AudioUrl = row.audio_url ?? string.Empty,
            IsActive = row.IsActive,
            OwnerAdminId = row.owner_admin_id?.ToString(CultureInfo.InvariantCulture),
            Translations = translations.Select(t => new PoiTranslationDto
            {
                LangCode = t.lang_code ?? "vi",
                Name = t.name ?? string.Empty,
                Description = t.description ?? string.Empty,
                TtsText = t.tts_text ?? string.Empty,
                AudioUrl = t.audio_url ?? string.Empty
            }).ToList()
        };
    }

    public async Task<long?> UpsertPoiAdminAsync(
        long? poiId,
        PoiAdminUpsertRequest request,
        AdminActor actor,
        string mapLink,
        IReadOnlyList<PoiTranslationDto> normalizedTranslations,
        CancellationToken cancellationToken = default)
    {
        SupabasePoiAdminRow? existing = null;
        if (poiId is not null)
        {
            var rows = await _supabase.GetListAsync<SupabasePoiAdminRow>(
                $"/rest/v1/pois?select=id,owner_admin_id,is_deleted,is_active&id=eq.{poiId.Value}&limit=1",
                cancellationToken);
            existing = rows.FirstOrDefault();
            if (existing is null)
            {
                return null;
            }

            if (ActorIsOwner(actor) && existing.owner_admin_id != actor.Id)
            {
                return null;
            }
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var corePayload = new Dictionary<string, object?>
        {
            ["latitude"] = request.Latitude,
            ["longitude"] = request.Longitude,
            ["radius_meters"] = request.RadiusMeters,
            ["priority"] = request.Priority,
            ["price"] = request.Price >= 0 ? request.Price : 0,
            ["map_link"] = mapLink,
            ["image_url"] = (request.ImageUrl ?? string.Empty).Trim(),
            ["audio_url"] = (request.AudioUrl ?? string.Empty).Trim(),
            ["is_active"] = request.IsActive,
            ["updated_at"] = now,
            ["deleted_at"] = null,
            ["delete_status"] = "ACTIVE"
        };

        if (existing is not null)
        {
            corePayload["is_deleted"] = CoerceBoolStorageValue(existing.is_deleted, false);

            // Preserve ownership unless it's currently NULL and the owner is saving.
            if (existing.owner_admin_id is null && ActorIsOwner(actor))
            {
                corePayload["owner_admin_id"] = actor.Id;
            }
        }
        else
        {
            corePayload["is_deleted"] = false;
            corePayload["owner_admin_id"] = ActorIsOwner(actor) ? actor.Id : null;
        }

        long savedId;
        if (poiId is null)
        {
            var inserted = await _supabase.PostAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
                "/rest/v1/pois",
                corePayload,
                headers: PreferReturnRepresentation,
                cancellationToken: cancellationToken);
            savedId = inserted?.FirstOrDefault()?.id ?? 0;
            if (savedId <= 0)
            {
                throw new InvalidOperationException("Cannot create POI (missing returned id).");
            }
        }
        else
        {
            var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
                $"/rest/v1/pois?id=eq.{poiId.Value}",
                corePayload,
                headers: PreferReturnRepresentation,
                cancellationToken: cancellationToken);
            var id = updated?.FirstOrDefault()?.id ?? 0;
            if (id <= 0)
            {
                return null;
            }
            savedId = id;
        }

        if (normalizedTranslations.Count > 0)
        {
            var translationRows = normalizedTranslations.Select(t => new Dictionary<string, object?>
            {
                ["poi_id"] = savedId,
                ["lang_code"] = t.LangCode,
                ["name"] = t.Name ?? string.Empty,
                ["description"] = t.Description ?? string.Empty,
                ["tts_text"] = t.TtsText ?? string.Empty,
                ["audio_url"] = t.AudioUrl ?? string.Empty
            }).ToList();

            await _supabase.PostAsync(
                "/rest/v1/poi_translations?on_conflict=poi_id,lang_code",
                translationRows,
                headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Prefer"] = "resolution=merge-duplicates" },
                cancellationToken: cancellationToken);
        }

        return savedId;
    }

    public async Task<DeletePoiResult> DeletePoiAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default)
    {
        var rows = await _supabase.GetListAsync<SupabasePoiAdminRow>(
            $"/rest/v1/pois?select=id,owner_admin_id,is_deleted&id=eq.{poiId}&limit=1",
            cancellationToken);
        var existing = rows.FirstOrDefault();
        if (existing is null)
        {
            return DeletePoiResult.NotFound;
        }

        if (ActorIsOwner(actor) && existing.owner_admin_id != actor.Id)
        {
            return DeletePoiResult.NotFound;
        }

        if (existing.IsDeleted)
        {
            return DeletePoiResult.NotFound;
        }

        var payload = new Dictionary<string, object?>
        {
            ["is_deleted"] = CoerceBoolStorageValue(existing.is_deleted, true),
            ["deleted_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["delete_status"] = "DELETED"
        };

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/pois?id=eq.{poiId}",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return (updated is not null && updated.Count > 0) ? DeletePoiResult.Deleted : DeletePoiResult.NotFound;
    }

    public async Task<bool> RestorePoiAsync(long poiId, AdminActor actor, CancellationToken cancellationToken = default)
    {
        var rows = await _supabase.GetListAsync<SupabasePoiAdminRow>(
            $"/rest/v1/pois?select=id,is_deleted&id=eq.{poiId}&limit=1",
            cancellationToken);
        var existing = rows.FirstOrDefault();
        if (existing is null || !existing.IsDeleted)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["is_deleted"] = CoerceBoolStorageValue(existing.is_deleted, false),
            ["deleted_at"] = null,
            ["delete_status"] = "ACTIVE"
        };

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/pois?id=eq.{poiId}",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return updated is not null && updated.Count > 0;
    }

    public async Task<long> UpsertLegacyShopAsync(long? poiId, ShopUpsertJsonRequest request, string langCode, string mapLink, CancellationToken cancellationToken = default)
    {
        SupabasePoiAdminRow? existing = null;
        if (poiId is not null)
        {
            var rows = await _supabase.GetListAsync<SupabasePoiAdminRow>(
                $"/rest/v1/pois?select=id,image_url,audio_url,is_deleted&id=eq.{poiId.Value}&limit=1",
                cancellationToken);
            existing = rows.FirstOrDefault();
            if (existing is null)
            {
                poiId = null;
            }
        }

        var currentAudioUrl = existing?.audio_url ?? string.Empty;
        var currentImageUrl = existing?.image_url ?? string.Empty;
        var finalTtsText = request.TtsText?.Trim() ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentAudioUrl) && !string.IsNullOrWhiteSpace(finalTtsText))
        {
            throw new InvalidOperationException("POI dang co audio file. Hay xoa audio truoc khi nhap TTS.");
        }

        var now = DateTimeOffset.UtcNow.ToString("O");
        var corePayload = new Dictionary<string, object?>
        {
            ["latitude"] = request.Latitude,
            ["longitude"] = request.Longitude,
            ["radius_meters"] = request.RadiusMeters,
            ["priority"] = 0,
            ["price"] = 0,
            ["map_link"] = mapLink,
            ["image_url"] = currentImageUrl,
            ["audio_url"] = currentAudioUrl,
            ["is_active"] = true,
            ["updated_at"] = now,
            ["deleted_at"] = null,
            ["delete_status"] = "ACTIVE"
        };

        long savedId;
        if (poiId is null)
        {
            corePayload["is_deleted"] = false;
            corePayload["created_at"] = now;
            var inserted = await _supabase.PostAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
                "/rest/v1/pois",
                corePayload,
                headers: PreferReturnRepresentation,
                cancellationToken: cancellationToken);
            savedId = inserted?.FirstOrDefault()?.id ?? 0;
            if (savedId <= 0)
            {
                throw new InvalidOperationException("Cannot create POI (missing returned id).");
            }
        }
        else
        {
            if (existing is not null)
            {
                corePayload["is_deleted"] = CoerceBoolStorageValue(existing.is_deleted, false);
            }
            var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
                $"/rest/v1/pois?id=eq.{poiId.Value}",
                corePayload,
                headers: PreferReturnRepresentation,
                cancellationToken: cancellationToken);
            savedId = updated?.FirstOrDefault()?.id ?? 0;
            if (savedId <= 0)
            {
                throw new InvalidOperationException("Cannot update POI.");
            }
        }

        var translationPayload = new List<Dictionary<string, object?>>
        {
            new()
            {
                ["poi_id"] = savedId,
                ["lang_code"] = langCode,
                ["name"] = request.ShopName.Trim(),
                ["description"] = request.Description?.Trim() ?? string.Empty,
                ["tts_text"] = finalTtsText,
                ["audio_url"] = string.Empty
            }
        };
        await _supabase.PostAsync(
            "/rest/v1/poi_translations?on_conflict=poi_id,lang_code",
            translationPayload,
            headers: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Prefer"] = "resolution=merge-duplicates" },
            cancellationToken: cancellationToken);

        return savedId;
    }

    public async Task<AdminResult?> FindAdminForLoginAsync(string username, string password)
    {
        var url = $"/rest/v1/admin_accounts?select=id,username,password_hash,role,full_name,is_active,is_deleted&username=ilike.{Uri.EscapeDataString(username)}&limit=1";
        var accounts = await _supabase.GetListAsync<SupabaseAdminAccount>(url);
        var account = accounts.FirstOrDefault();
        if (account is null || !account.IsActive || account.IsDeleted)
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(account.password_hash) || !BCrypt.Net.BCrypt.Verify(password, account.password_hash))
        {
            return null;
        }

        return new AdminResult
        {
            Id = account.id,
            Username = account.username ?? string.Empty,
            Role = account.role ?? string.Empty,
            FullName = account.full_name ?? string.Empty
        };
    }

    public async Task<List<OwnerAccountDto>> GetOwnerAccountsAsync(bool includeDeleted = false)
    {
        var accounts = await _supabase.GetListAsync<SupabaseAdminAccount>(
            "/rest/v1/admin_accounts?select=id,username,full_name,is_active,is_deleted,deleted_at,delete_status&role=eq.owner&order=username.asc");

        var filtered = includeDeleted ? accounts : accounts.Where(a => !a.IsDeleted).ToList();
        return filtered.Select(a => new OwnerAccountDto
        {
            Id = a.id.ToString(CultureInfo.InvariantCulture),
            Username = a.username ?? string.Empty,
            FullName = a.full_name ?? string.Empty,
            IsDeleted = a.IsDeleted,
            DeletedAt = a.deleted_at,
            DeleteStatus = string.IsNullOrWhiteSpace(a.delete_status) ? (a.IsDeleted ? "DELETED" : "ACTIVE") : a.delete_status!
        }).ToList();
    }

    public async Task<long> CreateOwnerAccountAsync(string username, string password, string fullName, CancellationToken cancellationToken = default)
    {
        var exists = await _supabase.GetListAsync<SupabaseAdminAccount>(
            $"/rest/v1/admin_accounts?select=id&username=ilike.{Uri.EscapeDataString(username)}&limit=1",
            cancellationToken);
        if (exists.Count > 0)
        {
            throw new InvalidOperationException("Username da ton tai.");
        }

        var hash = BCrypt.Net.BCrypt.HashPassword(password);
        var payload = new Dictionary<string, object?>
        {
            ["username"] = username,
            ["password_hash"] = hash,
            ["role"] = "owner",
            ["full_name"] = fullName ?? string.Empty,
            ["is_active"] = true,
            ["is_deleted"] = false,
            ["delete_status"] = "ACTIVE",
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O")
        };

        var inserted = await _supabase.PostAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            "/rest/v1/admin_accounts",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        var id = inserted?.FirstOrDefault()?.id ?? 0;
        if (id <= 0)
        {
            throw new InvalidOperationException("Cannot create owner account (missing returned id).");
        }

        return id;
    }

    public async Task<bool> UpdateOwnerAccountAsync(long ownerId, string? username, string? fullName, string? password, CancellationToken cancellationToken = default)
    {
        var current = await _supabase.GetListAsync<SupabaseAdminAccount>(
            $"/rest/v1/admin_accounts?select=id,username,is_deleted,role&role=eq.owner&id=eq.{ownerId}&limit=1",
            cancellationToken);
        var existing = current.FirstOrDefault();
        if (existing is null || existing.IsDeleted)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(username))
        {
            var dup = await _supabase.GetListAsync<SupabaseAdminAccount>(
                $"/rest/v1/admin_accounts?select=id&username=ilike.{Uri.EscapeDataString(username)}&id=neq.{ownerId}&limit=1",
                cancellationToken);
            if (dup.Count > 0)
            {
                throw new InvalidOperationException("Username da ton tai.");
            }
        }

        var payload = new Dictionary<string, object?>();
        if (!string.IsNullOrWhiteSpace(username)) payload["username"] = username;
        if (fullName is not null) payload["full_name"] = fullName;
        if (!string.IsNullOrWhiteSpace(password)) payload["password_hash"] = BCrypt.Net.BCrypt.HashPassword(password);
        if (payload.Count == 0) return true;

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/admin_accounts?id=eq.{ownerId}&role=eq.owner",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return updated is not null && updated.Count > 0;
    }

    public async Task<bool> DeleteOwnerAccountAsync(long ownerId, CancellationToken cancellationToken = default)
    {
        // Unassign POIs before deleting owner.
        await _supabase.PatchAsync(
            $"/rest/v1/pois?owner_admin_id=eq.{ownerId}",
            new Dictionary<string, object?> { ["owner_admin_id"] = null },
            cancellationToken: cancellationToken);

        var existing = await _supabase.GetListAsync<SupabaseAdminAccount>(
            $"/rest/v1/admin_accounts?select=id,is_deleted&role=eq.owner&id=eq.{ownerId}&limit=1",
            cancellationToken);
        var current = existing.FirstOrDefault();
        if (current is null || current.IsDeleted)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["is_deleted"] = CoerceBoolStorageValue(current.is_deleted, true),
            ["is_active"] = CoerceBoolStorageValue(current.is_active, false),
            ["deleted_at"] = DateTimeOffset.UtcNow.ToString("O"),
            ["delete_status"] = "DELETED"
        };

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/admin_accounts?id=eq.{ownerId}&role=eq.owner",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return updated is not null && updated.Count > 0;
    }

    public async Task<bool> RestoreOwnerAccountAsync(long ownerId, CancellationToken cancellationToken = default)
    {
        var existing = await _supabase.GetListAsync<SupabaseAdminAccount>(
            $"/rest/v1/admin_accounts?select=id,is_deleted,is_active&role=eq.owner&id=eq.{ownerId}&limit=1",
            cancellationToken);
        var current = existing.FirstOrDefault();
        if (current is null || !current.IsDeleted)
        {
            return false;
        }

        var payload = new Dictionary<string, object?>
        {
            ["is_deleted"] = CoerceBoolStorageValue(current.is_deleted, false),
            ["is_active"] = CoerceBoolStorageValue(current.is_active, true),
            ["deleted_at"] = null,
            ["delete_status"] = "ACTIVE"
        };

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/admin_accounts?id=eq.{ownerId}&role=eq.owner",
            payload,
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return updated is not null && updated.Count > 0;
    }

    public async Task<bool> AssignOwnerToPoiAsync(long poiId, long? ownerId, CancellationToken cancellationToken = default)
    {
        if (ownerId is not null)
        {
            var owners = await _supabase.GetListAsync<SupabaseAdminAccount>(
                $"/rest/v1/admin_accounts?select=id,is_active,is_deleted&role=eq.owner&id=eq.{ownerId.Value}&limit=1",
                cancellationToken);
            var owner = owners.FirstOrDefault();
            if (owner is null || owner.IsDeleted || !owner.IsActive)
            {
                throw new InvalidOperationException("Owner khong ton tai hoac da bi khoa.");
            }
        }

        var updated = await _supabase.PatchAsync<Dictionary<string, object?>, List<SupabaseInsertId>>(
            $"/rest/v1/pois?id=eq.{poiId}",
            new Dictionary<string, object?> { ["owner_admin_id"] = ownerId },
            headers: PreferReturnRepresentation,
            cancellationToken: cancellationToken);
        return updated is not null && updated.Count > 0;
    }

    public async Task<bool> RecordUserActivityAsync(
        string sessionId,
        string platform,
        string action,
        string? language,
        string? deviceType,
        long? poiId,
        int? isRealScan,
        int? duration,
        string? deviceId,
        string? userAgent,
        string? ipAddress,
        string? screenInfo,
        double? latitude = null,
        double? longitude = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var (browser, os) = LocalHelpers.ParseUserAgent(userAgent);
            var payload = new
            {
                session_id = sessionId,
                platform = platform,
                action = action,
                language = language,
                device_type = deviceType,
                poi_id = poiId,
                is_real_scan = isRealScan,
                duration = duration,
                device_id = deviceId,
                browser_family = browser,
                os_family = os,
                ip_address = ipAddress,
                screen_info = screenInfo,
                latitude = latitude,
                longitude = longitude,
                created_at = DateTimeOffset.UtcNow.ToString("O")
            };

            await _supabase.PostAsync("/rest/v1/user_activity_events", payload, cancellationToken: cancellationToken);

            // Update online status (active_sessions) for any interactive action
            if (action == "ping" || action == "scan_qr" || action == "view_poi" || action == "play_audio")
            {
                var sessionPayload = new Dictionary<string, object?>
                {
                    ["session_id"] = sessionId,
                    ["platform"] = platform,
                    ["last_ping_at"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["device_id"] = deviceId,
                    ["browser_family"] = browser,
                    ["os_family"] = os,
                    ["latitude"] = latitude,
                    ["longitude"] = longitude
                };
                await _supabase.PostAsync(
                    "/rest/v1/active_sessions?on_conflict=session_id",
                    sessionPayload,
                    headers: PreferUpsertReturnRepresentation,
                    cancellationToken: cancellationToken);
            }
            else if (action == "offline")
            {
                await _supabase.DeleteAsync(
                    $"/rest/v1/active_sessions?session_id=eq.{Uri.EscapeDataString(sessionId)}",
                    cancellationToken: cancellationToken);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Supabase RecordUserActivityAsync failed.");
            return false;
        }
    }

    public async Task EnqueueTtsJobAsync(string jobId, string poiId, string text, CancellationToken cancellationToken = default)
    {
        var payload = new Dictionary<string, object?>
        {
            ["id"] = jobId,
            ["poi_id"] = poiId,
            ["text"] = text,
            ["status"] = "waiting",
            ["created_at"] = DateTimeOffset.UtcNow.ToString("O")
        };
        await _supabase.PostAsync("/rest/v1/audio_tts_queue", payload, cancellationToken: cancellationToken);
    }

    private static PoiMobileDto ToMobileDto(SupabasePoi p, string lang_code, SupabasePoiTranslation? t_req, SupabasePoiTranslation? t_vi)
    {
        var coreAudioUrl = (p.audio_url ?? string.Empty).Trim();
        var translatedAudioUrl = ((t_req?.audio_url ?? string.Empty).Trim());
        if (string.IsNullOrWhiteSpace(translatedAudioUrl))
        {
            translatedAudioUrl = ((t_vi?.audio_url ?? string.Empty).Trim());
        }

        return new PoiMobileDto
        {
            Id = p.id.ToString(CultureInfo.InvariantCulture),
            Latitude = p.latitude,
            Longitude = p.longitude,
            RadiusMeters = p.radius_meters,
            Priority = p.priority,
            Price = p.price,
            MapLink = (p.map_link ?? string.Empty).Trim(),
            ImageUrl = (p.image_url ?? string.Empty).Trim(),
            AudioUrl = coreAudioUrl,
            Name = !string.IsNullOrWhiteSpace(t_req?.name) ? t_req!.name!.Trim() : (t_vi?.name ?? string.Empty).Trim(),
            Description = !string.IsNullOrWhiteSpace(t_req?.description) ? t_req!.description!.Trim() : (t_vi?.description ?? string.Empty).Trim(),
            TtsText = !string.IsNullOrWhiteSpace(t_req?.tts_text) ? t_req!.tts_text!.Trim()
                       : (!string.IsNullOrWhiteSpace(t_req?.description) ? t_req!.description!.Trim()
                       : (!string.IsNullOrWhiteSpace(t_vi?.tts_text) ? t_vi!.tts_text!.Trim()
                       : (t_vi?.description ?? string.Empty).Trim())),
            AudioLang = translatedAudioUrl,
            LangCode = lang_code,
            IsPaid = true
        };
    }

    private static object CoerceBoolStorageValue(JsonElement kindProbe, bool desired)
        => kindProbe.ValueKind == JsonValueKind.Number ? (desired ? 1 : 0) : desired;

    private static bool ActorIsOwner(AdminActor actor)
        => string.Equals(actor.Role, "owner", StringComparison.OrdinalIgnoreCase);
}

public class SupabasePoi
{
    public long id { get; set; }
    public double latitude { get; set; }
    public double longitude { get; set; }
    public double radius_meters { get; set; }
    public int priority { get; set; }
    public double price { get; set; }
    public string? map_link { get; set; }
    public string? image_url { get; set; }
    public string? audio_url { get; set; }
    public JsonElement is_active { get; set; }
    public JsonElement is_deleted { get; set; }
    public List<SupabasePoiTranslation> poi_translations { get; set; } = [];

    public bool IsActive => ParseBoolish(is_active, defaultValue: true);
    public bool IsDeleted => ParseBoolish(is_deleted, defaultValue: false);

    public static bool ParseBoolish(JsonElement value, bool defaultValue)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => value.TryGetInt32(out var i) ? i != 0 : defaultValue,
            JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : defaultValue,
            _ => defaultValue
        };
    }
}

public class SupabasePoiTranslation
{
    public string lang_code { get; set; } = "";
    public string? name { get; set; }
    public string? description { get; set; }
    public string? tts_text { get; set; }
    public string? audio_url { get; set; }
}

public class SupabaseAdminAccount
{
    public long id { get; set; }
    public string? username { get; set; }
    public string? password_hash { get; set; }
    public string? role { get; set; }
    public string? full_name { get; set; }
    public JsonElement is_active { get; set; }
    public JsonElement is_deleted { get; set; }
    public string? deleted_at { get; set; }
    public string? delete_status { get; set; }

    public bool IsActive => SupabasePoi.ParseBoolish(is_active, defaultValue: true);
    public bool IsDeleted => SupabasePoi.ParseBoolish(is_deleted, defaultValue: false);
}

sealed class SupabaseInsertId { public long id { get; set; } }

sealed class SupabasePoiAdminRow
{
    public long id { get; set; }
    public double latitude { get; set; }
    public double longitude { get; set; }
    public double radius_meters { get; set; }
    public int priority { get; set; }
    public double price { get; set; }
    public string? map_link { get; set; }
    public string? image_url { get; set; }
    public string? audio_url { get; set; }
    public JsonElement is_active { get; set; }
    public JsonElement is_deleted { get; set; }
    public string? deleted_at { get; set; }
    public string? delete_status { get; set; }
    public long? owner_admin_id { get; set; }

    public bool IsActive => SupabasePoi.ParseBoolish(is_active, defaultValue: true);
    public bool IsDeleted => SupabasePoi.ParseBoolish(is_deleted, defaultValue: false);
}

sealed class SupabasePoiTranslationRow
{
    public long poi_id { get; set; }
    public string? lang_code { get; set; }
    public string? name { get; set; }
}

sealed class SupabasePoiTranslationFullRow
{
    public string? lang_code { get; set; }
    public string? name { get; set; }
    public string? description { get; set; }
    public string? tts_text { get; set; }
    public string? audio_url { get; set; }
}

sealed class SupabaseUserActivityEvent
{
    public long? poi_id { get; set; }
    public string? action { get; set; }
}

sealed class SupabaseActiveSessionRow
{
    public string? session_id { get; set; }
    public string? platform { get; set; }
    public string? last_ping_at { get; set; }
}

sealed class SupabaseActiveSessionDetailsRow
{
    public string? session_id { get; set; }
    public string? platform { get; set; }
    public double? latitude { get; set; }
    public double? longitude { get; set; }
    public string? device_id { get; set; }
    public string? browser_family { get; set; }
    public string? os_family { get; set; }
    public string? last_ping_at { get; set; }
}

sealed class SupabaseOwnerSessionRow
{
    public string? session_id { get; set; }
    public long? poi_id { get; set; }
    public DateTimeOffset created_at { get; set; }
}

sealed class SupabaseUserActivityReportRow
{
    public long id { get; set; }
    public string? session_id { get; set; }
    public long? poi_id { get; set; }
    public string? action { get; set; }
    public string? platform { get; set; }
    public string? language { get; set; }
    public string? device_id { get; set; }
    public string? browser_family { get; set; }
    public string? os_family { get; set; }
    public string? ip_address { get; set; }
    public string? screen_info { get; set; }
    public DateTimeOffset created_at { get; set; }
}

sealed class SupabasePoiReportRow
{
    public long id { get; set; }
    public double latitude { get; set; }
    public double longitude { get; set; }
    public double radius_meters { get; set; }
    public JsonElement is_deleted { get; set; }
    public long? owner_admin_id { get; set; }
    public List<SupabasePoiTranslationRow> poi_translations { get; set; } = [];
}

sealed class SupabasePoiIdRow
{
    public long id { get; set; }
    public long? owner_admin_id { get; set; }
    public JsonElement is_deleted { get; set; }
}

sealed class SupabaseTtsQueueReportRow
{
    public string? id { get; set; }
    public JsonElement poi_id { get; set; }
    public string? text { get; set; }
    public string? status { get; set; }
    public string? created_at { get; set; }
}

