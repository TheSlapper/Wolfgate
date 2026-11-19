using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._WG.CameraModes;

/// <summary>Gives a map the sky first person draws over it.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CameraSkyComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<CameraSkyPrototype> Sky;

    /// <summary>How far the map is from its star on the sector map. 0 when it has no place there.</summary>
    [AutoNetworkedField]
    public float StarDistance;
}
