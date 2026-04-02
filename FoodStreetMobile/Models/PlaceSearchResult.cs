namespace FoodStreetMobile.Models;

public sealed class SearchPlaceResult
{
    public required string Name { get; init; }
    public required string Address { get; init; }
    public double Latitude { get; init; }
    public double Longitude { get; init; }
    public double Importance { get; init; }
    public string? ImageUrl { get; init; }
    public string? PlaceId { get; init; }
    public string? PoiId { get; init; }
    public double? DistanceKm { get; set; }
    public string DistanceText
    {
        get
        {
            if (!DistanceKm.HasValue || double.IsNaN(DistanceKm.Value) || DistanceKm.Value < 0)
            {
                return "--";
            }

            if (DistanceKm.Value < 1)
            {
                var meters = Math.Round(DistanceKm.Value * 1000d);
                return $"{meters:0} m";
            }

            return $"{DistanceKm.Value:0.0} km";
        }
    }
    public bool HasCoordinates => Latitude is >= -90 and <= 90 && Longitude is >= -180 and <= 180;
}

