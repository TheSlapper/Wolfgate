using Content.Shared.Stacks;
using Content.Shared.Whitelist;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// Feeding the mob a food item splits off a copy, and feeding it an evolve key turns it into
/// <see cref="EvolveInto"/>. Items match by whitelist or by stack type.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaFeedComponent : Component
{
    [DataField]
    public EntityWhitelist? Food;

    [DataField]
    public List<ProtoId<StackPrototype>> FoodStacks = new();

    [DataField]
    public EntityWhitelist? EvolveKey;

    [DataField]
    public List<ProtoId<StackPrototype>> EvolveStacks = new();

    [DataField]
    public EntProtoId? EvolveInto;
}
