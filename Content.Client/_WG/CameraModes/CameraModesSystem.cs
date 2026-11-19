using System.Diagnostics;
using System.Numerics;
using Content.Client.Administration.Managers;
using Content.Client.Eye;
using Content.Client.UserInterface.Controls;
using Content.Client.Viewport;
using Content.Client._WG.CameraModes.Atlas;
using Content.Client._WG.CameraModes.Render;
using Content.Shared._WG.CameraModes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics;

namespace Content.Client._WG.CameraModes;

/// <summary>
/// The 2.5D views. The floor stays the stock render, squashed through the eye. Raised sprites are hidden from it and
/// drawn again as solids by <see cref="CameraModeMeshOverlay"/>.
/// </summary>
public sealed partial class CameraModesSystem : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPlayerManager _player = default!;
    [Dependency] private IUserInterfaceManager _ui = default!;
    [Dependency] private IClyde _clyde = default!;
    [Dependency] private IClientAdminManager _admin = default!;

    /// <summary>Time the isometric turn takes to swing in or out.</summary>
    private const float YawSeconds = 0.25f;

    private int _mode;
    private int _debug;
    private float _debugTimer;

    private int _debugFrame;
    private uint _viewportFrame = uint.MaxValue;
    private ScalingViewport? _mainViewport;

    /// <summary>Time spent in each stage since the last debug line, in stopwatch ticks.</summary>
    private readonly long[] _stageTicks = new long[3];

    /// <summary>Adds the time since a stopwatch timestamp to a stage: 0 gather, 1 draw, 2 background.</summary>
    public void AddTime(int stage, long start)
    {
        _stageTicks[stage] += Stopwatch.GetTimestamp() - start;
    }
    private int? _preview;
    private bool _billboards;
    private bool? _billboardsPreview;
    private bool? _retroPreview;
    private int? _fovPreview;
    private Angle _yaw;
    private bool _eyeTouched;

    private CameraModeHideOverlay? _hide;
    private CameraModeHideOverlay? _restore;
    private CameraModeBackdropOverlay? _backdrop;
    private CameraModeMeshOverlay? _mesh;
    private SpriteAtlas? _atlas;

    /// <summary>The view in use: the options menu preview if one is open, else the saved cvar.</summary>
    public CameraMode Mode => (_preview ?? _mode) is var mode && Enum.IsDefined(typeof(CameraMode), (byte) mode)
        ? (CameraMode) mode
        : CameraMode.Off;

    /// <summary>Whether any 3D view is on.</summary>
    public bool Active => Mode != CameraMode.Off;

    /// <summary>Whether objects stand as cards facing the camera, unsaved menu choice included.</summary>
    public bool Billboards => _billboardsPreview ?? _billboards;

    /// <summary>The tilt of the current view.</summary>
    public CameraModeProfile Profile => CameraModeProfile.For(Mode);

    /// <summary>Whether solids skip the light test, for debugging. Admins only, since it shows mobs in the dark.</summary>
    public bool DebugUnlit => _debug >= 2 && _admin.IsActive();

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(EyeLerpingSystem));
        Subs.CVar(_cfg, CameraModeCVars.Mode, value => _mode = value, true);
        Subs.CVar(_cfg, CameraModeCVars.Debug, value => _debug = value, true);
        Subs.CVar(_cfg, CameraModeCVars.Billboards, value => _billboards = value, true);
        InitializeScene();
        InitializeClick();
        InitializeLook();
        InitializeLevels();
    }

    public override void Shutdown()
    {
        base.Shutdown();
        DisposeLevels();
        RemoveOverlays();
        RestoreEye();
        EndLook();
    }

    /// <summary>Shows an unsaved choice from the options menu while it is open.</summary>
    public void SetPreview(int mode, bool billboards, bool retro, int fov)
    {
        _preview = mode;
        _billboardsPreview = billboards;
        _retroPreview = retro;
        _fovPreview = fov;
    }

    /// <summary>Drops the options menu preview and goes back to the saved choices.</summary>
    public void ClearPreview()
    {
        _preview = null;
        _billboardsPreview = null;
        _retroPreview = null;
        _fovPreview = null;
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        RestoreColors();

        // Movement turns with the view on screen, so a preview in the options window moves the way it looks.
        var iso = Mode == CameraMode.Isometric;
        if (_cfg.GetCVar(CameraModeCVars.IsoInput) != iso)
            _cfg.SetCVar(CameraModeCVars.IsoInput, iso);

        UpdateLook();
        UpdateLevelBlur();
        if (!Active)
        {
            RemoveOverlays();
            RestoreEye();
            return;
        }

        AddOverlays();
        ApplyEye(frameTime);
        UpdateHover();
        LogDebug(frameTime);
    }

    private void LogDebug(float frameTime)
    {
        if (_debug <= 0)
            return;

        _debugTimer += frameTime;
        if (_debugTimer < 1f)
            return;

        var frames = Math.Max(Frame - _debugFrame, 1);
        var perFrame = 1000.0 / Stopwatch.Frequency / frames;
        var gather = _stageTicks[0] * perFrame;
        var draw = _stageTicks[1] * perFrame;
        var back = _stageTicks[2] * perFrame;
        Array.Clear(_stageTicks);
        _debugFrame = Frame;
        _debugTimer = 0f;
        var pending = 0;
        foreach (var entry in _geometry.Values)
        {
            if (entry.Mesh == null && entry.Analysis == null)
                pending++;
        }

        var flats = 0;
        foreach (var obj in Drawn)
        {
            if (obj.Kind == SceneKind.Flat)
                flats++;
        }

        Log.Info($"mode {Mode}: collected {Collected}, drawn {Drawn.Count} ({flats} on tables, {_surfaces.Count} table tiles), " +
                 $"quads {(_mesh?.VertexCount ?? 0) / 4}, looks {_geometry.Count} ({pending} unread), " +
                 $"cells {_atlas?.Count ?? 0} in {_atlas?.Size ?? 0} px, left flat: atlas full {_droppedFull}, waiting {_droppedWaiting}, " +
                 $"main pass frames {Frame}, " +
                 $"ms per frame: gather {gather:F2}, draw {draw:F2}, background {back:F2}");
        _droppedFull = 0;
        _droppedWaiting = 0;
    }

    /// <summary>The main game viewport, the only one the views change.</summary>
    public ScalingViewport? MainViewport()
    {
        // Asked for by every overlay pass and every mark, so it is looked up once a frame.
        if (_viewportFrame != _timing.CurFrame)
        {
            _viewportFrame = _timing.CurFrame;
            _mainViewport = _ui.ActiveScreen?.GetWidget<MainViewport>()?.Viewport;
        }

        return _mainViewport;
    }

    /// <summary>Squashes the floor and turns the view after the eye has been lerped for this frame.</summary>
    private void ApplyEye(float frameTime)
    {
        if (_player.LocalEntity is not { } local || !TryComp<EyeComponent>(local, out var eye))
            return;

        var profile = Profile;
        var step = (float) (CameraModeInputSystem.IsoTurn.Theta * frameTime / YawSeconds);
        var diff = (profile.Yaw - _yaw).Theta;
        _yaw = new Angle(_yaw.Theta + Math.Clamp(diff, -step, step));

        IEye view = eye.Eye;
        _eyeTouched = true;
        if (FirstPerson)
        {
            ApplyLook(local, eye, view);
            return;
        }

        view.Rotation = eye.Rotation - _yaw;
        view.Zoom = eye.Zoom * new Vector2(profile.Squash, 1f);
    }

    private void RestoreEye()
    {
        _yaw = Angle.Zero;
        if (!_eyeTouched)
            return;

        _eyeTouched = false;
        if (_player.LocalEntity is not { } local || !TryComp<EyeComponent>(local, out var eye))
            return;

        IEye view = eye.Eye;
        view.Rotation = eye.Rotation;
        view.Zoom = eye.Zoom;
        view.Offset = eye.Offset;
    }

    private void AddOverlays()
    {
        if (_mesh != null)
            return;

        _atlas = new SpriteAtlas(_clyde);
        _hide = new CameraModeHideOverlay(this, false);
        _restore = new CameraModeHideOverlay(this, true);
        _backdrop = new CameraModeBackdropOverlay(this);
        _mesh = new CameraModeMeshOverlay(this, _atlas, _backdrop);
        _overlays.AddOverlay(_hide);
        _overlays.AddOverlay(_restore);
        _overlays.AddOverlay(_backdrop);
        _overlays.AddOverlay(_mesh);
    }

    private void RemoveOverlays()
    {
        if (_mesh == null)
            return;

        _overlays.RemoveOverlay(_hide!);
        _overlays.RemoveOverlay(_restore!);
        _overlays.RemoveOverlay(_backdrop!);
        _overlays.RemoveOverlay(_mesh);
        _mesh.Dispose();
        _backdrop!.Dispose();
        _atlas!.Dispose();
        _hide = null;
        _restore = null;
        _backdrop = null;
        _mesh = null;
        _atlas = null;
        RestoreColors();
        ClearScene();
    }
}

/// <summary>How a view tilts: the floor squash, the height scale and the turn.</summary>
public readonly record struct CameraModeProfile(float Squash, float Height, Angle Yaw)
{
    /// <summary>Sine of the camera's angle above the floor.</summary>
    public float PitchSin => Squash;

    /// <summary>Cosine of the camera's angle above the floor.</summary>
    public float PitchCos => Height;

    /// <summary>The tilt for a view.</summary>
    public static CameraModeProfile For(CameraMode mode)
    {
        return mode switch
        {
            // 45 degrees above the floor.
            CameraMode.Perspective => new CameraModeProfile(0.7071f, 0.7071f, Angle.Zero),
            // 35.26 degrees above the floor, turned 45 degrees: the classic isometric angle.
            CameraMode.Isometric => new CameraModeProfile(0.5774f, 0.8165f, CameraModeInputSystem.IsoTurn),
            // The floor stays flat and unturned. The view is drawn from it by its own projection.
            CameraMode.FirstPerson => new CameraModeProfile(1f, 1f, Angle.Zero),
            _ => new CameraModeProfile(1f, 0f, Angle.Zero),
        };
    }
}
