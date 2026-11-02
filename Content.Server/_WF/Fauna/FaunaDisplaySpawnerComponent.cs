using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Marks a mob's spot on a demo map. Spawns the mob with its AI off at map init and respawns it when a
/// <see cref="FaunaDisplayButtonComponent"/> on the same map is pressed.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDisplaySpawnerComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Prototype;

    [ViewVariables]
    public EntityUid? Spawned;
}
