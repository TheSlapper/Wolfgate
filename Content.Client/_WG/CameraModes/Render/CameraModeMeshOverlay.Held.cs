using System.Numerics;
using Content.Client._WG.CameraModes.Atlas;
using Content.Client._WG.CameraModes.Fold;

namespace Content.Client._WG.CameraModes.Render;

public sealed partial class CameraModeMeshOverlay
{
    /// <summary>Where a held item sits from the eye: to its side, ahead and below, in tiles.</summary>
    private static readonly Vector3 HeldAt = new(0.24f, 0.5f, -0.24f);

    /// <summary>How much higher the item in the active hand sits.</summary>
    private const float HeldActiveRise = 0.05f;
    private const float HeldScale = 0.24f;

    /// <summary>How far a held item turns from facing the player, towards pointing ahead.</summary>
    private static readonly float HeldTurn = MathHelper.DegreesToRadians(68f);

    private int[] _heldOrder = new int[64];
    private float[] _heldDepth = new float[64];

    /// <summary>
    /// Draws an item the player holds, fixed to the screen at the lower left or right: its solid stood upright and
    /// turned so the right of its image points ahead, lit by the floor under the player. The right hand mirrors the
    /// left.
    /// </summary>
    private void AddHeld(SceneObject obj, AtlasSlot slot, FoldMesh mesh)
    {
        var mirror = obj.Hand == 1 ? 1f : -1f;
        var (sin, cos) = MathF.SinCos(HeldTurn);
        var at = new Vector3(-HeldAt.X, HeldAt.Y, HeldAt.Z + (obj.Lift > 0f ? HeldActiveRise : 0f));

        // The solid lies flat with the top of its image to the north. Its north goes up and its top faces the player,
        // then it turns about its upright.
        Vector3 Turn(Vector3 v)
        {
            return new Vector3((v.X * cos + v.Z * sin) * mirror, v.X * sin - v.Z * cos, v.Y);
        }

        Vector3 Place(Vector3 p)
        {
            return new Vector3(at.X * mirror, at.Y, at.Z) + Turn(p) * HeldScale;
        }

        var quads = mesh.Quads;
        if (quads.Length > _heldOrder.Length)
        {
            _heldOrder = new int[quads.Length];
            _heldDepth = new float[quads.Length];
        }

        // Far faces first, by how far each is from the eye. Faces turned away are left out.
        var count = 0;
        for (var i = 0; i < quads.Length; i++)
        {
            var centre = Place(quads[i].Centre);
            if (Vector3.Dot(Turn(quads[i].Normal), centre) >= 0f)
                continue;

            _heldOrder[count] = i;
            _heldDepth[count] = -centre.LengthSquared();
            count++;
        }

        Array.Sort(_heldDepth, _heldOrder, 0, count);
        obj.VertexStart = _count;
        for (var k = 0; k < count; k++)
        {
            ref var q = ref quads[_heldOrder[k]];
            var normal = Turn(q.Normal);
            // Faces turned up or to the middle of the view are brightest.
            var shade = q.Shade * Math.Clamp(0.8f + 0.2f * normal.Z + 0.15f * normal.X * mirror, 0.55f, 1f);
            var color = new Color(shade, shade, 0f, 1f);
            AddEyeQuad(HeldCorner(Place(q.P0), q.T0, slot, color), HeldCorner(Place(q.P1), q.T1, slot, color),
                HeldCorner(Place(q.P2), q.T2, slot, color), HeldCorner(Place(q.P3), q.T3, slot, color));
        }

        obj.VertexCount = _count - obj.VertexStart;
        obj.Bounds = VertexBounds(obj.VertexStart, _count);
    }

    private EyeVertex HeldCorner(Vector3 eye, Vector2 pixel, AtlasSlot slot, Color color)
    {
        return new EyeVertex { Eye = eye, Ground = _eye, Uv = SpriteAtlas.Uv(slot, pixel), Color = color };
    }
}
