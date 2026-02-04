using Microsoft.Maui.Devices.Sensors;

namespace FoodStreetMobile.Services;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6371000;

    public static double DistanceMeters(Location a, Location b)
    {
        var dLat = DegreesToRadians(b.Latitude - a.Latitude);
        var dLon = DegreesToRadians(b.Longitude - a.Longitude);

        var lat1 = DegreesToRadians(a.Latitude);
        var lat2 = DegreesToRadians(b.Latitude);

        var sinLat = Math.Sin(dLat / 2);
        var sinLon = Math.Sin(dLon / 2);

        var h = sinLat * sinLat + Math.Cos(lat1) * Math.Cos(lat2) * sinLon * sinLon;
        var c = 2 * Math.Asin(Math.Min(1, Math.Sqrt(h)));

        return EarthRadiusMeters * c;
    }

    private static double DegreesToRadians(double degrees) => degrees * (Math.PI / 180d);
}
