using System.Resources;

namespace FoodStreetMobile;

internal static class AppResources
{
    private static ResourceManager? _resourceManager;

    public static ResourceManager ResourceManager
        => _resourceManager ??= new ResourceManager("FoodStreetMobile.AppResources", typeof(AppResources).Assembly);
}

