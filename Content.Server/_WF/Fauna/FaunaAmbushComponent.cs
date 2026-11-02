using Content.Shared._WF.Fauna;
using Content.Shared.Damage;
using Robust.Shared.Audio;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Keeps a mob hidden until it attacks, and plays whispers while it closes in on its target.
/// </summary>
// Hidden means nearly invisible with Stealth, otherwise the FaunaAmbushVisuals.Revealed appearance.
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaAmbushComponent : Component
{
    /// <summary>
    /// Stealth visibility while hidden, from -1 (invisible) to 1 (fully visible).
    /// </summary>
    [DataField]
    public float HiddenVisibility = -0.6f;

    [DataField]
    public SoundSpecifier? Whisper;

    [DataField]
    public TimeSpan WhisperCooldown = TimeSpan.FromSeconds(6);

    /// <summary>
    /// Extra damage dealt by the hit that reveals the mob.
    /// </summary>
    [DataField]
    public DamageSpecifier? AmbushDamage;

    /// <summary>
    /// Knockdown dealt by the hit that reveals the mob.
    /// </summary>
    [DataField]
    public TimeSpan? AmbushKnockdown;

    [DataField]
    public SoundSpecifier? AmbushSound;

    /// <summary>
    /// Hides again this long after the last reveal, null to stay revealed until the target is lost.
    /// </summary>
    [DataField]
    public TimeSpan? Rehide;

    /// <summary>
    /// Taking damage reveals the mob and restarts the <see cref="Rehide"/> timer.
    /// </summary>
    [DataField]
    public bool RevealOnDamage;

    /// <summary>
    /// Vision radius while revealed by damage, so an attacker at range becomes a target. Null keeps the mob's own.
    /// </summary>
    [DataField]
    public float? WakeVisionRadius;

    /// <summary>
    /// How long a damage reveal holds before a mob with no target hides again.
    /// </summary>
    [DataField]
    public TimeSpan WakeHold = TimeSpan.FromSeconds(3);

    [DataField, AutoPausedField]
    public TimeSpan WakeUntil;

    [ViewVariables]
    public float? NormalVisionRadius;

    [DataField, AutoPausedField]
    public TimeSpan HideAt;

    [ViewVariables]
    public bool Revealed;

    [DataField, AutoPausedField]
    public TimeSpan NextWhisper;
}
