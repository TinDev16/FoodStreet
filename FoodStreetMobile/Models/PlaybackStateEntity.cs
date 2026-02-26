using SQLite;

namespace FoodStreetMobile.Models;

[Table("playback_states")]
public sealed class PlaybackStateEntity
{
    [PrimaryKey]
    [Column("poi_id")]
    public string PoiId { get; set; } = string.Empty;

    [Column("last_played_utc")]
    public long LastPlayedUtc { get; set; }

    [Column("last_language")]
    public string LastLanguage { get; set; } = "vi";

    [Column("play_count")]
    public int PlayCount { get; set; }
}
