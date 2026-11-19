using System.Numerics;
using Content.Client._WG.WolfWeather;
using Content.Shared._WG.WolfWeather;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.CameraModes.Render;

// First-person weather: the falling and drifting layers of each weather on the map. Their particles stand on upright
// planes fixed in the world. The view's depth is cut into slices, each drawn between the objects in draw order, so
// nearer walls hide the weather behind them.
public sealed partial class CameraModeMeshOverlay
{
    private static readonly ProtoId<ShaderPrototype> WeatherShader = "WGCameraModeWeather";
    private static readonly ProtoId<ShaderPrototype> RainFloorShader = "WGCameraModeRainFloor";
    private static readonly string[] WeatherTints = { "TINT0", "TINT1", "TINT2", "TINT3" };

    /// <summary>Layers one weather pass draws together.</summary>
    private const int LayersPerPass = 4;

    /// <summary>How far along the view each slice of the weather starts, in tiles, far to near.</summary>
    private static readonly float[] SheetDistances = { 16f, 12f, 9f, 6.8f, 5f, 3.6f, 2.6f, 1.8f, 1f };

    private const float WeatherPixelsPerTile = 32f;
    private const double WeatherCellPeriod = 1024.0;

    /// <summary>Share of a layer's flat-view density one plane shows, as the planes add up along a ray.</summary>
    private const float SheetDensity = 0.7f;

    /// <summary>How far apart the world's weather planes stand on each axis, in tiles.</summary>
    private const float PlaneStep = 2f;

    /// <summary>Nearest the weather is drawn to the eye, in tiles.</summary>
    private const float NearestWeather = 0.4f;

    /// <summary>Darkest the weather is drawn at night, as a share of its own colour.</summary>
    private const float WeatherDarkest = 0.6f;

    private readonly List<WeatherLayer> _weatherLayers = new();
    private readonly List<(int Vertex, int Sheet)> _sheetBreaks = new();
    private readonly Dictionary<(ProtoId<WolfWeatherPrototype>, TimeSpan, int), WeatherDrift> _weatherDrift = new();
    private readonly List<(ProtoId<WolfWeatherPrototype>, TimeSpan, int)> _weatherGone = new();
    private readonly Dictionary<(int Sheet, int Group), ShaderInstance> _weatherShaders = new();
    private ShaderInstance? _rainFloorShader;
    private IRenderTexture? _rainFloor;
    private bool _floorsOn;
    private int _floorCount;
    private Vector4 _floorDrops;
    private Vector4 _floorDrops2;
    private WolfWeatherSystem? _wolfWeather;
    private int _weatherFrame;
    private int _nextSheet;
    private Texture? _weatherMask;
    private Vector2 _weatherMaskEye;
    private Vector2 _weatherMaskSize;
    private Color _weatherLight;
    private Vector2 _weatherWind;

    // The fog of this frame, worked out per pixel by distance in the floor, mesh and weather shaders.
    private bool _fogOn;
    private Vector3 _fogLight;
    private Vector3 _fogColor;
    private float _fogMax;
    private float _fogThick;
    private float _fogScale;
    private Vector2 _fogCell;

    /// <summary>How thick fog is per tile at a fog layer's density of 1.</summary>
    private const float FogThickness = 0.5f;

    private static readonly ProtoId<ShaderPrototype> FogFieldShader = "WGCameraModeFogField";

    /// <summary>Pixels across the top-down picture of the fog's banks, and the tiles it covers each side of the eye.</summary>
    private const int FogFieldSize = 256;
    private const float FogFieldTiles = 32f;

    /// <summary>The fog's thickness per tile that full red in the picture of its banks stands for.</summary>
    private const float FogBankThickness = 2f;

    /// <summary>
    /// Share of the distance fog kept while the banks show, so only a light haze stays at the far edge. The 2D fog has
    /// no distance fog.
    /// </summary>
    private const float BaseFogShare = 0.1f;

    // The fog layers drawn as banks this frame, at most one pass's worth, and the picture of them.
    private readonly List<FogBank> _fogBanks = new();
    private ShaderInstance? _fogFieldShader;
    private IRenderTexture? _fogField;
    private bool _fieldOn;

    /// <summary>One fog layer drawn as banks: its noise cell at the eye, bank size, cover, edge and strength.</summary>
    private struct FogBank
    {
        public Vector2 Cell;
        public float Spacing;
        public float Density;
        public float Softness;
        public float Alpha;
    }

    /// <summary>One weather layer to draw on the sheets this frame.</summary>
    private struct WeatherLayer
    {
        public (ProtoId<WolfWeatherPrototype>, TimeSpan, int) Key;
        public WolfWeatherLayer Layer;
        public WeatherDrift Drift;
        public float Strength;
        public int Seed;
    }

    /// <summary>How far a layer has fallen and blown since it started.</summary>
    private sealed class WeatherDrift
    {
        public double Fall;
        public double WindX;
        public double WindY;
        public int Seen;
    }

    /// <summary>Finds the weather layers the sheets show this frame and moves them on.</summary>
    private void PrepareWeather(in OverlayDrawArgs args)
    {
        _weatherLayers.Clear();
        _fogBanks.Clear();
        _fieldOn = false;
        _weatherFrame++;
        _weatherMask = null;
        _floorsOn = false;
        _fogOn = false;
        _fogMax = 0f;
        _wolfWeather ??= _entities.System<WolfWeatherSystem>();
        var map = args.MapUid;
        // A story takes the weather of the surface below it.
        if (!_first || _wolfWeather.Quality <= 0 || !_wolfWeather.TryGetWeatherMap(map, out var resolved)
            || resolved.Comp is not { Weathers.Count: > 0 } weather
            || !_wolfWeather.TryGetState(resolved.Owner, out var state) || _wolfWeather.ViewMask is not { } mask
            || _wolfWeather.ViewMaskMap != args.MapId)
        {
            return;
        }

        _weatherMask = mask;
        _weatherWind = state.Wind;
        _weatherMaskEye = _eye - _wolfWeather.ViewMaskRect.BottomLeft;
        _weatherMaskSize = _wolfWeather.ViewMaskRect.Size;
        var light = _entities.TryGetComponent(map, out MapLightComponent? ambient) ? ambient.AmbientLightColor : Color.White;
        _weatherLight = new Color(MathF.Max(light.R, WeatherDarkest), MathF.Max(light.G, WeatherDarkest),
            MathF.Max(light.B, WeatherDarkest));
        // Fog is lit only by the sky, so it darkens at night with everything it covers.
        _fogLight = new Vector3(light.R, light.G, light.B);

        var now = _timing.CurTime;
        var frameTime = (float) _timing.FrameTime.TotalSeconds;
        foreach (var entry in weather.Weathers)
        {
            if (!_prototypes.TryIndex(entry.Weather, out var proto))
                continue;

            var strength = SharedWolfWeatherSystem.GetStrength(entry, proto, now);
            if (strength <= 0f)
                continue;

            var layers = proto.FinalLayers;
            for (var i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.Kind == WolfWeatherLayerKind.Splash || layer.MinQuality > _wolfWeather.Quality)
                    continue;

                var key = (entry.Weather, entry.Start, i);
                if (!_weatherDrift.TryGetValue(key, out var drift))
                {
                    drift = new WeatherDrift();
                    _weatherDrift[key] = drift;
                }

                drift.Seen = _weatherFrame;
                if (layer.Kind != WolfWeatherLayerKind.Fog)
                    drift.Fall += layer.FallSpeed * frameTime;

                drift.WindX += state.Wind.X * layer.WindScale * frameTime;
                drift.WindY += state.Wind.Y * layer.WindScale * frameTime;
                if (layer.Kind == WolfWeatherLayerKind.Fog)
                {
                    AddFog(layer, drift, strength);
                    continue;
                }

                _weatherLayers.Add(new WeatherLayer
                {
                    Key = key, Layer = layer, Drift = drift, Strength = strength, Seed = entry.Seed + i * 31,
                });
            }
        }

        _weatherGone.Clear();
        foreach (var (key, drift) in _weatherDrift)
        {
            if (drift.Seen != _weatherFrame)
                _weatherGone.Add(key);
        }

        foreach (var key in _weatherGone)
        {
            _weatherDrift.Remove(key);
        }

        if (_fogBanks.Count > 0)
            _fogThick *= BaseFogShare;
    }

    /// <summary>Whether this frame draws the weather slices: for falling layers or the fog's banks.</summary>
    private bool AnySheets => _weatherLayers.Count > 0 || _fogBanks.Count > 0;

    /// <summary>
    /// Marks which floors are solid across the picture, the player's and each level's below, so the weather falls
    /// through holes down to the first solid one.
    /// </summary>
    private void DrawRainFloor(DrawingHandleWorld handle, IRenderTexture mask)
    {
        _floorsOn = false;
        if (!AnySheets)
            return;

        _rainFloorShader ??= _prototypes.Index(RainFloorShader).InstanceUnique();
        (_floorCount, _floorDrops, _floorDrops2) = SetLowerLevels(_rainFloorShader, mask.Texture);
        if (_floorCount == 0)
            return;

        var size = mask.Size;
        if (_rainFloor == null || _rainFloor.Size != size)
        {
            _rainFloor?.Dispose();
            _rainFloor = _clyde.CreateRenderTarget(size,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8),
                new TextureSampleParameters { Filter = false },
                "WGCameraModeRainFloor");
        }

        var shader = _rainFloorShader;
        shader.SetParameter("MASK_TEXTURE", mask.Texture);
        shader.SetParameter("SIZE", (Vector2) size);
        handle.RenderInRenderTarget(_rainFloor, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(shader);
            handle.DrawRect(Box2.FromDimensions(Vector2.Zero, size), Color.White);
            handle.UseShader(null);
        }, Color.Black);

        _floorsOn = true;
    }

    /// <summary>
    /// Notes where the slices that lie wholly past a depth go in the vertex buffer: before whatever comes next. A
    /// slice is drawn after the objects inside it, so the weather in front of a wall shows over the wall.
    /// </summary>
    private void BreakForSheets(float depth)
    {
        if (!AnySheets)
            return;

        var step = _wolfWeather!.Quality >= 2 ? 1 : 2;
        for (; _nextSheet < SheetDistances.Length; _nextSheet += step)
        {
            var near = _nextSheet + step < SheetDistances.Length ? SheetDistances[_nextSheet + step] : NearestWeather;
            if (near <= depth)
                break;

            _sheetBreaks.Add((_count, _nextSheet));
        }
    }

    /// <summary>Draws the first-person vertices with the weather sheets between them.</summary>
    private void DrawWithSheets(DrawingHandleWorld handle)
    {
        // The engine binds a shader's textures again only when its instance changes. A slice's shader takes over the
        // light map's slot, so the mesh shader swaps to its spare instance after each one.
        var from = DrawLower(handle);
        var swap = 0;
        foreach (var (vertex, sheet) in _sheetBreaks)
        {
            if (vertex > from)
            {
                handle.UseShader(_meshShaders[swap++ % 2]);
                DrawRange(handle, from, vertex);
                from = vertex;
            }

            DrawSheet(handle, sheet);
        }

        handle.UseShader(_meshShaders[swap % 2]);
        DrawRange(handle, from, _count);
    }

    private void DrawSheet(DrawingHandleWorld handle, int sheet)
    {
        // A slice runs from its own distance in to the next slice's.
        var step = _wolfWeather!.Quality >= 2 ? 1 : 2;
        var far = SheetDistances[sheet];
        var near = sheet + step < SheetDistances.Length ? SheetDistances[sheet + step] : NearestWeather;
        var planeX = Math.Floor(_eye.X / PlaneStep);
        var planeY = Math.Floor(_eye.Y / PlaneStep);
        var eyeStep = new Vector2((float) (_eye.X - planeX * PlaneStep), (float) (_eye.Y - planeY * PlaneStep));
        var plane0 = new Vector2((float) WrapCell(planeX), (float) WrapCell(planeY));
        // The nearest tile of the eye and the place of the eye in it, so the shader works in small numbers.
        var originX = Math.Floor(_eye.X);
        var originY = Math.Floor(_eye.Y);
        var eyeRel = new Vector2((float) (_eye.X - originX), (float) (_eye.Y - originY));
        var time = (float) (_timing.RealTime.TotalSeconds % 3600.0);
        // The fog's banks in the slice go under the falling layers in it.
        if (_fieldOn)
        {
            var fog = SheetShader(sheet, -1);
            SetSheet(fog, near, far, eyeStep, plane0, eyeRel, time);
            fog.SetParameter("FOG_PASS", 1f);
            fog.SetParameter("FOG_FIELD", _fogField!.Texture);
            fog.SetParameter("FOG_FIELD_TILES", FogFieldTiles);
            fog.SetParameter("FOG_SAMPLES", 3f);
            fog.SetParameter("FOG_BANK", FogBankThickness);
            fog.SetParameter("LAYERS", 0f);
            handle.UseShader(fog);
            handle.DrawRect(new Box2(Vector2.Zero, _viewSize), Color.White);
        }

        for (var group = 0; group * LayersPerPass < _weatherLayers.Count; group++)
        {
            var shader = SheetShader(sheet, group);

            var layers = Math.Min(LayersPerPass, _weatherLayers.Count - group * LayersPerPass);
            Vector4 cellX = default, cellY = default, cellZ = default, spacings = default, alongs = default;
            Vector4 densities = default, streaks = default, thicks = default, wobbles = default, modes = default;
            Vector4 seeds = default, dirX01 = default, dirX23 = default, dirY01 = default, dirY23 = default;
            for (var lane = 0; lane < LayersPerPass; lane++)
            {
                if (lane >= layers)
                {
                    shader.SetParameter(WeatherTints[lane], Color.Transparent);
                    continue;
                }

                var entry = _weatherLayers[group * LayersPerPass + lane];
                var layer = entry.Layer;
                var spacing = MathF.Max(layer.Spacing, 1f / WeatherPixelsPerTile);
                var along = layer.Kind == WolfWeatherLayerKind.Rain
                    ? MathF.Max(spacing, layer.Length / WeatherPixelsPerTile * 2f)
                    : spacing;
                var drift = entry.Drift;
                var blownX = originX - drift.WindX;
                var blownY = originY - drift.WindY;
                // A streak leans the way the wind carries it along its plane as it falls.
                var fall = MathF.Max(layer.FallSpeed, 0.01f);
                var leanX = Vector2.Normalize(new Vector2(_weatherWind.Y * layer.WindScale, fall));
                var leanY = Vector2.Normalize(new Vector2(_weatherWind.X * layer.WindScale, fall));
                var alpha = layer.Color.A * entry.Strength;

                SetLane(ref cellX, lane, (float) WrapCell(blownY / spacing));
                SetLane(ref cellY, lane, (float) WrapCell(blownX / spacing));
                SetLane(ref cellZ, lane, (float) WrapCell(-drift.Fall / along));
                SetLane(ref spacings, lane, spacing);
                SetLane(ref alongs, lane, along);
                SetLane(ref densities, lane, layer.Density * entry.Strength * SheetDensity);
                SetLane(ref streaks, lane, layer.Length);
                SetLane(ref thicks, lane, layer.Width);
                SetLane(ref wobbles, lane, layer.Wobble);
                SetLane(ref modes, lane, (float) layer.Kind);
                SetLane(ref seeds, lane, (entry.Seed + sheet * 13) % 997);
                if (lane < 2)
                {
                    SetLane(ref dirX01, lane * 2, leanX.X);
                    SetLane(ref dirX01, lane * 2 + 1, leanX.Y);
                    SetLane(ref dirY01, lane * 2, leanY.X);
                    SetLane(ref dirY01, lane * 2 + 1, leanY.Y);
                }
                else
                {
                    SetLane(ref dirX23, lane * 2 - 4, leanX.X);
                    SetLane(ref dirX23, lane * 2 - 3, leanX.Y);
                    SetLane(ref dirY23, lane * 2 - 4, leanY.X);
                    SetLane(ref dirY23, lane * 2 - 3, leanY.Y);
                }

                shader.SetParameter(WeatherTints[lane], new Color(layer.Color.R * _weatherLight.R,
                    layer.Color.G * _weatherLight.G, layer.Color.B * _weatherLight.B, alpha));
            }

            SetSheet(shader, near, far, eyeStep, plane0, eyeRel, time);
            shader.SetParameter("FOG_PASS", 0f);
            shader.SetParameter("FOG_FIELD", Texture.White);
            shader.SetParameter("LAYERS", (float) layers);
            shader.SetParameter("CELL_X", cellX);
            shader.SetParameter("CELL_Y", cellY);
            shader.SetParameter("CELL_Z", cellZ);
            shader.SetParameter("SPACING", spacings);
            shader.SetParameter("ALONG", alongs);
            shader.SetParameter("DENSITY", densities);
            shader.SetParameter("STREAK", streaks);
            shader.SetParameter("THICK", thicks);
            shader.SetParameter("WOBBLE", wobbles);
            shader.SetParameter("MODE", modes);
            shader.SetParameter("SEED", seeds);
            shader.SetParameter("DIR_X01", dirX01);
            shader.SetParameter("DIR_X23", dirX23);
            shader.SetParameter("DIR_Y01", dirY01);
            shader.SetParameter("DIR_Y23", dirY23);
            handle.UseShader(shader);
            handle.DrawRect(new Box2(Vector2.Zero, _viewSize), Color.White);
        }
    }

    private ShaderInstance SheetShader(int sheet, int group)
    {
        if (!_weatherShaders.TryGetValue((sheet, group), out var shader))
        {
            shader = _prototypes.Index(WeatherShader).InstanceUnique();
            _weatherShaders[(sheet, group)] = shader;
        }

        return shader;
    }

    /// <summary>Gives a slice's shader what its fog and falling passes share: the view, the slice and the floors.</summary>
    private void SetSheet(ShaderInstance shader, float near, float far, Vector2 eyeStep, Vector2 plane0, Vector2 eyeRel,
        float time)
    {
        shader.SetParameter("VIEW", _viewSize);
        shader.SetParameter("FOCAL", _focal);
        shader.SetParameter("PITCH", new Vector2(_pitchSin, _pitchCos));
        shader.SetParameter("EYE_HEIGHT", CameraModesSystem.EyeHeight);
        shader.SetParameter("ROOF", _system.CeilingHeight);
        shader.SetParameter("LOOK_RIGHT", _right);
        shader.SetParameter("LOOK_UP", _up);
        shader.SetParameter("LOOK_HALF", _system.LookHalf);
        shader.SetParameter("NEAR", near);
        shader.SetParameter("FAR", far);
        shader.SetParameter("STEP", PlaneStep);
        shader.SetParameter("EYE_STEP", eyeStep);
        shader.SetParameter("PLANE0", plane0);
        shader.SetParameter("EYE_REL", eyeRel);
        shader.SetParameter("mask", _weatherMask!);
        shader.SetParameter("maskEye", _weatherMaskEye);
        shader.SetParameter("maskSize", _weatherMaskSize);
        shader.SetParameter("FLOORS", _floorsOn ? _rainFloor!.Texture : _weatherMask!);
        shader.SetParameter("FLOORS_ON", _floorsOn ? 1f : 0f);
        shader.SetParameter("EYE_UV", _lookUv);
        shader.SetParameter("AXIS_R", ScreenUv(_eye + _right) - _lookUv);
        shader.SetParameter("AXIS_F", ScreenUv(_eye + _up) - _lookUv);
        shader.SetParameter("LOWER_COUNT", (float) _floorCount);
        shader.SetParameter("LOWER_DROP", _floorDrops);
        shader.SetParameter("LOWER_DROP2", _floorDrops2);
        shader.SetParameter("time", time);
        SetFog(shader);
    }

    /// <summary>
    /// Adds a fog layer to this frame's fog. Fog is drawn by distance, so several layers merge into one: the thickest
    /// sets the drift and the patch size, and their covers add up. The first four layers are also kept as banks.
    /// </summary>
    private void AddFog(WolfWeatherLayer layer, WeatherDrift drift, float strength)
    {
        var amount = layer.Color.A * strength;
        if (amount <= 0f)
            return;

        var spacing = MathF.Max(layer.Spacing, 1f / WeatherPixelsPerTile);
        var thick = FogThickness * layer.Density;
        var color = new Vector3(layer.Color.R, layer.Color.G, layer.Color.B) * _fogLight;
        if (!_fogOn || thick > _fogThick)
        {
            _fogThick = thick;
            _fogScale = 1f / spacing;
            _fogCell = new Vector2((float) WrapCell((_eye.X - drift.WindX) / spacing),
                (float) WrapCell((_eye.Y - drift.WindY) / spacing));
        }

        _fogColor = _fogOn ? Vector3.Lerp(_fogColor, color, amount / MathF.Max(_fogMax + amount, 1e-4f)) : color;
        _fogMax = 1f - (1f - _fogMax) * (1f - amount);
        _fogOn = true;

        if (_fogBanks.Count < LayersPerPass)
        {
            _fogBanks.Add(new FogBank
            {
                Cell = new Vector2((float) WrapCell((_eye.X - drift.WindX) / spacing),
                    (float) WrapCell((_eye.Y - drift.WindY) / spacing)),
                Spacing = spacing,
                Density = layer.Density,
                Softness = layer.Softness,
                Alpha = amount,
            });
        }
    }

    /// <summary>
    /// Draws the top-down picture of the fog's banks round the eye, which the fog slices read. First person only, and
    /// only with a fog layer on.
    /// </summary>
    private void DrawFogField(DrawingHandleWorld handle)
    {
        _fieldOn = false;
        if (!_first || _fogBanks.Count == 0 || _weatherMask == null)
            return;

        _fogFieldShader ??= _prototypes.Index(FogFieldShader).InstanceUnique();
        _fogField ??= _clyde.CreateRenderTarget(new Vector2i(FogFieldSize, FogFieldSize),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8),
            new TextureSampleParameters { Filter = true },
            "WGCameraModeFogField");

        Vector4 cellX = default, cellY = default, spacings = default, densities = default, softs = default, alphas = default;
        for (var lane = 0; lane < _fogBanks.Count; lane++)
        {
            var bank = _fogBanks[lane];
            SetLane(ref cellX, lane, bank.Cell.X);
            SetLane(ref cellY, lane, bank.Cell.Y);
            SetLane(ref spacings, lane, bank.Spacing);
            SetLane(ref densities, lane, bank.Density);
            SetLane(ref softs, lane, bank.Softness);
            SetLane(ref alphas, lane, bank.Alpha);
        }

        var shader = _fogFieldShader;
        shader.SetParameter("SIZE", (float) FogFieldSize);
        shader.SetParameter("TILES", FogFieldTiles);
        shader.SetParameter("SCALE", FogBankThickness);
        shader.SetParameter("LAYERS", (float) _fogBanks.Count);
        shader.SetParameter("CELL_X", cellX);
        shader.SetParameter("CELL_Y", cellY);
        shader.SetParameter("SPACING", spacings);
        shader.SetParameter("DENSITY", densities);
        shader.SetParameter("SOFT", softs);
        shader.SetParameter("ALPHA", alphas);
        shader.SetParameter("FOG_MASK", _weatherMask);
        shader.SetParameter("FOG_MASK_EYE", _weatherMaskEye);
        shader.SetParameter("FOG_MASK_SIZE", _weatherMaskSize);
        handle.RenderInRenderTarget(_fogField, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(shader);
            handle.DrawRect(Box2.FromDimensions(Vector2.Zero, new Vector2(FogFieldSize)), Color.White);
            handle.UseShader(null);
        }, new Color(0f, 0f, 0f, 0f));

        _fieldOn = true;
    }

    /// <summary>Gives a shader this frame's fog and the cover mask that keeps it out from under roofs.</summary>
    private void SetFog(ShaderInstance shader)
    {
        shader.SetParameter("FOG_ON", _fogOn ? 1f : 0f);
        shader.SetParameter("FOG_COLOR", _fogColor);
        shader.SetParameter("FOG_MAX", _fogMax);
        shader.SetParameter("FOG_THICK", _fogThick);
        shader.SetParameter("FOG_SCALE", _fogScale);
        shader.SetParameter("FOG_CELL", _fogCell);
        shader.SetParameter("FOG_LOOK_RIGHT", _right);
        shader.SetParameter("FOG_LOOK_UP", _up);
        // The weather overlay frees masks it stops using, so with no mask a shader gets white, not the old one.
        shader.SetParameter("FOG_MASK", _weatherMask ?? Texture.White);

        shader.SetParameter("FOG_MASK_EYE", _weatherMaskEye);
        shader.SetParameter("FOG_MASK_SIZE", _fogOn ? _weatherMaskSize : Vector2.Zero);
    }

    /// <summary>
    /// Gives a shader the weather's picture of the falling rain, which the 2.5D views lay over the solids it falls in
    /// front of. First person draws its own.
    /// </summary>
    private void SetRain(ShaderInstance shader)
    {
        _wolfWeather ??= _entities.System<WolfWeatherSystem>();
        var picture = _first ? null : _wolfWeather.RainPicture;
        shader.SetParameter("RAIN_ON", picture != null ? 1f : 0f);
        shader.SetParameter("RAIN_TEXTURE", picture ?? Texture.White);
    }

    private static double WrapCell(double value)
    {
        var r = value % WeatherCellPeriod;
        return r < 0 ? r + WeatherCellPeriod : r;
    }

    private void DisposeWeather()
    {
        foreach (var shader in _weatherShaders.Values)
        {
            shader.Dispose();
        }

        _weatherShaders.Clear();
        _rainFloorShader?.Dispose();
        _rainFloorShader = null;
        _rainFloor?.Dispose();
        _rainFloor = null;
        _fogFieldShader?.Dispose();
        _fogFieldShader = null;
        _fogField?.Dispose();
        _fogField = null;
    }
}
