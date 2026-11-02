namespace Content.Server._WF.Fauna;

/// <summary>
/// A demo arena button that picks the previous or next mob, starts the fight or restocks the item tables.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaArenaButtonComponent : Component
{
    [DataField(required: true)]
    public FaunaArenaAction Action;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Not saved, so a press before a map save doesn't lock the button in the next round.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPress;
}

public enum FaunaArenaAction : byte
{
    Previous,
    Next,
    Fight,
    Restock,
}
