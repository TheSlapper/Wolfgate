using Content.Shared._WF.Fauna;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.NPC.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Fauna;

/// <summary>
/// A mob that banks the food it eats, radiates by how much it holds, turns hostile when starving and splits in two
/// after enough meals. Needs <see cref="FaunaCorpseEaterComponent"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaGourmongerComponent : Component
{
    /// <summary>
    /// Banked food.
    /// </summary>
    [DataField]
    public float Kcal = 200f;

    /// <summary>
    /// Below this it turns hostile.
    /// </summary>
    [DataField]
    public float StarvingKcal = 25f;

    /// <summary>
    /// Below this its stomach grumbles.
    /// </summary>
    [DataField]
    public float HungryKcal = 150f;

    /// <summary>
    /// Above this a hostile one calms down.
    /// </summary>
    [DataField]
    public float SatisfiedKcal = 500f;

    /// <summary>
    /// Above this examining says it's fat.
    /// </summary>
    [DataField]
    public float FatKcal = 1000f;

    /// <summary>
    /// Food from a food item, per unit of its solution.
    /// </summary>
    [DataField]
    public float KcalPerFoodUnit = 7.5f;

    /// <summary>
    /// Food from a corpse, per point of damage the corpse could take before dying.
    /// </summary>
    [DataField]
    public float KcalPerCorpseHealth = 2f;

    /// <summary>
    /// Meals needed to split, raised by the number alive when it spawns.
    /// </summary>
    [DataField]
    public int GrowToSplit = 20;

    [DataField]
    public int MaxAlive = 30;

    [DataField]
    public int SplitInto = 2;

    /// <summary>
    /// Share of its banked food each offspring gets on top of its own.
    /// </summary>
    [DataField]
    public float OffspringKcalShare = 0.1f;

    /// <summary>
    /// Meal count and max health gained from a food item and from a corpse.
    /// </summary>
    [DataField]
    public int FoodGrowth = 1;

    [DataField]
    public int CorpseGrowth = 2;

    /// <summary>
    /// Radiation per second per banked kcal.
    /// </summary>
    [DataField]
    public float RadiationPerKcal = 1f / 150f;

    /// <summary>
    /// Tiles the radiation reaches.
    /// </summary>
    [DataField]
    public float RadiationRange = 3f;

    [DataField]
    public ProtoId<NpcFactionPrototype> CalmFaction = "Passive";

    [DataField]
    public ProtoId<NpcFactionPrototype> HangryFaction = "SimpleHostile";

    [DataField]
    public EntProtoId Meat = "FoodMeat";

    [DataField]
    public ProtoId<ReagentPrototype> Nutriment = "Nutriment";

    [DataField]
    public float KcalPerNutriment = 15f;

    [DataField]
    public float NutrimentPerMeat = 20f;

    [DataField]
    public EntProtoId? Heart;

    [DataField]
    public float HeartChance = 0.05f;

    [DataField]
    public float GrumbleChance = 0.1f;

    [DataField]
    public List<LocId> Grumbles = new()
    {
        "fauna-gourmonger-grumble-1",
        "fauna-gourmonger-grumble-2",
        "fauna-gourmonger-grumble-3",
    };

    [DataField]
    public LocId SniffPopup = "fauna-gourmonger-sniff";

    [DataField]
    public LocId BusyPopup = "fauna-gourmonger-busy";

    [DataField]
    public LocId FamishedExamine = "fauna-gourmonger-examine-famished";

    [DataField]
    public LocId FatExamine = "fauna-gourmonger-examine-fat";

    [DataField]
    public TimeSpan TickInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public TimeSpan SniffInterval = TimeSpan.FromSeconds(15);

    [DataField]
    public TimeSpan SniffDuration = TimeSpan.FromSeconds(1);

    [DataField]
    public Color GlowColor = Color.FromHex("#5ad35a");

    [ViewVariables]
    public bool Hangry;

    [ViewVariables]
    public int Meals;

    /// <summary>
    /// Ticks since it last lost a meal. Raises food loss and the chance to lose a meal.
    /// </summary>
    [ViewVariables]
    public int FastingTicks;

    [DataField, AutoPausedField]
    public TimeSpan NextTick;

    [DataField, AutoPausedField]
    public TimeSpan NextSniff;

    [DataField, AutoPausedField]
    public TimeSpan SniffEnd;

    /// <summary>
    /// Last state written to appearance.
    /// </summary>
    [ViewVariables]
    public FaunaGourmongerState? VisualState;
}
