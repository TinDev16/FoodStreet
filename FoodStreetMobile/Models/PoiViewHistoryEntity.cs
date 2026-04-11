using SQLite;

namespace FoodStreetMobile.Models;

[Table("poi_view_history")]
public sealed class PoiViewHistoryEntity
{
    [PrimaryKey]
    [AutoIncrement]
    [Column("id")]
    public int Id { get; set; }

    [Indexed]
    [Column("poi_id")]
    public string PoiId { get; set; } = string.Empty;

    [Column("server_user_id")]
    public long ServerUserId { get; set; }

    [Column("poi_name")]
    public string PoiName { get; set; } = string.Empty;

    [Column("poi_image_url")]
    public string? PoiImageUrl { get; set; }

    [Column("viewed_at_utc_ticks")]
    public long ViewedAtUtcTicks { get; set; }
}

