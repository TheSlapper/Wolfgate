using Content.Shared.Damage;
using Content.Shared.Maps;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets the mob dig into the ground, heal while under it, and erupt beside its target to attack from below. It can
/// also dig away when hurt and come up somewhere else.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaDigAmbushComponent : Component
{
    /// <summary>
    /// Left on the ground where the mob digs in. It holds the mob until it erupts.
    /// </summary>
    [DataField(required: true)]
    public EntProtoId Hole;

    /// <summary>
    /// Spawned where the mob digs in and where it erupts.
    /// </summary>
    [DataField]
    public EntProtoId? Splash;

    [DataField]
    public TimeSpan Duration = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Healing applied each second while underground.
    /// </summary>
    [DataField]
    public DamageSpecifier? Heal;

    /// <summary>
    /// Dealt to the target if it stands next to the spot the mob erupts at.
    /// </summary>
    [DataField]
    public DamageSpecifier? EruptDamage;

    /// <summary>
    /// Knocks down the target if it stands next to the spot the mob erupts at.
    /// </summary>
    [DataField]
    public TimeSpan? EruptKnockdown;

    [DataField]
    public SoundSpecifier? Sound = new SoundPathSpecifier("/Audio/_WF/Fauna/Shiptest/crystal_bamf.ogg");

    /// <summary>
    /// The mob only digs in and erupts on these tiles. Empty allows any floor.
    /// </summary>
    [DataField]
    public List<ProtoId<ContentTileDefinition>> Tiles = new();

    /// <summary>
    /// Digs away once damage leaves at most this fraction of its health. One reacts to any damage, zero turns it off.
    /// </summary>
    [DataField]
    public float FleeHealth;

    /// <summary>
    /// Wait between the damage that triggers a flee and the dig.
    /// </summary>
    [DataField]
    public TimeSpan FleeDelay;

    /// <summary>
    /// Tiles from the hole where a fleeing mob comes up.
    /// </summary>
    [DataField]
    public int FleeDistance = 6;

    [DataField]
    public TimeSpan FleeCooldown = TimeSpan.FromSeconds(30);

    [DataField]
    public bool FleePending;

    [DataField]
    public bool Fleeing;

    [DataField, AutoPausedField]
    public TimeSpan FleeAt;

    [DataField, AutoPausedField]
    public TimeSpan NextFlee;

    [DataField]
    public EntityUid? Target;

    [DataField]
    public EntityUid? HoleEntity;

    [DataField, AutoPausedField]
    public TimeSpan EruptAt;

    [DataField, AutoPausedField]
    public TimeSpan NextHeal;
}
