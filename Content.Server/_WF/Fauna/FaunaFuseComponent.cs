namespace Content.Server._WF.Fauna;

/// <summary>
/// Explodes the mob through its Explosive component: after a fuse when it lands a melee hit, at once when it dies,
/// and by chance when something damages it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaFuseComponent : Component
{
    [DataField]
    public TimeSpan FuseTime = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Chance that a hit of at least <see cref="MinDamage"/> sets it off at once.
    /// </summary>
    [DataField]
    public float BurstChance = 0.2f;

    [DataField]
    public float MinDamage = 5f;

    /// <summary>
    /// Jitter amplitude the mob reaches as a lit fuse runs out. Zero turns it off.
    /// </summary>
    [DataField]
    public float Jitter;

    [ViewVariables]
    public int JitterStep;

    [DataField, AutoPausedField]
    public TimeSpan ExplodeAt;

    [ViewVariables]
    public bool Exploded;
}
