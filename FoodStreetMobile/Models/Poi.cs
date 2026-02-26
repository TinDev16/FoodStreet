namespace FoodStreetMobile.Models;

public sealed class Poi
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double RadiusMeters { get; init; } = 40;
    public int Priority { get; init; } = 0;
    public string Narration { get; init; } = string.Empty;
    public string ImageUrl { get; init; } = string.Empty;
    public string MapLink { get; init; } = string.Empty;
    public string AudioUrl { get; init; } = string.Empty;
    public string Language { get; init; } = "vi";
}
