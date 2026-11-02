namespace Content.Server._WF.Fauna;

/// <summary>
/// Drains hunger from what the mob hits into its own store and buds off a copy once the store is full.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaHungerDrainComponent : Component
{
    [DataField]
    public float MinDrain = 1f;

    [DataField]
    public float MaxDrain = 50f;

    /// <summary>
    /// Chance that a hit also deals stamina damage equal to the drained amount.
    /// </summary>
    [DataField]
    public float StaminaChance = 0.25f;

    [DataField]
    public float Stored;

    [DataField]
    public float BudAt = 250f;

    [DataField]
    public float BudCost = 200f;

    /// <summary>
    /// Chance per <see cref="TickInterval"/> to bud while the store is full.
    /// </summary>
    [DataField]
    public float BudChance = 0.1f;

    [DataField]
    public TimeSpan BudCooldown = TimeSpan.FromSeconds(120);

    /// <summary>
    /// No budding while this many mobs of the same prototype are alive.
    /// </summary>
    [DataField]
    public int MaxAlive = 50;

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    [DataField, AutoPausedField]
    public TimeSpan NextBud;
}
