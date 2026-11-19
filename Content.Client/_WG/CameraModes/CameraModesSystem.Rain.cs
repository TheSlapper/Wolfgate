using Content.Client._WG.WolfWeather;
using Content.Shared._WG.WolfWeather;
using Content.Shared.Light.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private WolfWeatherSystem _wolfWeather = default!;

    private static readonly Vector2i[] RainSides =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
    };

    /// <summary>
    /// Works out whether the weather falls in front of an object: one on a tile under open sky, or a wall, door or window
    /// on a building's edge. Worked out again only when its tile or its map's tiles change.
    /// </summary>
    private void UpdateRained(SceneObject obj, MapId map)
    {
        var version = _wolfWeather.MaskVersionOf(map);
        if (obj.RainVersion == version && obj.RainGrid == obj.Grid && obj.RainTile == obj.Tile)
            return;

        obj.RainVersion = version;
        obj.RainGrid = obj.Grid;
        obj.RainTile = obj.Tile;
        obj.Rained = false;
        if (!_gridQuery.TryComp(obj.Grid, out var grid))
            return;

        TryComp<RoofComponent>(obj.Grid, out var roof);
        var roofed = new Entity<MapGridComponent, RoofComponent?>(obj.Grid, grid, roof);
        var flags = _wolfWeather.Classify(roofed, _map.GetTileRef(obj.Grid, grid, obj.Tile));
        if ((flags & WolfWeatherTile.Exposed) != 0)
        {
            obj.Rained = true;
            return;
        }

        if ((flags & WolfWeatherTile.Blocker) == 0)
            return;

        if ((flags & WolfWeatherTile.OpenSky) != 0)
        {
            obj.Rained = true;
            return;
        }

        foreach (var side in RainSides)
        {
            if (_wolfWeather.IsExposed(roofed, _map.GetTileRef(obj.Grid, grid, obj.Tile + side)))
            {
                obj.Rained = true;
                return;
            }
        }
    }
}
