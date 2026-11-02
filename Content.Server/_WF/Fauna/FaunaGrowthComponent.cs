using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Replaces the mob with its adult form after a random time, while it is alive.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaGrowthComponent : Component
{
    [DataField(required: true)]
    public EntProtoId GrowInto;

    /// <summary>
    /// When set, the mob grows into a random one of these instead of <see cref="GrowInto"/>.
    /// </summary>
    [DataField]
    public List<EntProtoId> GrowIntoAny = new();

    /// <summary>
    /// Chance the mob grows up at all. Otherwise it stays young.
    /// </summary>
    [DataField]
    public float Chance = 1f;

    [DataField]
    public TimeSpan MinDelay = TimeSpan.FromMinutes(10);

    [DataField]
    public TimeSpan MaxDelay = TimeSpan.FromMinutes(20);

    [DataField, AutoPausedField]
    public TimeSpan GrowAt;
}
