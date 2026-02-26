using SQLite;

namespace FoodStreetMobile.Models;

[Table("app_settings")]
public sealed class AppSettingEntity
{
    [PrimaryKey]
    [Column("key")]
    public string Key { get; set; } = string.Empty;

    [Column("value")]
    public string Value { get; set; } = string.Empty;
}
