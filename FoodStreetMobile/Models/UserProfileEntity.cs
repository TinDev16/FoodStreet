using SQLite;

namespace FoodStreetMobile.Models;

/// <summary>
/// Bản ghi cache tài khoản đăng nhập trên thiết bị (một hàng, Id = 1).
/// </summary>
[Table("user_profile")]
public sealed class UserProfileEntity
{
    [PrimaryKey]
    public int Id { get; set; } = 1;

    public long ServerUserId { get; set; }

    [MaxLength(128)]
    public string Username { get; set; } = string.Empty;

    [MaxLength(256)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string Phone { get; set; } = string.Empty;

    [MaxLength(4096)]
    public string Token { get; set; } = string.Empty;
}
