using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Whitelist;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Lets the mob eat dead mobs, and food if <see cref="Food"/> is set, to heal and feed, or wrap corpses in a
/// <see cref="Cocoon"/>.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaCorpseEaterComponent : Component
{
    [DataField]
    public TimeSpan Delay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Healing spread over the mob's damage in <see cref="HealGroup"/>.
    /// </summary>
    [DataField]
    public FixedPoint2 Heal;

    /// <summary>
    /// Extra healing as a fraction of the eaten mob's dead threshold.
    /// </summary>
    [DataField]
    public float HealTargetFraction;

    /// <summary>
    /// Also eats or cocoons critical mobs, not only dead ones.
    /// </summary>
    [DataField]
    public bool EatCritical;

    [DataField]
    public ProtoId<DamageGroupPrototype> HealGroup = "Brute";

    /// <summary>
    /// Damage dealt to the corpse by each bite.
    /// </summary>
    [DataField]
    public DamageSpecifier? Bite;

    [DataField]
    public float Hunger;

    /// <summary>
    /// Whether the mob also eats food lying on the floor. Eaten food is deleted, or wrapped if the mob has a cocoon.
    /// </summary>
    [DataField]
    public bool Food;

    /// <summary>
    /// Items, not mobs, it also eats from the floor, such as ore. Deleted, or wrapped if the mob has a cocoon.
    /// </summary>
    [DataField]
    public EntityWhitelist? Whitelist;

    /// <summary>
    /// Whether an eaten corpse is used up: deleted, or gibbed if a player owns it.
    /// </summary>
    [DataField]
    public bool Consume;

    /// <summary>
    /// Spawned on the corpse, which goes into its <see cref="CocoonContainer"/>.
    /// </summary>
    [DataField]
    public EntProtoId? Cocoon;

    [DataField]
    public string CocoonContainer = "cocoon";

    /// <summary>
    /// Spawned under the mob after each meal, such as a spider's egg cluster.
    /// </summary>
    [DataField]
    public EntProtoId? Spawn;

    [DataField]
    public SoundSpecifier? Sound;

    [DataField]
    public LocId StartPopup = "fauna-eat-corpse-start";

    [DataField]
    public LocId DonePopup = "fauna-eat-corpse-done";

    [ViewVariables]
    public EntityUid? Eating;
}
