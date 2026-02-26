using SQLite;

namespace FoodStreetMobile.Models;

[Table("pois")]
public sealed class PoiEntity
{
    [PrimaryKey]
    [Column("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [Column("latitude")]
    public double Latitude { get; set; }

    [Column("longitude")]
    public double Longitude { get; set; }

    [Column("radius_meters")]
    public double RadiusMeters { get; set; } = 40;

    [Column("priority")]
    public int Priority { get; set; }

    [Column("map_link")]
    public string MapLink { get; set; } = string.Empty;

    [Column("image_url")]
    public string ImageUrl { get; set; } = string.Empty;

    [Column("audio_url")]
    public string AudioUrl { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;
}
