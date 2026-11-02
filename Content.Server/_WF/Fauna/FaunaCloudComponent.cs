using Content.Shared.Damage;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A cloud that hurts, tires, slows, disorients or ignites every living mob inside it, once per interval.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaCloudComponent : Component
{
    [DataField]
    public float Radius = 1.5f;

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public float Stamina;

    [DataField]
    public TimeSpan Slow = TimeSpan.Zero;

    /// <summary>
    /// Seconds of drunken wobble added per interval, without slurred speech.
    /// </summary>
    [DataField]
    public float Disorient;

    /// <summary>
    /// Fire stacks added per interval. Above zero also sets the mob alight.
    /// </summary>
    [DataField]
    public float FireStacks;

    /// <summary>
    /// Members of this faction are left alone, so the mob that made the cloud isn't caught in it.
    /// </summary>
    [DataField]
    public ProtoId<NpcFactionPrototype>? SparedFaction;

    /// <summary>
    /// Only hits mobs that breathe and have no working internals.
    /// </summary>
    [DataField]
    public bool BreathingOnly;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
