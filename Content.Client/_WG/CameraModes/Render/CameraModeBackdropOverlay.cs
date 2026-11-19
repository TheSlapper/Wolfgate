using System.Diagnostics;
using System.Numerics;
using Content.Shared._WG.CameraModes;
using Robust.Client.Graphics;
using Robust.Shared.Enums;
using Robust.Shared.Graphics;
using Robust.Shared.Prototypes;

namespace Content.Client._WG.CameraModes.Render;

/// <summary>
/// Takes the background the flat view draws under the world, once every overlay that draws it is done, and gives it
/// back at the flat view's shape and size. The tilted views get it redrawn in place before the world. First person
/// keeps a copy that its sky shows below the eye.
/// </summary>
public sealed class CameraModeBackdropOverlay : Overlay
{
    private static readonly ProtoId<ShaderPrototype> BackdropShader = "WGCameraModeBackdrop";

    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    private readonly CameraModesSystem _system;
    private readonly ShaderInstance _shader;
    private IRenderTexture? _target;
    private int _frame = -1;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public override bool RequestScreenTexture => true;

    /// <summary>The flat view's background taken this frame for first person, or null when there is none.</summary>
    public Texture? FirstPersonBackdrop => _frame == _system.Frame ? _target?.Texture : null;

    public CameraModeBackdropOverlay(CameraModesSystem system)
    {
        IoCManager.InjectDependencies(this);
        _system = system;
        _shader = _prototypes.Index(BackdropShader).InstanceUnique();
        // After every overlay that draws the background.
        ZIndex = int.MaxValue;
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        // A level below is world seen through the floor, not background, so the tilted views leave it be. First
        // person takes the picture for its background, or in orbit to lay the level below at its depth.
        if (!_system.Active || !_system.IsMainPass(args))
            return false;

        return _system.FirstPerson
            ? !_system.TakesLevels || _system.FirstPersonBackdrop(args.MapUid)
            : !_system.HasLevelBelow(args.MapUid);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (ScreenTexture == null)
            return;

        var start = Stopwatch.GetTimestamp();
        DrawBackdrop(args);
        _system.AddTime(2, start);
    }

    private void DrawBackdrop(in OverlayDrawArgs args)
    {
        var toLocal = args.Viewport.GetWorldToLocalMatrix();
        var size = args.Viewport.Size;
        var centre = _system.ViewCentre;
        var pivot = Uv(centre);
        _shader.SetParameter("SCREEN_TEXTURE", ScreenTexture!);
        _shader.SetParameter("PIVOT", pivot);
        _shader.SetParameter("AXIS_R", Uv(centre + _system.Right) - pivot);
        _shader.SetParameter("AXIS_U", Uv(centre + _system.Up) - pivot);

        var handle = args.WorldHandle;
        if (_system.FirstPerson)
        {
            // The first-person picture is flat, so the copy is one to one.
            _shader.SetParameter("LAMBDA", 0f);
            _shader.SetParameter("SIGMA", 1f);
            if (_target == null || _target.Size != size)
            {
                _target?.Dispose();
                _target = _clyde.CreateRenderTarget(size,
                    new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
                    new TextureSampleParameters { Filter = true },
                    nameof(CameraModeBackdropOverlay));
            }

            handle.RenderInRenderTarget(_target, () =>
            {
                handle.SetTransform(Matrix3x2.Identity);
                handle.UseShader(_shader);
                handle.DrawRect(new Box2(Vector2.Zero, size), Color.White);
                handle.UseShader(null);
            }, Color.Black);

            _frame = _system.Frame;
            return;
        }

        var (lambda, sigma) = _system.Mode == CameraMode.Perspective
            ? CameraModeMeshOverlay.Warp(_system.Profile, size)
            : (0f, 1f);

        _shader.SetParameter("LAMBDA", lambda);
        _shader.SetParameter("SIGMA", sigma);
        handle.SetTransform(Matrix3x2.Identity);
        handle.UseShader(_shader);
        handle.DrawRect(args.WorldBounds, Color.White);
        handle.UseShader(null);

        Vector2 Uv(Vector2 world)
        {
            var local = Vector2.Transform(world, toLocal);
            return new Vector2(local.X / size.X, 1f - local.Y / size.Y);
        }
    }

    protected override void DisposeBehavior()
    {
        base.DisposeBehavior();
        _target?.Dispose();
        _target = null;
    }
}
