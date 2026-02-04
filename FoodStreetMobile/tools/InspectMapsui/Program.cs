using System.Reflection;
using System.Runtime.Loader;

var basePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
var paths = new[]
{
    Path.Combine(basePath, "mapsui", "4.1.9", "lib", "net6.0", "Mapsui.dll"),
    Path.Combine(basePath, "mapsui.nts", "4.1.9", "lib", "net6.0", "Mapsui.Nts.dll"),
    Path.Combine(basePath, "mapsui.tiling", "4.1.9", "lib", "net6.0", "Mapsui.Tiling.dll"),
    Path.Combine(basePath, "mapsui.rendering.skia", "4.1.9", "lib", "net6.0", "Mapsui.Rendering.Skia.dll"),
    Path.Combine(basePath, "mapsui.maui", "4.1.9", "lib", "net8.0", "Mapsui.UI.Maui.dll")
};

var lookup = paths.Where(File.Exists).ToDictionary(Path.GetFileName, p => p, StringComparer.OrdinalIgnoreCase);

AssemblyLoadContext.Default.Resolving += (_, name) =>
{
    var fileName = name.Name + ".dll";
    return lookup.TryGetValue(fileName, out var path) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(path) : null;
};

foreach (var path in paths.Where(File.Exists))
{
    AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
}

var mapsuiAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[0]);
var types = mapsuiAsm.GetTypes();
Console.WriteLine("Types containing Feature:");
foreach (var t in types.Where(t => t.Name.Contains("Feature", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(t.FullName);
}

var pointFeatureType = types.FirstOrDefault(t => t.FullName == "Mapsui.Layers.PointFeature");
if (pointFeatureType is not null)
{
    Console.WriteLine("\nPointFeature constructors:");
    foreach (var ctor in pointFeatureType.GetConstructors())
    {
        Console.WriteLine(ctor);
    }

    Console.WriteLine("\nPointFeature properties:");
    foreach (var prop in pointFeatureType.GetProperties())
    {
        Console.WriteLine(prop.Name + " : " + prop.PropertyType.FullName);
    }
}

var symbolStyleType = types.FirstOrDefault(t => t.FullName == "Mapsui.Styles.SymbolStyle");
if (symbolStyleType is not null)
{
    Console.WriteLine("\nSymbolStyle properties:");
    foreach (var prop in symbolStyleType.GetProperties())
    {
        Console.WriteLine(prop.Name + " : " + prop.PropertyType.FullName);
    }
}

var brushType = types.FirstOrDefault(t => t.FullName == "Mapsui.Styles.Brush");
if (brushType is not null)
{
    Console.WriteLine("\nBrush constructors:");
    foreach (var ctor in brushType.GetConstructors())
    {
        Console.WriteLine(ctor);
    }
}

Console.WriteLine("\nTypes containing Map:");
foreach (var t in types.Where(t => t.Name.Contains("Map", StringComparison.OrdinalIgnoreCase)).Take(20))
{
    Console.WriteLine(t.FullName);
}

var mapsuiUiAsm = AssemblyLoadContext.Default.LoadFromAssemblyPath(paths[4]);
Console.WriteLine("\nMapsui.UI.Maui types:");
foreach (var t in mapsuiUiAsm.GetTypes().Where(t => t.Name.Contains("Map", StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine(t.FullName);
}
