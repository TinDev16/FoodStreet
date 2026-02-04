namespace FoodStreetMobile.Models;

public sealed class Poi
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string Name { get; init; } = string.Empty;
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double RadiusMeters { get; init; } = 40;
    public int Priority { get; init; } = 0;
    public string Narration { get; init; } = string.Empty;
}
