using System.Numerics;
using Content.Shared.Examine;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private ExamineSystemShared _examine = default!;

    private const float IconPixelsPerTile = 32f;

    /// <summary>Farthest a mark over an entity shows in first person, in tiles.</summary>
    private const float MarkSightRange = 64f;

    /// <summary>
    /// Moves an icon an overlay draws at the top of an entity's stock sprite to the top of what the view draws. Gives
    /// how far to raise the icon in the eye's frame before the squash, and the upward stretch that undoes the squash.
    /// </summary>
    /// <param name="stockTop">Height of the stock sprite's top above the entity, in the eye's frame.</param>
    public (float Lift, float Stretch) IconRaise(EntityUid uid, float stockTop, in OverlayDrawArgs args)
    {
        // An overlay may draw before or after this frame's scene is gathered.
        if (!Active || FirstPerson || !IsMainPass(args) || !_objects.TryGetValue(uid, out var obj)
            || Frame - obj.Seen > 1 || !obj.Draw)
        {
            return (0f, 1f);
        }

        // Height on screen of the top of what the view draws, in tiles.
        var profile = Profile;
        var top = !obj.Billboard && obj.Mesh is { Fixed: true } mesh
            ? (mesh.Top + obj.Lift) * profile.Height
            : obj.FrameBottom / IconPixelsPerTile + obj.Offset.Y;

        return (top - stockTop, 1f / profile.Squash);
    }

    /// <summary>
    /// The space a mark over entities draws in: after the first-person view while it shows, so the mark stands over
    /// the figure and not on the floor, otherwise the overlay's own.
    /// </summary>
    public OverlaySpace MarkSpace(OverlaySpace stock)
    {
        return Active && FirstPerson ? OverlaySpace.WorldSpace : stock;
    }

    /// <summary>
    /// In first person, moves a mark an overlay draws over an entity to the top of the figure the view draws, sized by
    /// its distance. Leaves the matrix alone in other views. False when the mark should not draw.
    /// </summary>
    /// <param name="stockTop">Height of the mark's anchor above the entity in the overlay's frame, in tiles.</param>
    /// <param name="own">Whether the player's own mark shows, low in the middle of the view.</param>
    public bool PlaceMark(EntityUid uid, float stockTop, in OverlayDrawArgs args, ref Matrix3x2 matrix, bool own = false)
    {
        if (!Active || !FirstPerson)
            return true;

        if (!IsMainPass(args) || !TryMark(uid, own, out var world, out var scale))
            return false;

        var eyeRot = args.Viewport.Eye?.Rotation ?? Angle.Zero;
        matrix = Matrix3x2.CreateTranslation(0f, -stockTop) * Matrix3x2.CreateScale(scale)
                 * Matrix3Helpers.CreateRotation(-eyeRot) * Matrix3x2.CreateTranslation(world);
        return true;
    }

    /// <summary>
    /// In first person, the stock-view point that shows over the top of the figure the view draws for an entity, for
    /// marks placed by screen position. Null in other views, and false when first person does not show the entity.
    /// </summary>
    public bool TryMarkWorld(EntityUid uid, out Vector2? world)
    {
        world = null;
        if (!Active || !FirstPerson)
            return true;

        if (!TryMark(uid, false, out var point, out _))
            return false;

        world = point;
        return true;
    }

    /// <summary>The first-person spot and scale for a mark over an entity drawn this frame and in the player's sight.</summary>
    private bool TryMark(EntityUid uid, bool own, out Vector2 world, out float scale)
    {
        world = default;
        scale = 1f;
        if (_mesh == null)
            return false;

        if (uid == _player.LocalEntity)
        {
            world = _mesh.OwnMarkSpot();
            return own;
        }

        return _objects.TryGetValue(uid, out var obj) && Frame - obj.Seen <= 1 && obj.Draw && InSight(obj)
               && _mesh.TryMarkSpot(obj, out world, out scale);
    }

    /// <summary>Whether nothing that blocks sight stands between the player and an object, tested once a frame.</summary>
    private bool InSight(SceneObject obj)
    {
        if (obj.SightFrame != Frame)
        {
            obj.SightFrame = Frame;
            obj.InSight = _player.LocalEntity is { } local
                          && _examine.InRangeUnOccluded(local, obj.Uid, MarkSightRange);
        }

        return obj.InSight;
    }
}
