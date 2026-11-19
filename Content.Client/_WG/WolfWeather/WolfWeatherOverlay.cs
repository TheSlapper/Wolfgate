using System.Numerics;
using System.Runtime.InteropServices;
using Content.Client.Graphics;
using Content.Client.Viewport;
using Content.Client.Parallax;
using Content.Client._WG.CameraModes;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.WolfWeather;
using Content.Shared.Light.Components;
using Content.Shared.Maps;
using Content.Shared.Tag;
using Content.Shared.Weather;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.WolfWeather;

/// <summary>Draws weather layers over exposed tiles, masked off roofed and indoor tiles.</summary>
public sealed partial class WolfWeatherOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> WeatherShader = "WGWolfWeather";
    private static readonly ProtoId<ShaderPrototype> BlurShader = "WGWolfWeatherBlur";
    private static readonly ProtoId<ShaderPrototype> UnshadedShader = "unshaded";
    private static readonly Vector2i[] Cardinals = { new(0, 1), new(1, 0), new(0, -1), new(-1, 0) };

    private const float PixelsPerTile = 32f;
    private const double CellPeriod = 1024.0;
    private const double SplashPeriod = 3600.0;

    /// <summary>How far weather fades out before cover, in tiles.</summary>
    private const float Feather = 0.45f;

    /// <summary>How far wall edges fade, in tiles.</summary>
    private const float HardFeather = Feather * 0.2f;

    /// <summary>Mask texels per tile, lowered when zoomed far out so the mask stays under MaxMaskSize.</summary>
    private const float MaskTexelsPerTile = 8f;

    private const int MaxMaskSize = 768;

    /// <summary>A level below draws its mask at this share of the view's own density.</summary>
    private const float LowerMaskShare = 0.5f;

    /// <summary>How long a viewport keeps a mask it has stopped drawing, for a level that drops out of view for a moment.</summary>
    private static readonly TimeSpan MaskKeep = TimeSpan.FromSeconds(2);

    /// <summary>Tiles drawn into the mask past the view's edge, snapped so small camera moves keep the mask.</summary>
    private const float MaskMargin = 4f;

    private const float MaskSnap = 4f;

    /// <summary>How often the mask is redrawn anyway, to catch walls and roofs built or removed.</summary>
    private static readonly TimeSpan MaskRefresh = TimeSpan.FromSeconds(1);

    /// <summary>Soonest the mask is redrawn for changed tiles, as terrain streaming in changes tiles every tick.</summary>
    private static readonly TimeSpan MaskMinAge = TimeSpan.FromSeconds(0.25);

    private static readonly Color Soft = new(1f, 0f, 0f);
    private static readonly Color Hard = new(0f, 1f, 0f);
    private static readonly Color Open = new(0f, 0f, 1f);

    /// <summary>A wall under open sky or on a building's edge: rain falls in front of it, but no splash lands on it.</summary>
    private static readonly Color OpenWall = new(0f, 0f, 0.5f);
    private static readonly Color ClearColor = new(0f, 0f, 0f, 0f);

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IPlayerManager _player = default!;

    private readonly WolfWeatherSystem _wolfWeather;
    private readonly CameraModesSystem _camera;
    private readonly EntityLookupSystem _lookup;
    private readonly SharedMapSystem _map;
    private readonly SharedTransformSystem _xform;
    private readonly TagSystem _tag;
    private readonly EntityQuery<BlockWeatherComponent> _blockQuery;
    private readonly EntityQuery<RoofComponent> _roofQuery;
    private readonly EntityQuery<TransformComponent> _xformQuery;
    private readonly OverlayResourceCache<MaskSet> _masks = new();
    private readonly List<Vector2i> _diagonalTiles = new();
    private readonly List<Vector2> _wallHalves = new();
    private readonly List<WorldRect> _rects = new();
    private List<Entity<MapGridComponent>> _grids = new();
    private readonly Dictionary<ShaderKey, ShaderInstance> _shaders = new();
    private readonly HashSet<ShaderKey> _usedShaders = new();
    private readonly List<ShaderKey> _unusedShaders = new();
    private readonly List<MapId> _staleMasks = new();
    private readonly EntityQuery<CEZTransitMapComponent> _transitQuery;
    private uint _frame = uint.MaxValue;

    /// <summary>Whether a level below has already redrawn its mask this frame. One a frame keeps the cost even.</summary>
    private bool _lowerRebuilt;
    private readonly ShaderInstance _blurH;
    private readonly ShaderInstance _blurV;
    private readonly ShaderInstance _unshaded;
    private readonly Action _drawMask;

    private DrawingHandleWorld _maskHandle = default!;
    private Matrix3x2 _maskMatrix;
    private MapId _maskMapId;
    private Box2 _maskRect;
    private bool _maskHoles;
    private Box2? _maskOpen;

    private IRenderTexture? _rainPicture;
    private readonly List<IRenderTexture> _retired = new();
    private readonly Action _drawPicture;
    private DrawingHandleWorld _pictureHandle = default!;
    private MapId _pictureMap;
    private Box2 _pictureBounds;
    private WolfWeatherMapComponent _pictureWeather = default!;
    private WolfWeatherMapState _pictureState = default!;
    private MaskEntry _pictureMask = default!;
    private TimeSpan _pictureNow;
    private float _pictureTime;
    private Matrix3x2 _pictureMatrix;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public WolfWeatherOverlay()
    {
        IoCManager.InjectDependencies(this);
        ZIndex = ParallaxSystem.ParallaxZIndex + 2;
        _wolfWeather = _entManager.System<WolfWeatherSystem>();
        _camera = _entManager.System<CameraModesSystem>();
        _lookup = _entManager.System<EntityLookupSystem>();
        _map = _entManager.System<SharedMapSystem>();
        _xform = _entManager.System<SharedTransformSystem>();
        _tag = _entManager.System<TagSystem>();
        _blockQuery = _entManager.GetEntityQuery<BlockWeatherComponent>();
        _roofQuery = _entManager.GetEntityQuery<RoofComponent>();
        _xformQuery = _entManager.GetEntityQuery<TransformComponent>();
        _transitQuery = _entManager.GetEntityQuery<CEZTransitMapComponent>();
        _blurH = _proto.Index(BlurShader).InstanceUnique();
        _blurV = _proto.Index(BlurShader).InstanceUnique();
        _unshaded = _proto.Index(UnshadedShader).Instance();
        _drawMask = DrawMask;
        _drawPicture = DrawPicture;
    }

    protected override void DisposeBehavior()
    {
        _masks.Dispose();
        _rainPicture?.Dispose();
        foreach (var target in _retired)
        {
            target.Dispose();
        }

        _retired.Clear();
        foreach (var shader in _shaders.Values)
        {
            shader.Dispose();
        }

        _shaders.Clear();
        _blurH.Dispose();
        _blurV.Dispose();
        base.DisposeBehavior();
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_wolfWeather.Quality <= 0 || args.MapId == MapId.Nullspace)
            return;

        // A level below the view gets its weather in its own pass, under that level's blur, but only the weather the
        // viewer stands under: never the ground seen from a cavern, never a level above.
        var mapUid = _map.GetMapOrInvalid(args.MapId);
        var zEye = args.Viewport.Eye as ScalingViewport.ZEye;
        var lower = zEye is { Primary: false };
        if (lower && (zEye!.Depth >= 0f || _transitQuery.HasComp(mapUid)))
            return;

        if (!_wolfWeather.TryGetWeatherMap(mapUid, out var resolved)
            || resolved.Comp.Weathers.Count == 0
            || !_wolfWeather.TryGetState(resolved.Owner, out var state))
            return;

        if (lower && (_player.LocalEntity is not { } viewer
                      || !_wolfWeather.TryGetSkyWeather(viewer, out var sky) || sky.Owner != resolved.Owner))
            return;

        NextFrame();

        // On a story a gap shows the level below, which has the rain falling through it, so the gap stays dry here.
        var holes = mapUid != resolved.Owner;
        var mask = GetMask(args, holes, lower);
        var handle = args.WorldHandle;
        handle.SetTransform(Matrix3x2.Identity);

        var now = _timing.CurTime;
        var time = (float) (_timing.RealTime.TotalSeconds % SplashPeriod);
        var bounds = holes && mask.Open is { } open ? args.WorldAABB.Intersect(open.Enlarged(1f)) : args.WorldAABB;
        if (bounds.IsEmpty())
            return;

        // First person stands the falling layers up in front of the eye itself, down through the levels below too.
        // Splashes stay on the ground here.
        var firstPerson = _camera.FirstPerson;
        if (firstPerson && _camera.IsMainPass(args))
            _wolfWeather.SetViewMask(mask.Blurred!.Texture, mask.Rect, args.MapId);
        else if (!firstPerson)
            DrawWeathers(handle, args.MapId, resolved.Comp, state, mask, bounds, now, time, false, holes, lower);

        // The 2.5D views stand walls up over the floor's rain, so the solids under the sky take it from a picture of
        // the falling layers with no cover.
        if (_camera.Active && !firstPerson && _camera.IsMainPass(args))
            DrawRainPicture(args, resolved.Comp, state, mask, now, time);

        if (_wolfWeather.Quality >= 2)
            DrawWeathers(handle, args.MapId, resolved.Comp, state, mask, bounds, now, time, true, holes, lower);

        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    /// <summary>On the first draw of a frame, drops the shaders and masks the last frames didn't use.</summary>
    private void NextFrame()
    {
        var frame = _timing.CurFrame;
        if (frame == _frame)
            return;

        _frame = frame;
        _lowerRebuilt = false;
        foreach (var target in _retired)
        {
            target.Dispose();
        }

        _retired.Clear();
        DropUnusedShaders();
        _usedShaders.Clear();
    }

    /// <summary>Draws the falling layers over the whole view, with no cover, into the picture the 2.5D solids read.</summary>
    private void DrawRainPicture(in OverlayDrawArgs args, WolfWeatherMapComponent weather, WolfWeatherMapState state,
        MaskEntry mask, TimeSpan now, float time)
    {
        var size = (Vector2i) args.Viewport.Size;
        if (size.X <= 0 || size.Y <= 0)
            return;

        if (_rainPicture == null || _rainPicture.Size != size)
        {
            // A lower level's pass may still hold the old one this frame, so it goes at the next.
            if (_rainPicture != null)
                _retired.Add(_rainPicture);

            _rainPicture = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = false },
                "wolfweather-rain-picture");
        }

        _pictureHandle = args.WorldHandle;
        _pictureMap = args.MapId;
        _pictureBounds = args.WorldAABB;
        _pictureWeather = weather;
        _pictureState = state;
        _pictureMask = mask;
        _pictureNow = now;
        _pictureTime = time;
        _pictureMatrix = args.Viewport.GetWorldToLocalMatrix();
        args.WorldHandle.RenderInRenderTarget(_rainPicture, _drawPicture, new Color(0f, 0f, 0f, 0f));
        _wolfWeather.SetRainPicture(_rainPicture.Texture);
    }

    private void DrawPicture()
    {
        var handle = _pictureHandle;
        handle.SetTransform(_pictureMatrix);
        DrawWeathers(handle, _pictureMap, _pictureWeather, _pictureState, _pictureMask, _pictureBounds, _pictureNow,
            _pictureTime, false, false, false, true);
        handle.UseShader(null);
        handle.SetTransform(Matrix3x2.Identity);
    }

    private void DrawWeathers(DrawingHandleWorld handle, MapId mapId, WolfWeatherMapComponent weather,
        WolfWeatherMapState state, MaskEntry mask, Box2 bounds, TimeSpan now, float time, bool splashes, bool holes,
        bool lower, bool picture = false)
    {
        foreach (var entry in weather.Weathers)
        {
            if (!_proto.TryIndex(entry.Weather, out var proto)
                || !state.Offsets.TryGetValue((entry.Weather, entry.Start), out var offsets))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            if (strength <= 0f)
                continue;

            var layers = proto.FinalLayers;
            for (var i = 0; i < layers.Count && i < offsets.X.Length; i++)
            {
                var layer = layers[i];
                if (layer.MinQuality > _wolfWeather.Quality || (layer.Kind == WolfWeatherLayerKind.Splash) != splashes)
                    continue;

                // Fog hangs round the viewer only, so a level below doesn't add a second layer of it.
                var fog = layer.Kind == WolfWeatherLayerKind.Fog;
                if (fog && lower)
                    continue;

                // The picture is drawn without fog and with shaders of its own, as a shader reads its values when drawn.
                if (fog && picture)
                    continue;

                var shader = GetShader(new ShaderKey(entry.Weather, entry.Start, i, mapId, picture), layer,
                    entry.Seed + i * 31);
                SetMovingParameters(shader, bounds, layer, state.Wind, _wolfWeather.Down, offsets.X[i], offsets.Y[i], strength, time);
                shader.SetParameter("invert", picture ? 3f : splashes ? 1f : holes && !fog ? 2f : 0f);
                shader.SetParameter("mask", mask.Blurred!.Texture);
                shader.SetParameter("maskOffset", bounds.BottomLeft - mask.Rect.BottomLeft);
                shader.SetParameter("maskSize", mask.Rect.Size);
                handle.UseShader(shader);
                handle.DrawRect(bounds, Color.White);
            }
        }
    }

    /// <summary>A layer's shader on one map, made with its fixed parameters the first time the layer draws there.</summary>
    private ShaderInstance GetShader(ShaderKey key, WolfWeatherLayer layer, int seed)
    {
        _usedShaders.Add(key);
        if (_shaders.TryGetValue(key, out var shader))
            return shader;

        shader = _proto.Index(WeatherShader).InstanceUnique();
        var spacing = Spacing(layer);
        shader.SetParameter("spacing", spacing);
        shader.SetParameter("along", Along(layer, spacing));
        shader.SetParameter("streak", layer.Length);
        shader.SetParameter("thick", layer.Width);
        shader.SetParameter("wobble", layer.Wobble);
        shader.SetParameter("rate", MathF.Max(layer.Rate, 0.01f));
        shader.SetParameter("soft", layer.Softness);
        shader.SetParameter("mode", (float) layer.Kind);
        shader.SetParameter("seed", (float) seed);
        _shaders[key] = shader;
        return shader;
    }

    private void DropUnusedShaders()
    {
        if (_shaders.Count == _usedShaders.Count)
            return;

        _unusedShaders.Clear();
        foreach (var key in _shaders.Keys)
        {
            if (!_usedShaders.Contains(key))
                _unusedShaders.Add(key);
        }

        foreach (var key in _unusedShaders)
        {
            _shaders.Remove(key, out var shader);
            shader?.Dispose();
        }
    }

    private static void SetMovingParameters(ShaderInstance shader, Box2 bounds, WolfWeatherLayer layer, Vector2 wind, Vector2 down, double offsetX, double offsetY, float strength, float time)
    {
        var velocity = WolfWeatherSystem.GetVelocity(layer, wind, down);
        var axisA = layer.Kind == WolfWeatherLayerKind.Fog ? new Vector2(0f, 1f) : down;
        var axisC = new Vector2(-axisA.Y, axisA.X);
        var heading = velocity.LengthSquared() < 0.0001f ? axisA : Vector2.Normalize(velocity);
        var spacing = Spacing(layer);
        var along = Along(layer, spacing);

        var originX = Math.Floor(bounds.Left * PixelsPerTile) / PixelsPerTile;
        var originY = Math.Floor(bounds.Bottom * PixelsPerTile) / PixelsPerTile;
        var qx = originX - offsetX;
        var qy = originY - offsetY;
        var u0 = Wrap((qx * axisC.X + qy * axisC.Y) / spacing);
        var v0 = Wrap((qx * axisA.X + qy * axisA.Y) / along);

        var density = layer.Kind is WolfWeatherLayerKind.Splash or WolfWeatherLayerKind.Fog ? layer.Density : layer.Density * strength;

        shader.SetParameter("size", bounds.Size);
        shader.SetParameter("fracOrigin", new Vector2((float) (bounds.Left - originX), (float) (bounds.Bottom - originY)));
        shader.SetParameter("axisA", axisA);
        shader.SetParameter("axisC", axisC);
        shader.SetParameter("cell0", new Vector2((float) u0, (float) v0));
        shader.SetParameter("density", density);
        shader.SetParameter("time", time);
        shader.SetParameter("tint", layer.Color.WithAlpha(layer.Color.A * strength));
        shader.SetParameter("dir", new Vector2(Vector2.Dot(heading, axisC), Vector2.Dot(heading, axisA)));
    }

    private static float Spacing(WolfWeatherLayer layer)
    {
        return MathF.Max(layer.Spacing, 1f / PixelsPerTile);
    }

    private static float Along(WolfWeatherLayer layer, float spacing)
    {
        return layer.Kind == WolfWeatherLayerKind.Rain ? MathF.Max(spacing, layer.Length / PixelsPerTile * 2f) : spacing;
    }

    private static double Wrap(double value)
    {
        var r = value % CellPeriod;
        return r < 0 ? r + CellPeriod : r;
    }

    /// <summary>
    /// The viewport's world-space cover mask of the drawn map, redrawn only when the view leaves it, the map's tiles
    /// change or it ages. A level below draws its mask coarser, and at most one of them redraws a frame.
    /// </summary>
    private MaskEntry GetMask(in OverlayDrawArgs args, bool holes, bool lower)
    {
        var set = _masks.GetForViewport(args.Viewport, static _ => new MaskSet());
        var real = _timing.RealTime;
        PruneMasks(set, real);
        if (!set.Entries.TryGetValue(args.MapId, out var mask))
        {
            mask = new MaskEntry();
            set.Entries[args.MapId] = mask;
        }

        mask.LastUsed = real;
        var version = _wolfWeather.MaskVersionOf(args.MapId);
        var view = args.WorldAABB.Enlarged(MaskMargin * 0.5f);
        var usable = mask.Blurred != null
                     && mask.Holes == holes
                     && mask.Rect.Contains(view.BottomLeft)
                     && mask.Rect.Contains(view.TopRight);
        if (usable
            && (mask.Version == version || real < mask.BuiltAt + MaskMinAge)
            && real < mask.BuiltAt + MaskRefresh)
            return mask;

        if (usable && lower && _lowerRebuilt)
            return mask;

        _lowerRebuilt |= lower;
        var outer = args.WorldAABB.Enlarged(MaskMargin);
        var rect = new Box2(
            MathF.Floor(outer.Left / MaskSnap) * MaskSnap,
            MathF.Floor(outer.Bottom / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Right / MaskSnap) * MaskSnap,
            MathF.Ceiling(outer.Top / MaskSnap) * MaskSnap);

        var density = lower ? MaskTexelsPerTile * LowerMaskShare : MaskTexelsPerTile;
        var texels = MathF.Min(density, MaxMaskSize / MathF.Max(rect.Width, rect.Height));
        var size = new Vector2i((int) MathF.Ceiling(rect.Width * texels), (int) MathF.Ceiling(rect.Height * texels));

        EnsureTarget(ref mask.Raw, size, "wolfweather-mask-raw");
        EnsureTarget(ref mask.Across, size, "wolfweather-mask-across");
        EnsureTarget(ref mask.Blurred, size, "wolfweather-mask");

        mask.Holes = holes;
        mask.Rect = rect;
        mask.Version = version;
        mask.BuiltAt = real;

        var handle = args.WorldHandle;
        _maskHandle = handle;
        _maskMapId = args.MapId;
        _maskRect = rect;
        _maskHoles = holes;
        _maskOpen = null;
        _maskMatrix = Matrix3x2.CreateTranslation(-rect.Left, -rect.Top) * Matrix3x2.CreateScale(texels, -texels);
        handle.RenderInRenderTarget(mask.Raw!, _drawMask, ClearColor);
        mask.Open = _maskOpen;

        var radius = new Vector3(Feather, HardFeather, Feather) * texels;
        Blur(handle, mask.Raw!, mask.Across!, _blurH, new Vector2(1f / size.X, 0f), radius);
        Blur(handle, mask.Across!, mask.Blurred!, _blurV, new Vector2(0f, 1f / size.Y), radius);
        return mask;
    }

    /// <summary>Drops the masks of maps this viewport hasn't drawn for a while.</summary>
    private void PruneMasks(MaskSet set, TimeSpan real)
    {
        _staleMasks.Clear();
        foreach (var (map, entry) in set.Entries)
        {
            if (real > entry.LastUsed + MaskKeep)
                _staleMasks.Add(map);
        }

        foreach (var map in _staleMasks)
        {
            set.Entries.Remove(map, out var entry);
            if (entry?.Blurred != null)
                _wolfWeather.ForgetViewMask(entry.Blurred.Texture);

            entry?.Dispose();
        }
    }

    private void EnsureTarget(ref IRenderTexture? target, Vector2i size, string name)
    {
        if (target != null && target.Texture.Size == size)
            return;

        if (target != null)
            _wolfWeather.ForgetViewMask(target.Texture);

        target?.Dispose();
        target = _clyde.CreateRenderTarget(size,
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            name);
    }

    private static void Blur(DrawingHandleWorld handle, IRenderTexture from, IRenderTexture to, ShaderInstance shader, Vector2 step, Vector3 radius)
    {
        shader.SetParameter("texelStep", step);
        shader.SetParameter("radius", radius);
        handle.RenderInRenderTarget(to,
            () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.UseShader(shader);
                handle.DrawTextureRect(from.Texture, Box2.FromDimensions(Vector2.Zero, (Vector2) to.Size));
                handle.UseShader(null);
            },
            ClearColor);
    }

    /// <summary>Draws the mask. Red is cover that fades softly, green is walls that cut hard, blue is open ground.</summary>
    private void DrawMask()
    {
        var handle = _maskHandle;
        handle.UseShader(_unshaded);
        _grids.Clear();
        _map.FindGridsIntersecting(_maskMapId, _maskRect, ref _grids);

        // Grids that are always under cover, such as a landed vessel, go last so their tiles win over the ground beneath.
        for (var pass = 0; pass < 2; pass++)
        {
            foreach (var grid in _grids)
            {
                if (_wolfWeather.IsCoverGrid(grid.Owner) == (pass == 0))
                    continue;

                DrawMaskGrid(handle, grid);
            }
        }
    }

    private void DrawMaskGrid(DrawingHandleWorld handle, Entity<MapGridComponent> grid)
    {
        handle.SetTransform(Matrix3x2.Multiply(_xform.GetWorldMatrix(grid.Owner), _maskMatrix));
        _roofQuery.TryComp(grid.Owner, out var roof);
        var roofed = new Entity<MapGridComponent, RoofComponent?>(grid.Owner, grid.Comp, roof);
        _rects.Clear();
        _diagonalTiles.Clear();

        Box2? open = null;
        var tiles = _map.GetTilesIntersecting(grid.Owner, grid, _maskRect);
        while (tiles.MoveNext(out var tile))
        {
            if (MaskColor(roofed, tile) is not { } color)
                continue;

            var bounds = _lookup.GetLocalBounds(tile, grid.Comp.TileSize);
            _rects.Add(new WorldRect(bounds, color));
            if (color == Open)
                open = open?.Union(bounds) ?? bounds;
        }

        if (open is { } local)
        {
            var world = _xform.GetWorldMatrix(grid.Owner).TransformBox(local);
            _maskOpen = _maskOpen?.Union(world) ?? world;
        }

        handle.DrawRectsUnmodulated(CollectionsMarshal.AsSpan(_rects));
        MaskDiagonals(handle, grid);
    }

    /// <summary>
    /// Open ground in blue, walls in green, other cover in red. Diagonal edge tiles are left for MaskDiagonals. With
    /// holes on, a see-through tile counts as a gap, as the level under it shows there.
    /// </summary>
    private Color? MaskColor(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        if (tile.Tile.IsEmpty)
            return null;

        if (_maskHoles && _tileDefs[tile.Tile.TypeId] is ContentTileDefinition def && (def.MapAtmosphere || def.Transparent))
            return null;

        var flags = _wolfWeather.Classify(grid, tile);
        if ((flags & WolfWeatherTile.Exposed) != 0)
            return Open;

        if ((flags & WolfWeatherTile.ExposedPastDiagonals) != 0 && BordersExposed(grid, tile))
        {
            _diagonalTiles.Add(tile.GridIndices);
            return null;
        }

        // Rain falls in front of a wall, door or window under open sky or on a building's outer edge.
        if ((flags & WolfWeatherTile.Blocker) != 0
            && ((flags & WolfWeatherTile.OpenSky) != 0 || BordersExposed(grid, tile)))
            return OpenWall;

        return (flags & WolfWeatherTile.Blocker) != 0 ? Hard : Soft;
    }

    private bool BordersExposed(Entity<MapGridComponent, RoofComponent?> grid, TileRef tile)
    {
        foreach (var dir in Cardinals)
        {
            if (_wolfWeather.IsExposed(grid, _map.GetTileRef(grid.Owner, grid.Comp1, tile.GridIndices + dir)))
                return true;
        }

        return false;
    }

    /// <summary>Adds the wall half of each diagonal blocker on the grid's collected edge tiles to the mask.</summary>
    private void MaskDiagonals(DrawingHandleWorld handle, Entity<MapGridComponent> grid)
    {
        foreach (var indices in _diagonalTiles)
        {
            var size = grid.Comp.TileSize;
            var sw = (Vector2) indices * size;
            var se = sw + new Vector2(size, 0);
            var ne = sw + new Vector2(size, size);
            var nw = sw + new Vector2(0, size);

            var anchored = _map.GetAnchoredEntities(grid.Owner, grid.Comp, indices);
            while (anchored.MoveNext(out var ent))
            {
                if (!_blockQuery.HasComp(ent.Value) || !_tag.HasTag(ent.Value, SharedWolfWeatherSystem.DiagonalTag))
                    continue;

                switch (_xformQuery.GetComponent(ent.Value).LocalRotation.GetCardinalDir())
                {
                    case Direction.South: _wallHalves.Add(sw); _wallHalves.Add(se); _wallHalves.Add(ne); break;
                    case Direction.East: _wallHalves.Add(se); _wallHalves.Add(ne); _wallHalves.Add(nw); break;
                    case Direction.North: _wallHalves.Add(ne); _wallHalves.Add(nw); _wallHalves.Add(sw); break;
                    case Direction.West: _wallHalves.Add(nw); _wallHalves.Add(sw); _wallHalves.Add(se); break;
                }
            }
        }

        if (_wallHalves.Count > 0)
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _wallHalves, Hard);

        _wallHalves.Clear();
        _diagonalTiles.Clear();
    }

    /// <summary>A viewport's cover masks, one for each map it draws weather on.</summary>
    private sealed class MaskSet : IDisposable
    {
        public readonly Dictionary<MapId, MaskEntry> Entries = new();

        public void Dispose()
        {
            foreach (var entry in Entries.Values)
            {
                entry.Dispose();
            }

            Entries.Clear();
        }
    }

    /// <summary>One map's cover mask, the world box it covers, and the box of its open ground.</summary>
    private sealed class MaskEntry : IDisposable
    {
        public IRenderTexture? Raw;
        public IRenderTexture? Across;
        public IRenderTexture? Blurred;
        public Box2 Rect;
        public Box2? Open;
        public bool Holes;
        public int Version;
        public TimeSpan BuiltAt;
        public TimeSpan LastUsed;

        public void Dispose()
        {
            Raw?.Dispose();
            Across?.Dispose();
            Blurred?.Dispose();
        }
    }

    private readonly record struct ShaderKey(ProtoId<WolfWeatherPrototype> Weather, TimeSpan Start, int Layer, MapId Map,
        bool Picture);
}
