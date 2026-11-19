using System.Numerics;
using Content.Shared.Maps;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    /// <summary>The most tiles the building round the player can span before the fill stops.</summary>
    private const int CutawayCap = 4096;

    /// <summary>How often the building is filled again while the player stands still, for doors opening and closing.</summary>
    private const int CutawayFrames = 30;

    /// <summary>Points tested along the floor between an object above and the first-person eye.</summary>
    private const int UnderFootSamples = 4;

    private readonly HashSet<Vector2i> _cutaway = new();
    private readonly Queue<Vector2i> _cutawayQueue = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _gatheredWindows = new();
    private readonly List<SceneObject> _slabPool = new();
    private int _slabsUsed;
    private EntityUid _cutawayGrid;
    private Vector2i _cutawayStart;
    private int _cutawayFrame = -CutawayFrames;

    /// <summary>
    /// Finds the building the player stands in under a real level, in the 2.5D views: its floor under a floor above,
    /// reached through doors but not walls or windows, and the walls and windows round it. The levels above are left
    /// out over it.
    /// </summary>
    private void UpdateCutaway()
    {
        if (FirstPerson || LevelsAbove == 0 || _player.LocalEntity is not { } player)
        {
            _cutaway.Clear();
            _cutawayGrid = EntityUid.Invalid;
            return;
        }

        var xform = Transform(player);
        if (xform.GridUid is not { } gridUid || !_gridQuery.TryComp(gridUid, out var grid))
        {
            _cutaway.Clear();
            _cutawayGrid = EntityUid.Invalid;
            return;
        }

        var start = _map.TileIndicesFor(gridUid, grid, xform.Coordinates);
        if (gridUid == _cutawayGrid && start == _cutawayStart && Frame - _cutawayFrame < CutawayFrames)
            return;

        _cutawayGrid = gridUid;
        _cutawayStart = start;
        _cutawayFrame = Frame;
        _cutaway.Clear();
        _cutawayQueue.Clear();
        if (!RoofOver(_map.GridTileToWorldPos(gridUid, grid, start)))
            return;

        _cutaway.Add(start);
        _cutawayQueue.Enqueue(start);
        while (_cutawayQueue.TryDequeue(out var tile) && _cutaway.Count < CutawayCap)
        {
            foreach (var offset in Neighbours)
            {
                var next = tile + offset;
                if (_cutaway.Contains(next) || !RoofOver(_map.GridTileToWorldPos(gridUid, grid, next)))
                    continue;

                // The walls and windows round the building are part of it, corners too, but the fill stops at them
                // and only spreads sideways.
                var shuts = Shuts(gridUid, grid, next);
                if (offset.X != 0 && offset.Y != 0 && !shuts)
                    continue;

                _cutaway.Add(next);
                if (!shuts)
                    _cutawayQueue.Enqueue(next);
            }
        }
    }

    /// <summary>The eight tiles round a tile.</summary>
    private static readonly Vector2i[] Neighbours =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(1, -1), new(-1, 1), new(-1, -1),
    };

    /// <summary>Whether a tile holds a wall, rock or window that isn't a door, which ends a building's fill.</summary>
    private bool Shuts(EntityUid gridUid, MapGridComponent grid, Vector2i tile)
    {
        var anchored = _map.GetAnchoredEntitiesEnumerator(gridUid, grid, tile);
        while (anchored.MoveNext(out var uid))
        {
            if (_doorQuery.HasComp(uid))
                return false;

            if (_tags.HasTag(uid.Value, WindowTag)
                || TryComp<SpriteComponent>(uid.Value, out var sprite) && Wall(uid.Value, sprite))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a world point lies over the building the player stands in.</summary>
    private bool InCutaway(Vector2 world)
    {
        if (_cutaway.Count == 0 || !_gridQuery.TryComp(_cutawayGrid, out var grid))
            return false;

        return _cutaway.Contains(_map.WorldToTile(_cutawayGrid, grid, world));
    }

    /// <summary>Whether a floor lies over a world point on any level above the player's, as over a stairwell.</summary>
    private bool RoofOver(Vector2 world)
    {
        for (var level = 0; level < LevelsAbove; level++)
        {
            if (FloorOver(level, world))
                return true;
        }

        return false;
    }

    /// <summary>Whether a floor lies over a world point, on the level above the given one.</summary>
    private bool FloorOver(int level, Vector2 world)
    {
        if (level < 0 || level + 1 > LevelsAbove)
            return false;

        var mapId = MapAbove(level + 1);
        if (!_map.TryFindGridAt(mapId, world, out var gridUid, out var grid)
            || !_map.TryGetTileRef(gridUid, grid, _map.WorldToTile(gridUid, grid, world), out var tile)
            || tile.Tile.IsEmpty)
        {
            return false;
        }

        return _tileDefs[tile.Tile.TypeId] is ContentTileDefinition { MapAtmosphere: false };
    }

    /// <summary>
    /// Leaves out what can't be seen: whatever stands over the player's building on a level above in the 2.5D views,
    /// and what sits in a closed room under a real floor, away from any window. Walls and windows always stay. What is
    /// left out on the player's own level has its stock sprite hidden too.
    /// </summary>
    private void CullEnclosed()
    {
        if (LevelsAbove == 0)
            return;

        _gatheredWindows.Clear();
        foreach (var obj in Drawn)
        {
            if (obj.Kind == SceneKind.Window && obj.Grid.IsValid())
                _gatheredWindows.Add((obj.Grid, obj.Tile));
        }

        var local = _player.LocalEntity;
        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            if (obj.Level < 0 || obj.Uid == local || obj.Level == 0 && FirstPerson)
                continue;

            // Above, the building's own stories go. On the player's level, its rooms stay.
            var unseen = obj.Level > 0
                ? InCutaway(obj.Position) || Enclosed(obj) || FirstPerson && UnderFoot(obj)
                : !InCutaway(obj.Position) && Enclosed(obj);
            if (!unseen)
                continue;

            Drawn.RemoveAt(i);
            if (obj.Level == 0)
                HideObject(obj);
        }
    }

    /// <summary>
    /// Whether the floor an object on a level above stands on hides all of it from the first-person eye: floor lies
    /// under it and all along the stretch where the eye's lines to it pass through that floor's height.
    /// </summary>
    private bool UnderFoot(SceneObject obj)
    {
        var rise = StoryRise(obj) - EyeHeight;
        if (rise <= 0f)
            return false;

        // The line to the object's top crosses the floor's height nearest the eye.
        var tall = obj.SlotSize / (float) EyeManager.PixelsPerMeter;
        var from = rise / (rise + tall);
        for (var i = 0; i <= UnderFootSamples; i++)
        {
            var at = Vector2.Lerp(_viewCentre, obj.Position, from + (1f - from) * i / UnderFootSamples);
            if (!FloorOver(obj.Level - 1, at))
                return false;
        }

        return true;
    }

    /// <summary>Whether an object sits under a floor above, in a room with no window within a tile of it.</summary>
    private bool Enclosed(SceneObject obj)
    {
        // Art painted on a wall's open face is on the wall, not in the room behind it.
        if (obj.Kind is SceneKind.Block or SceneKind.Window || obj.Painted || !FloorOver(obj.Level, obj.Position))
            return false;

        if (!obj.Grid.IsValid())
            return true;

        for (var x = -1; x <= 1; x++)
        {
            for (var y = -1; y <= 1; y++)
            {
                if (_gatheredWindows.Contains((obj.Grid, obj.Tile + new Vector2i(x, y))))
                    return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Adds the floors of the levels above as slabs in the 2.5D views, each at its level's height, the roof of the
    /// story under it. None are laid over the player's building.
    /// </summary>
    private void CollectSlabs(Box2 box)
    {
        _slabsUsed = 0;
        if (FirstPerson || LevelsAbove == 0 || _atlas == null)
            return;

        Ceiling.Clear();
        for (var level = 1; level <= LevelsAbove; level++)
        {
            AddTiles(MapAbove(level), box, level, null, null);
        }

        foreach (var tile in Ceiling)
        {
            if (InCutaway(tile.Centre))
                continue;

            if (_slabsUsed == _slabPool.Count)
                _slabPool.Add(new SceneObject { Uid = EntityUid.Invalid, Kind = SceneKind.Slab });

            var slab = _slabPool[_slabsUsed++];
            slab.Level = tile.Level;
            slab.Position = tile.Centre;
            slab.Rotation = tile.Rotation;
            slab.Slot = tile.Slot;
            slab.Alpha = 1f;
            slab.Faded = false;
            slab.Draw = true;
            slab.Seen = Frame;
            Drawn.Add(slab);
        }

        Ceiling.Clear();
    }

    /// <summary>The world box the levels above are read in: the view, and as far south as their height rises.</summary>
    private Box2 AboveBounds(Box2 bounds, int level)
    {
        var profile = Profile;
        var rise = level * BlockHeight * profile.Height / profile.Squash;
        return bounds.Union(bounds.Translated(-Up * rise));
    }
}
