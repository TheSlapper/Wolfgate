namespace Content.Server._WF.Fauna;

/// <summary>
/// Respawns every display mob on its map, switching them between still and live.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDisplayButtonComponent : Component
{
    [ViewVariables]
    public bool Live;

    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Not saved, so a press before a map save doesn't lock the button in the next round.
    /// </summary>
    [ViewVariables]
    public TimeSpan NextPress;
}
