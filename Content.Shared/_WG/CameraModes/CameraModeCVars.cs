using Robust.Shared.Configuration;

namespace Content.Shared._WG.CameraModes;

/// <summary>Settings for the 2.5D views.</summary>
[CVarDefs]
public sealed class CameraModeCVars
{
    /// <summary>The view, as a <see cref="CameraMode"/>.</summary>
    public static readonly CVarDef<int> Mode =
        CVarDef.Create("wg.camera_mode", 0, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Whether the view on screen is isometric, preview included. Set by the client every time that changes and
    /// replicated so the server turns movement with it. Never saved.
    /// </summary>
    public static readonly CVarDef<bool> IsoInput =
        CVarDef.Create("wg.camera_iso_input", false, CVar.CLIENT | CVar.REPLICATED);

    /// <summary>Width of the first-person view, in degrees.</summary>
    public static readonly CVarDef<int> FieldOfView =
        CVarDef.Create("wg.camera_fov", 90, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>How fast the mouse turns the first-person view. 1 turns 0.022 degrees for each mouse count.</summary>
    public static readonly CVarDef<float> LookSensitivity =
        CVarDef.Create("wg.camera_look_sensitivity", 2.5f, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Whether first person keeps the look of early 3D consoles: bending images, jittering edges, coarse dithered
    /// pixels.
    /// </summary>
    public static readonly CVarDef<bool> Retro =
        CVarDef.Create("wg.camera_retro", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Whether objects stand as upright cards facing the camera instead of solids. Walls, windows, doors, tables and
    /// things lying on the floor or on a wall keep their shape.
    /// </summary>
    public static readonly CVarDef<bool> Billboards =
        CVarDef.Create("wg.camera_billboards", false, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Console only. How far first person sees ahead: 0 the normal view, -1 the view zoomed out as far as the player
    /// may, -2 the whole range the server sends, or a number of tiles.
    /// </summary>
    public static readonly CVarDef<int> ViewRange =
        CVarDef.Create("wg.camera_view_range", 0, CVar.ARCHIVE | CVar.CLIENTONLY);

    /// <summary>
    /// Console only. First person shows the background centred on the world's north horizon instead of straight down.
    /// </summary>
    public static readonly CVarDef<bool> BackdropAhead =
        CVarDef.Create("wg.camera_backdrop_ahead", false, CVar.CLIENTONLY);

    /// <summary>
    /// Console only. 1 logs what the view draws once a second. 2 also draws solids without light or line of sight.
    /// </summary>
    public static readonly CVarDef<int> Debug =
        CVarDef.Create("wg.camera_mode_debug", 0, CVar.CLIENTONLY);
}
