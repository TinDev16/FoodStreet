using CommunityToolkit.Maui.Alerts;
using CommunityToolkit.Maui.Core;
using Microsoft.Maui.ApplicationModel;

namespace FoodStreetMobile.Services;

/// <summary>
/// Thông báo nổi (toast) ở cuối màn hình — dùng cho đăng nhập/đăng ký và lỗi mạng.
/// </summary>
public sealed class ToastNotificationService
{
    public Task ShowAsync(string message, bool isError = false)
    {
        return MainThread.InvokeOnMainThreadAsync(async () =>
        {
            var duration = isError ? ToastDuration.Long : ToastDuration.Short;
            var toast = Toast.Make(message, duration, 14);
            await toast.Show();
        });
    }
}
