using Content.Client.Viewport;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;

namespace Content.Client._WG.CameraModes;

/// <summary>
/// Raised before and after the z-level view draws a level below the player, so a view can draw it as a picture of
/// its own.
/// </summary>
[ByRefEvent]
public record struct WGZLevelPassEvent(
    ScalingViewport Control,
    IClydeViewport Viewport,
    IRenderHandle Handle,
    EntityUid Map,
    float Depth,
    ScalingViewport.ZEye Eye,
    IEye Base)
{
    /// <summary>Whether the level has been drawn.</summary>
    public bool After;
}
