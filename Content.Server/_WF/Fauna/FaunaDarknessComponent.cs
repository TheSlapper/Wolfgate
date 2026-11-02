using Content.Shared.Damage;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Heals the mob while no light reaches it, or hurts it while one does, and fades it out with Stealth while it is
/// dark, idle and nearly unhurt.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaDarknessComponent : Component
{
    /// <summary>
    /// Healing per check while dark, as negative damage. Null for no healing.
    /// </summary>
    [DataField]
    public DamageSpecifier? Heal;

    /// <summary>
    /// Damage per check while a light reaches the mob.
    /// </summary>
    [DataField]
    public DamageSpecifier? LitDamage;

    /// <summary>
    /// Stealth visibility while hidden, null to never hide.
    /// </summary>
    [DataField]
    public float? HiddenVisibility = -0.6f;

    /// <summary>
    /// The mob only hides with at least this fraction of its health left.
    /// </summary>
    [DataField]
    public float HideHealth = 0.9f;

    /// <summary>
    /// Lights are only searched this far out.
    /// </summary>
    [DataField]
    public float LightRange = 10f;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(2);

    [DataField, AutoPausedField]
    public TimeSpan NextCheck;

    [ViewVariables]
    public bool Dark;

    [ViewVariables]
    public bool Hidden;
}
