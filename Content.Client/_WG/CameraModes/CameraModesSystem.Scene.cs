using System.Numerics;
using Content.Client.IconSmoothing;
using Content.Client.Viewport;
using Content.Client._WG.CameraModes.Fold;
using Content.Shared._CE.ZLevels.Core.Components;
using Content.Shared._WG.BitmaskWalls;
using Content.Shared._WG.Vehicles.Core;
using Content.Shared.Buckle.Components;
using Content.Shared.Climbing.Components;
using Content.Shared.Computer;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Ghost;
using Content.Shared.Item;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Placeable;
using Content.Shared.Tag;
using Content.Shared.Temperature.Components;
using Content.Shared.Wall;
using Robust.Client.ComponentTrees;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Collision.Shapes;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private SpriteSystem _sprite = default!;
    [Dependency] private SharedAppearanceSystem _appearance = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _map = default!;
    [Dependency] private SpriteTreeSystem _spriteTree = default!;
    [Dependency] private TagSystem _tags = default!;

    private static readonly ProtoId<TagPrototype> WallTag = "Wall";
    private static readonly ProtoId<TagPrototype> WindowTag = "Window";

    /// <summary>Height of a wall, window or door block, and of one story, in tiles.</summary>
    public const float BlockHeight = 1f;

    /// <summary>Height of a table top, in tiles.</summary>
    public const float TableHeight = 0.42f;

    /// <summary>The tallest rise past the floor a solid is drawn with, in tiles.</summary>
    private const float MaxRise = 2f;
    private const int PruneFrames = 60;

    /// <summary>A sprite offset this far north is drawn on the wall tile beyond its entity.</summary>
    private const float HungOffset = 0.75f;

    /// <summary>A built solid no entity has used for this many frames is dropped.</summary>
    private const int GeometryFrames = 1800;
    private const int RedrawBudget = 64;
    private const int AnalysisBudget = 16;
    /// <summary>Alpha of the solids between the camera and the view centre.</summary>
    public const float FrontFade = 0.55f;

    /// <summary>Half the drawn width of the player plus the margin round it a solid must reach to fade, in tiles.</summary>
    private const float FadeReach = 0.5f;

    /// <summary>Share of the player's height, standing a tile tall, a solid in front must hide to fade.</summary>
    private const float FadeCover = 0.33f;
    private const ulong KeySeed = 14695981039346656037UL;

    private EntityQuery<MobStateComponent> _mobQuery;
    private EntityQuery<ItemComponent> _itemQuery;
    private EntityQuery<DoorComponent> _doorQuery;
    private EntityQuery<AppearanceComponent> _appearanceQuery;
    private EntityQuery<IconSmoothComponent> _smoothQuery;
    private EntityQuery<PlaceableSurfaceComponent> _surfaceQuery;
    private EntityQuery<ClimbableComponent> _climbQuery;
    private EntityQuery<WallFrillComponent> _frillQuery;
    private EntityQuery<CEZLevelHighGroundComponent> _highGroundQuery;
    private EntityQuery<VehicleInteriorLinkComponent> _interiorQuery;
    private EntityQuery<VehicleInteriorRoofComponent> _roofQuery;
    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<WallMountComponent> _wallMountQuery;
    private EntityQuery<AlwaysHotComponent> _hotQuery;
    private EntityQuery<GhostComponent> _ghostQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<StrapComponent> _strapQuery;

    /// <summary>Sort key added for each level below, more than any sort key within one level.</summary>
    private const float LevelSort = 10000f;

    /// <summary>More levels than are ever drawn above, so the levels below sort past all of them.</summary>
    private const int LowerSortBase = 8;

    /// <summary>How much further a story up sorts in first person, more than any nudge within a tile.</summary>
    private const float LevelTie = 0.01f;

    /// <summary>The least share of its tile a soft fixture covers for its owner to count as ground cover.</summary>
    private const float GroundCoverSpan = 0.9f;
    private Vector2 _viewCentre;

    /// <summary>World position of the eye this frame.</summary>
    public Vector2 ViewCentre => _viewCentre;

    private readonly Dictionary<EntityUid, SceneObject> _objects = new();
    private readonly Dictionary<ulong, GeometryEntry> _geometry = new();
    private readonly Dictionary<(int Size, float Height), FoldMesh> _blocks = new();
    private readonly Dictionary<int, FoldMesh> _doors = new();
    private readonly Dictionary<ulong, FoldMesh> _prisms = new();
    private readonly Dictionary<int, FoldMesh> _sheets = new();
    private readonly Dictionary<string, bool> _floraRsi = new();

    /// <summary>Depth of a free-standing door, in tiles.</summary>
    private const float SheetDepth = 2f / 32f;
    private readonly Dictionary<int, FoldMesh> _tables = new();
    private readonly List<Entity<SpriteComponent, TransformComponent>> _query = new();
    private readonly List<SceneObject> _hidden = new();
    private readonly HashSet<EntityUid> _hiddenUids = new();
    private readonly List<EntityUid> _prune = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _blockTiles = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _doorTiles = new();

    private Vector2[] _turnedPrism = Array.Empty<Vector2>();

    /// <summary>Full-tile walls in view by tile, which lend their upper story to the windows and doors beside them.</summary>
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), SceneObject> _walls = new();

    /// <summary>Tile sides a diagonal block covers in full, in its grid's frame.</summary>
    private readonly HashSet<(EntityUid Grid, Vector2i Tile, Direction Side)> _prismSides = new();

    /// <summary>Tiles holding a window, whose glass shows what stands behind it.</summary>
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _windowTiles = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _tableTiles = new();
    private readonly List<ulong> _pruneGeometry = new();
    private readonly Dictionary<(EntityUid Grid, Vector2i Tile), SceneObject> _surfaces = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _surfaceTiles = new();
    private readonly HashSet<(EntityUid Grid, Vector2i Tile)> _wallTiles = new();

    /// <summary>The objects drawn this frame, far to near.</summary>
    public readonly List<SceneObject> Drawn = new();

    /// <summary>Solids whose shape still has to be read from their image.</summary>
    public readonly List<SceneObject> Analyse = new();

    /// <summary>Count of main passes collected, used to age cached shapes and atlas cells.</summary>
    public int Frame { get; private set; }

    /// <summary>Raised sprites found in view last frame, before any were left to the stock view.</summary>
    public int Collected { get; private set; }

    /// <summary>Objects left flat since the last debug line: the atlas was full, or a first image waited its turn.</summary>
    private int _droppedFull;
    private int _droppedWaiting;

    /// <summary>World direction of screen up this frame.</summary>
    public Vector2 Up { get; private set; }

    /// <summary>World direction of screen right this frame.</summary>
    public Vector2 Right { get; private set; }

    private void InitializeScene()
    {
        _mobQuery = GetEntityQuery<MobStateComponent>();
        _itemQuery = GetEntityQuery<ItemComponent>();
        _doorQuery = GetEntityQuery<DoorComponent>();
        _appearanceQuery = GetEntityQuery<AppearanceComponent>();
        _smoothQuery = GetEntityQuery<IconSmoothComponent>();
        _surfaceQuery = GetEntityQuery<PlaceableSurfaceComponent>();
        _climbQuery = GetEntityQuery<ClimbableComponent>();
        _frillQuery = GetEntityQuery<WallFrillComponent>();
        _highGroundQuery = GetEntityQuery<CEZLevelHighGroundComponent>();
        _interiorQuery = GetEntityQuery<VehicleInteriorLinkComponent>();
        _roofQuery = GetEntityQuery<VehicleInteriorRoofComponent>();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _wallMountQuery = GetEntityQuery<WallMountComponent>();
        _hotQuery = GetEntityQuery<AlwaysHotComponent>();
        _ghostQuery = GetEntityQuery<GhostComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _strapQuery = GetEntityQuery<StrapComponent>();
    }

    /// <summary>
    /// Whether an overlay pass is the player's own view in the main viewport. Between two levels, as on stairs, CE
    /// draws the player's own level through a z-level eye of its own.
    /// </summary>
    public bool IsMainPass(in OverlayDrawArgs args)
    {
        if (args.Viewport.Eye is not { } eye || MainViewport() is not { } main)
            return false;

        return eye is ScalingViewport.ZEye zEye
            ? zEye.Primary && args.ViewportControl == main
            : eye == main.Eye;
    }

    /// <summary>Gathers the raised sprites in view and hides the ones drawn as solids from the stock render.</summary>
    public void Collect(in OverlayDrawArgs args)
    {
        Frame++;
        Drawn.Clear();
        Analyse.Clear();
        _blockTiles.Clear();
        _doorTiles.Clear();
        _windowTiles.Clear();
        _tableTiles.Clear();
        _prismSides.Clear();
        _walls.Clear();
        _surfaces.Clear();

        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        Up = (-eyeRot).RotateVec(Vector2.UnitY);
        Right = new Vector2(Up.Y, -Up.X);

        // Solids standing just below the bottom edge rise into view, so the query reaches that far past it. First
        // person draws nothing past the edge of the stock picture, where its floor ends.
        var profile = Profile;
        var reach = MaxRise * profile.Height / profile.Squash;
        var bounds = FirstPerson ? args.WorldAABB : args.WorldAABB.Union(args.WorldAABB.Translated(-Up * reach));
        Collected = 0;
        _viewCentre = args.Viewport.Eye?.Position.Position ?? args.WorldAABB.Center;

        // The levels above stand on the player's, one story each, less what can't be seen.
        _surfaceTiles.Clear();
        _wallTiles.Clear();
        FindLevels(args.MapUid);
        _query.Clear();
        _spriteTree.QueryAabb(_query, args.MapId, bounds);
        Gather(eyeRot, 0);
        for (var level = 1; level <= LevelsAbove; level++)
        {
            _query.Clear();
            _spriteTree.QueryAabb(_query, MapAbove(level), FirstPerson ? bounds : AboveBounds(bounds, level));
            Gather(eyeRot, level);
        }

        UpdateCutaway();
        CullEnclosed();
        CollectLower(eyeRot, args.WorldAABB);

        CollectHeld(eyeRot);

        var redraws = 0;
        _atlas?.BeginFrame();
        CollectCeiling(args.MapUid, args.MapId, args.WorldAABB);
        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            if (obj.Mesh == null && obj.Kind != SceneKind.Mob && !obj.Card)
            {
                Drawn.RemoveAt(i);
                continue;
            }

            obj.Slot = _atlas?.Get(obj.TextureKey, obj.SlotSize, Frame);
            var waiting = obj.Slot is { Drawn: false } && redraws >= RedrawBudget;
            if (waiting)
            {
                // A new look waiting its turn shows the last one meanwhile, so the object never drops to its flat
                // stock sprite while the view turns.
                obj.Slot = obj.ShownKey != 0 ? _atlas?.Drawn(obj.ShownKey, Frame) : null;
                if (obj.Slot == null)
                    _droppedWaiting++;
            }
            else if (obj.Slot == null)
            {
                _droppedFull++;
            }

            if (obj.Slot == null)
            {
                Drawn.RemoveAt(i);
                continue;
            }

            if (!waiting)
                obj.ShownKey = obj.TextureKey;

            if (!obj.Slot.Drawn)
                redraws++;

            if (!obj.Views)
                continue;

            obj.Slots[0] = obj.Slot;
            for (var face = 1; face < 4; face++)
            {
                var side = _atlas?.Get(obj.ViewKeys[face], obj.SlotSize, Frame);
                obj.Slots[face] = side;
                if (side is { Drawn: false })
                    redraws++;
            }

            // A side image the atlas can't hold this frame is drawn from the south one meanwhile. The object never
            // drops back to its stock sprite, which would show the frame for the view's turn.
        }

        ListStanding();

        // Blocks know their neighbours, and items know the table under them, from the objects actually drawn.
        foreach (var obj in Drawn)
        {
            if (!obj.Grid.IsValid())
                continue;

            if (obj.Door)
                _doorTiles.Add((obj.Grid, obj.Tile));
            else if (obj.Kind is SceneKind.Block or SceneKind.Window && obj.Prism == null)
            {
                _blockTiles.Add((obj.Grid, obj.Tile));
                if (obj.Kind == SceneKind.Block && !obj.Cladding)
                    _walls[(obj.Grid, obj.Tile)] = obj;
            }
            else if (obj.Prism != null && obj.Mesh != null)
            {
                foreach (var quad in obj.Mesh.Quads)
                {
                    if (quad.Side != Direction.Invalid)
                        _prismSides.Add((obj.Grid, obj.Tile, quad.Side));
                }
            }

            if (obj.Kind == SceneKind.Window)
                _windowTiles.Add((obj.Grid, obj.Tile));

            if (obj.Kind == SceneKind.Table)
                _tableTiles.Add((obj.Grid, obj.Tile));

            // A table wins a tile over any other surface on it.
            if (obj.IsSurface && (!_surfaces.TryGetValue((obj.Grid, obj.Tile), out var held)
                                  || held.Kind != SceneKind.Table && obj.Kind == SceneKind.Table))
            {
                _surfaces[(obj.Grid, obj.Tile)] = obj;
            }
        }

        // A door between blocks or other doors fills its tile. Any other door hangs as a sheet and leaves its
        // neighbours' faces showing.
        foreach (var obj in Drawn)
        {
            if (!obj.Door)
                continue;

            obj.Sheet = !Spanned(obj);
            if (obj.Sheet)
                obj.Mesh = SheetMesh(obj.SlotSize);
        }

        foreach (var obj in Drawn)
        {
            if (obj.Door && !obj.Sheet)
                _blockTiles.Add((obj.Grid, obj.Tile));
        }

        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            if ((obj.Kind == SceneKind.Flat || obj.OnSurface) && obj.Grid.IsValid()
                && _surfaces.TryGetValue((obj.Grid, obj.Tile), out var surface) && surface != obj)
            {
                obj.Surface = surface;
                obj.Lift = surface.Mesh!.Top;
            }

            if (obj.Kind is SceneKind.Block or SceneKind.Window or SceneKind.Table || obj.Cladding)
            {
                var tiles = obj.Kind == SceneKind.Table ? _tableTiles : _blockTiles;
                // A door between walls to its north and south turns a quarter, so its faces look along the passage.
                // Its neighbours are then kept in the turned frame its faces use, as are a turned plate's.
                if (obj.Door && !obj.Sheet && DoorTurned(obj))
                    obj.Rotation += new Angle(MathF.PI / 2f);

                var turn = 0;
                if (_gridQuery.HasComp(obj.Grid))
                {
                    var quarters = (int) Math.Round((obj.Rotation - _transform.GetWorldRotation(obj.Grid)).Theta
                                                    / (Math.PI / 2));
                    turn = (quarters % 4 + 4) % 4 * 2;
                }

                // In first person a doorway is walked through, so the walls beside it keep the faces it shows.
                var doorway = FirstPerson && !obj.Door && obj.Kind is SceneKind.Block or SceneKind.Window;
                foreach (var dir in obj.Sheet ? Array.Empty<Direction>() : Cardinals)
                {
                    var world = (Direction) (((int) dir + turn) % 8);
                    var next = (obj.Grid, obj.Tile + world.ToIntVec());
                    // A solid block keeps its face against a window, which is seen through the glass.
                    var glass = obj.Kind == SceneKind.Block && !_windowTiles.Contains((obj.Grid, obj.Tile))
                                && _windowTiles.Contains(next);
                    // A diagonal block counts where its straight side covers the whole shared edge.
                    var filled = tiles.Contains(next)
                                 || obj.Kind != SceneKind.Table
                                 && _prismSides.Contains((obj.Grid, next.Item2, world.GetOpposite()));
                    if (filled && !(doorway && _doorTiles.Contains(next)) && !glass)
                        obj.Neighbours |= dir.AsFlag();
                }
            }

            if (obj.OnWall && obj.Grid.IsValid())
                obj.Rotation = OpenFace(obj);

            // A fronted wall shows its drawn front only while nothing stands to its south.
            if (obj.FrontMesh != null && (obj.Neighbours & DirectionFlag.South) == 0)
                obj.Mesh = obj.FrontMesh;

            obj.Draw = true;
        }

        CollectSlabs(AboveBounds(bounds, LevelsAbove));
        Sort();
        Hide();
        if (Frame % PruneFrames == 0)
            Prune();
    }

    private static readonly Direction[] Cardinals =
        { Direction.North, Direction.East, Direction.South, Direction.West };

    /// <summary>Which way a sprite is drawn, or null when the stock view keeps it.</summary>
    private SceneKind? Classify(EntityUid uid, SpriteComponent sprite, TransformComponent xform)
    {
        if (!sprite.Visible)
            return null;

        // Floor art turns with the floor in the stock drawing, unless its sprite stays upright on screen. That kind
        // is laid on the floor here instead.
        var depth = sprite.DrawDepth;
        if (depth <= (int) DrawDepth.HighFloorObjects)
            return xform.Anchored && Upright(sprite) ? SceneKind.Solid : null;

        // Ghosts draw above effects but stand like any mob.
        if (_ghostQuery.HasComp(uid))
            return SceneKind.Mob;

        if (depth >= (int) DrawDepth.Effects)
            return null;

        // Cloaks and holograms read the screen or update per draw, which an atlas image can't do.
        foreach (var shader in _sprite.GetPostShaders(sprite))
        {
            if (shader.GetScreenTexture || shader.RaiseShaderEvent)
                return null;
        }

        if (_mobQuery.TryComp(uid, out var mob))
            return mob.CurrentState == MobState.Dead ? null : SceneKind.Mob;

        // Doors keep their solid in every state. Their clear pixels leave the doorway open.
        if (_doorQuery.HasComp(uid))
            return SceneKind.Block;

        if (xform.Anchored && Wall(uid, sprite))
            return SceneKind.Block;

        if (xform.Anchored && _smoothQuery.TryComp(uid, out var smooth)
            && (smooth.Mode == IconSmoothingMode.Diagonal || _tags.HasTag(uid, WindowTag)))
            return SceneKind.Window;

        // A table smooths with the tables beside it. A machine you can climb and set things on stays a solid.
        if (xform.Anchored && _surfaceQuery.HasComp(uid) && _climbQuery.HasComp(uid) && _smoothQuery.HasComp(uid))
            return SceneKind.Table;

        if (_itemQuery.HasComp(uid) && !xform.Anchored)
            return SceneKind.Flat;

        return SceneKind.Solid;
    }

    /// <summary>Whether the stock drawing keeps a sprite upright on screen however the view is turned.</summary>
    private bool Upright(SpriteComponent sprite)
    {
        return sprite.NoRotation || sprite.SnapCardinals || Directional(sprite);
    }

    /// <summary>Whether a sprite's art sits in a Flora folder or with the planet tiles, as plants, trees and rocks do.</summary>
    private bool IsFlora(SpriteComponent sprite)
    {
        RSI? rsi = sprite.BaseRSI;
        if (rsi == null)
        {
            foreach (var layer in sprite.AllLayers)
            {
                if (layer.Rsi is { } own)
                {
                    rsi = own;
                    break;
                }
            }
        }

        if (rsi == null)
            return false;

        var path = rsi.Path.ToString();
        if (!_floraRsi.TryGetValue(path, out var flora))
        {
            flora = path.Contains("/Flora/", StringComparison.OrdinalIgnoreCase)
                    || path.Contains("/Tiles/Planet/", StringComparison.OrdinalIgnoreCase);
            _floraRsi[path] = flora;
        }

        return flora;
    }

    /// <summary>Whether an entity has a fixture that blocks movement and its body takes part in collisions.</summary>
    private bool Collides(EntityUid uid)
    {
        return Blocks(uid) && (!TryComp<PhysicsComponent>(uid, out var body) || body.CanCollide);
    }

    /// <summary>
    /// Whether an anchored thing is cover growing over the ground, like kudzu: it fills its tile, nothing bumps into
    /// it and it can be cut down.
    /// </summary>
    private bool GroundCover(EntityUid uid, TransformComponent xform)
    {
        if (!xform.Anchored || !_fixturesQuery.TryComp(uid, out var fixtures) || !HasComp<DamageableComponent>(uid))
            return false;

        var fills = false;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard)
                return false;

            var box = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            fills |= box.Width >= GroundCoverSpan && box.Height >= GroundCoverSpan;
        }

        return fills;
    }

    /// <summary>Whether an entity has a fixture that blocks movement.</summary>
    private bool Blocks(EntityUid uid)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return false;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard)
                return true;
        }

        return false;
    }

    /// <summary>
    /// The way a solid faces. A wall mount's front is the side its interaction arc faces. Any other sprite that never
    /// shows a turn faces grid south, as the stock view always draws it.
    /// </summary>
    private Angle SolidRotation(SceneObject obj, SpriteComponent sprite, Angle worldRot, Angle gridRot)
    {
        if (obj.OnWall && _wallMountQuery.TryComp(obj.Uid, out var mount))
            return Snap(worldRot + mount.Direction, gridRot);

        return sprite.NoRotation && !Directional(sprite) ? gridRot : Snap(worldRot, gridRot);
    }

    /// <summary>
    /// The box round an entity's hard fixtures in tiles, turned by the given angle into the frame its solid faces
    /// south in, or null when it has none.
    /// </summary>
    private Box2? Footprint(EntityUid uid, Angle turn)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return null;

        Box2? box = null;
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            var aabb = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            var turned = new Box2Rotated(aabb, turn, Vector2.Zero).CalcBoundingBox();
            box = box?.Union(turned) ?? turned;
        }

        return box;
    }

    /// <summary>
    /// The corners of a diagonal block's hard collision polygon, in its own frame, or null for any other entity.
    /// </summary>
    private Vector2[]? Prism(EntityUid uid)
    {
        if (!_smoothQuery.TryComp(uid, out var smooth) || smooth.Mode != IconSmoothingMode.Diagonal
            || !_fixturesQuery.TryComp(uid, out var fixtures))
            return null;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (fixture.Hard && fixture.Shape is PolygonShape { VertexCount: >= 3 } poly)
                return poly.VertexCount == poly.Vertices.Length ? poly.Vertices : poly.Vertices[..poly.VertexCount];
        }

        return null;
    }

    /// <summary>A collision outline turned from the entity's frame into its grid's.</summary>
    private Vector2[] TurnedPrism(Vector2[] corners, Angle turn)
    {
        var quarters = ((int) Math.Round(turn.Theta / (Math.PI / 2)) % 4 + 4) % 4;
        if (quarters == 0)
            return corners;

        if (_turnedPrism.Length != corners.Length)
            _turnedPrism = new Vector2[corners.Length];

        for (var i = 0; i < corners.Length; i++)
        {
            _turnedPrism[i] = turn.RotateVec(corners[i]);
        }

        return _turnedPrism;
    }

    /// <summary>The wall a door sits in, nearest along its row, whose upper story runs on over it. Null when none.</summary>
    public SceneObject? WallBeside(SceneObject obj)
    {
        for (var reach = 1; reach <= WallReach; reach++)
        {
            foreach (var dir in Cardinals)
            {
                if (_walls.TryGetValue((obj.Grid, obj.Tile + dir.ToIntVec() * reach), out var wall)
                    && wall.Mesh != null && wall.Slot != null)
                {
                    return wall;
                }
            }
        }

        return null;
    }

    /// <summary>How far along a row a door looks for the wall over it, in tiles.</summary>
    private const int WallReach = 8;

    /// <summary>Whether a wall, window or door stands next to an object on the side a world direction points to.</summary>
    public bool TallBeside(SceneObject obj, Vector2 worldDir)
    {
        var local = _gridQuery.HasComp(obj.Grid) ? (-_transform.GetWorldRotation(obj.Grid)).RotateVec(worldDir) : worldDir;
        var step = MathF.Abs(local.X) > MathF.Abs(local.Y)
            ? new Vector2i(Math.Sign(local.X), 0)
            : new Vector2i(0, Math.Sign(local.Y));
        var next = (obj.Grid, obj.Tile + step);
        return _blockTiles.Contains(next) || _windowTiles.Contains(next) || _doorTiles.Contains(next);
    }

    /// <summary>Drops the built diagonal blocks so the next frame builds them again.</summary>
    public void ClearPrisms()
    {
        _prisms.Clear();
    }

    private FoldMesh PrismMesh(int slotSize, Vector2[] corners)
    {
        var key = Mix(0, slotSize);
        foreach (var corner in corners)
        {
            key = Mix(key, corner);
        }

        if (!_prisms.TryGetValue(key, out var mesh))
        {
            mesh = BlockBuilder.BuildPrism(new Vector2(slotSize / 2f, slotSize / 2f), BlockHeight, corners);
            _prisms[key] = mesh;
        }

        return mesh;
    }

    /// <summary>Thinnest side of a fixture, in tiles, that still counts as a strip of fence.</summary>
    private const float FenceStrip = 0.1f;

    /// <summary>Thickest blocking strip a climbable railing may have and still stand as a fence, in tiles.</summary>
    private const float RailStrip = 0.3f;

    /// <summary>
    /// An entity's hard fixtures turned into the frame its solid faces south in, or null unless every one is a thin
    /// strip.
    /// </summary>
    private Box2[]? Fence(EntityUid uid, Angle turn)
    {
        if (!_fixturesQuery.TryComp(uid, out var fixtures))
            return null;

        // Railings that can be climbed over block a wider strip along their edge than a fence does.
        var widest = _climbQuery.HasComp(uid) ? RailStrip : FenceStrip;
        var strips = new List<Box2>();
        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            var aabb = fixture.Shape.ComputeAABB(Robust.Shared.Physics.Transform.Empty, 0);
            if (Math.Min(aabb.Width, aabb.Height) >= widest)
                return null;

            strips.Add(new Box2Rotated(aabb, turn, Vector2.Zero).CalcBoundingBox());
        }

        return strips.Count > 0 ? strips.ToArray() : null;
    }

    /// <summary>Whether a sprite is drawn as a wall.</summary>
    private bool Wall(EntityUid uid, SpriteComponent sprite)
    {
        return sprite.DrawDepth == (int) DrawDepth.Walls || _tags.HasTag(uid, WallTag);
    }

    /// <summary>Whether things can be placed on top of an anchored entity.</summary>
    private bool Surface(EntityUid uid, TransformComponent xform)
    {
        return xform.Anchored && _surfaceQuery.TryComp(uid, out var surface) && surface.IsPlaceable;
    }

    /// <summary>Square image size and the frame bottom, from the sprite's bounds.</summary>
    private void SizeSlot(SceneObject obj, Entity<SpriteComponent> sprite)
    {
        var offset = obj.Kind == SceneKind.Mob ? Vector2.Zero : sprite.Comp.Offset;
        var bounds = _sprite.GetLocalBounds(sprite).Translated(offset);
        var radius = MathF.Max(
            MathF.Max(bounds.BottomLeft.Length(), bounds.TopRight.Length()),
            MathF.Max(bounds.TopLeft.Length(), bounds.BottomRight.Length()));
        obj.SlotSize = 2 * (int) MathF.Ceiling(radius * EyeManager.PixelsPerMeter) + 4;
        obj.FrameBottom = obj.SlotSize / 2f - bounds.Bottom * EyeManager.PixelsPerMeter;

        // A frill wall's image holds its frill a tile above its own art, so the cell reaches a tile up from the centre.
        obj.Frill = null;
        if (obj.Kind == SceneKind.Block && _frillQuery.HasComp(obj.Uid) && sprite.Comp.BaseRSI is { } rsi
            && rsi.TryGetState(FrillWallState, out _) && rsi.TryGetState(FrillCapState, out _))
        {
            obj.Frill = rsi.Path;
            obj.SlotSize = Math.Max(obj.SlotSize, FrillSlot);
        }
    }

    /// <summary>The unjoined wall state of a frill wall, whose front shows on every side.</summary>
    public const string FrillWallState = "wall-0";

    /// <summary>The unjoined frill state, whose rim is the wall's cap.</summary>
    public const string FrillCapState = "frill-0";

    /// <summary>Image size of a frill wall: its own tile plus the frill's half tile above, both ways from the centre.</summary>
    private const int FrillSlot = 68;

    /// <summary>Picks the image and the mesh for an object.</summary>
    private void Prepare(SceneObject obj, Entity<SpriteComponent> sprite, Angle worldRot, Angle gridRot, Angle eyeRot)
    {
        obj.Mesh = null;
        var offset = obj.Kind != SceneKind.Mob;
        obj.GeometryKey = Mix(SpriteKey(sprite.Comp, false, Angle.Zero, obj.SlotSize, offset),
            obj.Kind == SceneKind.Flat ? 2 : obj.OnWall ? 1 : obj.Hung ? 3 : obj.Console ? 6 : obj.Turnable ? 9
            : obj.Low ? 23 : 0);
        if (obj.OnSurface)
            obj.GeometryKey = Mix(obj.GeometryKey, 11);

        if (obj.LieOn || obj.Seat)
            obj.GeometryKey = Mix(obj.GeometryKey, obj.LieOn ? 13 : 17);

        if (obj.Decal || obj.Tall)
            obj.GeometryKey = Mix(obj.GeometryKey, obj.Decal ? 29 : 31);

        if (obj.Painted || obj.Stair)
            obj.GeometryKey = Mix(obj.GeometryKey, obj.Painted ? 59 : 61);

        if (obj.Panel || obj.Ceiling)
            obj.GeometryKey = Mix(obj.GeometryKey, obj.Panel ? 71 : 73);

        obj.Footprint = null;
        obj.Fence = null;
        if (obj.Kind == SceneKind.Solid && !obj.OnWall && !obj.Hung && !obj.Console && !obj.Low && !obj.Stair
            && Footprint(obj.Uid, worldRot - SolidRotation(obj, sprite.Comp, worldRot, gridRot)) is { } footprint)
        {
            obj.Footprint = footprint;
            obj.GeometryKey = Mix(Mix(obj.GeometryKey, footprint.BottomLeft), footprint.TopRight);
            obj.Fence = Fence(obj.Uid, worldRot - SolidRotation(obj, sprite.Comp, worldRot, gridRot));
            if (obj.Fence != null)
                obj.GeometryKey = Mix(obj.GeometryKey, 19 + obj.Fence.Length);
        }

        // A vehicle is always a card. A parked car is anchored on one end tile, its body reaching past it.
        obj.Vehicle = obj.Footprint is { } body && Math.Max(body.Width, body.Height) >= VehicleLength
                      && !obj.Seat && !obj.LieOn
                      && (!Transform(obj.Uid).Anchored || body.Center.Length() > VehicleOffCentre);
        obj.Card |= obj.Vehicle;
        switch (obj.Kind)
        {
            case SceneKind.Block:
            case SceneKind.Window:
            case SceneKind.Table:
                obj.ViewFrame = false;
                obj.FrontMesh = null;
                obj.Door = obj.Kind == SceneKind.Block && _doorQuery.HasComp(obj.Uid);
                obj.Sheet = false;
                // Smoothing works out a block's corners in its own rotation and the stock view turns the sprite by it,
                // so the top turns with the entity. Doors stay square to the grid and turn by their walls instead.
                obj.Rotation = obj.Door || sprite.Comp.NoRotation ? gridRot : Snap(worldRot, gridRot);
                if (obj.Door)
                {
                    obj.Fronted = false;
                    obj.Mesh = DoorMesh(obj.SlotSize);
                    if (TallArt(sprite) && Requested(obj, Mix(obj.GeometryKey, 47)) is { } tallDoor)
                        obj.Mesh = tallDoor;

                    break;
                }

                if (obj.Prism != null)
                {
                    obj.Fronted = false;
                    // A diagonal's art has a drawn frame for each way it faces, not one frame turned. The art is drawn
                    // as the stock view shows it on the grid, and the outline is turned to lie under it.
                    var turn = obj.Rotation - gridRot;
                    obj.Rotation = gridRot;
                    obj.ViewFrame = true;
                    obj.FrameAngle = turn;

                    // The outline is read from that frame's opaque pixels. The collision shape stands in until then.
                    var quarters = ((int) Math.Round(turn.Theta / (Math.PI / 2)) % 4 + 4) % 4;
                    obj.GeometryKey = Mix(Mix(SpriteKey(sprite.Comp, false, turn, obj.SlotSize, true), 37), quarters);
                    if (!_geometry.TryGetValue(obj.GeometryKey, out var outline))
                    {
                        outline = new GeometryEntry();
                        _geometry[obj.GeometryKey] = outline;
                    }

                    outline.LastUsed = Frame;
                    if (outline.Mesh == null && !outline.Requested && Analyse.Count < AnalysisBudget)
                    {
                        outline.Requested = true;
                        obj.AnalyseKey = obj.GeometryKey;
                        Analyse.Add(obj);
                    }

                    obj.Mesh = outline.Mesh ?? PrismMesh(obj.SlotSize, TurnedPrism(obj.Prism, turn));
                    break;
                }

                if (obj.Frill is { } frill)
                {
                    // Every wall of one frill sheet shares the shape read once from its unjoined art.
                    obj.Fronted = false;
                    obj.GeometryKey = Mix(Mix(KeySeed, frill.GetHashCode()), 41);
                    if (!_geometry.TryGetValue(obj.GeometryKey, out var capped))
                    {
                        capped = new GeometryEntry();
                        _geometry[obj.GeometryKey] = capped;
                    }

                    capped.LastUsed = Frame;
                    if (capped.Mesh == null && !capped.Requested && Analyse.Count < AnalysisBudget)
                    {
                        capped.Requested = true;
                        obj.AnalyseKey = obj.GeometryKey;
                        Analyse.Add(obj);
                    }

                    obj.Mesh = capped.Mesh ?? BlockMesh(obj.SlotSize, BlockHeight);
                    break;
                }

                obj.Mesh = obj.Kind == SceneKind.Table ? TableMesh(obj.SlotSize) : BlockMesh(obj.SlotSize, BlockHeight);
                if (obj.Kind == SceneKind.Window && TallArt(sprite))
                {
                    obj.Fronted = false;
                    if (Requested(obj, Mix(obj.GeometryKey, 43)) is { } tallWindow)
                        obj.Mesh = tallWindow;

                    break;
                }

                obj.Fronted = obj.Kind == SceneKind.Block && _smoothQuery.TryComp(obj.Uid, out var smooth)
                              && smooth.Mode == IconSmoothingMode.Bitmask;
                obj.LowWall = obj.Kind == SceneKind.Block && _climbQuery.HasComp(obj.Uid)
                              && !_tags.HasTag(obj.Uid, WallTag) && !HasComp<OccluderComponent>(obj.Uid);
                if (obj.LowWall)
                {
                    obj.GeometryKey = Mix(obj.GeometryKey, 53);
                    obj.Mesh = BlockMesh(obj.SlotSize, LowWallHeight);
                }

                if (!obj.Fronted && obj.Kind != SceneKind.Table)
                    break;

                // Each smoothing state is read back once to find where its front face starts.
                obj.GeometryKey = Mix(obj.GeometryKey, obj.Kind == SceneKind.Table ? 7 : 5);
                if (!_geometry.TryGetValue(obj.GeometryKey, out var front))
                {
                    front = new GeometryEntry();
                    _geometry[obj.GeometryKey] = front;
                }

                front.LastUsed = Frame;
                if (front.Mesh == null && !front.Requested && Analyse.Count < AnalysisBudget)
                {
                    front.Requested = true;
                    obj.AnalyseKey = obj.GeometryKey;
                    Analyse.Add(obj);
                }

                obj.FrontMesh = front.Mesh;
                if ((obj.Kind == SceneKind.Table || obj.LowWall) && front.Mesh != null)
                    obj.Mesh = front.Mesh;

                break;
            case SceneKind.Solid when obj.Cladding:
                // A plate on a wall tile coats the wall block, its image laid on it as the wall's own is.
                obj.ViewFrame = false;
                obj.Rotation = SolidRotation(obj, sprite.Comp, worldRot, gridRot);
                obj.Mesh = BlockMesh(obj.SlotSize, BlockHeight);
                break;
            case SceneKind.Solid:
            case SceneKind.Flat:
                // An entity keeps its first shape while its image size holds, so an opened crate shows its open art
                // on the same box rather than growing a new one.
                var look = obj.GeometryKey;
                if (obj.KeptKey != 0 && obj.KeptSize == obj.SlotSize && obj.KeptKind == obj.Kind)
                    obj.GeometryKey = obj.KeptKey;

                if (!_geometry.TryGetValue(obj.GeometryKey, out var entry))
                {
                    entry = new GeometryEntry();
                    _geometry[obj.GeometryKey] = entry;
                }

                entry.LastUsed = Frame;

                if (entry.Mesh == null)
                {
                    if (!entry.Requested && !obj.Card && Analyse.Count < AnalysisBudget)
                    {
                        entry.Requested = true;
                        obj.AnalyseKey = obj.GeometryKey;
                        Analyse.Add(obj);
                    }

                    obj.ViewFrame = false;
                    break;
                }

                obj.Mesh = entry.Mesh;
                obj.ViewFrame = !entry.Mesh.Fixed;
                obj.KeptKey = obj.GeometryKey;
                obj.KeptSize = obj.SlotSize;
                obj.KeptKind = obj.Kind;
                if (look != obj.KeptKey && obj.Kind == SceneKind.Solid && entry.Mesh.Fixed && !entry.Mesh.Views)
                    obj.Mesh = Grown(obj, look, entry);
                // Items lie at their exact angle.
                if (obj.Kind == SceneKind.Flat)
                {
                    obj.Rotation = worldRot;
                    break;
                }

                obj.Rotation = SolidRotation(obj, sprite.Comp, worldRot, gridRot);
                break;
            default:
                obj.ViewFrame = true;
                break;
        }

        // With the Billboards option, a standing solid shows the image the stock view draws as a card. Things on the
        // floor or on a wall keep their shape.
        // Flora and open fires are cards whatever the option says.
        obj.Billboard = obj.Card
                        || Billboards && obj.Kind == SceneKind.Solid && !obj.Low && !obj.Decal && !obj.OnWall
                        && !obj.Hung && !obj.Cladding && !obj.Stair && !obj.Panel && !obj.Ceiling;
        if (obj.Billboard)
            obj.ViewFrame = true;

        // Billboards use the nearest straight-on frame, so a turned view never shows them at a slant. A sprite with
        // one direction stays upright, since turning its image would only lay it on its side.
        // A diagonal keeps the frame for its turn on the grid.
        // A card fixed in the first-person world, like a sign, keeps its own south frame.
        if (obj.Prism == null)
        {
            var fixedCard = FirstPerson && obj.Kind == SceneKind.Solid && !obj.Billboard
                            && obj.Mesh is { Fixed: false };
            obj.FrameAngle = !fixedCard && (obj.Kind == SceneKind.Mob || Directional(sprite.Comp))
                ? obj.Vehicle ? SnapSide(worldRot + eyeRot) : SnapView(worldRot + eyeRot)
                : Angle.Zero;
        }
        obj.Views = obj.Mesh is { Views: true };
        if (obj.Views)
        {
            for (var face = 1; face < 4; face++)
            {
                obj.ViewKeys[face] = SpriteKey(sprite.Comp, true, Angle.FromDegrees(90 * face), obj.SlotSize, offset);
            }
        }

        obj.TextureKey = obj.ViewFrame
            ? SpriteKey(sprite.Comp, true, obj.FrameAngle, obj.SlotSize, offset)
            : SpriteKey(sprite.Comp, true, Angle.Zero, obj.SlotSize, offset);
        // A window's image holds firmer glass than the same sprite drawn as anything else.
        if (obj.Kind == SceneKind.Window)
            obj.TextureKey = Mix(obj.TextureKey, 23);

        // A frill wall is drawn from its sheet's unjoined art, the same for every wall of that sheet and tint.
        if (obj.Frill is { } sheet)
            obj.TextureKey = Mix(Mix(Mix(KeySeed, sheet.GetHashCode()), Quantise(sprite.Comp.Color)), obj.SlotSize);
    }

    /// <summary>
    /// The kept shape of an object whose look has changed, with any part of the new look that reaches above it
    /// standing on its back edge. The new look is read back once.
    /// </summary>
    private FoldMesh Grown(SceneObject obj, ulong look, GeometryEntry kept)
    {
        // Pixels a new look must fall short of the kept one's top or side by to replace it.
        const int BaseDrop = 4;

        if (kept.Analysis == null)
            return kept.Mesh!;

        if (!_geometry.TryGetValue(look, out var now))
        {
            now = new GeometryEntry();
            _geometry[look] = now;
        }

        now.LastUsed = Frame;
        if (now.Analysis == null)
        {
            if (!now.Requested && Analyse.Count < AnalysisBudget)
            {
                now.Requested = true;
                obj.AnalyseKey = look;
                Analyse.Add(obj);
            }

            return kept.Mesh!;
        }

        // A look that lies inside the kept one and well short of its top or a side is the base shape, as when a crate
        // or closet first seen open shuts.
        var (a, b) = (now.Analysis, kept.Analysis);
        if (now.Mesh != null && a.X0 >= b.X0 && a.X1 <= b.X1 && a.Y0 >= b.Y0
            && (a.Y0 - b.Y0 >= BaseDrop || a.X0 - b.X0 >= BaseDrop || b.X1 - a.X1 >= BaseDrop))
        {
            obj.KeptKey = look;
            obj.GeometryKey = look;
            return now.Mesh;
        }

        var key = Mix(Mix(look, obj.KeptKey.GetHashCode()), 8);
        if (!_geometry.TryGetValue(key, out var grown))
        {
            var origin = new Vector2(obj.SlotSize / 2f, obj.SlotSize / 2f);
            grown = new GeometryEntry
            {
                Analysis = now.Analysis,
                Mesh = SilhouetteFold.Grow(kept.Mesh!, kept.Analysis, now.Analysis, origin),
                Requested = true,
            };
            _geometry[key] = grown;
        }

        grown.LastUsed = Frame;
        obj.GeometryKey = key;
        return grown.Mesh!;
    }

    /// <summary>Whether any visible layer has a look for each direction.</summary>
    public static bool Directional(SpriteComponent sprite)
    {
        foreach (var layerI in sprite.AllLayers)
        {
            if (layerI is SpriteComponent.Layer { Visible: true, ActualState: { } state }
                && state.RsiDirections != RsiDirectionType.Dir1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The nearest quarter turn, nudged so an angle exactly between two picks the same one each frame.</summary>
    private static Angle SnapView(Angle angle)
    {
        var quarter = Math.Round((angle.Theta + 0.01) / (Math.PI / 2));
        return new Angle(quarter * Math.PI / 2);
    }

    /// <summary>The nearer of the two side frames, the only ones a vehicle's wheels are drawn in.</summary>
    private static Angle SnapSide(Angle angle)
    {
        return new Angle(Math.Sin(angle.Theta + 0.01) >= 0 ? Math.PI / 2 : -Math.PI / 2);
    }

    /// <summary>
    /// The rotation that puts a wall panel on the open face nearest its front, when a wall blocks the front.
    /// </summary>
    private Angle OpenFace(SceneObject obj)
    {
        var gridRot = _transform.GetWorldRotation(obj.Grid);
        foreach (var turn in FaceTurns)
        {
            var rotation = obj.Rotation + turn;
            var front = (rotation - gridRot).RotateVec(new Vector2(0f, -1f));
            var tile = obj.Tile + new Vector2i((int) MathF.Round(front.X), (int) MathF.Round(front.Y));
            if (!_blockTiles.Contains((obj.Grid, tile)))
                return rotation;
        }

        return obj.Rotation;
    }

    private static readonly Angle[] FaceTurns =
        { Angle.Zero, new(Math.PI / 2), new(-Math.PI / 2), new(Math.PI) };

    /// <summary>The nearest grid cardinal to an entity's rotation.</summary>
    private static Angle Snap(Angle worldRot, Angle gridRot)
    {
        var quarter = Math.Round((worldRot - gridRot).Theta / (Math.PI / 2));
        return gridRot + new Angle(quarter * Math.PI / 2);
    }

    /// <summary>Whether a door has a block or another door on both sides of it along one axis.</summary>
    private bool Spanned(SceneObject obj)
    {
        bool Joined(Direction dir)
        {
            var tile = (obj.Grid, obj.Tile + dir.ToIntVec());
            return _blockTiles.Contains(tile) || _doorTiles.Contains(tile);
        }

        return Joined(Direction.North) && Joined(Direction.South) || Joined(Direction.East) && Joined(Direction.West);
    }

    /// <summary>Whether a door has blocks north or south of it and none east or west.</summary>
    private bool DoorTurned(SceneObject obj)
    {
        bool Block(Direction dir) => _blockTiles.Contains((obj.Grid, obj.Tile + dir.ToIntVec()));
        return (Block(Direction.North) || Block(Direction.South)) && !Block(Direction.East) && !Block(Direction.West);
    }

    private FoldMesh TableMesh(int slotSize)
    {
        if (!_tables.TryGetValue(slotSize, out var mesh))
        {
            mesh = BlockBuilder.BuildTable(new Vector2(slotSize / 2f, slotSize / 2f), TableHeight);
            _tables[slotSize] = mesh;
        }

        return mesh;
    }

    private FoldMesh SheetMesh(int slotSize)
    {
        if (!_sheets.TryGetValue(slotSize, out var mesh))
        {
            mesh = BlockBuilder.BuildDoor(new Vector2(slotSize / 2f, slotSize / 2f), BlockHeight, SheetDepth);
            _sheets[slotSize] = mesh;
        }

        return mesh;
    }

    private FoldMesh DoorMesh(int slotSize)
    {
        if (!_doors.TryGetValue(slotSize, out var mesh))
        {
            mesh = BlockBuilder.BuildDoor(new Vector2(slotSize / 2f, slotSize / 2f), BlockHeight);
            _doors[slotSize] = mesh;
        }

        return mesh;
    }

    /// <summary>Whether a sprite's frame reaches above its tile, as 3/4 art drawn taller than a tile does.</summary>
    private bool TallArt(Entity<SpriteComponent> sprite)
    {
        return _sprite.GetLocalBounds(sprite).Translated(sprite.Comp.Offset).Top > TallArtTop;
    }

    /// <summary>Shortest hard body, in tiles, that makes a solid a vehicle.</summary>
    private const float VehicleLength = 1.5f;

    /// <summary>How far, in tiles, an anchored vehicle's body centre sits from its entity, past its own tile.</summary>
    private const float VehicleOffCentre = 0.25f;

    /// <summary>Height of a low wall before its image has been read, in tiles.</summary>
    public const float LowWallHeight = 0.3f;

    /// <summary>Lowest frame top, in tiles above the tile centre, that counts as drawn taller than the tile.</summary>
    private const float TallArtTop = 0.55f;

    /// <summary>
    /// The shape read from an object's image under the given key, or null while it waits to be read. Asks for the read
    /// the first time.
    /// </summary>
    private FoldMesh? Requested(SceneObject obj, ulong key)
    {
        obj.GeometryKey = key;
        if (!_geometry.TryGetValue(key, out var entry))
        {
            entry = new GeometryEntry();
            _geometry[key] = entry;
        }

        entry.LastUsed = Frame;
        if (entry.Mesh == null && !entry.Requested && Analyse.Count < AnalysisBudget)
        {
            entry.Requested = true;
            obj.AnalyseKey = key;
            Analyse.Add(obj);
        }

        return entry.Mesh;
    }

    private FoldMesh BlockMesh(int slotSize, float height)
    {
        if (!_blocks.TryGetValue((slotSize, height), out var mesh))
        {
            mesh = BlockBuilder.Build(new Vector2(slotSize / 2f, slotSize / 2f), height);
            _blocks[(slotSize, height)] = mesh;
        }

        return mesh;
    }

    /// <summary>Stores a solid read from an object's image.</summary>
    public void SetGeometry(ulong key, SpriteAnalysis analysis, FoldMesh? mesh, string path = "")
    {
        if (!_geometry.TryGetValue(key, out var entry))
            return;

        entry.Analysis = analysis;
        entry.Mesh = mesh;
        entry.Path = path;
    }

    /// <summary>The cached shape for a geometry key, or null.</summary>
    public GeometryEntry? Geometry(ulong key)
    {
        return _geometry.GetValueOrDefault(key);
    }

    /// <summary>
    /// A hash of everything that changes how a sprite looks. The texture key adds animation frames and colours.
    /// </summary>
    private static ulong SpriteKey(SpriteComponent sprite, bool texture, Angle frame, int size, bool offset)
    {
        var h = KeySeed;
        h = Mix(h, size);
        h = Mix(h, sprite.NoRotation ? 1 : 0);
        h = Mix(h, sprite.EnableDirectionOverride ? (int) sprite.DirectionOverride + 1 : 0);
        h = Mix(h, offset ? sprite.Offset : Vector2.Zero);
        h = Mix(h, sprite.Scale);
        h = Mix(h, (int) MathF.Round((float) sprite.Rotation.Degrees));
        h = Mix(h, (int) MathF.Round((float) frame.Reduced().Degrees));
        if (texture)
            h = Mix(h, Quantise(sprite.Color));

        foreach (var layerI in sprite.AllLayers)
        {
            if (layerI is not SpriteComponent.Layer layer || !layer.Visible)
            {
                h = Mix(h, 0);
                continue;
            }

            // The loaded sheet and state, by reference, not by their names, which hash a string every time.
            h = Mix(h, layer.ActualRsi?.GetHashCode() ?? 1);
            h = Mix(h, layer.ActualState?.GetHashCode() ?? 2);
            h = Mix(h, layer.Texture?.GetHashCode() ?? 3);
            h = Mix(h, layer.Offset);
            h = Mix(h, layer.Scale);
            h = Mix(h, (int) MathF.Round((float) layer.Rotation.Degrees));
            h = Mix(h, (int) layer.DirOffset);
            h = Mix(h, layer.ShaderPrototype?.Id.GetHashCode() ?? 4);
            if (!texture)
                continue;

            h = Mix(h, layer.AnimationFrame);
            h = Mix(h, Quantise(layer.Color));
        }

        return h;
    }

    /// <summary>A colour in 16 steps a channel, so a hit flash makes a few images rather than one a frame.</summary>
    private static int Quantise(Color color)
    {
        return (int) (color.R * 15f + 0.5f)
               | (int) (color.G * 15f + 0.5f) << 4
               | (int) (color.B * 15f + 0.5f) << 8
               | (int) (color.A * 15f + 0.5f) << 12;
    }

    private static ulong Mix(ulong h, int value)
    {
        return (h ^ (uint) value) * 1099511628211UL;
    }

    private static ulong Mix(ulong h, Vector2 value)
    {
        return Mix(Mix(h, value.X.GetHashCode()), value.Y.GetHashCode());
    }

    /// <summary>Sorts far to near and fades raised objects standing between the camera and the player.</summary>
    private void Sort()
    {
        var profile = Profile;
        foreach (var obj in Drawn)
        {
            // A wall panel sorts from the wall face it hangs on, so the wall block under it draws first.
            var at = obj.OnWall ? obj.Position + obj.Rotation.RotateVec(new Vector2(0f, -0.55f)) : obj.Position;
            // First person sorts by distance, so art hung on a wall to the north sorts from that wall's face.
            if (FirstPerson && obj.Hung)
                at = obj.Position + obj.Rotation.RotateVec(new Vector2(0f, 0.45f));

            obj.SortKey = FirstPerson
                ? Vector2.Distance(at, _viewCentre)
                : Vector2.Dot(at, Up) * profile.PitchCos - (obj.Lift + StoryRise(obj)) * profile.PitchSin;
            // The levels below draw before everything else, the deepest first. In first person a level above sorts
            // by distance with the rest, drawing first only on its own tile. From above, a story sorts by its height.
            if (obj.Level < 0)
                obj.SortKey += (LowerSortBase - obj.Level) * LevelSort;
            else if (FirstPerson)
                obj.SortKey += obj.Level * LevelTie;

            // A slab draws before what stands on its tile.
            if (obj.Kind == SceneKind.Slab)
                obj.SortKey += 0.001f;
            // A plate draws right after the wall it coats.
            if (obj.Cladding)
                obj.SortKey -= 0.0005f;

            // A door or curtain draws after anything sharing its tile, the items on a bed inside it included.
            if (obj.Door)
                obj.SortKey -= 0.005f;
        }

        // Items draw right after their table, far to near among themselves.
        foreach (var obj in Drawn)
        {
            if (obj.Surface is { } surface)
                obj.SortKey = surface.SortKey - 0.002f + (FirstPerson
                    ? Vector2.Distance(obj.Position, _viewCentre) - Vector2.Distance(surface.Position, _viewCentre)
                    : Vector2.Dot(obj.Position - surface.Position, Up)) * 0.001f;
        }

        // Ties keep one order every frame, so touching objects never swap.
        Drawn.Sort((a, b) =>
        {
            var order = b.SortKey.CompareTo(a.SortKey);
            return order != 0 ? order : a.Uid.CompareTo(b.Uid);
        });

        // First person looks out from the player, so nothing stands in front of them.
        if (FirstPerson)
            return;

        // Solids drawn over the player at the view centre fade, so the player shows through walls in front of them.
        var local = _player.LocalEntity;
        var centreKey = Vector2.Dot(_viewCentre, Up) * profile.PitchCos;
        var stretch = profile.PitchCos / profile.PitchSin;
        // A tile seen at this turn is this wide on screen, either side of its centre.
        var tileHalf = 0.5f * (MathF.Abs(Right.X) + MathF.Abs(Right.Y));
        foreach (var obj in Drawn)
        {
            if (obj.SortKey >= centreKey)
                continue;

            float top;
            if (obj.Kind == SceneKind.Slab)
                top = 0f;
            else if (obj.Uid == local || obj.Kind == SceneKind.Flat || obj.Billboard || obj.Mesh is not { Fixed: true } mesh)
                continue;
            else
                top = mesh.Top;

            // The solid's top must reach over the player's feet and its foot sit under the player's head, so a story
            // lifted above the player on screen stays.
            var rel = _viewCentre - obj.Position;
            var along = Vector2.Dot(rel, Up);
            var rise = StoryRise(obj);
            if (MathF.Abs(Vector2.Dot(rel, Right)) < tileHalf + FadeReach
                && along < (top + rise - FadeCover) * stretch + tileHalf
                && along > (rise - 1f + FadeCover) * stretch - tileHalf)
            {
                obj.Faded = true;
            }
        }
    }

    /// <summary>
    /// How high an object's floor stands over the player's: a story for each level above, zero on the player's level,
    /// and the depth of a level below as a negative.
    /// </summary>
    public static float StoryRise(SceneObject obj)
    {
        return obj.Level >= 0 ? obj.Level * BlockHeight : -obj.Drop;
    }

    /// <summary>Whether a world point lies within 2 tiles of the area first person shows.</summary>
    private bool InLookArea(Vector2 world)
    {
        const float margin = 2f;
        var rel = world - _viewCentre;
        return MathF.Abs(Vector2.Dot(rel, LookRight)) < LookHalf.X + margin
               && MathF.Abs(Vector2.Dot(rel, LookUp)) < LookHalf.Y + margin;
    }

    /// <summary>Turns the sprites of one level found in view into scene objects.</summary>
    /// <param name="drop">How far the level's floor lies under the player's, in tiles.</param>
    private void Gather(Angle eyeRot, int level, float drop = 0f)
    {
        // Tiles with something placeable on them or a wall, so objects there know before their solid is built.
        foreach (var (uid, wallSprite, xform) in _query)
        {
            if (!xform.Anchored || !_gridQuery.TryComp(xform.GridUid, out var tileGrid))
                continue;

            var at = (xform.GridUid.Value, _map.TileIndicesFor(xform.GridUid.Value, tileGrid, xform.Coordinates));
            if (Surface(uid, xform))
                _surfaceTiles.Add(at);

            if (Wall(uid, wallSprite))
                _wallTiles.Add(at);
        }

        foreach (var (uid, sprite, xform) in _query)
        {
            if (Classify(uid, sprite, xform) is not { } kind)
                continue;

            // First person reads a picture wider than it shows. What lies past the look's area takes no cell.
            if (FirstPerson && level >= 0 && !InLookArea(_transform.GetWorldPosition(xform)))
                continue;

            // A vehicle interior is flat art: wall faces drawn upright and roof pieces drawn over the room.
            var piece = xform.Anchored && kind is SceneKind.Solid or SceneKind.Block && !_doorQuery.HasComp(uid)
                        && sprite.DrawDepth is (int) DrawDepth.Overdoors or (int) DrawDepth.Walls
                        && xform.MapUid is { } inside && _interiorQuery.HasComp(inside);
            if (piece)
                kind = SceneKind.Solid;

            Collected++;

            if (!_objects.TryGetValue(uid, out var obj))
            {
                obj = new SceneObject { Uid = uid };
                _objects[uid] = obj;
            }

            obj.Kind = kind;
            obj.Level = level;
            obj.Drop = drop;
            obj.Hand = 0;
            obj.Seen = Frame;
            obj.Draw = false;
            obj.Faded = false;
            obj.Alpha = sprite.Color.A;
            obj.Lift = 0f;
            obj.Surface = null;
            obj.Low = kind == SceneKind.Solid
                      && (sprite.DrawDepth <= (int) DrawDepth.HighFloorObjects || GroundCover(uid, xform));
            obj.Decal = obj.Low && !Blocks(uid);
            obj.Card = kind == SceneKind.Solid && !obj.Decal && (IsFlora(sprite) || _hotQuery.HasComp(uid));
            obj.Prism = kind is SceneKind.Block or SceneKind.Window ? Prism(uid) : null;
            // Doors keep their clear doorway and grilles their gaps. Walls block sight, windows hold glass.
            obj.SolidSides = kind == SceneKind.Window
                             || kind == SceneKind.Block && !_doorQuery.HasComp(uid) && HasComp<OccluderComponent>(uid);
            obj.Tall = kind == SceneKind.Solid && sprite.DrawDepth >= (int) DrawDepth.Mobs;
            obj.OnWall = kind == SceneKind.Solid && !obj.Low && _wallMountQuery.HasComp(uid);
            // Something drawn a tile north hangs on the wall there, unless it blocks its own tile like a machine.
            // In first person, wall-mounted art drawn there hangs too, like a wall light whose thin fixture blocks.
            obj.Hung = kind == SceneKind.Solid && !obj.OnWall && !obj.Low
                       && sprite.Offset.Y >= HungOffset && MathF.Abs(sprite.Offset.X) <= 0.25f
                       && (!Blocks(uid) || FirstPerson && sprite.DrawDepth == (int) DrawDepth.WallMountedItems);
            obj.Console = kind == SceneKind.Solid && _appearanceQuery.TryComp(uid, out var appearance)
                          && _appearance.TryGetData<bool>(uid, ComputerVisuals.Powered, out _, appearance);
            obj.Stair = kind == SceneKind.Solid && _highGroundQuery.HasComp(uid);
            obj.Turnable = kind == SceneKind.Solid && !obj.OnWall && !obj.Hung && !obj.Console && !obj.Low
                           && !obj.Stair && Directional(sprite) && !_tags.HasTag(uid, WindowTag);
            // Floating and jittering move a mob's offset every frame. It is added when placed, not drawn in.
            obj.Offset = kind == SceneKind.Mob ? sprite.Offset : Vector2.Zero;
            obj.Neighbours = DirectionFlag.None;
            (obj.Position, var worldRot) = _transform.GetWorldPositionRotation(xform);
            obj.Grid = xform.GridUid ?? EntityUid.Invalid;
            obj.Tile = _gridQuery.TryComp(xform.GridUid, out var grid)
                ? _map.TileIndicesFor(xform.GridUid.Value, grid, xform.Coordinates)
                : Vector2i.Zero;
            if (!FirstPerson)
                UpdateRained(obj, xform.MapID);
            obj.IsSurface = kind is SceneKind.Solid or SceneKind.Table && Surface(uid, xform);
            obj.OnSurface = kind == SceneKind.Solid && !obj.OnWall && !obj.Hung && !obj.IsSurface
                            && obj.Grid.IsValid() && _surfaceTiles.Contains((obj.Grid, obj.Tile));
            obj.LieOn = obj.Seat = false;
            if (kind == SceneKind.Solid && _strapQuery.TryComp(uid, out var strap))
            {
                obj.LieOn = strap.Position == StrapPosition.Down;
                obj.Seat = !obj.LieOn;
            }
            obj.Painted = kind == SceneKind.Solid && !obj.Low && obj.Grid.IsValid()
                          && _wallTiles.Contains((obj.Grid, obj.Tile)) && !Collides(uid)
                          && sprite.DrawDepth is (int) DrawDepth.Overdoors or (int) DrawDepth.WallTops;
            if (obj.Painted)
            {
                obj.OnWall = true;
                obj.Hung = obj.Turnable = obj.OnSurface = false;
            }

            obj.Ceiling = piece && _roofQuery.HasComp(uid);
            obj.Panel = piece && !obj.Ceiling;
            if (piece)
                obj.Painted = obj.OnWall = obj.Hung = obj.Turnable = obj.OnSurface = false;

            obj.Cladding = kind == SceneKind.Solid && xform.Anchored && !obj.OnWall && !obj.Hung && !piece
                           && obj.Grid.IsValid() && _wallTiles.Contains((obj.Grid, obj.Tile));

            var gridRot = xform.GridUid is { } gridUid ? _transform.GetWorldRotation(gridUid) : Angle.Zero;
            SizeSlot(obj, (uid, sprite));
            Prepare(obj, (uid, sprite), worldRot, gridRot, FirstPerson ? Bearing(obj.Position, eyeRot) : eyeRot);
            Drawn.Add(obj);
        }
    }

    /// <summary>
    /// The turn of an eye that looks straight at a point from the first-person eye, so a sprite shows the side that
    /// faces the player wherever it stands.
    /// </summary>
    private Angle Bearing(Vector2 position, Angle fallback)
    {
        var dir = position - _viewCentre;
        return dir.LengthSquared() < 0.0001f ? fallback : new Angle(MathF.Atan2(dir.X, dir.Y));
    }

    /// <summary>Makes the stock sprites of drawn objects see-through until the restore pass.</summary>
    private void Hide()
    {
        foreach (var obj in Drawn)
        {
            // The stock view never draws what the player holds.
            if (obj.Hand == 0)
                HideObject(obj);
        }
    }

    /// <summary>Makes one object's stock sprite see-through until the restore pass.</summary>
    private void HideObject(SceneObject obj)
    {
        // A sprite hidden twice would save its hidden colour as its own.
        if (!TryComp<SpriteComponent>(obj.Uid, out var sprite) || !_hiddenUids.Add(obj.Uid))
            return;

        obj.SavedColor = sprite.Color;
        _sprite.SetColor((obj.Uid, sprite), sprite.Color.WithAlpha(0f));
        _hidden.Add(obj);
    }

    /// <summary>Gives hidden sprites their colour back.</summary>
    public void RestoreColors()
    {
        foreach (var obj in _hidden)
        {
            if (TryComp<SpriteComponent>(obj.Uid, out var sprite))
                _sprite.SetColor((obj.Uid, sprite), obj.SavedColor);
        }

        _hidden.Clear();
        _hiddenUids.Clear();
    }

    private void Prune()
    {
        _prune.Clear();
        foreach (var (uid, obj) in _objects)
        {
            if (Frame - obj.Seen > PruneFrames || Deleted(uid))
                _prune.Add(uid);
        }

        foreach (var uid in _prune)
        {
            _objects.Remove(uid);
        }

        _pruneGeometry.Clear();
        foreach (var (key, entry) in _geometry)
        {
            if (Frame - entry.LastUsed > GeometryFrames)
                _pruneGeometry.Add(key);
        }

        foreach (var key in _pruneGeometry)
        {
            _geometry.Remove(key);
        }
    }

    private void ClearScene()
    {
        _objects.Clear();
        _geometry.Clear();
        _blocks.Clear();
        _doors.Clear();
        _sheets.Clear();
        _tables.Clear();
        Drawn.Clear();
        Analyse.Clear();
    }
}
