using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Gives each melee hit of the mob a chance to deal extra damage, inject venom, cause bleeding, pulse an EMP or spawn
/// something at the mob. Spawned mobs die with it. Blood drain applies to every hit target without a roll.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaMeleeProcComponent : Component
{
    [DataField]
    public float Chance = 0.25f;

    [DataField]
    public DamageSpecifier? Damage;

    [DataField]
    public EntProtoId? Spawn;

    /// <summary>
    /// Most spawns alive at once. Zero means no cap.
    /// </summary>
    [DataField]
    public int MaxSpawned;

    [DataField]
    public LocId? Popup;

    [DataField]
    public SoundSpecifier? Sound;

    /// <summary>
    /// Said by the mob when it spawns something.
    /// </summary>
    [DataField]
    public LocId? SpawnSpeech;

    /// <summary>
    /// Gibs or deletes the spawns when the mob dies, instead of killing them.
    /// </summary>
    [DataField]
    public bool GibSpawned;

    /// <summary>
    /// Solution on the mob that a proc injects into the target's bloodstream.
    /// </summary>
    [DataField]
    public string? Solution;

    [DataField]
    public FixedPoint2 TransferAmount = 5;

    /// <summary>
    /// Bleeding added to the target. Zero turns it off.
    /// </summary>
    [DataField]
    public float Bleed;

    /// <summary>
    /// Radius of an EMP pulse at the target. Zero turns it off.
    /// </summary>
    [DataField]
    public float EmpRange;

    [DataField]
    public float EmpEnergy = 1000f;

    [DataField]
    public TimeSpan EmpDuration = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Least blood drained from each hit target.
    /// </summary>
    [DataField]
    public float BloodDrainMin;

    /// <summary>
    /// Most blood drained from each hit target. Zero turns it off.
    /// </summary>
    [DataField]
    public float BloodDrainMax;

    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
