using Content.Shared.Damage;
using Content.Shared.FixedPoint;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Hits harder, moves faster and throws more often while the mob has taken at least <see cref="Threshold"/> damage.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaEnrageComponent : Component
{
    [DataField]
    public FixedPoint2 Threshold = 300;

    [DataField(required: true)]
    public DamageSpecifier Damage = new();

    [DataField]
    public float Speed = 3.5f;

    [DataField]
    public float ThrowChance = 0.45f;

    /// <summary>
    /// Attack rate multiplier while enraged.
    /// </summary>
    [DataField]
    public float AttackRate = 1f;

    /// <summary>
    /// How long the rage lasts. Without it, the mob stays enraged while past the threshold.
    /// </summary>
    [DataField]
    public TimeSpan? Duration;

    /// <summary>
    /// How long the mob is exhausted after a timed rage, before it can rage again.
    /// </summary>
    [DataField]
    public TimeSpan Exhaustion;

    /// <summary>
    /// Walk and sprint speed while exhausted.
    /// </summary>
    [DataField]
    public float ExhaustedSpeed = 2f;

    /// <summary>
    /// Brute healed when a timed rage starts.
    /// </summary>
    [DataField]
    public FixedPoint2 Heal;

    /// <summary>
    /// Shakes off stuns and knockdowns when the rage starts.
    /// </summary>
    [DataField]
    public bool ClearStuns;

    [DataField]
    public LocId? Popup;

    [DataField]
    public LocId? ExhaustedPopup;

    [DataField, AutoPausedField]
    public TimeSpan RageEnd;

    [DataField, AutoPausedField]
    public TimeSpan ExhaustionEnd;

    [ViewVariables]
    public bool Exhausted;

    [ViewVariables]
    public bool Enraged;

    /// <summary>
    /// Throw chance to restore when the rage ends.
    /// </summary>
    [ViewVariables]
    public float NormalThrowChance;
}
