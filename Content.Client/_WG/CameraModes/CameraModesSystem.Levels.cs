using System.Numerics;
using Content.Shared._WG.Planets;
using Content.Client._CE.ZLevels.Core.Overlays;
using Content.Client.Viewport;
using Content.Shared._WG.CameraModes;
using Content.Shared.Maps;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.CameraModes;

/// <summary>A level below the player, drawn this frame into a picture of its own.</summary>
public sealed class LowerLevel
{
    /// <summary>The level's map.</summary>
    public EntityUid Map;

    /// <summary>How far its floor lies under the player's, in tiles.</summary>
    public float Drop;

    /// <summary>The level as drawn, clear where it is open.</summary>
    public IRenderTexture? Target;

    /// <summary>The level's own light map, for what stands on it.</summary>
    public IRenderTexture? Light;
}

public sealed partial class CameraModesSystem
{
    /// <summary>
    /// The most levels below that the 3D views draw at their depth, so a tall building stands down to the street.
    /// The nearest are kept.
    /// </summary>
    public const int MaxLowerLevels = 8;

    /// <summary>How many tiles from a hole in the player's floor something below can still show through it.</summary>
    private const int HoleReach = 2;

    /// <summary>Clear black. The engine's Transparent is clear white, which comes out as a white wash.</summary>
    private static readonly Color Clear = new(0f, 0f, 0f, 0f);

    /// <summary>The stock blur of the levels below while first person holds it off.</summary>
    private Overlay? _heldBlur;

    private bool[] _hole = Array.Empty<bool>();
    private bool[] _holeRow = Array.Empty<bool>();
    private bool[] _nearHole = Array.Empty<bool>();
    private Box2i _holeBox;
    private EntityUid _holeMap;
    private int _holeFrame;
    private bool _anyHole;

    /// <summary>The eye of the level below the main viewport is drawing now, if any.</summary>
    private ScalingViewport.ZEye? _passEye;

    private readonly List<LowerLevel> _lowerSpare = new();
    private readonly Dictionary<EntityUid, List<SceneObject>> _lowerObjects = new();
    private List<Entity<MapGridComponent>> _levelGrids = new();
    private uint _lowerFrame = uint.MaxValue;

    /// <summary>The levels below drawn this frame, farthest first.</summary>
    public readonly List<LowerLevel> LowerLevels = new();

    /// <summary>
    /// Whether the view draws the levels below at their own depth, with what stands on them. Not in orbit, where the
    /// stock picture of the ground is shown one level down.
    /// </summary>
    public bool TakesLevels => Active && !InOrbit;

    /// <summary>Whether the player is in a planet's orbit layer.</summary>
    public bool InOrbit => _player.LocalEntity is { } player && HasComp<WGOrbitLayerComponent>(Transform(player).MapUid);

    private void InitializeLevels()
    {
        SubscribeLocalEvent<WGZLevelPassEvent>(OnZLevelPass);
    }

    /// <summary>The levels below drawn this frame, or none if they were drawn the stock way.</summary>
    public IReadOnlyList<LowerLevel> CurrentLowerLevels()
    {
        if (_lowerFrame != _timing.CurFrame)
            LowerLevels.Clear();

        return LowerLevels;
    }

    private void OnZLevelPass(ref WGZLevelPassEvent ev)
    {
        if (!TakesLevels || ev.Control != MainViewport() || _player.LocalEntity is not { } player)
            return;

        // The engine leaves the viewport control out of a level's own pass, so the overlays know it by its eye.
        _passEye = ev.After ? null : ev.Eye;

        if (_lowerFrame != _timing.CurFrame)
        {
            _lowerFrame = _timing.CurFrame;
            _lowerSpare.AddRange(LowerLevels);
            LowerLevels.Clear();
        }

        if (ev.After)
        {
            CaptureLevel(ev);
            return;
        }

        if (DropTo(ev.Map, player) is not { } drop)
            return;

        // An empty air layer would take a slot from the ground under it.
        var reach = ev.Viewport.Size / (EyeManager.PixelsPerMeter * ev.Base.Scale);
        var area = Box2.CenteredAround(ev.Eye.Position.Position + ev.Base.Offset, reach);
        _levelGrids.Clear();
        _map.FindGridsIntersecting(ev.Eye.Position.MapId, area, ref _levelGrids, true);
        if (_levelGrids.Count == 0)
            return;

        if (LowerLevels.Count >= MaxLowerLevels)
        {
            _lowerSpare.Add(LowerLevels[0]);
            LowerLevels.RemoveAt(0);
        }

        var level = _lowerSpare.Count > 0 ? _lowerSpare[^1] : new LowerLevel();
        if (_lowerSpare.Count > 0)
            _lowerSpare.RemoveAt(_lowerSpare.Count - 1);

        level.Map = ev.Map;
        level.Drop = drop;
        LowerLevels.Add(level);

        if (FirstPerson)
        {
            // Straight under the eye, no shift or shrink, with nothing round it so its holes stay clear. It ends where
            // the player's own floor does.
            ev.Eye.Offset = ev.Base.Offset;
            ev.Eye.Scale = ev.Base.Scale;
            ev.Viewport.ClearColor = Clear;
        }
        else
        {
            // The tilt draws a floor that far down shifted along the screen's down, and perspective draws it smaller.
            var profile = Profile;
            ev.Eye.Offset = ev.Base.Offset + Up * (drop * profile.Height / profile.Squash);
            ev.Eye.Scale = ev.Base.Scale / LowerShrink(drop);
        }

        // What stands on the level is drawn standing, so its picture leaves it out.
        if (_lowerObjects.TryGetValue(ev.Map, out var standing))
        {
            foreach (var obj in standing)
            {
                HideObject(obj);
            }
        }
    }

    /// <summary>
    /// First person copies a level just drawn, and its light map, into pictures of its own, then blacks out the shared
    /// picture under the next pass. The 2.5D views leave the level in the shared picture, what stands on it included,
    /// as their mesh overlay draws that inside the level's own pass.
    /// </summary>
    private void CaptureLevel(in WGZLevelPassEvent ev)
    {
        if (LowerLevels.Count == 0 || LowerLevels[^1].Map != ev.Map)
            return;

        if (FirstPerson)
        {
            var level = LowerLevels[^1];
            Copy(ev.Handle, ev.Viewport.RenderTarget, ref level.Target, Clear);
            Copy(ev.Handle, ev.Viewport.LightRenderTarget, ref level.Light, Color.Black);
            ev.Handle.RenderInRenderTarget(ev.Viewport.RenderTarget, () => { }, Color.Black);
        }

        RestoreColors();
    }

    /// <summary>
    /// Whether an overlay pass is the 2.5D views drawing a level below in the main viewport, whose standing objects the
    /// mesh overlay adds there, under the stock blur of that level and the floors of the nearer ones.
    /// </summary>
    public bool IsLowerPass(in OverlayDrawArgs args)
    {
        return TakesLevels && !FirstPerson && _passEye != null && args.Viewport.Eye == _passEye;
    }

    /// <summary>
    /// How much smaller the perspective view draws a floor that far down, about the view's centre. Under its warp that
    /// is exact: a level floor sits where the flat picture would, shrunk by this much. 1 in the other views.
    /// </summary>
    public float LowerShrink(float drop)
    {
        if (Mode != CameraMode.Perspective || _mesh == null)
            return 1f;

        return 1f + _mesh.WarpLambda * _mesh.MainPixelsPerTile * drop / Profile.Height;
    }

    /// <summary>Copies a render target into a kept one, made again when the size changes.</summary>
    private void Copy(IRenderHandle handle, IRenderTexture source, ref IRenderTexture? target, Color clear)
    {
        if (target == null || target.Size != source.Size)
        {
            target?.Dispose();
            target = _clyde.CreateRenderTarget(source.Size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = true },
                nameof(LowerLevel));
        }

        var screen = handle.DrawingHandleScreen;
        var size = source.Size;
        handle.RenderInRenderTarget(target,
            () => screen.DrawTextureRect(source.Texture, UIBox2.FromDimensions(Vector2.Zero, size)), clear);
    }

    /// <summary>
    /// Gathers what stands on each level below: in first person all in view, in the 2.5D views only near a hole in the
    /// player's floor, where it can show.
    /// </summary>
    private void CollectLower(Angle eyeRot, Box2 view)
    {
        if (!TakesLevels)
            return;

        var levels = CurrentLowerLevels();
        if (!FirstPerson && !UpdateHoles(view, levels))
            return;

        var profile = Profile;
        for (var i = 0; i < levels.Count; i++)
        {
            var level = levels[i];
            if (!TryComp<MapComponent>(level.Map, out var map))
                continue;

            // The 2.5D views draw a level below shifted down the screen, so what stands farther up comes into view.
            // Perspective draws it smaller too, so it reaches wider.
            var shift = FirstPerson ? Vector2.Zero : Up * (level.Drop * profile.Height / profile.Squash);
            var shrink = FirstPerson ? 1f : LowerShrink(level.Drop);
            var box = Box2.CenteredAround(view.Center + shift, view.Size * shrink);
            _query.Clear();
            _spriteTree.QueryAabb(_query, map.MapId, box);
            if (!FirstPerson)
                KeepNearHoles(view.Center, shift, shrink);

            var from = Drawn.Count;
            Gather(eyeRot, i - levels.Count, level.Drop);
            for (var k = from; k < Drawn.Count; k++)
            {
                Drawn[k].LevelMap = level.Map;
            }
        }
    }

    /// <summary>
    /// Drops from the query what stands too far from a hole in the player's floor to show through it. That floor point
    /// is where the level's picture puts the object: shifted, and shrunk about the view's centre.
    /// </summary>
    private void KeepNearHoles(Vector2 centre, Vector2 shift, float shrink)
    {
        var kept = 0;
        for (var i = 0; i < _query.Count; i++)
        {
            var at = centre + (_transform.GetWorldPosition(_query[i].Comp2) - shift - centre) / shrink;
            var x = (int) MathF.Floor(at.X) - _holeBox.Left;
            var y = (int) MathF.Floor(at.Y) - _holeBox.Bottom;
            if (x < 0 || y < 0 || x >= _holeBox.Width || y >= _holeBox.Height || !_nearHole[y * _holeBox.Width + x])
                continue;

            _query[kept++] = _query[i];
        }

        _query.RemoveRange(kept, _query.Count - kept);
    }

    /// <summary>
    /// Marks the tiles in view within <see cref="HoleReach"/> of a hole in the player's floor: no floor there, or a
    /// see-through one. Kept while the view stays on the same tiles, and redone every few frames for floors that
    /// change. False when the player's floor has no hole in view.
    /// </summary>
    private bool UpdateHoles(Box2 view, IReadOnlyList<LowerLevel> levels)
    {
        if (levels.Count == 0 || _player.LocalEntity is not { } player || Transform(player).MapUid is not { } mapUid
            || !TryComp<MapComponent>(mapUid, out var map))
        {
            _holeMap = EntityUid.Invalid;
            return false;
        }

        var box = new Box2i((int) MathF.Floor(view.Left), (int) MathF.Floor(view.Bottom),
            (int) MathF.Ceiling(view.Right), (int) MathF.Ceiling(view.Top));
        if (box == _holeBox && mapUid == _holeMap && Frame - _holeFrame < CutawayFrames)
            return _anyHole;

        _holeBox = box;
        _holeMap = mapUid;
        _holeFrame = Frame;
        var width = box.Width;
        var size = width * box.Height;
        if (_hole.Length < size)
        {
            _hole = new bool[size];
            _nearHole = new bool[size];
            _holeRow = new bool[size];
        }

        Array.Fill(_hole, true, 0, size);
        _levelGrids.Clear();
        _map.FindGridsIntersecting(map.MapId, view, ref _levelGrids, true);
        foreach (var grid in _levelGrids)
        {
            var tiles = _map.GetTilesIntersecting(grid, grid, view);
            while (tiles.MoveNext(out var tile))
            {
                if (_tileDefs[tile.Tile.TypeId] is ContentTileDefinition def && (def.MapAtmosphere || def.Transparent))
                    continue;

                var at = _map.GridTileToWorldPos(grid, grid, tile.GridIndices);
                var x = (int) MathF.Floor(at.X) - box.Left;
                var y = (int) MathF.Floor(at.Y) - box.Bottom;
                if (x >= 0 && y >= 0 && x < width && y < box.Height)
                    _hole[y * width + x] = false;
            }
        }

        // Grown by the reach along rows, then along columns.
        _anyHole = false;
        for (var y = 0; y < box.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var near = false;
                for (var dx = Math.Max(x - HoleReach, 0); dx <= Math.Min(x + HoleReach, width - 1) && !near; dx++)
                {
                    near = _hole[y * width + dx];
                }

                _holeRow[y * width + x] = near;
                _anyHole |= near;
            }
        }

        for (var y = 0; y < box.Height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var near = false;
                for (var dy = Math.Max(y - HoleReach, 0); dy <= Math.Min(y + HoleReach, box.Height - 1) && !near; dy++)
                {
                    near = _holeRow[dy * width + x];
                }

                _nearHole[y * width + x] = near;
            }
        }

        return _anyHole;
    }

    /// <summary>Notes what is drawn standing on each level below, for that level's picture to leave out.</summary>
    private void ListStanding()
    {
        foreach (var list in _lowerObjects.Values)
        {
            list.Clear();
        }

        var levels = CurrentLowerLevels();
        foreach (var obj in Drawn)
        {
            var index = obj.Level + levels.Count;
            if (obj.Level >= 0 || index < 0)
                continue;

            var map = levels[index].Map;
            if (!_lowerObjects.TryGetValue(map, out var standing))
            {
                standing = new List<SceneObject>();
                _lowerObjects[map] = standing;
            }

            standing.Add(obj);
        }
    }

    /// <summary>The level below an object stands on, or null.</summary>
    public LowerLevel? LevelOf(SceneObject obj)
    {
        var levels = CurrentLowerLevels();
        var index = obj.Level + levels.Count;
        return obj.Level < 0 && index >= 0 ? levels[index] : null;
    }

    /// <summary>
    /// How far a map's floor lies under the player's, in tiles: a wall's height for each level, as the ceiling under a
    /// real level sits on the walls. Null when the map is not below the player.
    /// </summary>
    private float? DropTo(EntityUid map, EntityUid player)
    {
        if (Transform(player).MapUid is not { } current)
            return null;

        var drop = _zLevels.GetLocalAltitude(player) * BlockHeight;
        for (var i = 0; i < 16; i++)
        {
            if (!_zLevels.TryMapDown(current, out var below))
                return null;

            drop += BlockHeight;
            current = below.Owner;
            if (current == map)
                return drop;
        }

        return null;
    }

    private void DisposeLevels()
    {
        foreach (var level in LowerLevels)
        {
            level.Target?.Dispose();
            level.Light?.Dispose();
        }

        foreach (var level in _lowerSpare)
        {
            level.Target?.Dispose();
            level.Light?.Dispose();
        }

        LowerLevels.Clear();
        _lowerSpare.Clear();
        _heldBlur = null;
    }

    /// <summary>
    /// Holds the stock blur of the levels below off in first person, so they show as sharp as the player's floor.
    /// </summary>
    private void UpdateLevelBlur()
    {
        if (TakesLevels && FirstPerson)
        {
            if (_heldBlur == null && _overlays.TryGetOverlay<CEZLevelBlurOverlay>(out var blur)
                && _overlays.RemoveOverlay(blur))
                _heldBlur = blur;

            return;
        }

        if (_heldBlur == null)
            return;

        _overlays.AddOverlay(_heldBlur);
        _heldBlur = null;
    }
}
