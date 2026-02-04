using FoodStreetMobile.ViewModels;
using Microsoft.Maui.Devices.Sensors;

namespace FoodStreetMobile.Services;

public sealed class GeofenceEngine
{
    public PoiViewModel? SelectActive(Location userLocation, IReadOnlyList<PoiViewModel> pois)
    {
        PoiViewModel? best = null;

        foreach (var poi in pois)
        {
            var distance = GeoMath.DistanceMeters(userLocation, new Location(poi.Latitude, poi.Longitude));
            poi.DistanceMeters = distance;

            if (distance > poi.RadiusMeters)
            {
                continue;
            }

            if (best is null)
            {
                best = poi;
                continue;
            }

            if (poi.Priority > best.Priority)
            {
                best = poi;
                continue;
            }

            if (poi.Priority == best.Priority && distance < best.DistanceMeters)
            {
                best = poi;
            }
        }

        return best;
    }
}
