namespace Content.Server._WF.Fauna;

/// <summary>
/// Hops to a random free tile next to it after landing a melee hit.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaHitAndRunComponent : Component
{
    [DataField]
    public float Chance = 1f;

    [DataField]
    public float Speed = 6f;
}
