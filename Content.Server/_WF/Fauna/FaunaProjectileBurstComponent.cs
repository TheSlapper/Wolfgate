using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Fires bursts of projectiles: a spread or a spiral when its action is used, and a ring when the mob dies.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaProjectileBurstComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Projectile;

    [DataField]
    public float Speed = 15f;

    /// <summary>
    /// Shots fanned around the target's direction.
    /// </summary>
    [DataField]
    public int SpreadCount = 3;

    [DataField]
    public Angle SpreadStep = Angle.FromDegrees(10);

    /// <summary>
    /// Chance the action fires a spiral instead of the spread.
    /// </summary>
    [DataField]
    public float SpiralChance;

    [DataField]
    public int SpiralCount = 10;

    [DataField]
    public Angle SpiralStep = Angle.FromDegrees(180 / Math.E);

    [DataField]
    public TimeSpan SpiralDelay = TimeSpan.FromSeconds(0.3);

    /// <summary>
    /// When above zero, the action fires this many shots one after another at the target instead of the spread,
    /// each off by up to <see cref="VolleyJitter"/>.
    /// </summary>
    [DataField]
    public int VolleyCount;

    [DataField]
    public Angle VolleyJitter = Angle.FromDegrees(10);

    [DataField]
    public TimeSpan VolleyDelay = TimeSpan.FromSeconds(0.1);

    /// <summary>
    /// A hit taken during the wind-up or a volley cancels the rest of it.
    /// </summary>
    [DataField]
    public bool CancelOnHit;

    /// <summary>
    /// Delay between using the action and the first shot.
    /// </summary>
    [DataField]
    public TimeSpan Windup = TimeSpan.Zero;

    [DataField]
    public SoundSpecifier? WindupSound;

    [DataField]
    public SoundSpecifier? SpreadSound;

    [DataField]
    public SoundSpecifier? SpiralSound;

    [DataField]
    public LocId? WindupPopup;

    /// <summary>
    /// Turns the mob's PointLight on during the wind-up and off after it.
    /// </summary>
    [DataField]
    public bool WindupLight;

    /// <summary>
    /// Shakes the mob this hard during the wind-up. 0 for no shake.
    /// </summary>
    [DataField]
    public float WindupJitter;

    [DataField]
    public LocId? SpreadPopup;

    [DataField]
    public LocId? SpiralPopup;

    /// <summary>
    /// Shots fired in an even ring when the mob dies or is destroyed. 0 for none.
    /// </summary>
    [DataField]
    public int DeathCount;

    [DataField]
    public EntityUid? Target;

    /// <summary>
    /// When the wind-up ends and the burst fires. Null when no wind-up is running.
    /// </summary>
    [DataField, AutoPausedField]
    public TimeSpan? FireAt;

    [DataField]
    public int SpiralShotsLeft;

    [DataField, AutoPausedField]
    public TimeSpan NextSpiralShot;

    [DataField]
    public int VolleyShotsLeft;

    [DataField, AutoPausedField]
    public TimeSpan NextVolleyShot;

    [DataField]
    public bool DeathBurstDone;

    /// <summary>
    /// Projectiles still in flight, so their shooter can be cleared when this mob is deleted.
    /// </summary>
    [ViewVariables]
    public List<EntityUid> Shots = new();

    /// <summary>
    /// Projectile chosen by the action that started the current burst, if it overrides <see cref="Projectile"/>.
    /// </summary>
    [ViewVariables]
    public EntProtoId? ActionProjectile;
}
