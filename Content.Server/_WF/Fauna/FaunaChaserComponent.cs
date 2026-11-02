using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Sends a chaser after the target that steps toward it tile by tile, leaving an effect on each tile.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaChaserComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Chaser;
}

/// <summary>
/// A chaser in flight. Picks a new cardinal step toward its target every few steps.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaChaserMoverComponent : Component
{
    [DataField(required: true)]
    public EntProtoId Effect;

    [DataField]
    public TimeSpan StepDelay = TimeSpan.FromSeconds(0.3);

    [DataField]
    public TimeSpan Lifetime = TimeSpan.FromSeconds(9.8);

    [DataField]
    public int RepickEvery = 4;

    [ViewVariables]
    public EntityUid? Target;

    [DataField, AutoPausedField]
    public TimeSpan NextStep;

    [DataField, AutoPausedField]
    public TimeSpan EndAt;

    [ViewVariables]
    public int Steps;

    [ViewVariables]
    public Vector2i Direction;
}
