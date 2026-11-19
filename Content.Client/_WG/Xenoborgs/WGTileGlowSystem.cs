using Content.Shared._WG.Xenoborgs;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Client._WG.Xenoborgs;

/// <summary>Resolves the glowing tiles and keeps their overlay up.</summary>
public sealed partial class WGTileGlowSystem : EntitySystem
{
    [Dependency] private IOverlayManager _overlays = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IResourceCache _resources = default!;
    [Dependency] private ITileDefinitionManager _tileDefs = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedMapSystem _map = default!;

    /// <summary>Glows by tile type id.</summary>
    public readonly Dictionary<int, (WGTileGlowPrototype Proto, Texture Texture)> Glows = new();

    /// <summary>How many glowing tiles each grid holds, so the overlay skips the grids without any.</summary>
    private readonly Dictionary<EntityUid, int> _glowTiles = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);
        SubscribeLocalEvent<TileChangedEvent>(OnTileChanged);
        SubscribeLocalEvent<GridRemovalEvent>(OnGridRemoval);
        Load();
        _overlays.AddOverlay(new WGTileGlowOverlay(EntityManager, _overlays, _prototypes, _timing, this));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<WGTileGlowOverlay>();
        base.Shutdown();
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<WGTileGlowPrototype>())
            Load();
    }

    private void Load()
    {
        Glows.Clear();
        foreach (var proto in _prototypes.EnumeratePrototypes<WGTileGlowPrototype>())
        {
            if (!_tileDefs.TryGetDefinition(proto.Tile, out var tile))
            {
                Log.Error($"Tile glow {proto.ID} names unknown tile {proto.Tile}");
                continue;
            }

            Glows[tile.TileId] = (proto, _resources.GetResource<TextureResource>(proto.Sprite).Texture);
        }

        CountGlowTiles();
    }

    /// <summary>Whether a grid has any glowing tile.</summary>
    public bool HasGlowTiles(EntityUid grid)
    {
        return _glowTiles.ContainsKey(grid);
    }

    private void OnTileChanged(ref TileChangedEvent args)
    {
        var delta = 0;
        foreach (var change in args.Changes)
        {
            if (Glows.ContainsKey(change.NewTile.TypeId))
                delta++;

            if (Glows.ContainsKey(change.OldTile.TypeId))
                delta--;
        }

        if (delta != 0)
            AddGlowTiles(args.Entity, delta);
    }

    private void OnGridRemoval(GridRemovalEvent args)
    {
        _glowTiles.Remove(args.EntityUid);
    }

    private void AddGlowTiles(EntityUid grid, int delta)
    {
        var count = _glowTiles.GetValueOrDefault(grid) + delta;
        if (count > 0)
            _glowTiles[grid] = count;
        else
            _glowTiles.Remove(grid);
    }

    /// <summary>Counts the glowing tiles of every grid afresh, after the glows themselves change.</summary>
    private void CountGlowTiles()
    {
        _glowTiles.Clear();
        var grids = EntityQueryEnumerator<MapGridComponent>();
        while (grids.MoveNext(out var uid, out var grid))
        {
            var tiles = _map.GetAllTilesEnumerator(uid, grid);
            while (tiles.MoveNext(out var tile))
            {
                if (Glows.ContainsKey(tile.Value.Tile.TypeId))
                    AddGlowTiles(uid, 1);
            }
        }
    }
}
