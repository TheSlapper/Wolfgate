using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds a sprite that has its own side frames as the solid its front and side outlines share, pixel by pixel. A
/// cell of the solid is filled when the front frame and the side frame both cover it at that height. Each face shows
/// the frame drawn looking at it: the front the south frame, the back the north frame, each side the frame that faces
/// it. The rows each frame draws as a top seen from above are left out of the height and laid on the tops.
/// </summary>
public static class HullBuilder
{
    private const float PixelsPerTile = 32f;

    /// <summary>Most the front and side outlines may differ in height, as a share of the taller.</summary>
    private const float HeightSlack = 0.25f;

    /// <summary>How many times its front's width the side outline must be for the sides to shape the solid.</summary>
    private const float LongShare = 1.25f;

    private const byte South = 0;
    private const byte East = 1;
    private const byte North = 2;
    private const byte West = 3;

    /// <summary>
    /// The solid facing south, or null when the frames don't describe one: a frame is empty, the east frame is the
    /// south one again, the side outline is not much longer than the front, the two outlines differ too much in
    /// height, or nothing is left to build.
    /// </summary>
    /// <param name="east">The frame drawn with the entity turned to face east. It shows the solid's west side.</param>
    /// <param name="west">The frame drawn with the entity turned to face west. It shows the solid's east side.</param>
    /// <param name="origin">Pixel of each image at the entity's position.</param>
    public static FoldMesh? Build(SpriteAnalysis south, SpriteAnalysis east, SpriteAnalysis north, SpriteAnalysis west,
        Vector2 origin)
    {
        if (south.Empty || east.Empty || north.Empty || west.Empty || SilhouetteFold.Same(south, east))
            return null;

        // Only something much longer than its front is wide, like a cart seen end on, needs its sides to show its
        // shape. Anything else keeps the fold of its front.
        int wide = south.X1 - south.X0, deep = east.X1 - east.X0;
        if (deep < wide * LongShare)
            return null;

        int tall = south.Y1 - south.Y0, sideTall = east.Y1 - east.Y0;
        if (Math.Abs(tall - sideTall) > HeightSlack * Math.Max(tall, sideTall))
            return null;

        // Rows above the front's lid line are its top seen from above. The side frame draws its tops over a span as
        // much shorter as the solid is narrower than it is deep.
        var lid = SilhouetteFold.LidLine(south);
        var sideLid = lid == 0 ? 0 : (int) MathF.Round((float) lid * wide / deep);
        var floor = south.Y1;
        var mirror = (int) MathF.Round(2f * origin.X) - 1;

        var top = new int[east.Width];
        for (var c = east.X0; c < east.X1; c++)
        {
            top[c] = east.Y1;
            for (var r = east.Y0; r < east.Y1; r++)
            {
                if (east.Opaque(c, r))
                {
                    top[c] = r;
                    break;
                }
            }
        }

        var height = floor - (south.Y0 + lid);
        if (height <= 0)
            return null;

        bool Front(int x, int z)
        {
            var r = floor - 1 - z;
            return r >= south.Y0 + lid && south.Opaque(x, r);
        }

        bool Side(int c, int z)
        {
            var r = floor - 1 - z;
            return c >= east.X0 && c < east.X1 && r >= top[c] + sideLid && east.Opaque(c, r);
        }

        bool Solid(int x, int c, int z) =>
            x >= south.X0 && x < south.X1 && z >= 0 && z < height && Front(x, z) && Side(c, z);

        // Each frame draws a top higher up the image the further it lies from the viewer, by its lid rows over the
        // solid's length along the view.
        float rise = (float) lid / deep, sideRise = (float) sideLid / wide;

        // Whether a top cell's surface is seen along a view that runs out across x or c, rising as it goes.
        bool Clear(int x, int c, int z, int dx, int dc, float up)
        {
            for (var k = 1;; k++)
            {
                int xk = x + dx * k, ck = c + dc * k;
                var zk = (int) (z + 1 + k * up);
                if (xk < south.X0 || xk >= south.X1 || ck < east.X0 || ck >= east.X1 || zk >= height)
                    return true;

                if (Solid(xk, ck, zk))
                    return false;
            }
        }

        // The pixel and frame a top cell is drawn at, from the first frame that sees it. The solid is always long from
        // front to back, so the side frames draw its top along its whole length and come first. The front frame
        // squeezes that length into its lid rows and draws what stands behind over what stands in front.
        (Vector2, byte)? Seen(int x, int c, int z)
        {
            // A side frame draws an open part, like a rack, with gaps. A top seen through one takes the first pixel
            // below it, which the cell's own side always has.
            var h = z + 1;
            var re = (int) (floor - h - (x + 0.5f - south.X0) * sideRise);
            if (Clear(x, c, z, -1, 0, sideRise))
            {
                while (re < floor - 1 - z && !east.Opaque(c, re))
                    re++;

                return (new Vector2(c + 0.5f, re + 0.5f), East);
            }

            var rw = (int) (floor - h - (south.X1 - x - 0.5f) * sideRise);
            if (Clear(x, c, z, 1, 0, sideRise))
            {
                while (rw < floor - 1 - z && !west.Opaque(mirror - c, rw))
                    rw++;

                return (new Vector2(mirror - c + 0.5f, rw + 0.5f), West);
            }

            var rs = (int) (floor - h - (east.X1 - c - 0.5f) * rise);
            if (Clear(x, c, z, 0, 1, rise) && south.Opaque(x, rs))
                return (new Vector2(x + 0.5f, rs + 0.5f), South);

            var rn = (int) (floor - h - (c + 0.5f - east.X0) * rise);
            if (Clear(x, c, z, 0, -1, rise) && north.Opaque(mirror - x, rn))
                return (new Vector2(mirror - x + 0.5f, rn + 0.5f), North);

            return null;
        }

        float X(float x) => (x - origin.X) / PixelsPerTile;
        // The east frame's right end is the solid's front, which faces south.
        float Y(float c) => (origin.X - c) / PixelsPerTile;
        float Z(float z) => z / PixelsPerTile;

        var quads = new List<FoldQuad>();
        for (var z = 0; z < height; z++)
        {
            float z0 = Z(z), z1 = Z(z + 1);
            var r = floor - 1 - z;

            // Front and back faces, merged along each row of cells.
            for (var c = east.X0; c < east.X1; c++)
            {
                float yf = Y(c + 1), yb = Y(c);
                Runs(south.X0, south.X1, x => Solid(x, c, z) && !Solid(x, c + 1, z), (x0, x1) =>
                    Face(quads, new(X(x0), yf, z0), new(X(x1), yf, z0), new(X(x1), yf, z1), new(X(x0), yf, z1),
                        new(x0, r + 1), new(x1, r + 1), new(x1, r), new(x0, r), new(0, -1, 0), South));
                Runs(south.X0, south.X1, x => Solid(x, c, z) && !Solid(x, c - 1, z), (x0, x1) =>
                    Face(quads, new(X(x1), yb, z0), new(X(x0), yb, z0), new(X(x0), yb, z1), new(X(x1), yb, z1),
                        new(mirror + 1 - x1, r + 1), new(mirror + 1 - x0, r + 1), new(mirror + 1 - x0, r),
                        new(mirror + 1 - x1, r), new(0, 1, 0), North));
            }

            // Side faces, merged along each row of cells.
            for (var x = south.X0; x < south.X1; x++)
            {
                float xw = X(x), xe = X(x + 1);
                Runs(east.X0, east.X1, c => Solid(x, c, z) && !Solid(x - 1, c, z), (c0, c1) =>
                    Face(quads, new(xw, Y(c0), z0), new(xw, Y(c1), z0), new(xw, Y(c1), z1), new(xw, Y(c0), z1),
                        new(c0, r + 1), new(c1, r + 1), new(c1, r), new(c0, r), new(-1, 0, 0), East));
                Runs(east.X0, east.X1, c => Solid(x, c, z) && !Solid(x + 1, c, z), (c0, c1) =>
                    Face(quads, new(xe, Y(c1), z0), new(xe, Y(c0), z0), new(xe, Y(c0), z1), new(xe, Y(c1), z1),
                        new(mirror + 1 - c1, r + 1), new(mirror + 1 - c0, r + 1), new(mirror + 1 - c0, r),
                        new(mirror + 1 - c1, r), new(1, 0, 0), West));
            }

            // Tops. Each shows the first frame that sees it, the two sides, then the front, then the back, at the pixel
            // its centre is drawn at. Without one it shows the east frame's pixel at the foot of its top rows in that
            // column.
            for (var x = south.X0; x < south.X1; x++)
            {
                for (var c = east.X0; c < east.X1; c++)
                {
                    if (!Solid(x, c, z) || Solid(x, c, z + 1))
                        continue;

                    float xa = X(x), xb = X(x + 1), ya = Y(c + 1), yb = Y(c);
                    if (Seen(x, c, z) is var (seen, face))
                    {
                        Face(quads, new(xa, ya, z1), new(xb, ya, z1), new(xb, yb, z1), new(xa, yb, z1),
                            seen, seen, seen, seen, new(0, 0, 1), face);
                    }
                    else
                    {
                        var row = Math.Max(top[c], top[c] + sideLid - 1);
                        var t = new Vector2(c + 0.5f, row + 0.5f);
                        Face(quads, new(xa, ya, z1), new(xb, ya, z1), new(xb, yb, z1), new(xa, yb, z1),
                            t, t, t, t, new(0, 0, 1), East);
                    }
                }
            }
        }

        return quads.Count == 0 ? null : new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>Calls back with each run of indices where the test holds.</summary>
    private static void Runs(int from, int to, Func<int, bool> test, Action<int, int> run)
    {
        for (var i = from; i < to;)
        {
            if (!test(i))
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < to && test(end))
                end++;

            run(i, end);
            i = end;
        }
    }

    private static void Face(List<FoldQuad> quads, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 t0,
        Vector2 t1, Vector2 t2, Vector2 t3, Vector3 normal, byte face)
    {
        SilhouetteFold.Add(quads, p0, p1, p2, p3, t0, t1, t2, t3, normal, 1f);
        var q = quads[^1];
        q.Face = face;
        quads[^1] = q;
    }
}
