using System.Diagnostics;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WG.CameraModes.Render;

/// <summary>
/// Gathers the scene and hides the stock sprites of objects drawn as solids before the world is drawn, or, as the
/// restore instance, gives them back after it, before any system can see the change.
/// </summary>
public sealed class CameraModeHideOverlay : Overlay
{
    private readonly CameraModesSystem _system;
    private readonly bool _restore;

    public override OverlaySpace Space => _restore ? OverlaySpace.WorldSpaceBelowFOV : OverlaySpace.WorldSpaceBelowWorld;

    public CameraModeHideOverlay(CameraModesSystem system, bool restore)
    {
        _system = system;
        _restore = restore;
        ZIndex = restore ? int.MaxValue : int.MinValue;
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        if (!_system.IsMainPass(args))
            return;

        if (_restore)
            _system.RestoreColors();
        else
        {
            var start = Stopwatch.GetTimestamp();
            _system.Collect(args);
            _system.AddTime(0, start);
        }
    }
}
