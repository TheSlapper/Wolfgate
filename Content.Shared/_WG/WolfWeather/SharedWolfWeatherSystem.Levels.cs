using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;

namespace Content.Shared._WG.WolfWeather;

public abstract partial class SharedWolfWeatherSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private EntityQuery<WolfWeatherMapComponent> _weatherMapQuery = default!;
    [Dependency] private EntityQuery<CEZCloudLayerComponent> _cloudQuery = default!;

    /// <summary>Most levels walked down from a story to the surface whose weather falls on it.</summary>
    private const int MaxWeatherLevels = 16;

    /// <summary>
    /// The map whose weather falls on this one: itself, or for a story above a surface, that surface. A cloud deck
    /// in between, orbit, and any level below the ground have none.
    /// </summary>
    public bool TryGetWeatherMap(EntityUid map, out Entity<WolfWeatherMapComponent> weather)
    {
        weather = default;
        if (_orbitQuery.HasComp(map))
            return false;

        var current = map;
        for (var i = 0; i < MaxWeatherLevels; i++)
        {
            if (_cloudQuery.HasComp(current))
                return false;

            if (_weatherMapQuery.TryComp(current, out var comp) && IsSurface(current))
            {
                weather = (current, comp);
                return true;
            }

            if (!_zMapQuery.TryComp(current, out var z) || z.Depth <= 0 || _groundLayerQuery.HasComp(current)
                || !_zLevels.TryMapDown((current, z), out var below))
                return false;

            current = below.Owner;
        }

        return false;
    }

    /// <summary>The weather over the sky a body is under, its vehicle's hull included.</summary>
    public bool TryGetSkyWeather(EntityUid body, out Entity<WolfWeatherMapComponent> weather)
    {
        weather = default;
        return GetSkyMap(body) is { } map && TryGetWeatherMap(map, out weather);
    }
}
