using System.Numerics;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.PlanetCities;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server._WG.PlanetCities;

/// <summary>
/// Roofs the floors under city stories that are grids of their own, which don't roof what is below them the way a
/// layer's own tiles do.
/// </summary>
public sealed class WGStoryRoofSystem : EntitySystem
{
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    private readonly HashSet<Vector2i> _above = new();
    private readonly List<int> _depths = new();
    private readonly List<Entity<MapGridComponent>> _grids = new();

    /// <summary>
    /// Sets a roof on every tile of a network's layers and city grids that a solid city story tile above covers.
    /// Only adds roofs, and runs after the layers' own roofs are redrawn.
    /// </summary>
    public void ProjectRoofs(Entity<CEZMapNetworkComponent> network)
    {
        _above.Clear();
        _depths.Clear();
        _depths.AddRange(network.Comp.ZLevels.Keys);
        _depths.Sort();

        for (var i = _depths.Count - 1; i >= 0; i--)
        {
            if (network.Comp.ZLevels[_depths[i]] is not { } map || !TryComp<MapComponent>(map, out var mapComp))
                continue;

            _grids.Clear();
            _grids.AddRange(_map.GetAllGrids(mapComp.MapId));
            if (_above.Count > 0)
            {
                foreach (var grid in _grids)
                {
                    if (grid.Owner == map || HasComp<WGPlanetCityGridComponent>(grid))
                        RoofUnder(grid, grid.Owner == map);
                }
            }

            foreach (var grid in _grids)
            {
                if (HasComp<WGPlanetCityGridComponent>(grid))
                    AddSolidTiles(grid);
            }
        }
    }

    /// <summary>Roofs the grid's tiles under the story tiles gathered so far. A layer's own grid skips the bounds check.</summary>
    private void RoofUnder(Entity<MapGridComponent> grid, bool layer)
    {
        var roof = EnsureComp<RoofComponent>(grid);
        var bounds = _transform.GetWorldMatrix(grid).TransformBox(grid.Comp.LocalAABB);
        foreach (var world in _above)
        {
            var centre = new Vector2(world.X + 0.5f, world.Y + 0.5f);
            if (!layer && !bounds.Contains(centre))
                continue;

            var index = _map.WorldToTile(grid, grid.Comp, centre);
            if (!_map.TryGetTileRef(grid, grid.Comp, index, out var tile) || tile.Tile.IsEmpty)
                continue;

            _roof.SetRoof((grid, grid.Comp, roof), index, true);
        }
    }

    /// <summary>Adds the world tiles of the grid's solid floor to the story tiles gathered so far.</summary>
    private void AddSolidTiles(Entity<MapGridComponent> grid)
    {
        var matrix = _transform.GetWorldMatrix(grid);
        var tiles = _map.GetAllTilesEnumerator(grid, grid.Comp);
        while (tiles.MoveNext(out var tile))
        {
            if (((ContentTileDefinition) _tileDefs[tile.Value.Tile.TypeId]).Transparent)
                continue;

            var centre = Vector2.Transform(_map.TileCenterToVector(grid, tile.Value.GridIndices), matrix);
            _above.Add(new Vector2i((int) MathF.Floor(centre.X), (int) MathF.Floor(centre.Y)));
        }
    }
}
