using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A hole that spawns mobs in a fixed cycle while fewer than <see cref="MaxAlive"/> of them live. A shovel closes it.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaBurrowComponent : Component
{
    /// <summary>
    /// Spawned in order, then from the start again.
    /// </summary>
    [DataField(required: true)]
    public List<EntProtoId> Prototypes = new();

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(80);

    [DataField]
    public int MaxAlive = 15;

    [DataField]
    public TimeSpan CloseDelay = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Whether a shovel can fill it in. Off for a mob that spawns others off its own body.
    /// </summary>
    [DataField]
    public bool Closable = true;

    [DataField]
    public SoundSpecifier? SpawnSound;

    /// <summary>
    /// Throws each spawn at the mob's target at this speed. Zero drops it in place.
    /// </summary>
    [DataField]
    public float ThrowSpeed;

    [DataField, AutoPausedField]
    public TimeSpan NextSpawn;

    [DataField]
    public int Index;

    [ViewVariables]
    public List<EntityUid> Spawned = new();
}
