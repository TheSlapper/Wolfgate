namespace Content.Shared._WG.CameraModes;

/// <summary>Overrides the shape the 3D views pick for an entity from its sprite and collision.</summary>
[RegisterComponent]
public sealed partial class CameraShapeComponent : Component
{
    [DataField]
    public CameraShape Shape = CameraShape.Cutout;
}

/// <summary>How the 3D views build an entity.</summary>
public enum CameraShape : byte
{
    /// <summary>A flat upright picture across the entity's facing, fixed in the world, like a billboard sign.</summary>
    Card,

    /// <summary>
    /// The whole picture standing on the front and back of the collision area, with walls across the depth following
    /// its outline, even when it is as large as a vehicle.
    /// </summary>
    Cutout,
}
