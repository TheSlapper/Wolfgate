using System.Numerics;
using Content.Client._WG.CameraModes.Atlas;
using Content.Client._WG.CameraModes.Fold;
using Content.Client._WG.Vehicles.Turret;
using Content.Client.Parallax;
using Content.Shared._WG.CameraModes;
using Content.Shared._WG.Vehicles.Turret;
using Content.Shared.Maps;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Client.Utility;
using Robust.Shared.Utility;
using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.CameraModes.Render;

/// <summary>
/// Draws the raised objects of a 2.5D view as meshes over the stock floor, far to near. Each pixel takes its light and
/// line of sight from the floor point it stands on, so nothing shows that the flat view would hide.
/// </summary>
public sealed partial class CameraModeMeshOverlay : Overlay
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private ILightManager _lights = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    private static readonly ProtoId<ShaderPrototype> MeshShader = "WGCameraModeMesh";
    private static readonly ProtoId<ShaderPrototype> ClearShader = "WGCameraModeClear";
    private static readonly ProtoId<ShaderPrototype> PerspectiveShader = "WGCameraModePerspective";
    private static readonly ProtoId<ShaderPrototype> FadeShader = "WGCameraModeFade";
    private static readonly ProtoId<ShaderPrototype> FirstPersonShader = "WGCameraModeFirstPerson";
    private static readonly ProtoId<ShaderPrototype> SkyShader = "WGCameraModeSky";
    private static readonly ProtoId<ShaderPrototype> Unshaded = "unshaded";

    /// <summary>The layer every computer draws its keyboard in.</summary>
    private const string KeyboardLayer = "computerLayerKeyboard";

    /// <summary>Vertical field of view of the perspective camera.</summary>
    private static readonly float FieldOfView = MathHelper.DegreesToRadians(32f);

    /// <summary>Quads per draw call. The engine's index batch holds about 13,600.</summary>
    private const int MaxQuads = 8192;

    /// <summary>Lowest and highest a low wall stands, in tiles, whatever its drawn front.</summary>
    private const float LowWallMin = 0.15f;
    private const float LowWallMax = 0.6f;

    /// <summary>Marks over entities in first person: nearest depth shown, size limits, own marks' screen height.</summary>
    private const float MarkNearest = 0.3f;
    private const float MarkSmallest = 0.3f;
    private const float MarkLargest = 1.5f;
    private const float OwnMarkHeight = 0.62f;
    private const float Nudge = 0.2f;

    /// <summary>Darkness of the middle of the shadow under a solid.</summary>
    private const float ShadowAlpha = 0.4f;

    /// <summary>How far the shadow reaches past the base of a solid, as a share of the base and in tiles.</summary>
    private const float ShadowSpread = 1.15f;
    private const float ShadowEdge = 0.1f;

    /// <summary>Share of the shadow that is evenly dark before it fades out.</summary>
    private const float ShadowCore = 0.55f;
    private const int ShadowSides = 10;

    /// <summary>Brightness of a standing face where it meets the floor, and the height in tiles where that ends.</summary>
    private const float BaseShade = 0.72f;
    private const float BaseRise = 0.4f;
    private const float PixelsPerTile = 32f;

    /// <summary>Size and strength of the weather drawn across a planet sky in first person.</summary>
    private const float WeatherScale = 2f;
    private const float WeatherAlpha = 0.6f;

    /// <summary>Nearest distance ahead of the first-person eye that is drawn, in tiles.</summary>
    private const float Near = 0.1f;

    /// <summary>How much deeper the far end of a retro quad may be than its near end before it is split.</summary>
    private const float DepthRatio = 1.15f;
    private const int MaxSplit = 8;

    /// <summary>
    /// How much larger than the screen the clean first-person look draws its meshes. The engine snaps each vertex to a
    /// whole pixel, which then is a fraction of a screen pixel.
    /// </summary>
    private const float CleanScale = 2f;

    /// <summary>Screen pixels to one pixel of the retro look.</summary>
    private const float RetroPixel = 3f;

    /// <summary>The camera height the faces of a first-person solid are ordered for, as a sine and cosine.</summary>
    private const float OrderSin = 0.2f;
    private const float OrderCos = 0.98f;

    /// <summary>Height of furniture a body lies down on, in tiles.</summary>
    private const float LieHeight = 0.3f;

    /// <summary>Height of floor fixtures nothing bumps into, like vents: just above the floor.</summary>
    private const float DecalHeight = 0.25f / EyeManager.PixelsPerMeter;

    /// <summary>Width and depth of hard collision box top-down art needs to lie as a round body, in tiles.</summary>
    private const float LyingFill = 0.6f;

    private readonly CameraModesSystem _system;
    private readonly SpriteAtlas _atlas;
    private readonly SpriteSystem _sprite;
    private readonly ShaderInstance _shader;
    private readonly ShaderInstance _shaderSpare;
    private readonly ShaderInstance[] _meshShaders;
    private readonly ShaderInstance _clear;
    private readonly ShaderInstance _blit;
    private readonly ShaderInstance _fade;
    private readonly ShaderInstance _firstBlit;
    private readonly ShaderInstance _skyShader;
    private readonly ParallaxSystem _parallax;
    private IRenderTexture? _skyTarget;
    private IRenderTexture? _meshTarget;
    private IRenderTexture? _fadeTarget;
    private readonly Dictionary<int, IRenderTexture> _staging = new();
    private readonly ushort[] _indices = new ushort[MaxQuads * 6];
    private readonly List<(SpriteComponent.Layer Layer, Color Color)> _dimmed = new();

    private DrawVertexUV2DColor[] _vertices = new DrawVertexUV2DColor[4096];
    private int _count;
    private DrawVertexUV2DColor[] _shadows = new DrawVertexUV2DColor[1024];
    private int _shadowCount;

    // Per frame.
    private Matrix3x2 _worldToLocal;
    private Vector2 _viewSize;
    private Vector2 _up;
    private Vector2 _right;
    private CameraModeProfile _profile;
    private bool _perspective;
    private float _lambda;
    private float _sigma;
    private float _pixelsPerTile;
    private bool _first;
    private Vector2 _eye;
    private float _focal;
    private float _pitchSin;
    private float _pitchCos;
    private Vector2 _shadeRight;
    private EntityUid? _self;
    private bool _shadowCut;
    private bool _retro;
    private bool _meshFilter;
    private Vector2 _tileUv;

    // The eye in the stock picture, and two rows that take a picture offset to tiles across and ahead of the look.
    private Vector2 _lookUv;
    private Vector2 _lookAcross;
    private Vector2 _lookAhead;
    private readonly SharedMapSystem _mapSystem;
    private readonly SharedTransformSystem _transformSystem;
    private readonly VehicleTurretVisualSystem _turretVisuals;
    private readonly CameraModeBackdropOverlay _backdrop;

    public override OverlaySpace Space => OverlaySpace.WorldSpace;

    public override bool RequestScreenTexture => _system.Mode is CameraMode.Perspective or CameraMode.FirstPerson;

    /// <summary>The vertices drawn this frame, for click tests. Only the first <see cref="VertexCount"/> are valid.</summary>
    public DrawVertexUV2DColor[] Vertices => _vertices;

    /// <summary>How many of <see cref="Vertices"/> were drawn this frame.</summary>
    public int VertexCount => _count;

    /// <summary>The perspective warp's lambda for this view size. See <see cref="Warp"/>.</summary>
    public float WarpLambda => _lambda;

    /// <summary>Pixels per tile in the player's own pass, last drawn.</summary>
    public float MainPixelsPerTile { get; private set; } = EyeManager.PixelsPerMeter;

    /// <summary>
    /// Set while a level below is drawn into its pass under perspective: the pass's centre, and how much the pass
    /// shrinks that level's floor. Each vertex then moves to where the warp shows its own height.
    /// </summary>
    private bool _lowerWarp;
    private Vector2 _lowerCentre;
    private float _lowerDrop;
    private float _lowerShrink;

    public CameraModeMeshOverlay(CameraModesSystem system, SpriteAtlas atlas, CameraModeBackdropOverlay backdrop)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _atlas = atlas;
        _backdrop = backdrop;
        _sprite = _entities.System<SpriteSystem>();
        _shader = _prototypes.Index(MeshShader).InstanceUnique();
        _shaderSpare = _prototypes.Index(MeshShader).InstanceUnique();
        _meshShaders = new ShaderInstance[2 + CameraModesSystem.MaxLowerLevels];
        _meshShaders[0] = _shader;
        _meshShaders[1] = _shaderSpare;
        for (var i = 2; i < _meshShaders.Length; i++)
        {
            _meshShaders[i] = _prototypes.Index(MeshShader).InstanceUnique();
        }

        _clear = _prototypes.Index(ClearShader).Instance();
        _blit = _prototypes.Index(PerspectiveShader).InstanceUnique();
        _fade = _prototypes.Index(FadeShader).InstanceUnique();
        _firstBlit = _prototypes.Index(FirstPersonShader).InstanceUnique();
        _skyShader = _prototypes.Index(SkyShader).InstanceUnique();
        _parallax = _entities.System<ParallaxSystem>();
        _mapSystem = _entities.System<SharedMapSystem>();
        _transformSystem = _entities.System<SharedTransformSystem>();
        _turretVisuals = _entities.System<VehicleTurretVisualSystem>();
        // Under the post-process overlays, over everything else in world space.
        ZIndex = -20;

        for (var q = 0; q < MaxQuads; q++)
        {
            var v = (ushort) (q * 4);
            var i = q * 6;
            _indices[i] = v;
            _indices[i + 1] = (ushort) (v + 1);
            _indices[i + 2] = (ushort) (v + 2);
            _indices[i + 3] = v;
            _indices[i + 4] = (ushort) (v + 2);
            _indices[i + 5] = (ushort) (v + 3);
        }
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // Skips the screen copy on passes the view doesn't draw in.
        return _system.IsMainPass(args) || _system.IsLowerPass(args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        if (_system.IsMainPass(args))
            DrawView(args);
        else if (_system.IsLowerPass(args))
            DrawLowerPass(args);
        else
            return;

        _system.AddTime(1, start);
    }

    /// <summary>
    /// Draws what stands on a level below inside that level's own pass in the 2.5D views, flat-projected like the
    /// level's floor, so the stock blur and the floors of nearer levels fall on it as on the rest of the level.
    /// </summary>
    private void DrawLowerPass(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        PrepareView(args, true);
        _count = 0;
        _shadowCount = 0;
        _lowerWarp = _system.Mode == CameraMode.Perspective;
        _lowerCentre = args.Viewport.LocalToWorld(_viewSize / 2f).Position;
        foreach (var obj in _system.Drawn)
        {
            if (obj.Level >= 0 || obj.LevelMap != args.MapUid || obj.Slot is not { } slot)
                continue;

            _lowerDrop = obj.Drop;
            _lowerShrink = _system.LowerShrink(obj.Drop);
            if (!obj.Billboard && obj.Mesh is { Fixed: true } mesh)
                AddMesh(obj, slot, mesh);
            else
                AddBillboard(obj, slot);
        }

        _lowerWarp = false;
        if (_count == 0)
            return;

        SetMeshParameters(args);
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        DrawRange(handle, 0, _count);
        handle.UseShader(null);
    }

    private void DrawView(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        DrawAtlas(handle, args.RenderHandle);
        RequestAnalysis(handle, args.RenderHandle);
        PrepareView(args, false);
        MainPixelsPerTile = _pixelsPerTile;
        PrepareWeather(args);
        Build();
        if (_count == 0 && !_perspective)
            return;

        SetMeshParameters(args);

        // First person hides what stands below under its own solid floor, so the floor's mask is drawn first.
        var mask = _first ? DrawMask(handle, args.Viewport.RenderTarget.Size, args) : null;
        if (mask != null)
        {
            SetLowerObjects(mask);
            DrawRainFloor(handle, mask);
        }

        DrawFogField(handle);

        DrawMeshes(handle, args, mask);
    }

    /// <summary>
    /// Reads the view's frame for this pass. A level below in the 2.5D views is drawn flat into its pass, as the stock
    /// picture holds its floor, and the perspective warp then takes it with that picture.
    /// </summary>
    private void PrepareView(in OverlayDrawArgs args, bool lower)
    {
        _worldToLocal = args.Viewport.GetWorldToLocalMatrix();
        _viewSize = args.Viewport.Size;
        _profile = _system.Profile;
        _first = !lower && _system.FirstPerson;
        // First person projects along where it looks. The stock picture under it only turns in quarters.
        _up = _first ? _system.LookUp : _system.Up;
        _right = _first ? _system.LookRight : _system.Right;
        // Both views hold their vertices in viewport pixels and draw the floor in a pass of their own.
        _perspective = !lower && (_first || _system.Mode == CameraMode.Perspective);
        _eye = _system.ViewCentre;
        _focal = _system.Focal(_viewSize.X);
        (_pitchSin, _pitchCos) = MathF.SinCos(_system.Pitch);
        // First-person faces keep their shade as the view turns.
        _shadeRight = _first ? Vector2.UnitX : _right;
        _self = _first ? _system.LocalBody : null;
        _retro = _first && _system.Retro;
        _pixelsPerTile = (Vector2.Transform(_right, _worldToLocal) - Vector2.Transform(Vector2.Zero, _worldToLocal)).Length();
        _tileUv = new Vector2(_pixelsPerTile / _viewSize.X, _pixelsPerTile / _viewSize.Y);
        (_lambda, _sigma) = Warp(_profile, _viewSize);
        // Picture UV to tiles across and ahead of the look, as two rows: the inverse of the look's axes in UV.
        _lookUv = ScreenUv(_eye);
        var axisR = ScreenUv(_eye + _right) - _lookUv;
        var axisF = ScreenUv(_eye + _up) - _lookUv;
        var det = axisR.X * axisF.Y - axisF.X * axisR.Y;
        _lookAcross = MathF.Abs(det) > 1e-9f ? new Vector2(axisF.Y, -axisF.X) / det : Vector2.Zero;
        _lookAhead = MathF.Abs(det) > 1e-9f ? new Vector2(-axisR.Y, axisR.X) / det : Vector2.Zero;
    }

    /// <summary>Gives every mesh shader the pass's light map, view and look.</summary>
    private void SetMeshParameters(in OverlayDrawArgs args)
    {
        var lit = _lights.Enabled
                  && args.Viewport.Eye is { DrawLight: true }
                  && _entities.TryGetComponent(args.MapUid, out MapComponent? map)
                  && map.LightingEnabled;
        var fov = lit && _lights.DrawHardFov && args.Viewport.Eye is { DrawFov: true };

        // Screen UV to world, as two rows of an affine map.
        var w00 = args.Viewport.LocalToWorld(new Vector2(0, _viewSize.Y)).Position;
        var w10 = args.Viewport.LocalToWorld(new Vector2(_viewSize.X, _viewSize.Y)).Position;
        var w01 = args.Viewport.LocalToWorld(Vector2.Zero).Position;
        // The spare instance (see DrawWithSheets) and the shaders for the levels below take the same values.
        // In first person, SetLowerObjects then gives the levels below their own.
        foreach (var shader in _meshShaders)
        {
            shader.SetParameter("LOWER", 0f);
            shader.SetParameter("WORLD_ROW_X", new Vector3(w10.X - w00.X, w01.X - w00.X, w00.X));
            shader.SetParameter("WORLD_ROW_Y", new Vector3(w10.Y - w00.Y, w01.Y - w00.Y, w00.Y));
            shader.SetParameter("EYE_POS", args.Viewport.Eye?.Position.Position ?? Vector2.Zero);
            shader.SetParameter("LIGHT_TEXTURE", args.Viewport.LightRenderTarget.Texture);
            shader.SetParameter("FOV_TEXTURE", args.Viewport.FovRenderTarget.Texture);
            shader.SetParameter("USE_LIGHT", lit && !_system.DebugUnlit ? 1f : 0f);
            shader.SetParameter("USE_FOV", fov ? 1f : 0f);
            shader.SetParameter("DEPTH", _first && !_retro ? 1f : 0f);
            shader.SetParameter("EDGE", _first ? 1f : 0f);
            shader.SetParameter("TILE_UV", _tileUv);
            shader.SetParameter("EYE_UV", _lookUv);
            shader.SetParameter("LOOK_ACROSS", _lookAcross);
            shader.SetParameter("LOOK_AHEAD", _lookAhead);
            shader.SetParameter("LOOK_HALF", _system.LookHalf);
            SetFog(shader);
            SetRain(shader);
        }
    }

    /// <summary>Draws the built meshes, straight into the view or through the perspective warp.</summary>
    private void DrawMeshes(DrawingHandleWorld handle, in OverlayDrawArgs args, IRenderTexture? mask)
    {
        var faded = DrawFaded(handle, args.Viewport.RenderTarget.Size);
        if (!_perspective)
        {
            handle.SetTransform(Matrix3x2.Identity);
            DrawVertices(handle);
            if (faded)
                DrawFadeLayer(handle, args.WorldBounds);

            return;
        }

        // Perspective meshes go to their own target in screen pixels. A full-screen pass then warps the floor under
        // them, before the overlays that follow.
        if (ScreenTexture == null)
            return;

        var size = args.Viewport.RenderTarget.Size;
        var scale = !_first ? 1f : _retro ? 1f / RetroPixel : CleanScale;
        var filter = _first && !_retro;
        var meshSize = new Vector2i((int) MathF.Ceiling(size.X * scale), (int) MathF.Ceiling(size.Y * scale));
        if (_meshTarget == null || _meshTarget.Size != meshSize || _meshFilter != filter)
        {
            _meshTarget?.Dispose();
            _meshFilter = filter;
            _meshTarget = _clyde.CreateRenderTarget(meshSize,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = filter },
                nameof(CameraModeMeshOverlay));
        }

        handle.RenderInRenderTarget(_meshTarget, () =>
        {
            handle.SetTransform(Matrix3x2.CreateScale(scale));
            DrawVertices(handle);
            if (faded)
                DrawFadeLayer(handle, new Box2Rotated(new Box2(Vector2.Zero, size)));
        }, new Color(0, 0, 0, 0));

        var blit = _blit;
        if (_first)
        {
            blit = _firstBlit;
            var eyeUv = ScreenUv(_eye);
            var sky = DrawSky(handle, args).Texture;
            var backdrop = _backdrop.FirstPersonBackdrop;
            var ground = _system.FirstPersonBackdrop(args.MapUid) ? backdrop : null;
            var bodies = DrawBodies(handle, args);
            blit.SetParameter("SKY_TEXTURE", sky);
            blit.SetParameter("BODY_TEXTURE", bodies ?? sky);
            blit.SetParameter("BODY_ON", bodies != null ? 1f : 0f);
            SetNearBodies(blit, sky);
            blit.SetParameter("BACKDROP_TEXTURE", ground ?? backdrop ?? sky);
            blit.SetParameter("BACKDROP_ON", ground != null ? 1f : 0f);
            SetLowerLevels(blit, sky);
            // Where the view doesn't draw the levels below itself, a hole shows the stock picture of them, a block's
            // height down.
            var stockShift = Vector2.Zero;
            var stockScale = 1f;
            var stockLower = backdrop != null && !_system.TakesLevels
                             && _system.TryLevelBelow(args.MapUid, args.Viewport.Eye?.Rotation ?? Angle.Zero,
                                 out stockShift, out stockScale);
            blit.SetParameter("STOCK_LOWER_ON", stockLower ? 1f : 0f);
            blit.SetParameter("STOCK_LOWER_SHIFT", stockLower ? ScreenUv(_eye + stockShift) - eyeUv : Vector2.Zero);
            blit.SetParameter("STOCK_LOWER_SCALE", stockScale);
            blit.SetParameter("STOCK_LOWER_DEPTH", CameraModesSystem.BlockHeight);
            // The background is laid round a centre by angle: straight down, or the horizon to the world's north, which
            // stays put as the view turns. A quarter turn from the centre reaches the nearer edge of the picture, so
            // none of it is cut off.
            var reach = MathF.Min(_viewSize.X, _viewSize.Y) / 2f / MathF.Max(_pixelsPerTile, 1f);
            blit.SetParameter("BACKDROP_REACH", reach);
            if (_cfg.GetCVar(CameraModeCVars.BackdropAhead))
            {
                var up = Vector2.UnitY;
                var right = Vector2.UnitX;
                blit.SetParameter("BACKDROP_CENTRE", new Vector3(Vector2.Dot(up, _right), Vector2.Dot(up, _up), 0f));
                blit.SetParameter("BACKDROP_E1", new Vector3(Vector2.Dot(right, _right), Vector2.Dot(right, _up), 0f));
                blit.SetParameter("BACKDROP_E2", Vector3.UnitZ);
                blit.SetParameter("BACKDROP_U1", ScreenUv(_eye + right) - eyeUv);
                blit.SetParameter("BACKDROP_U2", ScreenUv(_eye + up) - eyeUv);
            }
            else
            {
                blit.SetParameter("BACKDROP_CENTRE", -Vector3.UnitZ);
                blit.SetParameter("BACKDROP_E1", Vector3.UnitX);
                blit.SetParameter("BACKDROP_E2", Vector3.UnitY);
                blit.SetParameter("BACKDROP_U1", ScreenUv(_eye + _right) - eyeUv);
                blit.SetParameter("BACKDROP_U2", ScreenUv(_eye + _up) - eyeUv);
            }
            blit.SetParameter("MASK_TEXTURE", (mask ?? DrawMask(handle, size, args)).Texture);
            blit.SetParameter("FOCAL", _focal);
            blit.SetParameter("PITCH", new Vector2(_pitchSin, _pitchCos));
            blit.SetParameter("EYE_HEIGHT", CameraModesSystem.EyeHeight);
            blit.SetParameter("EYE_UV", eyeUv);
            blit.SetParameter("AXIS_R", ScreenUv(_eye + _right) - eyeUv);
            blit.SetParameter("AXIS_F", ScreenUv(_eye + _up) - eyeUv);
            blit.SetParameter("TILE_UV", _tileUv);
            blit.SetParameter("LOOK_HALF", _system.LookHalf);
            SetFog(blit);
            blit.SetParameter("LOOK_RIGHT", _right);
            blit.SetParameter("LOOK_UP", _up);
            blit.SetParameter("OPEN", _system.OpenGround(args.MapUid) ? 1f : 0f);
            blit.SetParameter("ROOF", _system.CeilingHeight);
            // Far ground is lit by the map's light alone, as the stock picture it joins is already lit.
            var horizon = _system.HorizonOn && _system.HorizonTexture != null && _system.HorizonRise != null;
            var lit = _lights.Enabled && args.Viewport.Eye is { DrawLight: true }
                      && _entities.TryGetComponent(args.MapUid, out MapLightComponent? ambient)
                ? ambient.AmbientLightColor
                : Color.White;
            blit.SetParameter("HORIZON_ON", horizon ? 1f : 0f);
            blit.SetParameter("HORIZON_TEXTURE", horizon ? _system.HorizonTexture! : sky);
            blit.SetParameter("HORIZON_RISE", horizon ? _system.HorizonRise! : sky);
            blit.SetParameter("HORIZON_HIGH", CameraModesSystem.HorizonHigh);
            // A texture filled from a buffer holds its first row at the top, so world north reads down the picture.
            var inPicture = (_eye - _system.HorizonCentre) / _system.HorizonTiles;
            blit.SetParameter("HORIZON_EYE", new Vector2(0.5f + inPicture.X, 0.5f - inPicture.Y));
            blit.SetParameter("HORIZON_SPAN", new Vector2(_system.HorizonTiles, -_system.HorizonTiles));
            blit.SetParameter("HORIZON_LIGHT", new Vector3(lit.R, lit.G, lit.B));
            blit.SetParameter("HORIZON_HAZE", _system.HorizonHaze(args.MapUid));
            blit.SetParameter("HORIZON_REACH", _system.HorizonReach(args.MapUid));
            blit.SetParameter("PIXEL", _retro ? RetroPixel : 1f);
            blit.SetParameter("RETRO", _retro ? 1f : 0f);
        }
        else
        {
            blit.SetParameter("LAMBDA", _lambda);
            blit.SetParameter("SIGMA", _sigma);
        }

        blit.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        blit.SetParameter("MESH_TEXTURE", _meshTarget.Texture);
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(blit);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);
    }

    private void DrawVertices(DrawingHandleWorld handle)
    {
        handle.UseShader(null);
        for (var start = 0; start < _shadowCount; start += MaxQuads * 4)
        {
            var count = Math.Min(MaxQuads * 4, _shadowCount - start);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, Texture.White,
                _indices.AsSpan(0, count / 4 * 6), _shadows.AsSpan(start, count));
        }

        handle.UseShader(_shader);
        // First person fills the buffer in draw order, ceilings and held items included.
        if (_first)
            DrawWithSheets(handle);
        else
            DrawObjects(handle, false);

        handle.UseShader(null);
    }

    /// <summary>Draws the vertices of the objects that are, or are not, faded, in runs of neighbours in draw order.</summary>
    private void DrawObjects(DrawingHandleWorld handle, bool faded)
    {
        var start = -1;
        var end = -1;
        foreach (var obj in _system.Drawn)
        {
            if (obj.Faded != faded || obj.VertexCount == 0)
                continue;

            if (obj.VertexStart != end)
            {
                DrawRange(handle, start, end);
                start = obj.VertexStart;
            }

            end = obj.VertexStart + obj.VertexCount;
        }

        DrawRange(handle, start, end);
    }

    private void DrawRange(DrawingHandleWorld handle, int from, int to)
    {
        for (var start = from; start >= 0 && start < to; start += MaxQuads * 4)
        {
            var count = Math.Min(MaxQuads * 4, to - start);
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, _atlas.Target.Texture,
                _indices.AsSpan(0, count / 4 * 6), _vertices.AsSpan(start, count));
        }
    }

    /// <summary>
    /// Draws the faded solids solid into their own image, in viewport pixels. Returns whether there were any.
    /// </summary>
    private bool DrawFaded(DrawingHandleWorld handle, Vector2i size)
    {
        var any = false;
        foreach (var obj in _system.Drawn)
        {
            any |= obj.Faded && obj.VertexCount > 0;
        }

        if (!any)
            return false;

        if (_fadeTarget == null || _fadeTarget.Size != size)
        {
            _fadeTarget?.Dispose();
            _fadeTarget = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                new TextureSampleParameters { Filter = false },
                nameof(CameraModeMeshOverlay));
        }

        handle.RenderInRenderTarget(_fadeTarget, () =>
        {
            handle.SetTransform(_perspective ? Matrix3x2.Identity : _worldToLocal);
            handle.UseShader(_shader);
            DrawObjects(handle, true);
            handle.UseShader(null);
        }, new Color(0, 0, 0, 0));

        return true;
    }

    /// <summary>Lays the faded solids' image over the view at the fade alpha.</summary>
    private void DrawFadeLayer(DrawingHandleWorld handle, Box2Rotated bounds)
    {
        _fade.SetParameter("FADE_TEXTURE", _fadeTarget!.Texture);
        _fade.SetParameter("FADE", CameraModesSystem.FrontFade);
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_fade);
        handle.DrawRect(bounds, Color.White);
        handle.UseShader(null);
    }

    /// <summary>Whether the last frame's vertices are in viewport pixels, as in perspective and first person.</summary>
    public bool Perspective => _perspective;

    /// <summary>The viewport's size in pixels, last frame.</summary>
    public Vector2 ViewSize => _viewSize;

    /// <summary>Pixels of the stock picture to one tile, last frame.</summary>
    public float PicturePixelsPerTile => _pixelsPerTile;

    /// <summary>
    /// A point as the first-person eye sees it: how far it is to the right, ahead and up along the tilted view.
    /// </summary>
    private Vector3 ToEye(Vector2 ground, float height)
    {
        var rel = ground - _eye;
        var ahead = Vector2.Dot(rel, _up);
        var up = height - CameraModesSystem.EyeHeight;
        return new Vector3(Vector2.Dot(rel, _right), ahead * _pitchCos + up * _pitchSin, up * _pitchCos - ahead * _pitchSin);
    }

    /// <summary>Where a point ahead of the first-person eye lands on screen, in viewport pixels.</summary>
    private Vector2 EyeScreen(Vector3 eye)
    {
        var scale = _focal / eye.Y;
        return new Vector2(_viewSize.X / 2f + eye.X * scale, _viewSize.Y / 2f - eye.Z * scale);
    }

    /// <summary>
    /// The stock-view point over the top middle of what first person drew of an object this frame, and how much bigger
    /// a tile looks there than in the stock view.
    /// </summary>
    public bool TryMarkSpot(SceneObject obj, out Vector2 world, out float scale)
    {
        world = default;
        scale = 1f;
        var depth = ToEye(obj.Position, CameraModesSystem.EyeHeight).Y;
        if (obj.VertexCount == 0 || depth < MarkNearest || !Matrix3x2.Invert(_worldToLocal, out var toWorld))
            return false;

        world = Vector2.Transform(new Vector2(obj.Bounds.Center.X, obj.Bounds.Bottom), toWorld);
        scale = Math.Clamp(_focal / (depth * _pixelsPerTile), MarkSmallest, MarkLargest);
        return true;
    }

    /// <summary>The stock-view point where the player's own marks show in first person, low in the middle.</summary>
    public Vector2 OwnMarkSpot()
    {
        Matrix3x2.Invert(_worldToLocal, out var toWorld);
        return Vector2.Transform(new Vector2(_viewSize.X / 2f, _viewSize.Y * OwnMarkHeight), toWorld);
    }

    /// <summary>
    /// The flat-image point on the floor seen at a pixel of the first-person view, or null at the horizon and above.
    /// </summary>
    public Vector2? FloorPoint(Vector2 screen)
    {
        var p = new Vector2(screen.X - _viewSize.X / 2f, _viewSize.Y / 2f - screen.Y) / _focal;
        var ahead = _pitchCos - p.Y * _pitchSin;
        var up = _pitchSin + p.Y * _pitchCos;
        if (up > -0.0001f)
            return null;

        var t = CameraModesSystem.EyeHeight / -up;
        return Vector2.Transform(_eye + _right * (p.X * t) + _up * (ahead * t), _worldToLocal);
    }

    /// <summary>Where a point of the flat image lands on screen in the perspective view, in viewport pixels.</summary>
    public Vector2 Project(Vector2 local, float height)
    {
        var o = new Vector2(local.X - _viewSize.X / 2f, _viewSize.Y / 2f - local.Y);
        var depth = 1f + _lambda * (o.Y - _pixelsPerTile * height / _profile.Height);
        var p = o * (_sigma / depth);
        return new Vector2(p.X + _viewSize.X / 2f, _viewSize.Y / 2f - p.Y);
    }

    /// <summary>The flat-image point on the floor seen at a pixel of the perspective view.</summary>
    public Vector2 Unproject(Vector2 screen)
    {
        // A point off the top of the flat image stands for the sky.
        if (_first)
            return FloorPoint(screen) ?? new Vector2(_viewSize.X / 2f, -_viewSize.Y);

        var p = new Vector2(screen.X - _viewSize.X / 2f, _viewSize.Y / 2f - screen.Y) / _sigma;
        var o = p / (1f - _lambda * p.Y);
        return new Vector2(o.X + _viewSize.X / 2f, _viewSize.Y / 2f - o.Y);
    }

    /// <summary>Draws the images that changed into their atlas cells.</summary>
    private void DrawAtlas(DrawingHandleWorld handle, IRenderHandle render)
    {
        var any = false;
        foreach (var obj in _system.Drawn)
        {
            if (obj.Slot is { Drawn: false } || obj.Views && (obj.Slots[1] is { Drawn: false }
                    || obj.Slots[2] is { Drawn: false } || obj.Slots[3] is { Drawn: false }))
            {
                any = true;
                break;
            }
        }

        if (!any && _system.TileArt.Count == 0)
            return;

        handle.RenderInRenderTarget(_atlas.Target, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            foreach (var (slot, tile) in _system.TileArt)
            {
                DrawTile(handle, slot, tile);
            }

            _system.TileArt.Clear();
            foreach (var obj in _system.Drawn)
            {
                // A slab's cell holds tile art, drawn above.
                if (obj.Kind == SceneKind.Slab)
                    continue;

                if (obj.Slot is { Drawn: false } slot)
                    DrawSlot(handle, render, slot, obj);

                if (!obj.Views)
                    continue;

                // A side frame is drawn as the view would see the entity turned to it.
                for (var face = 1; face < 4; face++)
                {
                    if (obj.Slots[face] is not { Drawn: false } side)
                        continue;

                    var turned = new SceneObject
                    {
                        Uid = obj.Uid, Kind = obj.Kind, ViewFrame = true, FrameAngle = Angle.FromDegrees(90 * face),
                        SlotSize = obj.SlotSize,
                    };
                    DrawSlot(handle, render, side, turned);
                }
            }
        }, null);
    }

    /// <summary>Draws the art of a tile kind into an atlas cell, for the ceilings of first person.</summary>
    private void DrawTile(DrawingHandleWorld handle, AtlasSlot slot, ushort tile)
    {
        var corner = new Vector2(slot.X, slot.Y);
        var cell = new Vector2(slot.Size);
        handle.UseShader(_clear);
        handle.DrawRect(Box2.FromDimensions(corner, cell), Color.Transparent);
        handle.DrawRect(Box2.FromDimensions(corner + new Vector2(slot.AtlasSize, 0f), cell), Color.Transparent);
        handle.UseShader(null);
        slot.Drawn = true;
        if (_tileDefs[tile] is not ContentTileDefinition { Sprite: { } path })
            return;

        // A tile image holds its variants side by side. The first one stands for the kind.
        var texture = _resources.GetResource<TextureResource>(path).Texture;
        var pixels = new Vector2(TilePixels);
        handle.DrawTextureRectRegion(texture, Box2.FromDimensions(corner, pixels), null, new UIBox2(Vector2.Zero, pixels));
    }

    private void DrawSlot(DrawingHandleWorld handle, IRenderHandle render, AtlasSlot slot, SceneObject obj)
    {
        var corner = new Vector2(slot.X, slot.Y);
        var glow = new Vector2(slot.AtlasSize, 0f);
        handle.UseShader(_clear);
        handle.DrawRect(Box2.FromDimensions(corner, new Vector2(slot.Size)), Color.Transparent);
        handle.DrawRect(Box2.FromDimensions(corner + glow, new Vector2(slot.Size)), Color.Transparent);
        handle.UseShader(null);
        var centre = corner + new Vector2(obj.SlotSize / 2f);
        DrawSprite(render, obj, centre);
        DrawGlow(render, obj, centre + glow);
        slot.Drawn = true;
    }

    /// <summary>Draws new looks on their own and reads them back to build their solids.</summary>
    private void RequestAnalysis(DrawingHandleWorld handle, IRenderHandle render)
    {
        RequestFootings(handle, render);
        foreach (var obj in _system.Analyse)
        {
            if (!_entities.EntityExists(obj.Uid))
                continue;

            // A turnable sprite is drawn four times side by side: facing south, east, north and west. A console is
            // drawn whole, then its keyboard layer alone.
            var size = obj.SlotSize;
            var turnable = obj.Turnable;
            var console = obj.Console;
            var pair = turnable || console;
            var images = turnable ? 4 : pair ? 2 : 1;
            var width = size * images;
            var staging = size + images * StagingStep;
            if (!_staging.TryGetValue(staging, out var target))
            {
                target = _clyde.CreateRenderTarget((width, size),
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    new TextureSampleParameters { Filter = false },
                    nameof(CameraModeMeshOverlay));
                _staging[staging] = target;
            }

            // A diagonal block is read in the frame it is drawn with, whose outline differs for each way it faces.
            var prism = obj.Prism != null;
            var south = new SceneObject
            {
                Uid = obj.Uid, Kind = obj.Kind, ViewFrame = prism, FrameAngle = obj.FrameAngle, SlotSize = size,
                Frill = obj.Frill, Chain = obj.Chain,
            };
            handle.RenderInRenderTarget(target, () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                DrawSprite(render, south, new Vector2(size / 2f));
                if (turnable)
                {
                    for (var face = 1; face < 4; face++)
                    {
                        var turned = new SceneObject
                        {
                            Uid = obj.Uid, Kind = obj.Kind, ViewFrame = true,
                            FrameAngle = Angle.FromDegrees(90 * face), SlotSize = size,
                        };
                        DrawSprite(render, turned, new Vector2(size * (face + 0.5f), size / 2f));
                    }
                }
                else if (console)
                {
                    DrawKeyboard(render, south, new Vector2(size * 1.5f, size / 2f));
                }
            }, new Color(0, 0, 0, 0));

            var key = obj.AnalyseKey;
            var bottom = obj.FrameBottom;
            var onWall = obj.OnWall;
            var hung = obj.Hung;
            var rest = obj.OnSurface ? CameraModesSystem.TableHeight * EyeManager.PixelsPerMeter : 0f;
            var footprint = obj.Footprint;
            var fence = obj.Fence;
            var joined = obj.Chain != null;
            var cutout = obj.Shape == CameraShape.Cutout;
            var lieOn = obj.LieOn;
            var seat = obj.Seat;
            var item = obj.Kind == SceneKind.Flat || obj.Low;
            var decal = obj.Decal;
            var tall = obj.Tall;
            var wall = obj.Kind == SceneKind.Block;
            var frilled = obj.Frill != null;
            var door = obj.Door;
            var lowWall = obj.LowWall;
            var painted = obj.Painted;
            var stair = obj.Stair;
            var interior = obj.Panel;
            var ceiling = obj.Ceiling;
            var window = obj.Kind == SceneKind.Window;
            var table = obj.Kind == SceneKind.Table;
            target.CopyPixelsToMemory<Rgba32>(image =>
            {
                var pixels = image.GetPixelSpan();
                SpriteAnalysis analysis;
                var above = false;
                (int Top, int Bottom)? keyboard = null;
                SpriteAnalysis? side = null;
                SpriteAnalysis? north = null, west = null;
                if (pair)
                {
                    var left = new Rgba32[size * size];
                    var right = new Rgba32[size * size];
                    for (var r = 0; r < size; r++)
                    {
                        pixels.Slice(r * width, size).CopyTo(left.AsSpan(r * size, size));
                        pixels.Slice(r * width + size, size).CopyTo(right.AsSpan(r * size, size));
                    }

                    analysis = new SpriteAnalysis(size, size, left);
                    var other = new SpriteAnalysis(size, size, right);
                    if (turnable)
                    {
                        above = SilhouetteFold.DrawnFromAbove(analysis, other);
                        side = above ? null : other;
                        north = Frame(pixels, width, size, 2);
                        west = Frame(pixels, width, size, 3);
                    }
                    else if (!other.Empty)
                        keyboard = (other.Y0, other.Y1);
                }
                else
                {
                    analysis = new SpriteAnalysis(image.Width, image.Height, pixels);
                }

                var origin = new Vector2(size / 2f, size / 2f);

                FoldMesh? mesh = null;
                if (prism)
                {
                    mesh = analysis.Empty
                        ? null
                        : BlockBuilder.BuildPrism(origin, CameraModesSystem.BlockHeight,
                            BlockBuilder.Outline(analysis, origin));
                }
                else if (table)
                {
                    mesh = BlockBuilder.BuildTable(analysis, origin, CameraModesSystem.TableHeight);
                }
                else if (frilled)
                {
                    mesh = BlockBuilder.BuildFrilled(analysis, origin, CameraModesSystem.BlockHeight);
                }
                else if (door)
                {
                    mesh = analysis.Empty
                        ? null
                        : BlockBuilder.BuildDoor(origin, CameraModesSystem.BlockHeight, 1f, analysis.Y0, analysis.Y1);
                }
                else if (window)
                {
                    mesh = analysis.Empty ? null : BlockBuilder.BuildTall(analysis, origin, CameraModesSystem.BlockHeight);
                }
                else if (lowWall)
                {
                    // A low wall is as tall as its drawn front, at the tile's own scale.
                    var lid = analysis.Empty ? 0 : SilhouetteFold.LidLine(analysis);
                    var frontRow = analysis.Y0 + lid;
                    var height = lid > 0
                        ? Math.Clamp((origin.Y + 16f - frontRow) / EyeManager.PixelsPerMeter, LowWallMin, LowWallMax)
                        : CameraModesSystem.LowWallHeight;
                    mesh = lid > 0
                        ? BlockBuilder.Build(origin, height, frontRow)
                        : BlockBuilder.Build(origin, height);
                }
                else if (wall)
                {
                    var lid = analysis.Empty ? 0 : SilhouetteFold.LidLine(analysis);
                    mesh = lid > 0
                        ? BlockBuilder.Build(origin, CameraModesSystem.BlockHeight, analysis.Y0 + lid)
                        : BlockBuilder.Build(origin, CameraModesSystem.BlockHeight);
                }
                else if (ceiling && !analysis.Empty)
                {
                    mesh = BlockBuilder.BuildCeiling(analysis, origin, CameraModesSystem.BlockHeight);
                    SilhouetteFold.Path = "interior ceiling";
                }
                else if (interior && !analysis.Empty)
                {
                    mesh = BlockBuilder.BuildPanel(analysis, origin);
                    SilhouetteFold.Path = "interior panel";
                }
                else if (stair && !analysis.Empty)
                {
                    mesh = StairBuilder.Build(analysis, origin, CameraModesSystem.BlockHeight);
                }
                else if (painted && !analysis.Empty)
                {
                    mesh = BlockBuilder.BuildPainted(analysis, origin);
                }
                else if (cutout && footprint is { } area && !analysis.Empty)
                {
                    mesh = FootprintBuilder.BuildCutout(analysis, area);
                    SilhouetteFold.Path = "cut-out";
                }
                else if (fence != null && !analysis.Empty)
                {
                    mesh = FenceBuilder.Build(analysis, fence, joined);
                }
                else if (!analysis.Empty)
                {
                    // Top-down art on something that blocks most of its tile lies as a round body when it is
                    // mirror-symmetric, like a thruster's barrel. Anything else drawn from above lies as a slab.
                    var lying = above && !item && footprint is { } box && box.Width >= LyingFill
                                && box.Height >= LyingFill && ExtrudeBuilder.Lying(analysis);
                    // A round front with a long side image is a barrel lying along its facing. Other sprites with their
                    // own side frames, unless they are seats, beds, items or trays, may be cut from their front and side
                    // outlines together.
                    var barrel = side != null && north != null && !lieOn && !item && !seat && footprint != null
                                 && ExtrudeBuilder.Barrel(analysis, side);
                    var hull = barrel
                        ? ExtrudeBuilder.BuildBarrel(analysis, side!, north!, origin)
                        : side != null && north != null && west != null && !lieOn && !item && !seat
                          && !SilhouetteFold.Trayed(analysis, side)
                        ? HullBuilder.Build(analysis, side, north, west, origin)
                        : null;
                    // Furniture a body lies on is a slab at lying height. Anything a body sits on stands as a seat,
                    // however its art is drawn.
                    mesh = hull ?? (lieOn
                        ? ExtrudeBuilder.Build(analysis, origin, LieHeight)
                        : decal
                        ? ExtrudeBuilder.Build(analysis, origin, DecalHeight)
                        : lying
                        ? ExtrudeBuilder.BuildLying(analysis, origin)
                        : item || above && !seat
                        ? ExtrudeBuilder.Build(analysis, origin)
                        : SilhouetteFold.Build(analysis, origin, bottom, onWall, hung, console,
                            keyboard, rest, footprint, seat, side, tall));
                    if (hull != null)
                        SilhouetteFold.Path = (barrel ? "barrel" : "hull") + $" quads={hull.Quads.Length}";
                }

                var path = prism ? "prism" : table ? "table" : wall ? "block" : analysis.Empty ? "empty" : fence != null ? "fence"
                    : lieOn ? "slab at lying height" : decal ? "decal" : item ? "item slab"
                    : above && !seat ? "drawn from above slab" : SilhouetteFold.Path;
                if (mesh != null && (above || turnable))
                    path += $" (above={above})";

                _system.SetGeometry(key, analysis, mesh, path);
            });
        }
    }

    /// <summary>Reads back the picture each card shows, for the lowest pixel row it stands on.</summary>
    private void RequestFootings(DrawingHandleWorld handle, IRenderHandle render)
    {
        foreach (var obj in _system.Footings)
        {
            if (!_entities.EntityExists(obj.Uid))
                continue;

            var size = obj.SlotSize;
            var staging = size + StagingStep;
            if (!_staging.TryGetValue(staging, out var target))
            {
                target = _clyde.CreateRenderTarget((size, size),
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    new TextureSampleParameters { Filter = false },
                    nameof(CameraModeMeshOverlay));
                _staging[staging] = target;
            }

            var shown = new SceneObject
            {
                Uid = obj.Uid, Kind = obj.Kind, ViewFrame = obj.ViewFrame, FrameAngle = obj.FrameAngle,
                SlotSize = size, Frill = obj.Frill, Chain = obj.Chain,
            };
            handle.RenderInRenderTarget(target, () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                DrawSprite(render, shown, new Vector2(size / 2f));
            }, new Color(0, 0, 0, 0));

            var key = obj.FootingKey;
            target.CopyPixelsToMemory<Rgba32>(image =>
            {
                var analysis = new SpriteAnalysis(image.Width, image.Height, image.GetPixelSpan());
                _system.SetFooting(key, analysis.Empty ? 0f : analysis.Y1);
            });
        }
    }

    /// <summary>Keeps staging targets of one size but different image counts apart.</summary>
    private const int StagingStep = 100000;

    /// <summary>One image of a row of square images read back side by side.</summary>
    private static SpriteAnalysis Frame(ReadOnlySpan<Rgba32> pixels, int width, int size, int index)
    {
        var frame = new Rgba32[size * size];
        for (var r = 0; r < size; r++)
        {
            pixels.Slice(r * width + index * size, size).CopyTo(frame.AsSpan(r * size, size));
        }

        return new SpriteAnalysis(size, size, frame);
    }

    /// <summary>Layer turns of a steered vehicle, kept while its frame is drawn unturned.</summary>
    private readonly List<Angle> _turns = new();

    /// <summary>Draws a sprite opaque, facing south or as the view sees it.</summary>
    private void DrawSprite(IRenderHandle render, SceneObject obj, Vector2 centre)
    {
        if (!_entities.TryGetComponent(obj.Uid, out SpriteComponent? sprite))
            return;

        // A frill wall is its unjoined art: the wall on its own tile and the frill on the tile above.
        if (obj.Frill is { } frill)
        {
            var tint = sprite.Color.WithAlpha(1f);
            var wall = _sprite.Frame0(new SpriteSpecifier.Rsi(frill, CameraModesSystem.FrillWallState));
            var cap = _sprite.Frame0(new SpriteSpecifier.Rsi(frill, CameraModesSystem.FrillCapState));
            var tile = new Vector2(EyeManager.PixelsPerMeter);
            render.DrawingHandleWorld.DrawTextureRect(wall, Box2.FromDimensions(centre - tile / 2f, tile), tint);
            render.DrawingHandleWorld.DrawTextureRect(cap,
                Box2.FromDimensions(centre - new Vector2(tile.X / 2f, tile.Y * 1.5f), tile), tint);
            return;
        }

        // A joining fence is its straight art, whichever piece it is.
        if (obj.Chain is { } chain)
        {
            var art = _sprite.Frame0(chain);
            render.DrawingHandleWorld.DrawTextureRect(art,
                Box2.FromDimensions(centre - (Vector2) art.Size / 2f, art.Size), sprite.Color.WithAlpha(1f));
            return;
        }

        var color = sprite.Color;
        var offset = sprite.Offset;
        // The hover outline's shader can't draw into the atlas, and the view shows hover by its own glow.
        var post = sprite.PostShader;
        sprite.PostShader = null;
        _sprite.SetColor((obj.Uid, sprite), color.WithAlpha(1f));
        if (obj.Kind == SceneKind.Mob)
            _sprite.SetOffset((obj.Uid, sprite), Vector2.Zero);
        // The view's image: the sprite at its angle relative to the eye, drawn with no eye turn. Passing the eye
        // angle to DrawEntity turns the image the opposite way to the world view.
        var angle = obj.ViewFrame ? obj.FrameAngle : Angle.Zero;
        // A steered vehicle shows one forced direction, its layers turned to suit. For this draw the frame asked for
        // is forced instead and the layers are left unturned.
        var steered = sprite.EnableDirectionOverride;
        if (steered)
        {
            _turns.Clear();
            var index = 0;
            foreach (var layer in sprite.AllLayers)
            {
                _turns.Add(layer.Rotation);
                _sprite.LayerSetRotation((obj.Uid, sprite), index++, Angle.Zero);
            }
        }

        render.DrawEntity(obj.Uid, centre, Vector2.One, steered ? Angle.Zero : angle, Angle.Zero,
            steered ? angle.GetCardinalDir() : null, sprite);
        if (obj.Riders.Count > 0)
            DrawRiders(render, obj, centre, angle);
        if (steered)
        {
            for (var i = 0; i < _turns.Count; i++)
            {
                _sprite.LayerSetRotation((obj.Uid, sprite), i, _turns[i]);
            }
        }

        _sprite.SetColor((obj.Uid, sprite), color);
        sprite.PostShader = post;
        if (obj.Kind == SceneKind.Mob)
            _sprite.SetOffset((obj.Uid, sprite), offset);
    }

    /// <summary>
    /// Paints a vehicle's turret overlays onto its picture, placed and turned as an eye that sees the vehicle in the
    /// picture's frame would see them.
    /// </summary>
    private void DrawRiders(IRenderHandle render, SceneObject obj, Vector2 centre, Angle frame)
    {
        var eyeTurn = frame - _transformSystem.GetWorldRotation(obj.Uid);
        foreach (var uid in obj.Riders)
        {
            if (!_entities.TryGetComponent(uid, out VehicleTurretVisualComponent? visual)
                || !_entities.TryGetComponent(uid, out SpriteComponent? sprite)
                || !_turretVisuals.TryGetOverlayPose((uid, visual), eyeTurn, out var offset, out var turn))
            {
                continue;
            }

            // The picture's rows run down, the world's north up.
            var at = frame.RotateVec(offset) * EyeManager.PixelsPerMeter;
            var color = sprite.Color;
            _sprite.SetColor((uid, sprite), color.WithAlpha(1f));
            render.DrawEntity(uid, centre + new Vector2(at.X, -at.Y), Vector2.One, frame + turn, Angle.Zero, null,
                sprite);
            _sprite.SetColor((uid, sprite), color);
        }
    }

    /// <summary>Draws only a computer's keyboard layer, by hiding the others for the draw.</summary>
    private void DrawKeyboard(IRenderHandle render, SceneObject obj, Vector2 centre)
    {
        if (!_entities.TryGetComponent(obj.Uid, out SpriteComponent? sprite)
            || !_sprite.LayerMapTryGet((obj.Uid, sprite), KeyboardLayer, out var index, false))
        {
            return;
        }

        _dimmed.Clear();
        var i = 0;
        foreach (var layerI in sprite.AllLayers)
        {
            if (layerI is SpriteComponent.Layer layer && i != index)
            {
                _dimmed.Add((layer, layer.Color));
                _sprite.LayerSetColor(layer, Color.Transparent);
            }

            i++;
        }

        DrawSprite(render, obj, centre);
        foreach (var (layer, color) in _dimmed)
        {
            _sprite.LayerSetColor(layer, color);
        }

        _dimmed.Clear();
    }

    /// <summary>Draws only the sprite's unshaded layers, by hiding the others for the draw.</summary>
    private void DrawGlow(IRenderHandle render, SceneObject obj, Vector2 centre)
    {
        if (!_entities.TryGetComponent(obj.Uid, out SpriteComponent? sprite))
            return;

        var any = false;
        foreach (var layerI in sprite.AllLayers)
        {
            if (layerI is SpriteComponent.Layer { Visible: true } layer && layer.ShaderPrototype == Unshaded)
            {
                any = true;
                break;
            }
        }

        if (!any)
            return;

        _dimmed.Clear();
        foreach (var layerI in sprite.AllLayers)
        {
            if (layerI is not SpriteComponent.Layer layer || layer.ShaderPrototype == Unshaded)
                continue;

            _dimmed.Add((layer, layer.Color));
            _sprite.LayerSetColor(layer, Color.Transparent);
        }

        DrawSprite(render, obj, centre);
        foreach (var (layer, color) in _dimmed)
        {
            _sprite.LayerSetColor(layer, color);
        }

        _dimmed.Clear();
    }

    /// <summary>Fills the vertex buffer, object by object, far to near.</summary>
    private void Build()
    {
        if (_first)
        {
            BuildEye();
            return;
        }

        _count = 0;
        _shadowCount = 0;
        foreach (var obj in _system.Drawn)
        {
            obj.VertexStart = _count;
            obj.VertexCount = 0;
            // What stands below is drawn in its own level's pass. See DrawLowerPass.
            if (obj.Slot is not { } slot || obj.Level < 0)
                continue;

            if (obj.Kind == SceneKind.Slab)
            {
                // A floor above the player in the see-through strip would lie over the faded walls as a grey stair.
                if (obj.Faded)
                    continue;

                AddSlab(obj, slot);
            }
            else if (!obj.Billboard && obj.Mesh is { Fixed: true } mesh)
            {
                if (obj.Level == 0 && Grounded(obj) && mesh.Base is { } floor)
                    AddShadow(obj, floor);

                AddMesh(obj, slot, mesh);
            }
            else
            {
                AddBillboard(obj, slot);
            }

            obj.VertexCount = _count - obj.VertexStart;
            obj.Bounds = VertexBounds(obj.VertexStart, _count);
        }
    }

    private Box2 VertexBounds(int start, int end)
    {
        if (end <= start)
            return default;

        var min = _vertices[start].Position;
        var max = min;
        for (var v = start + 1; v < end; v++)
        {
            min = Vector2.Min(min, _vertices[v].Position);
            max = Vector2.Max(max, _vertices[v].Position);
        }

        return new Box2(min, max);
    }

    /// <summary>Whether a solid stands on the floor, so it darkens the floor under it and its own faces near it.</summary>
    private static bool Grounded(SceneObject obj)
    {
        return obj.Kind is SceneKind.Solid or SceneKind.Table && !obj.OnWall && !obj.Hung && !obj.Cladding
               && obj.Lift == 0f;
    }

    /// <summary>A soft dark patch on the floor under the base of a solid: an even middle fading out to nothing.</summary>
    private void AddShadow(SceneObject obj, Box2 floor)
    {
        var centre = obj.Position + obj.Rotation.RotateVec(floor.Center);
        var across = obj.Rotation.RotateVec(new Vector2(floor.Width / 2f * ShadowSpread + ShadowEdge, 0f));
        var along = obj.Rotation.RotateVec(new Vector2(0f, floor.Height / 2f * ShadowSpread + ShadowEdge));
        var dark = new Color(0f, 0f, 0f, ShadowAlpha * obj.Alpha);
        var clear = new Color(0f, 0f, 0f, 0f);
        if (_shadowCount + ShadowSides * 8 > _shadows.Length)
            Array.Resize(ref _shadows, _shadows.Length * 2);

        var start = _shadowCount;
        _shadowCut = false;
        var from = across;
        for (var k = 1; k <= ShadowSides; k++)
        {
            var (sin, cos) = MathF.SinCos(k * MathF.Tau / ShadowSides);
            var to = across * cos + along * sin;
            PutShadow(centre, dark);
            PutShadow(centre + from * ShadowCore, dark);
            PutShadow(centre + to * ShadowCore, dark);
            PutShadow(centre + to * ShadowCore, dark);
            PutShadow(centre + from * ShadowCore, dark);
            PutShadow(centre + from, clear);
            PutShadow(centre + to, clear);
            PutShadow(centre + to * ShadowCore, dark);
            from = to;
        }

        // A shadow that reaches behind the first-person eye is left out.
        if (_shadowCut)
            _shadowCount = start;
    }

    private void PutShadow(Vector2 world, Color color)
    {
        var position = world;
        if (_first)
        {
            var eye = ToEye(world, 0f);
            _shadowCut |= eye.Y < Near;
            position = EyeScreen(eye with { Y = MathF.Max(eye.Y, Near) });
        }
        else if (_perspective)
        {
            position = Project(Vector2.Transform(world, _worldToLocal), 0f);
        }

        _shadows[_shadowCount++] = new DrawVertexUV2DColor { Position = position, Color = color };
    }

    private void AddMesh(SceneObject obj, AtlasSlot slot, FoldMesh mesh)
    {
        var stretch = _profile.Height / _profile.Squash;
        var grounded = Grounded(obj) || obj.Kind is SceneKind.Block or SceneKind.Window || obj.Cladding;
        var rot = obj.Rotation;
        var camH = -_up * _profile.PitchCos;
        // The perspective camera sees an object off to one side a little from the other side, so that side shows.
        if (_perspective)
            camH -= _right * ((Vector2.Transform(obj.Position, _worldToLocal).X - _viewSize.X / 2f) * _lambda);

        var camObj = (-rot).RotateVec(camH);
        var yaw = MathF.Atan2(camObj.X, -camObj.Y);
        var bucket = ((int) MathF.Round(yaw / (MathF.Tau / FoldMesh.YawBuckets)) % FoldMesh.YawBuckets
                      + FoldMesh.YawBuckets) % FoldMesh.YawBuckets;
        var order = mesh.Order(bucket, _profile.PitchSin, _profile.PitchCos);
        var alpha = obj.Alpha;
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;

        foreach (var index in order)
        {
            ref var q = ref mesh.Quads[index];
            if (q.Side != Direction.Invalid && (obj.Neighbours & q.Side.AsFlag()) != 0)
                continue;

            var nW = rot.RotateVec(new Vector2(q.Normal.X, q.Normal.Y));
            if (Vector2.Dot(nW, camH) + q.Normal.Z * _profile.PitchSin <= 0.0001f)
                continue;

            var shade = q.Shade * FaceShade(nW, q.Normal.Z) * glow;
            var color = new Color(shade, Rain(obj), shade, alpha).WithBlue(SideFlag(obj, q.Normal.Z));
            // Solids take light from the floor in front of each face. Blocks take it from inside their own tile,
            // where the light that bleeds onto walls lands.
            var nudge = q.Normal.Z != 0f ? Vector2.Zero
                : obj.Kind is SceneKind.Block or SceneKind.Window or SceneKind.Table || obj.Cladding ? nW * -Nudge
                : nW * Nudge;
            // Standing faces darken towards the floor they meet.
            var darken = grounded && q.Normal.Z == 0f;
            var image = q.Face == 0 ? slot : obj.Slots[q.Face] ?? slot;
            EnsureRoom();
            AddVertex(obj, rot, q.P0, q.T0, image, stretch, nudge, color, darken);
            AddVertex(obj, rot, q.P1, q.T1, image, stretch, nudge, color, darken);
            AddVertex(obj, rot, q.P2, q.T2, image, stretch, nudge, color, darken);
            AddVertex(obj, rot, q.P3, q.T3, image, stretch, nudge, color, darken);
        }
    }

    private void AddVertex(SceneObject obj, Angle rot, Vector3 p, Vector2 t, AtlasSlot slot, float stretch,
        Vector2 nudge, Color color, bool darken)
    {
        if (darken && p.Z < BaseRise)
        {
            var k = BaseShade + (1f - BaseShade) * MathF.Max(p.Z, 0f) / BaseRise;
            color = new Color(color.R * k, color.G * k, color.B, color.A);
        }

        var z = p.Z + obj.Lift + Rise(obj);
        var ground = obj.Position + rot.RotateVec(new Vector2(p.X, p.Y));
        var drawn = ground + _up * (z * stretch);
        Put(drawn, ground + nudge, SpriteAtlas.Uv(slot, t), color, z);
    }

    /// <summary>The vertex green of an object in the 2.5D views: 1 where the weather falls in front of it.</summary>
    private static float Rain(SceneObject obj)
    {
        return obj.Rained ? 1f : 0f;
    }

    /// <summary>
    /// How high an object stands over the floor of the pass drawing it in the 2.5D views. A level below is drawn in its
    /// own pass, which already sits at its depth.
    /// </summary>
    private static float Rise(SceneObject obj)
    {
        return obj.Level < 0 ? 0f : CameraModesSystem.StoryRise(obj);
    }

    /// <summary>A floor tile of a level above, laid flat at its height, lit as a top.</summary>
    private void AddSlab(SceneObject obj, AtlasSlot slot)
    {
        var height = CameraModesSystem.StoryRise(obj);
        var rise = _up * (height * _profile.Height / _profile.Squash);
        var east = obj.Rotation.RotateVec(new Vector2(0.5f, 0f));
        var north = obj.Rotation.RotateVec(new Vector2(0f, 0.5f));
        var color = new Color(1f, 1f, 0f, obj.Alpha);
        EnsureRoom();
        Corner(obj.Position - east - north, new Vector2(0f, TilePixels));
        Corner(obj.Position - east + north, Vector2.Zero);
        Corner(obj.Position + east + north, new Vector2(TilePixels, 0f));
        Corner(obj.Position + east - north, new Vector2(TilePixels, TilePixels));

        void Corner(Vector2 ground, Vector2 pixel)
        {
            Put(ground + rise, ground, SpriteAtlas.Uv(slot, pixel), color, height);
        }
    }

    /// <summary>
    /// A sprite standing on the entity's position, shown at its true size. A standing card stays fixed across its own
    /// facing (<see cref="AddStandingCard"/>). Anything else faces the camera.
    /// </summary>
    private void AddBillboard(SceneObject obj, AtlasSlot slot)
    {
        if (obj.Standing)
        {
            AddStandingCard(obj, slot);
            return;
        }

        var s = obj.SlotSize;
        var half = s / 2f / PixelsPerTile;
        var top = obj.Foot / PixelsPerTile + obj.Offset.Y;
        var bottom = (obj.Foot - s) / PixelsPerTile + obj.Offset.Y;
        var lean = 1f / _profile.Squash;
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;
        var color = new Color(glow, Rain(obj), 0f, obj.Alpha);
        var rise = Rise(obj);
        var at = obj.Position + _right * obj.Offset.X + _up * (rise * _profile.Height / _profile.Squash);
        EnsureRoom();
        // The billboard leans back to face the camera, so a point's height is its distance up the sprite times cos(pitch).
        var zb = bottom * _profile.Height + rise;
        var zt = top * _profile.Height + rise;
        Put(at - _right * half + _up * (bottom * lean), obj.Position, SpriteAtlas.Uv(slot, new Vector2(0, s)), color, zb);
        Put(at + _right * half + _up * (bottom * lean), obj.Position, SpriteAtlas.Uv(slot, new Vector2(s, s)), color, zb);
        Put(at + _right * half + _up * (top * lean), obj.Position, SpriteAtlas.Uv(slot, new Vector2(s, 0)), color, zt);
        Put(at - _right * half + _up * (top * lean), obj.Position, SpriteAtlas.Uv(slot, Vector2.Zero), color, zt);
    }

    /// <summary>
    /// A sprite standing upright across its own facing, fixed in the world, read the right way round from the side the
    /// camera is on.
    /// </summary>
    private void AddStandingCard(SceneObject obj, AtlasSlot slot)
    {
        var stretch = _profile.Height / _profile.Squash;
        var right = obj.Rotation.RotateVec(Vector2.UnitX);
        if (Vector2.Dot(obj.Rotation.RotateVec(-Vector2.UnitY), -_up) < 0f)
            right = -right;

        var s = obj.SlotSize;
        var half = s / 2f / PixelsPerTile;
        var top = obj.Foot / PixelsPerTile + obj.Offset.Y;
        var bottom = (obj.Foot - s) / PixelsPerTile + obj.Offset.Y;
        var glow = obj.Uid == _system.Hovered ? _system.HoverGlow : 1f;
        var color = new Color(glow, Rain(obj), 0f, obj.Alpha);
        var rise = Rise(obj);
        var at = obj.Position + right * obj.Offset.X;
        EnsureRoom();
        Corner(-half, bottom, new Vector2(0, s));
        Corner(half, bottom, new Vector2(s, s));
        Corner(half, top, new Vector2(s, 0));
        Corner(-half, top, Vector2.Zero);

        void Corner(float across, float height, Vector2 pixel)
        {
            var ground = at + right * across;
            var z = height + rise;
            Put(ground + _up * (z * stretch), obj.Position, SpriteAtlas.Uv(slot, pixel), color, z);
        }
    }

    /// <summary>
    /// Tops are lit in full. Standing faces take one of three steps by how far they turn to a light on the left of the
    /// screen, so the two sides of a box differ and a round solid shades in bands.
    /// </summary>
    private float FaceShade(Vector2 normal, float up)
    {
        if (up > 0.5f)
            return 1f;

        if (up < -0.5f)
            return 0.6f;

        var lit = Vector2.Dot(normal, -_shadeRight);
        return lit > SideTurn ? SideLit : lit < -SideTurn ? SideDark : SideMid;
    }

    /// <summary>
    /// The blue channel of a face's vertex colour: 1 on the standing sides of walls and windows, which the mesh shader
    /// fills where their art is clear, so a block has no holes through its sides. Shade is read from red.
    /// </summary>
    private static float SideFlag(SceneObject obj, float normalZ)
    {
        return normalZ == 0f && obj.SolidSides ? 1f : 0f;
    }

    private const float SideTurn = 0.3f;
    private const float SideLit = 0.96f;
    private const float SideMid = 0.84f;
    private const float SideDark = 0.72f;

    /// <summary>
    /// Adds a vertex. The flat views take world positions. The perspective view takes viewport pixels, and its UV2 still
    /// points into the flat image, where the light and line-of-sight maps are.
    /// </summary>
    private void Put(Vector2 drawn, Vector2 ground, Vector2 uv, Color color, float height)
    {
        Vector2 position;
        Vector2 screen;
        if (_lowerWarp)
        {
            // The pass shrinks the level's floor to where the warp shows it. A point higher up stands nearer the eye,
            // so it shrinks less. See LowerShrink.
            var own = 1f + _lambda * MainPixelsPerTile * (_lowerDrop - height) / _profile.Height;
            drawn = _lowerCentre + (drawn - _lowerCentre) * (_lowerShrink / MathF.Max(own, 0.05f));
        }

        if (_perspective)
        {
            position = Project(Vector2.Transform(drawn, _worldToLocal), height);
            screen = new Vector2(position.X / _viewSize.X, 1f - position.Y / _viewSize.Y);
        }
        else
        {
            position = drawn;
            screen = ScreenUv(drawn);
        }

        _vertices[_count++] = new DrawVertexUV2DColor
        {
            Position = position,
            UV = uv,
            UV2 = ScreenUv(ground) - screen,
            Color = color,
        };
    }

    /// <summary>
    /// The perspective warp of a view this size, as perspective.swsl reads it. The camera keeps the top edge of the
    /// flat image and magnifies towards the bottom.
    /// </summary>
    public static (float Lambda, float Sigma) Warp(CameraModeProfile profile, Vector2 viewSize)
    {
        var lambda = profile.Height / profile.Squash * 2f * MathF.Tan(FieldOfView / 2f) / viewSize.Y;
        return (lambda, 1f + lambda * viewSize.Y / 2f);
    }

    private Vector2 ScreenUv(Vector2 world)
    {
        var local = Vector2.Transform(world, _worldToLocal);
        return new Vector2(local.X / _viewSize.X, 1f - local.Y / _viewSize.Y);
    }

    private void EnsureRoom(int room = 4)
    {
        if (_count + room <= _vertices.Length)
            return;

        Array.Resize(ref _vertices, _vertices.Length * 2);
        Array.Resize(ref _pickUv, _vertices.Length);
        Array.Resize(ref _pickShare, _vertices.Length);
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();
        _meshTarget?.Dispose();
        _skyTarget?.Dispose();
        _ownSkyTarget?.Dispose();
        DisposeBodies();
        _maskTarget?.Dispose();
        _fadeTarget?.Dispose();
        DisposeWeather();
        foreach (var target in _staging.Values)
        {
            target.Dispose();
        }

        _staging.Clear();
    }
}
