using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Containers;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A melee hit on a downed humanoid pulls it into the mob, which grows tougher and heals. Captives take damage until
/// they die and drop out.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaEngulfComponent : Component
{
    [DataField]
    public string ContainerId = "fauna_engulf";

    [ViewVariables]
    public Container Container = default!;

    /// <summary>
    /// Added to the mob's Dead threshold for each captive. It is kept after the captive drops out.
    /// </summary>
    [DataField]
    public FixedPoint2 ThresholdBonus = 250;

    /// <summary>
    /// Applied to the mob on each engulf, as negative damage.
    /// </summary>
    [DataField]
    public DamageSpecifier? Heal;

    [DataField]
    public DamageSpecifier? CaptiveDamage;

    /// <summary>
    /// Chance per check that each captive takes <see cref="CaptiveDamage"/>.
    /// </summary>
    [DataField]
    public float CaptiveChance = 0.15f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [DataField]
    public LocId? Popup;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
