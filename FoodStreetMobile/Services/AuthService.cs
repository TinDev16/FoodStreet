using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FoodStreetMobile.Models;
using Microsoft.Maui.Storage;

namespace FoodStreetMobile.Services;

/// <summary>
/// Đăng ký / đăng nhập / đăng xuất qua API server; lưu JWT và cache hồ sơ cục bộ (SQLite).
/// </summary>
public sealed class AuthService
{
    private readonly AppDatabase _database;
    private readonly PoiSyncService _poiSync;
    private readonly PoiViewHistoryService _history;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private long _currentUserId;

    public AuthService(AppDatabase database, PoiSyncService poiSync, PoiViewHistoryService history)
    {
        _database = database;
        _poiSync = poiSync;
        _history = history;
        _ = Task.Run(async () => {
            try {
                var profile = await LoadCachedProfileAsync();
                _currentUserId = profile?.ServerUserId ?? 0;
            } catch { }
        });
    }

    public long CurrentUserId => _currentUserId;

    public bool IsLoggedIn => !string.IsNullOrWhiteSpace(Preferences.Get(PoiSyncService.MobileJwtPreferenceKey, string.Empty));

    public string? JwtToken => Preferences.Get(PoiSyncService.MobileJwtPreferenceKey, string.Empty) is { Length: > 0 } t ? t : null;

    public static string NormalizeBaseUrl(string? raw)
    {
        var s = (raw ?? string.Empty).Trim().TrimEnd('/');
        return string.IsNullOrEmpty(s) ? string.Empty : s;
    }

    /// <summary>
    /// URL API đã lưu sau đăng nhập (ưu tiên cấu hình sync, sau đó base thành công gần nhất).
    /// </summary>
    public string? GetApiBaseUrl()
    {
        var configured = _poiSync.GetConfiguredBaseUrls();
        foreach (var item in configured.Split(
                     new[] { ',', ';', ' ', '\t', '\r', '\n' },
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var n = NormalizeBaseUrl(item);
            if (!string.IsNullOrEmpty(n))
            {
                return n;
            }
        }

        var last = _poiSync.LastSuccessfulBaseUrl;
        return string.IsNullOrWhiteSpace(last) ? null : NormalizeBaseUrl(last);
    }

    public async Task<(bool Ok, string Message)> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string? confirmPassword,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            return (false, "Mật khẩu mới cần ít nhất 6 ký tự.");
        }

        if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
        {
            return (false, "Mật khẩu mới và xác nhận không khớp.");
        }

        var root = GetApiBaseUrl();
        if (string.IsNullOrEmpty(root))
        {
            return (false, "Chưa có địa chỉ máy chủ. Hãy đăng nhập lại.");
        }

        var token = JwtToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Phiên đăng nhập không hợp lệ.");
        }

        try
        {
            var url = $"{root}/api/mobile/auth/change-password";
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { currentPassword, newPassword });
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (false, "Phiên đăng nhập hết hạn.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await TryReadErrorAsync(response, cancellationToken);
                return (false, err ?? $"Đổi mật khẩu thất bại ({(int)response.StatusCode}).");
            }

            return (true, "Đã đổi mật khẩu.");
        }
        catch (Exception ex)
        {
            return (false, FormatAuthNetworkError(ex));
        }
    }

    public async Task<(bool Ok, string Message)> TryRefreshProfileFromServerAsync(CancellationToken cancellationToken = default)
    {
        var root = GetApiBaseUrl();
        if (string.IsNullOrEmpty(root))
        {
            return (false, "Chưa có địa chỉ máy chủ.");
        }

        var token = JwtToken;
        if (string.IsNullOrWhiteSpace(token))
        {
            return (false, "Chưa đăng nhập.");
        }

        try
        {
            var url = $"{root}/api/mobile/auth/me";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                return (false, "Phiên đăng nhập hết hạn.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Không tải được hồ sơ ({(int)response.StatusCode}).");
            }

            var me = await response.Content.ReadFromJsonAsync<MobileMeDto>(cancellationToken: cancellationToken);
            if (me is null)
            {
                return (false, "Phản hồi không hợp lệ.");
            }

            await ApplyProfilePatchFromMeAsync(me);
            return (true, "Đã cập nhật thông tin.");
        }
        catch (Exception ex)
        {
            return (false, FormatAuthNetworkError(ex));
        }
    }

    private async Task ApplyProfilePatchFromMeAsync(MobileMeDto me)
    {
        var conn = await _database.GetConnectionAsync();
        var existing = await conn.FindAsync<UserProfileEntity>(1);
        var row = existing ?? new UserProfileEntity { Id = 1 };
        row.ServerUserId = me.Id;
        row.Username = me.Username ?? string.Empty;
        row.FullName = me.FullName ?? string.Empty;
        row.Phone = me.Phone ?? string.Empty;
        row.Token = JwtToken ?? row.Token;
        await conn.InsertOrReplaceAsync(row);
    }

    public async Task<(bool Ok, string Message)> RegisterAsync(
        string baseUrl,
        string username,
        string password,
        string fullName,
        string phone,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrEmpty(root))
        {
            return (false, "Nhập địa chỉ máy chủ (ví dụ http://10.0.2.2:5187).");
        }

        try
        {
            var url = $"{root}/api/mobile/auth/register";
            using var response = await _http.PostAsJsonAsync(url, new MobileRegisterDto
            {
                Username = username,
                Password = password,
                FullName = fullName,
                Phone = phone
            }, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var err = await TryReadErrorAsync(response, cancellationToken);
                return (false, err ?? $"Đăng ký thất bại ({(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<MobileAuthResponseDto>(cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.Token))
            {
                return (false, "Phản hồi đăng ký không hợp lệ.");
            }

            // Không lưu JWT sau đăng ký — người dùng đăng nhập ở màn hình đăng nhập (tránh lỗi Shell/phiên).
            return (true, "Đăng ký thành công.");
        }
        catch (Exception ex)
        {
            return (false, FormatAuthNetworkError(ex));
        }
    }

    public async Task<(bool Ok, string Message)> LoginAsync(
        string baseUrl,
        string usernameOrPhone,
        string password,
        CancellationToken cancellationToken = default)
    {
        var root = NormalizeBaseUrl(baseUrl);
        if (string.IsNullOrEmpty(root))
        {
            return (false, "Nhập địa chỉ máy chủ (ví dụ http://10.0.2.2:5187).");
        }

        try
        {
            var url = $"{root}/api/mobile/auth/login";
            using var response = await _http.PostAsJsonAsync(url, new MobileLoginDto
            {
                UsernameOrPhone = usernameOrPhone,
                Password = password
            }, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                return (false, "Sai tên đăng nhập hoặc mật khẩu.");
            }

            if (!response.IsSuccessStatusCode)
            {
                var err = await TryReadErrorAsync(response, cancellationToken);
                return (false, err ?? $"Đăng nhập thất bại ({(int)response.StatusCode}).");
            }

            var body = await response.Content.ReadFromJsonAsync<MobileAuthResponseDto>(cancellationToken);
            if (body is null || string.IsNullOrWhiteSpace(body.Token))
            {
                return (false, "Phản hồi đăng nhập không hợp lệ.");
            }

            await ApplySuccessAsync(root, body);
            return (true, "Đăng nhập thành công.");
        }
        catch (Exception ex)
        {
            return (false, FormatAuthNetworkError(ex));
        }
    }

    public void Logout()
    {
        Preferences.Set(PoiSyncService.MobileJwtPreferenceKey, string.Empty);
        _poiSync.SetMobileJwtToken(null);
        _ = Task.Run(async () =>
        {
            try
            {
                _currentUserId = 0;
                var conn = await _database.GetConnectionAsync();
                await conn.DeleteAsync<UserProfileEntity>(1);
            }
            catch
            {
                // ignore
            }
        });
    }

    public async Task<UserProfileEntity?> LoadCachedProfileAsync(CancellationToken cancellationToken = default)
    {
        var conn = await _database.GetConnectionAsync();
        return await conn.FindAsync<UserProfileEntity>(1);
    }

    private async Task ApplySuccessAsync(string baseUrl, MobileAuthResponseDto body)
    {
        _currentUserId = body.UserId;
        Preferences.Set(PoiSyncService.MobileJwtPreferenceKey, body.Token);
        _poiSync.SetConfiguredBaseUrls(baseUrl);
        _poiSync.SetMobileJwtToken(body.Token);

        var conn = await _database.GetConnectionAsync();
        await conn.InsertOrReplaceAsync(new UserProfileEntity
        {
            Id = 1,
            ServerUserId = body.UserId,
            Username = body.Username ?? string.Empty,
            FullName = body.FullName ?? string.Empty,
            Phone = body.Phone ?? string.Empty,
            Token = body.Token ?? string.Empty
        });
    }

    private static async Task<string?> TryReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (doc.RootElement.TryGetProperty("error", out var err))
            {
                return err.GetString();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    /// <summary>
    /// Thông báo thân thiện thay cho chuỗi tiếng Anh của HttpClient (timeout, không kết nối được).
    /// </summary>
    private static string FormatAuthNetworkError(Exception ex)
    {
        if (IsLikelyTimeoutOrNetworkCancel(ex))
        {
            return "Không nhận được phản hồi từ máy chủ (hết thời gian hoặc không kết nối được). Hãy chạy FoodStreetPoiAdmin, đúng cổng (ví dụ :5187). Trên Android dùng http://10.0.2.2:5187 thay cho localhost; máy thật dùng IP LAN của PC.";
        }

        if (ex is HttpRequestException)
        {
            return "Không kết nối được tới máy chủ. Kiểm tra Wi-Fi, tường lửa và địa chỉ API.";
        }

        return ex.Message;
    }

    private static bool IsLikelyTimeoutOrNetworkCancel(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is TaskCanceledException or OperationCanceledException)
            {
                return true;
            }

            if (e is HttpRequestException http
                && http.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (ex.Message.Contains("Timeout", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("HttpClient", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private sealed class MobileRegisterDto
    {
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? FullName { get; set; }
        public string? Phone { get; set; }
    }

    private sealed class MobileLoginDto
    {
        public string? UsernameOrPhone { get; set; }
        public string? Password { get; set; }
    }

    private sealed class MobileAuthResponseDto
    {
        [JsonPropertyName("token")]
        public string? Token { get; set; }

        [JsonPropertyName("userId")]
        public long UserId { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }

    private sealed class MobileMeDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("username")]
        public string? Username { get; set; }

        [JsonPropertyName("fullName")]
        public string? FullName { get; set; }

        [JsonPropertyName("phone")]
        public string? Phone { get; set; }
    }

}
