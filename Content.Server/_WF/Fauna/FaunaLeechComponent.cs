using Content.Shared.Damage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A melee hit on a humanoid may latch the mob onto it. A latched mob rides its victim, burns it and heals itself.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaLeechComponent : Component
{
    [DataField]
    public float Chance = 0.3f;

    /// <summary>
    /// Dealt to the victim each feed.
    /// </summary>
    [DataField]
    public DamageSpecifier? Damage;

    /// <summary>
    /// Applied to the mob each feed, as negative damage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Heal;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(3);

    [DataField]
    public float PainChance = 0.15f;

    /// <summary>
    /// The latch breaks when the victim gets further away than this.
    /// </summary>
    [DataField]
    public float Range = 1.5f;

    /// <summary>
    /// The latch breaks once the victim's Burn damage passes this fraction of its Dead threshold.
    /// </summary>
    [DataField]
    public float BurnLimit = 1.5f;

    [DataField]
    public LocId? LatchPopup;

    /// <summary>
    /// Shown to the victim only.
    /// </summary>
    [DataField]
    public LocId? PainPopup;

    [DataField]
    public LocId? ReleasePopup;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    [ViewVariables]
    public EntityUid? Victim;
}
