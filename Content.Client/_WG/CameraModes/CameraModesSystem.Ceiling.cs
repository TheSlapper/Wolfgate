using System.Numerics;
using Content.Client._WG.CameraModes.Atlas;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._CE.ZLevels.Core.EntitySystems;
using Content.Shared._WG.Caverns;
using Content.Shared._WG.Planets;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Content.Shared.Maps;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.CameraModes;

/// <summary>One tile drawn over the first-person view: a ceiling, or the floor of a level above seen from below.</summary>
public readonly record struct CeilingTile(Vector2 Centre, Angle Rotation, AtlasSlot Slot, int Level);

public sealed partial class CameraModesSystem
{
    [Dependency] private CESharedZLevelsSystem _zLevels = default!;
    [Dependency] private SharedRoofSystem _roof = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;

    /// <summary>Height of a room in first person, and of each level above, in tiles.</summary>
    public const float RoomHeight = 2f;

    /// <summary>
    /// The most levels above the player the views show. In first person the first one's floor is the ceiling.
    /// </summary>
    private const int MaxLevelsAbove = 8;
    private const string PlainCeilingTile = "Plating";
    private const int TilePixels = 32;
    private const ulong TileKey = 0x7ce11149f00dUL;

    private List<Entity<MapGridComponent>> _ceilingGrids = new();
    private readonly EntityUid[] _mapsAbove = new EntityUid[MaxLevelsAbove];

    /// <summary>The tiles over the view this frame, in any order. Level 1 sits at the room height.</summary>
    public readonly List<CeilingTile> Ceiling = new();

    /// <summary>Atlas cells of tile art still to be drawn, with the tile kind each shows.</summary>
    public readonly List<(AtlasSlot Slot, ushort Tile)> TileArt = new();

    /// <summary>The atlas cell of the plain ceiling art, which also covers the block over a door or window.</summary>
    public AtlasSlot? PlainSlot { get; private set; }

    /// <summary>How many levels above the player are drawn this frame.</summary>
    public int LevelsAbove { get; private set; }

    /// <summary>The ceiling's height: on top of the walls under a real level, otherwise a room's height.</summary>
    public float CeilingHeight => LevelsAbove > 0 ? BlockHeight : RoomHeight;

    /// <summary>The map of a level above the player, counted from 1.</summary>
    private MapId MapAbove(int level)
    {
        return Comp<MapComponent>(_mapsAbove[level - 1]).MapId;
    }

    /// <summary>
    /// Finds the levels above the player that have floors of their own. The 2.5D views show none over a cavern, as the
    /// stock view never shows the surface from below.
    /// </summary>
    private void FindLevels(EntityUid? map)
    {
        LevelsAbove = 0;
        if (map is not { } current || !FirstPerson && HasComp<WGCavernLayerComponent>(current))
            return;

        while (LevelsAbove < MaxLevelsAbove && _zLevels.TryMapUp(current, out var above)
               && !HasComp<CEZCloudLayerComponent>(above) && !HasComp<WGOrbitLayerComponent>(above)
               && !HasComp<CEZTransitMapComponent>(above) && HasComp<MapComponent>(above))
        {
            _mapsAbove[LevelsAbove++] = above;
            current = above;
        }
    }

    /// <summary>
    /// Gathers the tiles over the view. With a level above, each of its floor tiles shows its own art and a hole
    /// stays open. With none, a plain ceiling covers the floor: every tile of a station, a ship or a cavern, and the
    /// roofed tiles of a planet surface.
    /// </summary>
    private void CollectCeiling(EntityUid? map, MapId mapId, Box2 box)
    {
        Ceiling.Clear();
        TileArt.Clear();
        PlainSlot = null;
        if (!FirstPerson || map is not { } mapUid || _atlas == null)
            return;

        for (var level = 1; level <= LevelsAbove; level++)
        {
            AddTiles(MapAbove(level), box, level, null, null);
        }

        if (!_tileDefs.TryGetDefinition(PlainCeilingTile, out var plain))
            return;

        PlainSlot = _atlas.Get(TileKey ^ plain.TileId, TilePixels, Frame);
        if (PlainSlot is { Drawn: false } undrawn && !TileArt.Contains((undrawn, plain.TileId)))
            TileArt.Add((undrawn, plain.TileId));

        if (LevelsAbove > 0)
            return;

        AddTiles(mapId, box, 1, plain.TileId, OpenGround(mapUid) ? mapUid : null);
    }

    /// <summary>
    /// How the stock picture draws the level below a map, the way CE's z-level pass does: moved along the view by its
    /// depth and scaled by it. False when there is no level below.
    /// </summary>
    /// <param name="shift">How far the level is moved, in world tiles.</param>
    public bool TryLevelBelow(EntityUid? map, Angle eyeRotation, out Vector2 shift, out float scale)
    {
        shift = default;
        scale = 1f;
        if (map is not { } uid || _player.LocalEntity is not { } player || !_zLevels.TryMapDown(uid, out var below))
            return false;

        var depth = -1f - _zLevels.GetLocalAltitude(player);
        shift = -(-eyeRotation).ToWorldVec() * CESharedZLevelsSystem.ZLevelOffset;
        scale = WGPlanetView.LayerScale(WGPlanetView.Shrink(EntityManager, below.Owner, player), depth);
        return true;
    }

    /// <summary>Whether a map has a level below it, which the stock picture shows through its holes.</summary>
    public bool HasLevelBelow(EntityUid? map)
    {
        return map is { } uid && _zLevels.TryMapDown(uid, out _);
    }

    /// <summary>
    /// Whether a map is ground under an open sky: a planet surface, which is a map that is its own grid, and not a
    /// cavern.
    /// </summary>
    public bool OpenGround(EntityUid? map)
    {
        return HasComp<MapGridComponent>(map) && !HasComp<WGCavernLayerComponent>(map);
    }

    /// <summary>
    /// Whether first person lays the stock picture round the eye as background. Over ground or levels below that
    /// picture is floor, not background, so it isn't.
    /// </summary>
    public bool FirstPersonBackdrop(EntityUid? map)
    {
        return !HasLevelBelow(map) && !OpenGround(map);
    }

    /// <summary>Adds the indoor tiles of a map inside a box to <see cref="Ceiling"/>, queueing art not yet drawn.</summary>
    /// <param name="art">The tile kind every tile shows, or null for its own.</param>
    /// <param name="ground">Open ground, whose tiles only count where they are roofed.</param>
    private void AddTiles(MapId mapId, Box2 box, int level, ushort? art, EntityUid? ground)
    {
        _ceilingGrids.Clear();
        _map.FindGridsIntersecting(mapId, box, ref _ceilingGrids, true);
        foreach (var grid in _ceilingGrids)
        {
            var roofedOnly = grid.Owner == ground;
            var roof = roofedOnly ? CompOrNull<RoofComponent>(grid) : null;
            if (roofedOnly && roof == null)
                continue;

            var rotation = _transform.GetWorldRotation(grid);
            var tiles = _map.GetTilesIntersecting(grid, grid, box);
            while (tiles.MoveNext(out var tile))
            {
                if (_tileDefs[tile.Tile.TypeId] is not ContentTileDefinition def || def.MapAtmosphere)
                    continue;

                if (roof != null && !_roof.IsRooved((grid, grid, roof), tile.GridIndices))
                    continue;

                var kind = art ?? (ushort) tile.Tile.TypeId;
                if (_atlas!.Get(TileKey ^ kind, TilePixels, Frame) is not { } slot)
                    continue;

                if (!slot.Drawn && !TileArt.Contains((slot, kind)))
                    TileArt.Add((slot, kind));

                Ceiling.Add(new CeilingTile(_map.GridTileToWorldPos(grid, grid, tile.GridIndices), rotation, slot, level));
            }
        }
    }
}
