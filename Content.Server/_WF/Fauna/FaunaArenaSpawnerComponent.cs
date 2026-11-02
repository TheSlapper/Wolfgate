using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Marks the centre of a demo arena. <see cref="FaunaArenaButtonComponent"/> buttons on the same map pick a mob
/// from <see cref="Roster"/>, spawn it here still and turn its AI on.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaArenaSpawnerComponent : Component
{
    [DataField(required: true)]
    public List<EntProtoId> Roster = new();

    /// <summary>
    /// Leftover mobs, eggs and spawners within this range are deleted when a new mob is spawned.
    /// </summary>
    [DataField]
    public float ClearRadius = 11f;

    [ViewVariables]
    public int Index = -1;

    [ViewVariables]
    public EntityUid? Spawned;
}
