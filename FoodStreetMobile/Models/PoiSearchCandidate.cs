namespace FoodStreetMobile.Models;

public sealed class PoiSearchCandidate
{
    public required string PoiId { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public string? ImageUrl { get; init; }
}
