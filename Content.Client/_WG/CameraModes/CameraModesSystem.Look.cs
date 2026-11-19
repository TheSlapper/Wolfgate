using System.Numerics;
using Content.Client.Actions;
using Content.Client.CombatMode;
using Content.Client.Hands;
using Content.Client._WG.CameraModes.UI;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Content.Shared._WG.CameraModes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private ActionsSystem _actions = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private IGameTiming _timing = default!;

    private static readonly EntProtoId MouselookAction = "WGActionMouselook";

    /// <summary>Height of the first-person eye above the floor, in tiles.</summary>
    public const float EyeHeight = 0.8f;

    /// <summary>Turn for each mouse count at a sensitivity of 1, in degrees.</summary>
    private const float DegreesPerCount = 0.022f;

    /// <summary>How far the first-person view tilts up or down.</summary>
    private static readonly float MaxPitch = MathHelper.DegreesToRadians(50f);

    private Angle _look;
    private Angle _lookSent;
    private bool _looking;
    private bool _turned;
    private bool _paused;
    private EntityUid? _lookAction;
    private EntityUid? _lookOwner;
    private MouselookCapture? _capture;
    private readonly List<Overlay> _cursorOverlays = new();

    /// <summary>Whether the first-person view is on.</summary>
    public bool FirstPerson => Mode == CameraMode.FirstPerson;

    /// <summary>Whether first person keeps its bending, jittering, dithered look.</summary>
    public bool Retro => _retroPreview ?? _cfg.GetCVar(CameraModeCVars.Retro);

    /// <summary>The entity the first-person view looks out of, which it does not draw.</summary>
    public EntityUid? LocalBody => _player.LocalEntity;

    /// <summary>Whether the mouse turns the view in place of its normal use.</summary>
    public bool Mouselook => _capture != null;

    /// <summary>Upward tilt of the first-person view, in radians.</summary>
    public float Pitch { get; private set; }

    /// <summary>Focal length of the first-person view for a viewport this wide, in viewport pixels.</summary>
    public float Focal(float width)
    {
        var fov = Math.Clamp(_fovPreview ?? _cfg.GetCVar(CameraModeCVars.FieldOfView), 30, 150);
        return width / 2f / MathF.Tan(MathHelper.DegreesToRadians(fov) / 2f);
    }

    /// <summary>
    /// Half the size of the area first person shows, in tiles across and ahead of the look. It is the flat view's
    /// rectangle turned with the look, so it reaches as far at every turn.
    /// </summary>
    public Vector2 LookHalf { get; private set; } = new(15f, 7.5f);

    /// <summary>
    /// How far the eye zooms out so the stock picture, which first person draws from, holds the look's rectangle at any
    /// turn. The picture turns in quarters, so the look sits at most 45 degrees off it.
    /// </summary>
    private float LookZoom(EntityUid uid, Vector2 zoom)
    {
        if (MainViewport() is not { } viewport)
            return 1f;

        var flat = (Vector2) viewport.ViewportSize / EyeManager.PixelsPerMeter / 2f;
        var half = flat * zoom;
        var range = _cfg.GetCVar(CameraModeCVars.ViewRange);
        if (range == -1)
        {
            // The view zoomed out as far as the player may.
            var max = TryComp<ContentEyeComponent>(uid, out var content) ? MathF.Max(content.MaxZoom.Y, 1f) : 1f;
            half = flat * max;
        }
        else if (range != 0)
        {
            // The player can never see farther than the server sends.
            var sent = _cfg.GetCVar(CVars.NetMaxUpdateRange);
            var tiles = range < 0 || range > sent && !_admin.IsActive() ? sent : range;
            half = flat * (tiles / flat.Y);
        }

        LookHalf = half;
        var tall = (half.X + half.Y) / MathF.Sqrt(2f) + 0.5f;
        var wide = half.Length() + 0.5f;
        return MathF.Max(MathF.Max(tall / flat.Y, wide / flat.X) / zoom.Y, 0.1f);
    }

    private void InitializeLook()
    {
        SubscribeLocalEvent<WGMouselookActionEvent>(OnMouselookAction);
    }

    private void OnMouselookAction(WGMouselookActionEvent args)
    {
        SetMouselook(!Mouselook);
        args.Handled = true;
    }

    /// <summary>Turns the view by a mouse movement, in mouse counts.</summary>
    public void TurnLook(Vector2 counts)
    {
        if (!Mouselook)
            return;

        var step = MathHelper.DegreesToRadians(DegreesPerCount * _cfg.GetCVar(CameraModeCVars.LookSensitivity));
        _look = (_look - counts.X * step).Reduced();
        Pitch = Math.Clamp(Pitch - counts.Y * step, -MaxPitch, MaxPitch);
        _looking = true;
        _turned = true;
    }

    /// <summary>Turns mouselook on or off. It only turns on in first person while the view can turn.</summary>
    public void SetMouselook(bool on)
    {
        // A server that locks the view's turn also locks the walking frame, which mouselook would leave behind.
        if (on == Mouselook || on && (!FirstPerson || _mover.CameraRotationLocked))
            return;

        _clyde.MainWindow.SetRelativeMouseMode(on);
        if (on)
        {
            // These two draw at the mouse, which is hidden and not where the player aims.
            if (_overlays.TryGetOverlay<ShowHandItemOverlay>(out var item) && _overlays.RemoveOverlay(item))
                _cursorOverlays.Add(item);

            if (_overlays.TryGetOverlay<CombatModeIndicatorsOverlay>(out var combat) && _overlays.RemoveOverlay(combat))
                _cursorOverlays.Add(combat);

            _capture = new MouselookCapture(this, _input);
            _ui.PopupRoot.AddChild(_capture);
            LayoutContainer.SetAnchorPreset(_capture, LayoutContainer.LayoutPreset.Wide);
        }
        else
        {
            _capture!.Orphan();
            _capture = null;
            _paused = false;
            foreach (var overlay in _cursorOverlays)
            {
                _overlays.AddOverlay(overlay);
            }

            _cursorOverlays.Clear();
            // The stock turn takes over from where the mouse left the view.
            if (_looking)
                SendLook();

            _looking = false;
        }

        _actions.SetToggled(_lookAction, on);
    }

    /// <summary>
    /// Gives the player the mouselook action while first person is on. Ends mouselook when the game window loses
    /// focus, and pauses it while a window is open over the game.
    /// </summary>
    private void UpdateLook()
    {
        var local = _player.LocalEntity;
        if (!FirstPerson || local != _lookOwner)
            EndLook();

        if (!FirstPerson || local is not { } player)
            return;

        if (_lookAction == null)
        {
            _lookAction = Spawn(MouselookAction);
            _lookOwner = player;
            _actions.AddActionDirect(player, _lookAction.Value);
        }

        if (Mouselook && (!_clyde.MainWindow.IsFocused || _mover.CameraRotationLocked))
            SetMouselook(false);

        // An open window, like the game menu, needs the mouse. Mouselook waits until the last one closes.
        if (_capture is not { } capture)
            return;

        var window = WindowOpen();
        if (window == _paused)
            return;

        _paused = window;
        capture.Visible = !window;
        _clyde.MainWindow.SetRelativeMouseMode(!window);
    }

    /// <summary>Whether any window is open over the game.</summary>
    private bool WindowOpen()
    {
        foreach (var child in _ui.WindowRoot.Children)
        {
            if (child.Visible)
                return true;
        }

        return false;
    }

    /// <summary>Leaves first person: the mouse is freed, the action goes and the view squares up.</summary>
    private void EndLook()
    {
        // The server turns whatever body the player has now, so a turn read from a body they left isn't sent.
        var sameBody = _player.LocalEntity == _lookOwner;
        if (!sameBody)
            _looking = false;

        SetMouselook(false);
        if (_lookAction != null)
        {
            _actions.RemoveAction(_lookAction);
            _lookAction = null;
        }

        if (sameBody && _turned && TryComp<InputMoverComponent>(_lookOwner, out var mover))
        {
            _look = mover.TargetRelativeRotation.GetCardinalDir().ToAngle();
            SendLook();
        }

        _lookOwner = null;
        _looking = false;
        _turned = false;
        Pitch = 0f;
    }

    /// <summary>World direction the first-person view looks along, on the floor.</summary>
    public Vector2 LookUp { get; private set; } = Vector2.UnitY;

    /// <summary>World direction to the right of the first-person view.</summary>
    public Vector2 LookRight { get; private set; } = Vector2.UnitX;

    /// <summary>
    /// Sets the eye for first person. The view looks where the mouse has turned it, or else where the stock eye
    /// turns. The stock picture the floor is read from only turns in whole quarters from the grid, and its middle sits
    /// on a whole pixel of the grid, so its pixel art is never resampled at a slant or by a fraction of a pixel. The
    /// floor pass turns its rays by the rest.
    /// </summary>
    private void ApplyLook(EntityUid uid, EyeComponent eye, IEye view)
    {
        var grid = Angle.Zero;
        var look = -eye.Rotation;
        if (TryComp<InputMoverComponent>(uid, out var mover))
        {
            grid = _mover.GetParentGridAngle(mover) - mover.RelativeRotation;
            if (_looking)
            {
                look = grid + _look;
            }
            else
            {
                _look = mover.RelativeRotation;
                _lookSent = _look;
            }
        }

        LookUp = look.RotateVec(Vector2.UnitY);
        LookRight = new Vector2(LookUp.Y, -LookUp.X);

        const double quarter = Math.PI / 2;
        var quarters = Math.Round((look - grid).Theta / quarter);
        view.Rotation = -(grid + new Angle(quarters * quarter));
        view.Zoom = eye.Zoom * LookZoom(uid, eye.Zoom);

        // The middle of the picture on a whole pixel, counted in the frame of the grid the player stands on.
        var centre = view.Position.Position + eye.Offset;
        var pixel = 1f / (_mesh is { PicturePixelsPerTile: > 0f } mesh
            ? mesh.PicturePixelsPerTile
            : EyeManager.PixelsPerMeter / view.Zoom.X);
        var gridUid = Transform(uid).GridUid;
        var toGrid = gridUid is { } g ? _transform.GetInvWorldMatrix(g) : Matrix3x2.Identity;
        var toWorld = gridUid is { } h ? _transform.GetWorldMatrix(h) : Matrix3x2.Identity;
        var local = Vector2.Transform(centre, toGrid);
        var snapped = new Vector2(MathF.Round(local.X / pixel) * pixel, MathF.Round(local.Y / pixel) * pixel);
        view.Offset = Vector2.Transform(snapped, toWorld) - view.Position.Position;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (_looking && _timing.IsFirstTimePredicted && !_look.EqualsApprox(_lookSent))
            SendLook();
    }

    private void SendLook()
    {
        _lookSent = _look;
        RaisePredictiveEvent(new CameraLookEvent { Rotation = _look });
    }
}
