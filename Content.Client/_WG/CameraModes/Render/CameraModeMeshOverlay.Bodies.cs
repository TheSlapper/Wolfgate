using System.Numerics;
using Content.Client._FarHorizons.StarSystem;
using Content.Client._WG.BlackHole;
using Content.Client._WG.Lattice;
using Content.Shared._FarHorizons.StarSystem;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WG.Lattice;
using Content.Shared._WG.PlanetScan;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.CameraModes.Render;

public sealed partial class CameraModeMeshOverlay
{
    private static readonly ProtoId<ShaderPrototype> BodyShader = "WGCameraModeSkyBody";

    /// <summary>Size of the panorama the sector's bodies are painted into, see-through where there is none.</summary>
    private const int BodyWidth = 2048;
    private const int BodyHeight = 1024;

    /// <summary>Size of one planet's own picture, in pixels: one in the panorama, and the nearest.</summary>
    private const int BodyPicture = 512;
    private const int NearPicture = 2048;

    /// <summary>Size of the black hole's picture, in pixels.</summary>
    private const int HolePicture = 1024;

    /// <summary>
    /// The nearest planet's picture holds the part of it in view and this share of that part more on every side, so a
    /// turn of the view stays inside it for a while.
    /// </summary>
    private const float NearSpare = 0.4f;

    /// <summary>The smallest part of the nearest planet its picture holds, as a share of the whole.</summary>
    private const float NearSmallest = 0.02f;

    /// <summary>The view is tried at this many points a side to find the part of the nearest planet in it.</summary>
    private const int NearProbes = 9;

    /// <summary>How hard the black hole bends the sky behind it, as the flat view's shader does.</summary>
    private const float HoleLens = 0.85f;
    private const float HoleLensUnmoored = 1.2f;

    /// <summary>The bodies are painted again this often, as the planets' pictures move.</summary>
    private const float BodyRedrawSeconds = 0.25f;

    /// <summary>
    /// How far under the ship's plane a planet's middle lies, in its own radii. Over 1, so the eye is outside it.
    /// </summary>
    private const float BodyDepth = 1.15f;

    /// <summary>The radius a far planet is never drawn smaller than, in radians.</summary>
    private const float BodySmallest = 0.02f;

    /// <summary>How far past its core a planet's picture reaches, in core radii, so its air and rings fit.</summary>
    private const float BodyReach = 1.35f;
    private const float BodyRingMargin = 0.15f;

    /// <summary>How much of a planet's light comes from over its far edge, against the star's side.</summary>
    private const float BodyLightAhead = 0.8f;

    private const string SpaceHoleShader = "WGBlackHole";

    /// <summary>The black hole's radius in the sky from far off and from close by, in radians.</summary>
    private const float HoleSmallest = 0.05f;
    private const float HoleLargest = 0.45f;

    /// <summary>How far over the horizon the black hole's lower edge stands, in radians.</summary>
    private const float HoleClear = 0.04f;

    /// <summary>Black with no cover, what the pictures are blended onto.</summary>
    private static readonly Color Nothing = new(0f, 0f, 0f, 0f);

    private readonly Dictionary<Planet, BodyArt> _bodyArt = new();
    private readonly List<SkyBody> _bodies = new();
    private PlanetOverlay? _bodyBuilder;
    private PlanetarySystem? _bodySystem;
    private IRenderTexture? _bodyTarget;
    private float _bodyTime = float.MinValue;

    /// <summary>The nearest planet, drawn straight onto the screen, and its picture once that is drawn.</summary>
    private SkyBody _nearBody;
    private Planet? _nearPlanet;
    private BodyArt? _nearArt;

    /// <summary>The part of the nearest planet its picture holds, in core radii from its middle.</summary>
    private Box2 _nearWindow;

    private StarOverlay? _holeBuilder;
    private Star? _holeStar;
    private ShaderInstance? _holeLook;
    private IRenderTexture? _holePicture;
    private bool _holeShown;
    private bool _holeOver;
    private Vector3 _holeDirection;
    private float _holeSize;
    private float _holeLens;

    private sealed class BodyArt
    {
        public IRenderTexture Picture = default!;
        public ShaderInstance? Look;
        public ShaderInstance Globe = default!;
    }

    private struct SkyBody
    {
        public Planet Planet;
        public Vector3 Direction;
        public float Size;
        public float Far;
        public Vector2 Light;
    }

    /// <summary>
    /// Readies the sector's planets and its black hole for the first-person sky, each toward where it lies. A planet
    /// is a globe of its radar size whose middle lies under the ship's plane: a disc near the horizon from far off, the
    /// whole view below the horizon from over it. The nearest planet and the black hole are drawn straight onto the
    /// screen from their pictures (<see cref="SetNearBodies"/>). The other planets are painted into the panorama
    /// this returns, null on a map with none.
    /// </summary>
    private Texture? DrawBodies(DrawingHandleWorld handle, in OverlayDrawArgs args)
    {
        _nearArt = null;
        _holeShown = false;
        if (!_entities.TryGetComponent(args.MapUid, out StarSystemMapComponent? map)
            || map.StarSystem is not { } system)
        {
            return null;
        }

        var eye = args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center;
        _holeShown = TrySpaceHole(args, out _holeDirection, out _holeSize, out var holeFar)
            && DrawHolePicture(handle, args.MapUid, system.Star, eye);
        _bodies.Clear();
        foreach (var planet in system.Planets)
        {
            var radius = WGPlanetScanReader.ScopeRadius(planet, null);
            if (radius <= 0f)
                continue;

            var to = planet.Position - eye;
            var depth = radius * BodyDepth;
            var far = MathF.Sqrt(to.LengthSquared() + depth * depth);
            var toStar = system.Star.Position - planet.Position;
            var across = new Vector2(-to.Y, to.X);
            var lit = across.LengthSquared() > 0.0001f && toStar.LengthSquared() > 0.0001f
                ? Vector2.Dot(Vector2.Normalize(toStar), Vector2.Normalize(across))
                : 0f;
            _bodies.Add(new SkyBody
            {
                Planet = planet,
                Direction = new Vector3(to.X, to.Y, -depth) / far,
                Size = MathF.Max(MathF.Asin(MathF.Min(radius / far, 1f)), BodySmallest),
                Far = far,
                // The picture's up is the globe's far edge, the part seen from close by.
                Light = Vector2.Normalize(new Vector2(lit, -BodyLightAhead)),
            });
        }

        if (_bodies.Count == 0)
            return null;

        // The nearest is painted last, over the others.
        _bodies.Sort(static (a, b) => b.Far.CompareTo(a.Far));
        _nearBody = _bodies[^1];
        _bodies.RemoveAt(_bodies.Count - 1);
        _holeOver = _holeShown && holeFar < _nearBody.Far;

        _bodyTarget ??= _clyde.CreateRenderTarget((BodyWidth, BodyHeight),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            nameof(CameraModeMeshOverlay));

        var time = (float) _timing.RealTime.TotalSeconds;
        var stale = time - _bodyTime >= BodyRedrawSeconds || !ReferenceEquals(_bodySystem, system)
            || !ReferenceEquals(_nearPlanet, _nearBody.Planet);
        if (!ReferenceEquals(_bodySystem, system))
            DropBodyArt();

        // The nearest planet's picture is also drawn again as soon as the view turns past the part it holds.
        var reach = Reach(_nearBody.Planet);
        var seen = NearView(_nearBody, reach);
        if (stale || !_nearWindow.Encloses(seen))
        {
            var spare = seen.Size * NearSpare + new Vector2(reach * NearSmallest);
            _nearWindow = new Box2(MathF.Max(seen.Left - spare.X, -reach), MathF.Max(seen.Bottom - spare.Y, -reach),
                MathF.Min(seen.Right + spare.X, reach), MathF.Min(seen.Top + spare.Y, reach));
            DrawBodyPicture(handle, system, _nearBody, NearPicture, _nearWindow);
        }

        _nearArt = _bodyArt[_nearBody.Planet];
        if (!stale)
            return _bodyTarget.Texture;

        _bodySystem = system;
        _bodyTime = time;
        _nearPlanet = _nearBody.Planet;
        foreach (var body in _bodies)
        {
            var whole = Reach(body.Planet);
            DrawBodyPicture(handle, system, body, BodyPicture, new Box2(-whole, -whole, whole, whole));
        }

        var area = new Vector2(BodyWidth, BodyHeight);
        handle.RenderInRenderTarget(_bodyTarget, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            foreach (var body in _bodies)
            {
                var art = _bodyArt[body.Planet];
                art.Globe.SetParameter("BODY_PICTURE", art.Picture.Texture);
                art.Globe.SetParameter("SKY_SIZE", area);
                art.Globe.SetParameter("BODY_DIR", body.Direction);
                art.Globe.SetParameter("BODY_SIZE", body.Size);
                art.Globe.SetParameter("BODY_REACH", Reach(body.Planet));
                handle.UseShader(art.Globe);
                handle.DrawRect(Box2.FromDimensions(Vector2.Zero, area), Color.White);
            }

            handle.UseShader(null);
        }, Nothing);
        return _bodyTarget.Texture;
    }

    /// <summary>Hands the first-person shader the nearest planet and the black hole it draws straight onto the screen.</summary>
    /// <param name="spare">A texture to bind where there is none.</param>
    private void SetNearBodies(ShaderInstance blit, Texture spare)
    {
        blit.SetParameter("NEAR_PICTURE", _nearArt?.Picture.Texture ?? spare);
        blit.SetParameter("NEAR_ON", _nearArt != null ? 1f : 0f);
        blit.SetParameter("NEAR_DIR", _nearBody.Direction);
        blit.SetParameter("NEAR_SIZE", _nearBody.Size);
        blit.SetParameter("NEAR_MIDDLE", _nearWindow.Center);
        blit.SetParameter("NEAR_SPAN", Vector2.Max(_nearWindow.Size, new Vector2(0.0001f)));
        blit.SetParameter("HOLE_PICTURE", _holeShown ? _holePicture!.Texture : spare);
        blit.SetParameter("HOLE_ON", _holeShown ? 1f : 0f);
        blit.SetParameter("HOLE_OVER", _holeOver ? 1f : 0f);
        blit.SetParameter("HOLE_DIR", _holeDirection);
        blit.SetParameter("HOLE_SIZE", _holeSize);
        blit.SetParameter("HOLE_REACH", BlackHoleVisuals.EffectRadius);
        blit.SetParameter("HOLE_LENS", _holeLens);
    }

    /// <summary>
    /// The part of a planet's globe the view shows, in core radii across and up from its middle, as
    /// <c>firstperson.swsl</c> lays it. With none of it in view, a point at its middle.
    /// </summary>
    private Box2 NearView(SkyBody body, float reach)
    {
        var middle = new Vector3(Vector2.Dot(new Vector2(body.Direction.X, body.Direction.Y), _right),
            Vector2.Dot(new Vector2(body.Direction.X, body.Direction.Y), _up), body.Direction.Z);
        var over = MathF.Abs(middle.Z) > 0.999f ? Vector3.UnitY : Vector3.UnitZ;
        var side = Vector3.Normalize(Vector3.Cross(middle, over));
        var rise = Vector3.Cross(side, middle);
        var scale = 1f / MathF.Sin(body.Size);
        var low = new Vector2(float.MaxValue);
        var high = new Vector2(float.MinValue);
        for (var y = 0; y < NearProbes; y++)
        {
            for (var x = 0; x < NearProbes; x++)
            {
                var p = new Vector2(x / (NearProbes - 1f) - 0.5f, y / (NearProbes - 1f) - 0.5f) * _viewSize / _focal;
                var ray = Vector3.Normalize(new Vector3(p.X, _pitchCos - p.Y * _pitchSin, _pitchSin + p.Y * _pitchCos));
                if (Vector3.Dot(ray, middle) <= 0f)
                    continue;

                var at = Vector2.Clamp(new Vector2(Vector3.Dot(ray, side), Vector3.Dot(ray, rise)) * scale,
                    new Vector2(-reach), new Vector2(reach));
                low = Vector2.Min(low, at);
                high = Vector2.Max(high, at);
            }
        }

        return low.X > high.X ? default : new Box2(low, high);
    }

    /// <summary>
    /// Draws one planet into its own picture of a size through its planet shader, lit from the star's side. Its core
    /// has radius one.
    /// </summary>
    /// <param name="window">The part of it the picture holds, in core radii from its middle.</param>
    private BodyArt DrawBodyPicture(DrawingHandleWorld handle, PlanetarySystem system, SkyBody body, int pixels,
        Box2 window)
    {
        var planet = body.Planet;
        if (!_bodyArt.TryGetValue(planet, out var art))
        {
            _bodyBuilder ??= new PlanetOverlay(_entities, _prototypes);
            art = new BodyArt
            {
                Picture = BodyTarget(pixels),
                Look = _bodyBuilder.SetupPlanetShader(planet, system.Star),
                Globe = _prototypes.Index(BodyShader).InstanceUnique(),
            };
            _bodyArt[planet] = art;
        }
        else if (art.Picture.Size.X != pixels)
        {
            art.Picture.Dispose();
            art.Picture = BodyTarget(pixels);
        }

        var look = art.Look;
        if (look != null)
        {
            look.SetParameter("planetaryRadiusFactor", 1f);
            look.SetParameter("planetRadius", 1f);
            look.SetParameter("parallaxCenter", planet.Position);
            // The picture's up is the shader's down.
            look.SetParameter("viewportMin", planet.Position + new Vector2(window.Left, -window.Top));
            look.SetParameter("viewportSize", window.Size);
            look.SetParameter("starPos", planet.Position + body.Light * 1000f);
        }

        var box = Box2.FromDimensions(Vector2.Zero, new Vector2(pixels));
        handle.RenderInRenderTarget(art.Picture, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            if (look == null)
            {
                handle.DrawCircle(box.Center - window.Center * new Vector2(1f, -1f) / window.Size * pixels,
                    pixels / window.Width, Color.Gray);
                return;
            }

            handle.UseShader(look);
            handle.DrawRect(box, Color.White);
            handle.UseShader(null);
        }, Nothing);
        return art;
    }

    private IRenderTexture BodyTarget(int pixels)
    {
        return _clyde.CreateRenderTarget((pixels, pixels),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            nameof(CameraModeMeshOverlay));
    }

    /// <summary>
    /// Draws the black hole alone into its picture through the flat view's own shader, its shadow of radius one in
    /// the middle. False where that shader is missing.
    /// </summary>
    private bool DrawHolePicture(DrawingHandleWorld handle, EntityUid? map, Star star, Vector2 eye)
    {
        _holeBuilder ??= new StarOverlay(_entities, _prototypes);
        if (!ReferenceEquals(_holeStar, star))
        {
            _holeStar = star;
            _holeLook = _holeBuilder.BlackHolePictureShader(star);
        }

        if (_holeLook is not { } look)
            return false;

        _holePicture ??= BodyTarget(HolePicture);
        var unmooring = _holeBuilder.AimBlackHolePicture(look, star, eye, map);
        _holeLens = HoleLens * (1f + unmooring * HoleLensUnmoored);
        var box = Box2.FromDimensions(Vector2.Zero, new Vector2(HolePicture));
        handle.RenderInRenderTarget(_holePicture, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(look);
            handle.DrawRect(box, Color.White);
            handle.UseShader(null);
        }, Nothing);
        return true;
    }

    /// <summary>
    /// The sector's black hole as the sky shows it: low over the horizon toward where it lies, larger the nearer it
    /// is. False on a map without one, and once it has fallen in.
    /// </summary>
    /// <param name="size">The radius of its shadow as the eye sees it, in radians.</param>
    /// <param name="far">How far off it lies, in tiles.</param>
    private bool TrySpaceHole(in OverlayDrawArgs args, out Vector3 direction, out float size, out float far)
    {
        direction = Vector3.UnitY;
        size = 0f;
        far = 0f;
        if (!_entities.TryGetComponent(args.MapUid, out StarSystemMapComponent? system)
            || system.StarSystem?.Star is not { } star || star.Shader != SpaceHoleShader)
        {
            return false;
        }

        var view = _entities.System<WGLatticeViewSystem>();
        if (view.Collapsed())
            return false;

        var swell = view.ImplosionSeconds(args.MapUid, out _) is { } seconds ? WGLatticeImplosion.Scale(seconds) : 1f;
        if (swell <= 0.001f)
            return false;

        var to = star.Position - (args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center);
        far = MathF.Max(to.Length(), 1f);
        // The flat view draws it at a share of its distance, so it fills the view from as far off here.
        size = MathF.Atan(star.Radius * BlackHoleVisuals.SolarRadiusFactor / (far * BlackHoleVisuals.ParallaxFactor));
        size = MathF.Min(Math.Clamp(size, HoleSmallest, HoleLargest) * swell, HoleLargest);
        var rise = HoleClear + size * 1.2f;
        direction = new Vector3(to.X / far * MathF.Cos(rise), to.Y / far * MathF.Cos(rise), MathF.Sin(rise));
        return true;
    }

    private static float Reach(Planet planet)
    {
        return planet.Rings is { } rings ? MathF.Max(BodyReach, rings.RadiusOuter + BodyRingMargin) : BodyReach;
    }

    private void DropBodyArt()
    {
        foreach (var art in _bodyArt.Values)
        {
            art.Picture.Dispose();
        }

        _bodyArt.Clear();
        _nearArt = null;
        _nearPlanet = null;
    }

    private void DisposeBodies()
    {
        DropBodyArt();
        _bodyTarget?.Dispose();
        _bodyTarget = null;
        _holePicture?.Dispose();
        _holePicture = null;
    }
}
