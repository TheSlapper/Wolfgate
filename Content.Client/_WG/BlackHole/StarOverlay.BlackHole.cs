using System.Numerics;
using Content.Client._WG.BlackHole;
using Content.Client._WG.CameraModes;
using Content.Client._WG.Lattice;
using Content.Shared._FarHorizons.StarSystem.Helpers;
using Content.Shared._WG.Lattice;
using Robust.Client.Graphics;

namespace Content.Client._FarHorizons.StarSystem;

/// <summary>Refracts the parallax behind an accreting black hole before ships are drawn.</summary>
public sealed partial class StarOverlay
{
    private const string BlackHoleShader = "WGBlackHole";

    /// <summary>Only black holes need a copy of the background for refraction.</summary>
    public override bool RequestScreenTexture => _star?.Shader == BlackHoleShader;

    /// <summary>How big Kyphrus is drawn now: swelling and falling in at Tacet, otherwise its size.</summary>
    private float _blackHoleScale = 1f;

    /// <summary>How far through its swell it is, 0 to 1, for the shader.</summary>
    private float _blackHoleSwell;

    /// <summary>Culls only black holes, using the same footprint and parallax as the shader. After it falls in there is none.</summary>
    private bool BlackHoleVisible(Star star, in OverlayDrawArgs args)
    {
        if (star.Shader != BlackHoleShader)
            return true;

        // First person shows it in its own sky.
        if (_entMan.System<CameraModesSystem>().FirstPersonPass(args))
            return false;

        var view = _entMan.System<WGLatticeViewSystem>();
        if (view.Collapsed())
            return false;

        var seconds = view.ImplosionSeconds(args.MapUid, out _);
        _blackHoleScale = seconds is { } s ? WGLatticeImplosion.Scale(s) : 1f;
        _blackHoleSwell = seconds is { } t ? Math.Clamp(t / WGLatticeImplosion.Swell, 0f, 1f) : 0f;
        if (_blackHoleScale <= 0.001f)
            return false;

        return BlackHoleVisuals.IsVisible(star.Position, star.Radius * _blackHoleScale,
            args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center, args.WorldAABB);
    }

    /// <summary>A black hole's shader for drawing it alone into a square picture. Null for any other star.</summary>
    public ShaderInstance? BlackHolePictureShader(Star star)
    {
        return star.Shader == BlackHoleShader ? SetupStarShader(star) : null;
    }

    /// <summary>
    /// Sets such a shader to fill its picture with the hole as it is now, its shadow of radius one in the middle and
    /// nothing behind it. Returns how far the Lattice has failed, 0 to 1.
    /// </summary>
    public float AimBlackHolePicture(ShaderInstance shader, Star star, Vector2 eye, EntityUid? map)
    {
        var view = _entMan.System<WGLatticeViewSystem>();
        var swell = view.ImplosionSeconds(map, out _) is { } t ? Math.Clamp(t / WGLatticeImplosion.Swell, 0f, 1f) : 0f;
        var unmooring = swell > 0f ? 1f : view.Unmooring();
        var reach = star.Radius * BlackHoleVisuals.SolarRadiusFactor * BlackHoleVisuals.EffectRadius;
        shader.SetParameter("parallaxFactor", 1f);
        shader.SetParameter("solarRadiusFactor", BlackHoleVisuals.SolarRadiusFactor);
        shader.SetParameter("swell", swell);
        shader.SetParameter("effectRadius", BlackHoleVisuals.EffectRadius);
        shader.SetParameter("hasBackground", false);
        shader.SetParameter("unmooring", unmooring);
        shader.SetParameter("parallaxCenter", eye);
        shader.SetParameter("viewportMin", star.Position - new Vector2(reach));
        shader.SetParameter("viewportSize", new Vector2(reach * 2f));
        return unmooring;
    }

    /// <summary>Projects world displacements into fragment pixels, including camera rotation and zoom.</summary>
    private void PrepareBlackHoleShader(in OverlayDrawArgs args)
    {
        if (_star?.Shader != BlackHoleShader || _shaderInstance == null)
            return;

        _shaderInstance.SetParameter("parallaxFactor", BlackHoleVisuals.ParallaxFactor);
        _shaderInstance.SetParameter("solarRadiusFactor", BlackHoleVisuals.SolarRadiusFactor * _blackHoleScale);
        _shaderInstance.SetParameter("swell", _blackHoleSwell);
        _shaderInstance.SetParameter("effectRadius", BlackHoleVisuals.EffectRadius);
        _shaderInstance.SetParameter("hasBackground", ScreenTexture != null);
        _shaderInstance.SetParameter("unmooring", _blackHoleSwell > 0f ? 1f : _entMan.System<WGLatticeViewSystem>().Unmooring());
        if (ScreenTexture == null)
            return;

        var origin = args.Viewport.WorldToLocal(Vector2.Zero);
        var right = args.Viewport.WorldToLocal(Vector2.UnitX) - origin;
        var up = args.Viewport.WorldToLocal(Vector2.UnitY) - origin;
        right.Y = -right.Y;
        up.Y = -up.Y;
        _shaderInstance.SetParameter("SCREEN_TEXTURE", ScreenTexture);
        _shaderInstance.SetParameter("worldToScreenX", right);
        _shaderInstance.SetParameter("worldToScreenY", up);
    }
}
