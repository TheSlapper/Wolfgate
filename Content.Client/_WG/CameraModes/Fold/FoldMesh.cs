using System.Numerics;
using Robust.Shared.Maths;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>One textured quad of a mesh, in object space: x east, y north, z up, in tiles.</summary>
public struct FoldQuad
{
    public Vector3 P0, P1, P2, P3;

    /// <summary>Texture coordinates in pixels of the sprite image.</summary>
    public Vector2 T0, T1, T2, T3;

    public Vector3 Normal;
    public Vector3 Centre;

    /// <summary>Brightness multiplier. Most builders shade sides, backs and undersides below 1.</summary>
    public float Shade;

    /// <summary>For block faces, the grid direction the face looks to, so faces against a neighbour are skipped.</summary>
    public Direction Side;

    /// <summary>Which direction frame the quad shows: 0 south, 1 east, 2 north, 3 west.</summary>
    public byte Face;
}

/// <summary>A sprite turned into a solid, with the quad orders for each view direction.</summary>
public sealed class FoldMesh
{
    /// <summary>View directions the quad orders are cached for, round a full turn.</summary>
    public const int YawBuckets = 16;

    public readonly FoldQuad[] Quads;

    /// <summary>Height of the highest point.</summary>
    public readonly float Top;

    /// <summary>True when the solid stays put as the view turns. False for ones that turn to face the camera.</summary>
    public readonly bool Fixed;

    /// <summary>True when some quads show a direction frame other than the south one.</summary>
    public readonly bool Views;

    /// <summary>The box round the points that touch the floor, or null when the solid hangs clear of it.</summary>
    public readonly Box2? Base;

    private readonly int[]?[] _orders = new int[YawBuckets][];
    private float _orderPitch = float.NaN;

    public FoldMesh(FoldQuad[] quads, bool fixedInWorld)
    {
        Quads = quads;
        Fixed = fixedInWorld;
        foreach (var q in quads)
        {
            Views |= q.Face != 0;
            Top = Math.Max(Top, Math.Max(Math.Max(q.P0.Z, q.P1.Z), Math.Max(q.P2.Z, q.P3.Z)));
            Base = Touch(Touch(Touch(Touch(Base, q.P0), q.P1), q.P2), q.P3);
        }
    }

    private const float FloorReach = 0.02f;

    private static Box2? Touch(Box2? floor, Vector3 p)
    {
        if (p.Z > FloorReach)
            return floor;

        var at = new Vector2(p.X, p.Y);
        return floor is { } box ? box.ExtendToContain(at) : new Box2(at, at);
    }

    /// <summary>
    /// Quads sorted far to near for a camera looking along the object-space direction of a yaw bucket. Each order is
    /// cached until the pitch changes.
    /// </summary>
    public int[] Order(int bucket, float pitchSin, float pitchCos)
    {
        if (_orderPitch != pitchSin)
        {
            Array.Clear(_orders);
            _orderPitch = pitchSin;
        }

        if (_orders[bucket] is { } cached)
            return cached;

        // The camera sits on the south side when the bucket is 0 and turns with it.
        var yaw = bucket * MathF.Tau / YawBuckets;
        var toCam = new Vector3(MathF.Sin(yaw) * pitchCos, -MathF.Cos(yaw) * pitchCos, pitchSin);
        var keys = new float[Quads.Length];
        var order = new int[Quads.Length];
        for (var i = 0; i < Quads.Length; i++)
        {
            keys[i] = -Vector3.Dot(Quads[i].Centre, toCam);
            order[i] = i;
        }

        Array.Sort(keys, order);
        Array.Reverse(order);
        _orders[bucket] = order;
        return order;
    }
}
