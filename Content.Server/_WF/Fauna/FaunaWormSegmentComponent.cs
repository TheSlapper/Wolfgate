namespace Content.Server._WF.Fauna;

/// <summary>
/// A body segment of a <see cref="FaunaWormComponent"/> worm.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaWormSegmentComponent : Component
{
    [ViewVariables]
    public EntityUid? Head;
}
