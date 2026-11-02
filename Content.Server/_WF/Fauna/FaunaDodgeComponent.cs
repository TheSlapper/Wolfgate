using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets projectiles pass through the mob with a chance equal to its dodge pool, which drains as it dodges.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaDodgeComponent : Component
{
    [DataField]
    public float Pool = 100f;

    [DataField]
    public float MaxPool = 100f;

    /// <summary>
    /// Pool regained per second.
    /// </summary>
    [DataField]
    public float Regen = 0.6f;

    /// <summary>
    /// Chance that a dodge costs pool.
    /// </summary>
    [DataField]
    public float UseChance = 0.55f;

    [DataField]
    public float Usage = 8f;

    /// <summary>
    /// Steps the mob one tile aside on each dodge that costs pool.
    /// </summary>
    [DataField]
    public bool SideStep;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public LocId Popup = "fauna-dodge";

    /// <summary>
    /// Whether each projectile seen so far was dodged, since a contact is checked every physics step.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, bool> Seen = new();
}
