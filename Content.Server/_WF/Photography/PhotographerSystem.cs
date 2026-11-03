using System.Linq;
using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.Prototypes;

namespace Content.Server._WF.Photography;

/// <summary>Gives photographers a camera, and a personal album unless one came back with them.</summary>
public sealed partial class PhotographerSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private InventorySystem _inventory = default!;

    private static readonly EntProtoId Camera = "Camera";
    private static readonly EntProtoId Album = "PhotoAlbumPersonal";

    /// <summary>Spawned mobs to equip next tick, once traits and carried-over items are in place.</summary>
    private readonly List<EntityUid> _queued = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawnComplete);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_queued.Count == 0)
            return;

        foreach (var mob in _queued)
        {
            if (!TerminatingOrDeleted(mob) && HasComp<PhotographerComponent>(mob))
                Equip(mob);
        }

        _queued.Clear();
    }

    private void OnSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        _queued.Add(args.Mob);
    }

    private void Equip(EntityUid mob)
    {
        var coords = Transform(mob).Coordinates;
        _hands.PickupOrDrop(mob, Spawn(Camera, coords), checkActionBlocker: false);

        if (!CarriesAlbum(mob))
            _hands.PickupOrDrop(mob, Spawn(Album, coords), checkActionBlocker: false);
    }

    private bool CarriesAlbum(EntityUid mob)
    {
        return _inventory.GetHandOrInventoryEntities(mob).Any(uid => MetaData(uid).EntityPrototype?.ID == Album.Id);
    }
}
