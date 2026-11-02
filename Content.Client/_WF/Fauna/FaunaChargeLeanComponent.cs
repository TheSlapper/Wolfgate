using System.Numerics;

namespace Content.Client._WF.Fauna;

/// <summary>
/// Client state of a fauna mob's wind-up lean.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaChargeLeanComponent : Component
{
    [ViewVariables]
    public bool Active;

    [ViewVariables]
    public bool Leaning;

    [ViewVariables]
    public Vector2 Direction;

    [ViewVariables]
    public Vector2 BaseOffset;

    [ViewVariables]
    public TimeSpan Start;
}
