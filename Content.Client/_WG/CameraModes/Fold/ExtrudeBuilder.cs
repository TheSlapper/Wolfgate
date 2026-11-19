using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds solids for things lying down: a slab with the sprite on top and its outline pixels pushed down as side
/// walls, a barrel on its side read from its direction frames, or a round body lying along the sprite's rows.
/// </summary>
public static class ExtrudeBuilder
{
    private const float PixelsPerTile = 32f;

    /// <summary>Thickness of the slab, in tiles.</summary>
    public const float Thickness = 3f / PixelsPerTile;

    private const float SideShade = 0.75f;

    /// <summary>How far inside a run of edge pixels its wall samples, so it never picks up the pixel past the end.</summary>
    private const float In = 0.25f;

    /// <summary>How far the walls reach above the top, in tiles.</summary>
    private const float Lap = 0.5f / PixelsPerTile;

    /// <param name="origin">Pixel of the image at the entity's position.</param>
    /// <param name="z">Thickness of the slab, in tiles.</param>
    public static FoldMesh Build(SpriteAnalysis a, Vector2 origin, float z = Thickness)
    {
        var quads = new List<FoldQuad>();
        float X(float c) => (c - origin.X) / PixelsPerTile;
        float Y(float r) => (origin.Y - r) / PixelsPerTile;
        // Walls reach a little above the top, so no pixel along the outline falls between the two.
        var zw = z + Lap;

        // Walls along each row and column edge, one quad per run of edge pixels, coloured by the pixels behind them.
        for (var r = a.Y0; r < a.Y1; r++)
        {
            Runs(a, r, -1, (c0, c1) => SilhouetteFold.Add(quads,
                new(X(c0), Y(r), 0), new(X(c1), Y(r), 0), new(X(c1), Y(r), zw), new(X(c0), Y(r), zw),
                new(c0 + In, r + 0.5f), new(c1 - In, r + 0.5f), new(c1 - In, r + 0.5f), new(c0 + In, r + 0.5f),
                new(0, 1, 0), SideShade));
            Runs(a, r, 1, (c0, c1) => SilhouetteFold.Add(quads,
                new(X(c1), Y(r + 1), 0), new(X(c0), Y(r + 1), 0), new(X(c0), Y(r + 1), zw), new(X(c1), Y(r + 1), zw),
                new(c1 - In, r + 0.5f), new(c0 + In, r + 0.5f), new(c0 + In, r + 0.5f), new(c1 - In, r + 0.5f),
                new(0, -1, 0), SideShade));
        }

        for (var c = a.X0; c < a.X1; c++)
        {
            Columns(a, c, -1, (r0, r1) => SilhouetteFold.Add(quads,
                new(X(c), Y(r0), 0), new(X(c), Y(r1), 0), new(X(c), Y(r1), zw), new(X(c), Y(r0), zw),
                new(c + 0.5f, r0 + In), new(c + 0.5f, r1 - In), new(c + 0.5f, r1 - In), new(c + 0.5f, r0 + In),
                new(-1, 0, 0), SideShade));
            Columns(a, c, 1, (r0, r1) => SilhouetteFold.Add(quads,
                new(X(c + 1), Y(r1), 0), new(X(c + 1), Y(r0), 0), new(X(c + 1), Y(r0), zw), new(X(c + 1), Y(r1), zw),
                new(c + 0.5f, r1 - In), new(c + 0.5f, r0 + In), new(c + 0.5f, r0 + In), new(c + 0.5f, r1 - In),
                new(1, 0, 0), SideShade));
        }

        // The top last, so it covers the walls when quads share a depth.
        SilhouetteFold.Add(quads,
            new(X(a.X0), Y(a.Y1), z), new(X(a.X1), Y(a.Y1), z), new(X(a.X1), Y(a.Y0), z), new(X(a.X0), Y(a.Y0), z),
            new(a.X0, a.Y1), new(a.X1, a.Y1), new(a.X1, a.Y0), new(a.X0, a.Y0),
            new(0, 0, 1), 1f);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>Most pixels a round end's top widens by from one row to the next.</summary>
    private const int BarrelStep = 4;

    /// <summary>Share of the widest row the round end's top row is at most, and the share it must reach.</summary>
    private const float BarrelStart = 0.5f;
    private const float BarrelReach = 0.9f;

    /// <summary>Share of the height within which the round end's top reaches its full width.</summary>
    private const float BarrelCrown = 0.45f;

    /// <summary>How much wider than tall the side image's body must be.</summary>
    private const float BarrelLength = 1.1f;

    /// <summary>Share of the widest row a row of the side image must fill to be part of the body, not its stand.</summary>
    private const float BarrelBody = 0.6f;

    /// <summary>Height of a barrel's drawn round end as a share of its width, the squash of the usual three-quarter view.</summary>
    private const float EndSquash = 0.75f;

    /// <summary>Sides round a lying barrel, and the share of its width a stand leg spans across.</summary>
    private const int BarrelSegments = 16;
    private const float BarrelLeg = 0.6f;

    /// <summary>
    /// Whether a turnable sprite is a barrel lying along its facing: its front image is a round end, its top widening
    /// row by row, and its side image is longer than it is tall.
    /// </summary>
    public static bool Barrel(SpriteAnalysis south, SpriteAnalysis east)
    {
        if (south.X1 - south.X0 < 8 || east.X1 - east.X0 < 8)
            return false;

        var widest = 0;
        for (var r = south.Y0; r < south.Y1; r++)
        {
            var (c0, c1) = Span(south, r);
            widest = Math.Max(widest, c1 - c0);
        }

        var (t0, t1) = Span(south, south.Y0);
        if (t1 - t0 > widest * BarrelStart)
            return false;

        var last = t1 - t0;
        var reached = false;
        for (var r = south.Y0 + 1; r < south.Y0 + (south.Y1 - south.Y0) * BarrelCrown; r++)
        {
            var (c0, c1) = Span(south, r);
            if (c1 - c0 < last || c1 - c0 - last > BarrelStep)
                return false;

            last = c1 - c0;
            if (last >= widest * BarrelReach)
            {
                reached = true;
                break;
            }
        }

        if (!reached)
            return false;

        var (top, bottom, x0, x1) = Body(east);
        return bottom > top && x1 - x0 >= (bottom - top) * BarrelLength;
    }

    /// <summary>
    /// The rows and columns of a side image that are its barrel, leaving out the stand below it and a tap or handle
    /// sticking out at an end: the longest run of columns filled down most of the body's rows.
    /// </summary>
    private static (int Top, int Bottom, int X0, int X1) Body(SpriteAnalysis a)
    {
        var (top, bottom, x0, x1) = BodyRows(a);
        var need = (bottom - top) * BarrelBody;
        int best0 = x0, best1 = x0;
        for (var c = x0; c < x1;)
        {
            if (Filled(c) < need)
            {
                c++;
                continue;
            }

            var c0 = c;
            while (c < x1 && Filled(c) >= need)
                c++;

            if (c - c0 > best1 - best0)
                (best0, best1) = (c0, c);
        }

        return best1 > best0 ? (top, bottom, best0, best1) : (top, bottom, x0, x1);

        int Filled(int c)
        {
            var n = 0;
            for (var r = top; r < bottom; r++)
            {
                if (a.Opaque(c, r))
                    n++;
            }

            return n;
        }
    }

    /// <summary>
    /// The rows of a side image that are its barrel, the unbroken run of wide rows round the widest one, leaving out
    /// the stand below it, and their columns.
    /// </summary>
    private static (int Top, int Bottom, int X0, int X1) BodyRows(SpriteAnalysis a)
    {
        var widest = a.Y0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            if (a.Count[r] > a.Count[widest])
                widest = r;
        }

        var need = a.Count[widest] * BarrelBody;
        int top = widest, bottom = widest + 1;
        while (top > a.Y0 && a.Count[top - 1] >= need)
            top--;

        while (bottom < a.Y1 && a.Count[bottom] >= need)
            bottom++;

        int x0 = a.X1, x1 = a.X0;
        for (var r = top; r < bottom; r++)
        {
            var (c0, c1) = Span(a, r);
            x0 = Math.Min(x0, c0);
            x1 = Math.Max(x1, c1);
        }

        return (top, bottom, x0, x1);
    }

    /// <summary>
    /// A barrel lying along its facing, its front end at the south. The front image's width is its diameter and the
    /// side image's body its length. The curved side shows the side image unrolled from top to bottom, the ends show
    /// the front and back images, and the stand under the side image's body stands as legs across the barrel.
    /// </summary>
    public static FoldMesh BuildBarrel(SpriteAnalysis south, SpriteAnalysis east, SpriteAnalysis north, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        var (top, bottom, x0, x1) = Body(east);
        // Seen side on, the barrel is as tall as it is round. The front image's width would take in its stand.
        var diameter = bottom - top;
        var radius = diameter / 2f / PixelsPerTile;
        var legs = (east.Y1 - bottom) / PixelsPerTile;
        var axisZ = legs + radius;
        var middle = (south.X0 + south.X1) / 2f;
        var axisX = (middle - origin.X) / PixelsPerTile;
        var length = (x1 - x0) / PixelsPerTile;
        float yFront = -length / 2f, yBack = length / 2f;
        var step = MathF.Tau / BarrelSegments;

        Vector3 Ring(float y, float angle) =>
            new(axisX + radius * MathF.Sin(angle), y, axisZ + radius * MathF.Cos(angle));

        // Angle 0 is the top. Both halves show the side image's rows from top to bottom.
        float Row(float angle) => top + (bottom - top) * MathF.Min(angle, MathF.Tau - angle) / MathF.PI;

        for (var c = x0; c < x1; c++)
        {
            // The side image has the front at its right.
            float yn = yFront + (x1 - c - 1) / PixelsPerTile, yf = yFront + (x1 - c) / PixelsPerTile;
            for (var k = 0; k < BarrelSegments; k++)
            {
                float a0 = k * step, a1 = (k + 1) * step, mid = (a0 + a1) / 2f;
                var normal = new Vector3(MathF.Sin(mid), 0f, MathF.Cos(mid));
                SilhouetteFold.Add(quads, Ring(yf, a0), Ring(yn, a0), Ring(yn, a1), Ring(yf, a1),
                    new(c + 0.5f, Row(a0)), new(c + 0.5f, Row(a0)), new(c + 0.5f, Row(a1)), new(c + 0.5f, Row(a1)),
                    normal, 1f);
                quads[^1] = quads[^1] with { Face = 1 };
            }
        }

        AddEnd(south, yFront, -1f, 0);
        AddEnd(north, yBack, 1f, 2);

        // Each run of the stand's bottom row is a leg across the barrel, from the floor to the barrel's underside.
        if (bottom < east.Y1)
        {
            var span = radius * BarrelLeg;
            var c = east.X0;
            while (c < east.X1)
            {
                if (!east.Opaque(c, east.Y1 - 1))
                {
                    c++;
                    continue;
                }

                var c0 = c;
                while (c < east.X1 && east.Opaque(c, east.Y1 - 1))
                    c++;

                float yn = yFront + (x1 - c) / PixelsPerTile, yf = yFront + (x1 - c0) / PixelsPerTile;
                AddLeg(axisX - span, axisX + span, yn, yf, c0, c);
            }
        }

        return new FoldMesh(quads.ToArray(), true);

        // An end cap as a fan of triangles from the axis, each point taking the end image's pixel at its place on the
        // disc.
        void AddEnd(SpriteAnalysis image, float y, float facing, byte face)
        {
            var mid = (image.X0 + image.X1) / 2f;
            var r = diameter / 2f;
            // The end is drawn as wide as the barrel and squashed by the view, at the bottom of the image above any
            // foot narrower than half of it. The rows above it are the barrel's top going back.
            var end = image.Y1;
            while (end > image.Y0 && Span(image, end - 1) is var (e0, e1) && e1 - e0 < r)
                end--;

            var half = diameter * EndSquash / 2f;
            Vector2 Tex(float angle, float share) =>
                new(mid + facing * -1f * share * r * MathF.Sin(angle), end - half - share * half * MathF.Cos(angle));

            var centre = new Vector3(axisX, y, axisZ);
            for (var k = 0; k < BarrelSegments; k++)
            {
                float a0 = k * step, a1 = (k + 1) * step;
                SilhouetteFold.Add(quads, centre, Ring(y, a0), Ring(y, a1), centre,
                    Tex(0f, 0f), Tex(a0, 1f), Tex(a1, 1f), Tex(0f, 0f), new(0f, facing, 0f), 1f);
                quads[^1] = quads[^1] with { Face = face };
            }
        }

        // A leg box under the barrel, its long sides showing the stand's rows of the side image.
        void AddLeg(float xa, float xb, float yn, float yf, int c0, int c1)
        {
            var zt = axisZ - radius * 0.5f;
            Vector2 tl = new(c0, bottom), tr = new(c1, bottom), bl = new(c0, east.Y1), br = new(c1, east.Y1);
            SilhouetteFold.Add(quads, new(xa, yf, 0f), new(xa, yn, 0f), new(xa, yn, zt), new(xa, yf, zt),
                br, bl, tl, tr, new(-1, 0, 0), 1f);
            quads[^1] = quads[^1] with { Face = 1 };
            SilhouetteFold.Add(quads, new(xb, yn, 0f), new(xb, yf, 0f), new(xb, yf, zt), new(xb, yn, zt),
                bl, br, tr, tl, new(1, 0, 0), 1f);
            quads[^1] = quads[^1] with { Face = 1 };
            var edge = new Vector2(c0 + 0.5f, east.Y1 - 0.5f);
            SilhouetteFold.Add(quads, new(xb, yn, 0f), new(xa, yn, 0f), new(xa, yn, zt), new(xb, yn, zt),
                edge, edge, edge, edge, new(0, -1, 0), SideShade);
            quads[^1] = quads[^1] with { Face = 1 };
            SilhouetteFold.Add(quads, new(xa, yf, 0f), new(xb, yf, 0f), new(xb, yf, zt), new(xa, yf, zt),
                edge, edge, edge, edge, new(0, 1, 0), SideShade);
            quads[^1] = quads[^1] with { Face = 1 };
        }
    }

    /// <summary>Sides round a lying round solid.</summary>
    private const int LyingSegments = 12;

    /// <summary>Rows whose middle sits this far off the sprite's middle still count as mirror-symmetric, in pixels.</summary>
    private const float LyingSlack = 1.5f;

    /// <summary>Share of rows that must be mirror-symmetric for top-down art to lie as a round solid.</summary>
    private const float LyingShare = 0.85f;

    /// <summary>
    /// Whether top-down art is mirror-symmetric about the line down its middle and changes width along its rows, so
    /// each row reads as the width of a round body lying along its facing.
    /// </summary>
    public static bool Lying(SpriteAnalysis a)
    {
        if (a.X1 - a.X0 < 8 || a.Y1 - a.Y0 < 8)
            return false;

        var middle = (a.X0 + a.X1) / 2f;
        var rows = 0;
        var even = 0;
        var narrow = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            if (c1 <= c0)
                continue;

            rows++;
            if (MathF.Abs((c0 + c1) / 2f - middle) <= LyingSlack)
                even++;

            if (c1 - c0 <= a.X1 - a.X0 - LyingNarrow)
                narrow++;
        }

        // A body of one width all along is a frame or a box seen from above, not a barrel.
        return rows > 0 && even >= rows * LyingShare && narrow >= rows * LyingVaried;
    }

    /// <summary>Pixels narrower than the widest row a row must be to count towards a barrel's changing width.</summary>
    private const int LyingNarrow = 2;

    /// <summary>Share of rows that must be narrower than the widest for top-down art to lie as a round solid.</summary>
    private const float LyingVaried = 0.25f;

    /// <summary>
    /// A round solid lying on the floor along the sprite's rows, each row a ring as wide as that row, resting on its
    /// widest ring. The art is its top, so every point takes the pixel straight above it.
    /// </summary>
    public static FoldMesh BuildLying(SpriteAnalysis a, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        float Y(float r) => (origin.Y - r) / PixelsPerTile;
        var middle = (a.X0 + a.X1) / 2f;
        var axisX = (middle - origin.X) / PixelsPerTile;

        var radius = new float[a.Height];
        var height = 0f;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            radius[r] = c1 > c0 ? (c1 - c0) / 2f / PixelsPerTile : 0f;
            height = MathF.Max(height, radius[r]);
        }

        Vector3 Ring(float rad, float y, float angle) =>
            new(axisX + rad * MathF.Cos(angle), y, height + rad * MathF.Sin(angle));

        Vector2 Above(float rad, float row, float angle) =>
            new(middle + rad * PixelsPerTile * MathF.Cos(angle), row);

        var step = MathF.Tau / LyingSegments;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var rad = radius[r];
            if (rad <= 0f)
                continue;

            float yn = Y(r), ys = Y(r + 1);
            for (var k = 0; k < LyingSegments; k++)
            {
                float a0 = k * step, a1 = (k + 1) * step, mid = (a0 + a1) / 2f;
                var normal = new Vector3(MathF.Cos(mid), 0f, MathF.Sin(mid));
                SilhouetteFold.Add(quads, Ring(rad, ys, a0), Ring(rad, ys, a1), Ring(rad, yn, a1), Ring(rad, yn, a0),
                    Above(rad, r + 1, a0), Above(rad, r + 1, a1), Above(rad, r, a1), Above(rad, r, a0), normal, 1f);
            }

            // Where a neighbouring row is narrower or missing, the step between them faces along the axis.
            var next = r + 1 < a.Y1 ? radius[r + 1] : 0f;
            var before = r - 1 >= a.Y0 ? radius[r - 1] : 0f;
            AddStep(quads, rad, next, ys, -1f);
            AddStep(quads, rad, before, yn, 1f);

            void AddStep(List<FoldQuad> list, float outer, float inner, float y, float facing)
            {
                if (inner >= outer)
                    return;

                var t = new Vector2(middle, r + 0.5f);
                for (var k = 0; k < LyingSegments; k++)
                {
                    float a0 = k * step, a1 = (k + 1) * step;
                    SilhouetteFold.Add(list, Ring(inner, y, a0), Ring(outer, y, a0), Ring(outer, y, a1),
                        Ring(inner, y, a1), t, t, t, t, new Vector3(0f, facing, 0f), SideShade);
                }
            }
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>The first opaque column of a row and the column after its last.</summary>
    private static (int, int) Span(SpriteAnalysis a, int r)
    {
        var c0 = a.X0;
        while (c0 < a.X1 && !a.Opaque(c0, r))
            c0++;

        var c1 = a.X1;
        while (c1 > c0 && !a.Opaque(c1 - 1, r))
            c1--;

        return (c0, c1);
    }

    /// <summary>Runs of opaque pixels in a row whose neighbour in the row above or below is clear.</summary>
    private static void Runs(SpriteAnalysis a, int r, int dr, Action<int, int> add)
    {
        var start = -1;
        for (var c = a.X0; c <= a.X1; c++)
        {
            var edge = c < a.X1 && a.Opaque(c, r) && !a.Opaque(c, r + dr);
            if (edge && start < 0)
                start = c;

            if (edge || start < 0)
                continue;

            add(start, c);
            start = -1;
        }
    }

    /// <summary>Runs of opaque pixels in a column whose neighbour to the left or right is clear.</summary>
    private static void Columns(SpriteAnalysis a, int c, int dc, Action<int, int> add)
    {
        var start = -1;
        for (var r = a.Y0; r <= a.Y1; r++)
        {
            var edge = r < a.Y1 && a.Opaque(c, r) && !a.Opaque(c + dc, r);
            if (edge && start < 0)
                start = r;

            if (edge || start < 0)
                continue;

            add(start, r);
            start = -1;
        }
    }
}
