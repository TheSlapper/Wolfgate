using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Picks a new projectile from <see cref="Prototypes"/> for the mob's basic entity ammo after every shot.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaRandomAmmoComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = new();
}
