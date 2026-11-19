namespace Content.Shared._WG.CameraModes;

/// <summary>
/// The views a player can pick. Stored as an int in <see cref="CameraModeCVars.Mode"/>. A saved 1 names no view and
/// reads as Off.
/// </summary>
public enum CameraMode : byte
{
    /// <summary>The stock flat view.</summary>
    Off = 0,

    /// <summary>Tilted 45 degrees with perspective.</summary>
    Perspective = 2,

    /// <summary>Tilted 35.26 degrees and turned 45 degrees, without perspective.</summary>
    Isometric = 3,

    /// <summary>Seen from the player's eyes.</summary>
    FirstPerson = 4,
}
