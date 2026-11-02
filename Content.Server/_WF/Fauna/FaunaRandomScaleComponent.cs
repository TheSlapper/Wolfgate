namespace Content.Server._WF.Fauna;

/// <summary>
/// Gives the mob a random sprite size on spawn, like a source mob's random size.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaRandomScaleComponent : Component
{
    [DataField]
    public float Min = 0.8f;

    [DataField]
    public float Max = 1.1f;
}
