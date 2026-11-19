using System.Numerics;
using Content.Client.ContextMenu.UI;
using Content.Client.Gameplay;
using Content.Client.Interactable.Components;
using Content.Client.Viewport;
using Content.Client._WG.CameraModes.Atlas;
using Content.Shared.CCVar;
using Content.Shared.Interaction;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.State;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Graphics;
using Robust.Shared.Map;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private IStateManager _state = default!;

    /// <summary>Offsets from an object's centre tried for a stock point that picks it, nearest first.</summary>
    private static readonly Vector2[] ClickSpots = BuildClickSpots();

    [Dependency] private IInputManager _input = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;

    /// <summary>How much brighter the entity under the cursor draws, in reach and out of it.</summary>
    private const float HoverNear = 1.4f;
    private const float HoverFar = 1.15f;

    /// <summary>How far from the screen centre a mouselook click still picks an object, in viewport pixels.</summary>
    private const float LookReach = 40f;
    private const float LookStep = 8f;

    /// <summary>Points round the screen centre a mouselook click tries, nearest first.</summary>
    private static readonly Vector2[] LookSpots = BuildLookSpots();

    private EntityUid _pickUid;
    private Vector2 _pickPoint;

    /// <summary>
    /// The entity under the cursor, which draws brighter in place of the stock hover outline the view hides with its
    /// stock sprite.
    /// </summary>
    public EntityUid? Hovered { get; private set; }

    /// <summary>Brightness factor of the hovered entity this frame.</summary>
    public float HoverGlow { get; private set; } = 1f;

    /// <summary>Finds the hovered entity as the stock outline does: under the cursor, or in the menu.</summary>
    private void UpdateHover()
    {
        Hovered = null;
        HoverGlow = 1f;
        if (!_cfg.GetCVar(CCVars.OutlineEnabled) || _state.CurrentState is not GameplayStateBase screen)
            return;

        EntityUid? hovered = null;
        if (Mouselook && MainViewport() is { } main)
            hovered = screen.GetClickedEntity(main.PixelToMap(main.GlobalPixelPosition), main.Eye);
        else if (_ui.CurrentlyHovered is ScalingViewport vp && vp == MainViewport() && _input.MouseScreenPosition.IsValid)
            hovered = screen.GetClickedEntity(vp.PixelToMap(_input.MouseScreenPosition.Position), vp.Eye);
        else if (_ui.CurrentlyHovered is EntityMenuElement element)
            hovered = element.Entity;

        if (hovered is not { } uid || !HasComp<InteractionOutlineComponent>(uid))
            return;

        Hovered = uid;
        HoverGlow = _player.LocalEntity is { } local && _interaction.InRangeUnobstructed(local, uid)
            ? HoverNear
            : HoverFar;
    }

    private static Vector2[] BuildClickSpots()
    {
        var spots = new List<Vector2>();
        for (var x = -4; x <= 4; x++)
        {
            for (var y = -4; y <= 4; y++)
            {
                spots.Add(new Vector2(x, y) * 0.1f);
            }
        }

        spots.Sort((a, b) => a.LengthSquared().CompareTo(b.LengthSquared()));
        return spots.ToArray();
    }

    private static Vector2[] BuildLookSpots()
    {
        var spots = new List<Vector2>();
        var steps = (int) (LookReach / LookStep);
        for (var x = -steps; x <= steps; x++)
        {
            for (var y = -steps; y <= steps; y++)
            {
                var spot = new Vector2(x, y) * LookStep;
                if (spot.Length() <= LookReach)
                    spots.Add(spot);
            }
        }

        spots.Sort((a, b) => a.LengthSquared().CompareTo(b.LengthSquared()));
        return spots.ToArray();
    }

    private void InitializeClick()
    {
        SubscribeLocalEvent<PixelToMapEvent>(OnPixelToMap);
    }

    /// <summary>
    /// Moves a click on a drawn solid to a point of the same entity's stock sprite, so clicks, hover and the context
    /// menu work as in the flat view.
    /// </summary>
    private void OnPixelToMap(ref PixelToMapEvent args)
    {
        if (_mesh == null || args.Viewport.Eye is not { } eye || args.Control != MainViewport())
            return;

        // The isometric view holds vertices in world space, the perspective and first-person views in viewport pixels.
        var local = args.VisiblePosition;
        if (Mouselook)
        {
            // The mouse is hidden, so the click acts at the screen centre, or on the nearest object round it.
            local = _mesh.ViewSize / 2f;
            foreach (var spot in LookSpots)
            {
                if (PickSolid(local + spot, args.Viewport, eye) is not { } near)
                    continue;

                args.VisiblePosition = near;
                return;
            }
        }
        else if (PickSolid(_mesh.Perspective ? local : args.Viewport.LocalToWorld(local).Position, args.Viewport,
                     eye) is { } picked)
        {
            args.VisiblePosition = picked;
            return;
        }

        // The floor under the cursor, through the perspective warp.
        if (_mesh.Perspective)
            args.VisiblePosition = _mesh.Unproject(local);

        // Bare floor in the view can still lie under the stock sprite of a solid drawn elsewhere. The point moves to a
        // spot nearby where the stock pick finds nothing the view draws as a solid.
        if (_state.CurrentState is not GameplayStateBase state)
            return;

        var floor = args.Viewport.LocalToWorld(args.VisiblePosition);
        if (!Shown(state.GetClickedEntity(floor, eye)))
            return;

        foreach (var spot in ClickSpots)
        {
            var point = new MapCoordinates(floor.Position + spot, floor.MapId);
            if (Shown(state.GetClickedEntity(point, eye)))
                continue;

            args.VisiblePosition = args.Viewport.WorldToLocal(point.Position);
            return;
        }
    }

    /// <summary>
    /// The stock-view point, in viewport pixels, that picks the nearest drawn solid under a point of the view.
    /// </summary>
    private Vector2? PickSolid(Vector2 target, IClydeViewport viewport, IEye eye)
    {
        var vertices = _mesh!.Vertices;
        for (var i = Drawn.Count - 1; i >= 0; i--)
        {
            var obj = Drawn[i];
            // Only the player's own level is acted on.
            if (obj.Level != 0 || obj.Kind == SceneKind.Slab)
                continue;

            if (obj.VertexStart + obj.VertexCount > _mesh.VertexCount || !Hit(obj, vertices, target, out var pixel))
                continue;

            if (StockPoint(obj, pixel, eye) is not { } point)
                continue;

            _pickUid = obj.Uid;
            _pickPoint = point;
            return viewport.WorldToLocal(point);
        }

        return null;
    }

    /// <summary>Whether the view draws an entity as a solid this frame.</summary>
    private bool Shown(EntityUid? uid)
    {
        return uid is { } id && _objects.TryGetValue(id, out var obj) && obj.Seen == Frame && obj.Draw;
    }

    /// <summary>
    /// Whether a point falls on an opaque part of an object's drawn faces, and which pixel of its image it hit.
    /// Front faces come last in the buffer, so the last hit wins.
    /// </summary>
    private bool Hit(SceneObject obj, DrawVertexUV2DColor[] vertices, Vector2 target, out Vector2 pixel)
    {
        pixel = default;
        if (obj.Slot is not { } slot)
            return false;

        if (!obj.Bounds.Contains(target))
            return false;

        var mask = obj.Kind is SceneKind.Solid or SceneKind.Flat && !obj.Billboard && obj.Mesh is { Fixed: true }
            ? Geometry(obj.GeometryKey)?.Analysis
            : null;
        var hit = false;
        for (var v = obj.VertexStart; v + 4 <= obj.VertexStart + obj.VertexCount; v += 4)
        {
            Vector2 uv;
            if (InTriangle(vertices[v], vertices[v + 1], vertices[v + 2], target, out var s, out var t))
            {
                if (!_mesh!.Pick(v, v + 1, v + 2, s, t, target, out uv))
                    continue;
            }
            else if (InTriangle(vertices[v], vertices[v + 2], vertices[v + 3], target, out s, out t))
            {
                if (!_mesh!.Pick(v, v + 2, v + 3, s, t, target, out uv))
                    continue;
            }
            else
            {
                continue;
            }

            var at = new Vector2(uv.X * slot.AtlasSize * 2f - slot.X, (1f - uv.Y) * slot.AtlasSize - slot.Y);
            // The mask is the south frame. A face showing another frame counts whole.
            var south = at.X >= 0f && at.Y >= 0f && at.X < slot.Size && at.Y < slot.Size;
            if (mask != null && (south || !obj.Views)
                && !mask.Opaque((int) MathF.Floor(at.X), (int) MathF.Floor(at.Y)))
            {
                continue;
            }

            pixel = at;
            hit = true;
        }

        return hit;
    }

    /// <summary>
    /// A stock-view point where the stock pick chooses the object, so clicks, hover and the context menu land on what
    /// the view shows. The point under the clicked pixel is tried first, then points spreading out from the object.
    /// </summary>
    private Vector2? StockPoint(SceneObject obj, Vector2 pixel, IEye eye)
    {
        if (_state.CurrentState is not GameplayStateBase state)
            return null;

        var map = eye.Position.MapId;
        // Billboards show the image the stock view draws, and an item's slab shows it lying at the item's angle, so a
        // pixel maps straight back to the stock sprite.
        var offset = (pixel - new Vector2(obj.SlotSize / 2f)) / EyeManager.PixelsPerMeter;
        Vector2? under = obj.ViewFrame
            ? obj.Position + Right * (offset.X + obj.Offset.X) + Up * (obj.Offset.Y - offset.Y)
            : obj.Kind == SceneKind.Flat
                ? obj.Position + obj.Rotation.RotateVec(new Vector2(offset.X, -offset.Y))
                : null;

        if (under is { } direct && Picks(state, obj, direct, map, eye))
            return direct;

        // Hovering runs every frame, so the last point found for the same object is checked before a new search.
        if (_pickUid == obj.Uid && Picks(state, obj, _pickPoint, map, eye))
            return _pickPoint;

        // Spots spread from the middle of the stock sprite, which an offset can move off the entity's own tile.
        if (!TryComp<SpriteComponent>(obj.Uid, out var sprite))
            return null;

        var (pos, rot) = _transform.GetWorldPositionRotation(obj.Uid);
        var centre = _sprite.CalculateBounds((obj.Uid, sprite), pos, rot, eye.Rotation).Center;
        foreach (var spot in ClickSpots)
        {
            var point = centre + spot;
            if (Picks(state, obj, point, map, eye))
                return point;
        }

        return null;
    }

    private static bool Picks(GameplayStateBase state, SceneObject obj, Vector2 point, MapId map, IEye eye)
    {
        return state.GetClickedEntity(new MapCoordinates(point, map), eye) == obj.Uid;
    }

    /// <summary>Whether a point lies in a triangle, with its weights on the second and third corners.</summary>
    private static bool InTriangle(DrawVertexUV2DColor a, DrawVertexUV2DColor b, DrawVertexUV2DColor c, Vector2 p,
        out float s, out float t)
    {
        s = t = 0f;
        var v0 = b.Position - a.Position;
        var v1 = c.Position - a.Position;
        var v2 = p - a.Position;
        var den = v0.X * v1.Y - v1.X * v0.Y;
        if (MathF.Abs(den) < 1e-8f)
            return false;

        s = (v2.X * v1.Y - v1.X * v2.Y) / den;
        t = (v0.X * v2.Y - v2.X * v0.Y) / den;
        return s >= 0f && t >= 0f && s + t <= 1f;
    }
}
