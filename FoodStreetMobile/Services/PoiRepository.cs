using FoodStreetMobile.Models;

namespace FoodStreetMobile.Services;

public sealed class PoiRepository
{
    public IReadOnlyList<Poi> GetPois()
    {
        return Array.Empty<Poi>();
    }
}
