using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A shell that takes all damage meant for the mob until it breaks, then drops its loot.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaBreakableShellComponent : Component
{
    [DataField]
    public float MaxHealth = 60f;

    /// <summary>
    /// Share of a projectile's damage the shell takes.
    /// </summary>
    [DataField]
    public float ProjectileFactor = 0.25f;

    /// <summary>
    /// Spawned when the shell breaks, or on death if it never did.
    /// </summary>
    [DataField]
    public List<EntProtoId> Loot = new();

    [DataField]
    public SoundSpecifier? BreakSound = new SoundCollectionSpecifier("GlassBreak");

    [DataField]
    public float Absorbed;

    [DataField]
    public bool Broken;
}
