using System.Diagnostics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WG.CameraModes.Render;

/// <summary>
/// Gathers the scene and hides the stock sprites of objects drawn as solids before the world is drawn.
/// </summary>
public sealed class CameraModeHideOverlay : Overlay
{
    private readonly CameraModesSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowWorld;

    public CameraModeHideOverlay(CameraModesSystem system)
    {
        _system = system;
        ZIndex = int.MinValue;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.IsMainPass(args))
            return;

        var start = Stopwatch.GetTimestamp();
        _system.Collect(args);
        _system.AddTime(0, start);
    }
}

/// <summary>
/// Gives hidden sprites back after the world is drawn, before the interface draws them. A type of its own, as the
/// overlay manager holds one overlay of each type.
/// </summary>
public sealed class CameraModeRestoreOverlay : Overlay
{
    private readonly CameraModesSystem _system;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowFOV;

    public CameraModeRestoreOverlay(CameraModesSystem system)
    {
        _system = system;
        ZIndex = int.MaxValue;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (_system.IsMainPass(args))
            _system.RestoreColors();
    }
}
