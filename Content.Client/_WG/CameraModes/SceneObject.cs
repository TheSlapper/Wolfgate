using System.Numerics;
using Content.Client._WG.CameraModes.Atlas;
using Content.Client._WG.CameraModes.Fold;
using Robust.Shared.Map;
using Robust.Shared.Utility;

namespace Content.Client._WG.CameraModes;

/// <summary>How a raised sprite is drawn in a 2.5D view.</summary>
public enum SceneKind : byte
{
    /// <summary>Folded by its pixels: fixed in the world, or a card facing the camera when no solid fits it.</summary>
    Solid,

    /// <summary>A living mob, always facing the camera.</summary>
    Mob,

    /// <summary>A wall or closed door: a full-tile block.</summary>
    Block,

    /// <summary>A full-tile window: a block whose glass stays see-through.</summary>
    Window,

    /// <summary>A table: a low block that joins its neighbours, for items to lie on.</summary>
    Table,

    /// <summary>An item on a table: the flat sprite lifted to the table top.</summary>
    Flat,

    /// <summary>A floor tile of a level above, drawn at its height as the roof of the story under it.</summary>
    Slab,
}

/// <summary>A raised sprite in view and what the 2.5D view draws for it this frame.</summary>
public sealed class SceneObject
{
    public EntityUid Uid;
    public SceneKind Kind;

    /// <summary>The sprite's look facing south, which decides its shape.</summary>
    public ulong GeometryKey;

    /// <summary>The look whose shape this entity keeps while its image size stays the same.</summary>
    public ulong KeptKey;

    public int KeptSize;
    public SceneKind KeptKind;

    /// <summary>The image drawn this frame.</summary>
    public ulong TextureKey;

    /// <summary>Whether the image is the sprite as the view sees it, not facing south.</summary>
    public bool ViewFrame;

    /// <summary>Drawn as an upright card facing the camera, by the Billboards option or because it is always one.</summary>
    public bool Billboard;

    /// <summary>A plant, tree, rock or open fire, always drawn as a card whatever the Billboards option says.</summary>
    public bool Card;

    /// <summary>Mounted on the wall tile it stands on, like an APC or a wall ATM.</summary>
    public bool OnWall;

    /// <summary>Drawn a tile north of the entity, on the wall there, like a wall light.</summary>
    public bool Hung;

    /// <summary>A tg 3/4 wall, whose sprite draws a front face below its top when nothing is south of it.</summary>
    public bool Fronted;

    /// <summary>The block built from a fronted wall's own art, used while its south side is open.</summary>
    public FoldMesh? FrontMesh;

    /// <summary>
    /// The RSI of a wall whose top is a separate frill image. Such a wall is drawn from its unjoined art: the frill's
    /// cap on top and the unjoined front on every side.
    /// </summary>
    public ResPath? Frill;

    /// <summary>A low wall that can be climbed and doesn't block sight, as tall as its drawn front.</summary>
    public bool LowWall;

    /// <summary>Flat art on a wall tile that nothing collides with, like graffiti, painted on the wall's open face.</summary>
    public bool Painted;

    /// <summary>Wall art of a vehicle interior, stood up as a flat panel fixed along its tile's north edge.</summary>
    public bool Panel;

    /// <summary>Roof art of a vehicle interior, laid over its tile as a ceiling.</summary>
    public bool Ceiling;

    /// <summary>Stairs up to the next level, drawn as steps climbing their tile.</summary>
    public bool Stair;

    /// <summary>A solid with a long hard body, like a car, always drawn as a card.</summary>
    public bool Vehicle;

    /// <summary>A closed door, whose sprite is its faces rather than its top.</summary>
    public bool Door;

    /// <summary>A door without blocks at both ends, drawn as a thin sheet.</summary>
    public bool Sheet;

    /// <summary>A computer console, whose face leans back.</summary>
    public bool Console;

    /// <summary>
    /// A standing directional sprite that is not a window. Its readback also draws it turned a quarter, to tell art
    /// drawn from above, which lies flat on the floor.
    /// </summary>
    public bool Turnable;

    /// <summary>Drawn at floor depth. It lies on the floor as a low slab.</summary>
    public bool Low;

    /// <summary>The geometry key the next readback of this object's image is stored under.</summary>
    public ulong AnalyseKey;

    public AtlasSlot? Slot;

    /// <summary>The image last drawn for this object, shown while a new one waits its turn to be drawn.</summary>
    public ulong ShownKey;

    /// <summary>
    /// The images of the four direction frames, south first, for a mesh whose sides show their own frames. Unused
    /// otherwise.
    /// </summary>
    public readonly AtlasSlot?[] Slots = new AtlasSlot?[4];

    /// <summary>Atlas keys of the four direction frames.</summary>
    public readonly ulong[] ViewKeys = new ulong[4];

    /// <summary>Whether the mesh shows more than the south frame.</summary>
    public bool Views;
    public FoldMesh? Mesh;

    /// <summary>Side of the square image in pixels. The entity sits at its centre.</summary>
    public int SlotSize;

    /// <summary>Pixel row of the bottom of the sprite's frame.</summary>
    public float FrameBottom;

    public Vector2 Position;

    /// <summary>World rotation of the mesh.</summary>
    public Angle Rotation;

    /// <summary>A mob's sprite offset, added when the billboard is placed.</summary>
    public Vector2 Offset;

    /// <summary>Screen box of the drawn faces, in the same space as the vertices.</summary>
    public Box2 Bounds;

    /// <summary>Height of the surface a flat item lies on.</summary>
    public float Lift;

    /// <summary>The surface an item or solid stands on. It draws right after it.</summary>
    public SceneObject? Surface;

    /// <summary>It stands between the camera and the view centre and is drawn see-through.</summary>
    public bool Faded;

    /// <summary>The box round its hard fixtures, in the frame its solid faces south in.</summary>
    public Box2? Footprint;

    /// <summary>Its hard fixtures when every one is a thin strip, like a railing's, in the same frame.</summary>
    public Box2[]? Fence;

    /// <summary>Furniture a body lies down on. Its sprite is drawn from above and it stands as a low slab.</summary>
    public bool LieOn;

    /// <summary>Furniture a body sits on. It stands as legs, a seat and a backrest.</summary>
    public bool Seat;

    /// <summary>An anchored solid on a wall tile that isn't wall-mounted. It coats the wall block.</summary>
    public bool Cladding;

    /// <summary>A block on part of its tile, like a diagonal window: the corners of its collision polygon.</summary>
    public Vector2[]? Prism;

    /// <summary>Floor art nothing bumps into, like a vent. It lies flat on the floor.</summary>
    public bool Decal;

    /// <summary>Drawn at mob height or above, like a plant or a statue. Without a lid it stays an upright card.</summary>
    public bool Tall;

    /// <summary>Things can be placed on top of it.</summary>
    public bool IsSurface;

    /// <summary>An anchored solid on a tile with a surface. Its sprite's lift up to the surface is not a hang.</summary>
    public bool OnSurface;

    /// <summary>The sprite's angle to the view when its image is drawn as the view sees it.</summary>
    public Angle FrameAngle;

    public float Alpha;
    public float SortKey;

    /// <summary>Faces of a block that touch another block, as a direction flag set.</summary>
    public DirectionFlag Neighbours;

    public EntityUid Grid;
    public Vector2i Tile;

    /// <summary>Whether the weather falls in front of it in the 2.5D views, and the tile and mask version it was found for.</summary>
    public bool Rained;
    public int RainVersion = int.MinValue;
    public EntityUid RainGrid;
    public Vector2i RainTile;

    public Color SavedColor;
    public bool Draw;

    /// <summary>How many levels above the player the object is on. Below the player it counts down from -1.</summary>
    public int Level;

    /// <summary>How far under the player's floor the object's floor lies, in tiles.</summary>
    public float Drop;

    /// <summary>The map of the lower level the object stands on, when it is below the player.</summary>
    public EntityUid LevelMap;

    /// <summary>The hand of the player that holds it in first person: 0 none, 1 left, 2 right.</summary>
    public int Hand;

    /// <summary>The frame the object was last gathered in.</summary>
    public int Seen;

    /// <summary>A wall or window, whose standing sides are filled where their art is clear.</summary>
    public bool SolidSides;

    /// <summary>The frame the eye's line of sight to it was last tested, and whether nothing blocked it.</summary>
    public int SightFrame = -1;
    public bool InSight;

    /// <summary>Range of this object's vertices in the frame's vertex buffer, for click tests.</summary>
    public int VertexStart;
    public int VertexCount;
}

/// <summary>The solid built from one sprite look, shared by every entity that looks the same.</summary>
public sealed class GeometryEntry
{
    public FoldMesh? Mesh;
    public SpriteAnalysis? Analysis;

    /// <summary>Debug: which builder made the mesh, for the debug commands.</summary>
    public string Path = "";
    public bool Requested;
    public int LastUsed;
}
