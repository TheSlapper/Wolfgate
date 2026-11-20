using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds full-tile blocks for walls, windows, tables and closed doors, and part-tile prisms for blocks like diagonal
/// windows. A wall or window's sprite is its top, and each side folds the top's border band down or shows the front the
/// sprite draws. A door shows its sprite on both faces, and a table is read into a slab, rails and legs.
/// </summary>
public static class BlockBuilder
{
    private const float Half = 0.5f;
    private const float SideShade = 0.85f;

    /// <summary>How far in from the top's edge a side face reaches by its foot, in pixels.</summary>
    private const float Band = 12f;

    /// <summary>A block whose whole sprite is its top.</summary>
    /// <param name="origin">Pixel of the image at the tile centre.</param>
    /// <param name="height">Height of the block in tiles.</param>
    public static FoldMesh Build(Vector2 origin, float height)
    {
        return Build(origin, height, origin.Y + 16f);
    }

    /// <summary>
    /// A block whose sprite draws a front face below its top, like tg's 3/4 walls. The rows above
    /// <paramref name="frontRow"/> are the top, and the rows below are the south face.
    /// </summary>
    /// <param name="frontRow">Pixel row where the front face starts. The tile's bottom edge means no front face.</param>
    public static FoldMesh Build(Vector2 origin, float height, float frontRow)
    {
        var quads = new List<FoldQuad>();
        var l = origin.X - 16f;
        var r = origin.X + 16f;
        var t = origin.Y - 16f;
        var b = origin.Y + 16f;
        var front = frontRow < b - 0.5f;

        // The top, stretched over the whole tile when the sprite keeps a front face below it.
        var tb = front ? frontRow : b;
        Add(quads, new(-Half, -Half, height), new(Half, -Half, height), new(Half, Half, height), new(-Half, Half, height),
            new(l, tb), new(r, tb), new(r, t), new(l, t), new(0, 0, 1), 1f, Direction.Invalid);

        // Each side folds the top's border down: the top edge of the face is the top's outer row, and the foot reaches
        // Band pixels in. A drawn front face takes the south side instead.
        var band = MathF.Min(Band, height * 32f);
        var bandY = MathF.Min(band, (tb - t) / 2f);
        if (front)
        {
            Add(quads, new(-Half, -Half, 0), new(Half, -Half, 0), new(Half, -Half, height), new(-Half, -Half, height),
                new(l, b), new(r, b), new(r, frontRow), new(l, frontRow), new(0, -1, 0), SideShade, Direction.South);
        }
        else
        {
            Add(quads, new(-Half, -Half, 0), new(Half, -Half, 0), new(Half, -Half, height), new(-Half, -Half, height),
                new(l, tb - bandY), new(r, tb - bandY), new(r, tb), new(l, tb), new(0, -1, 0), SideShade, Direction.South);
        }

        Add(quads, new(Half, Half, 0), new(-Half, Half, 0), new(-Half, Half, height), new(Half, Half, height),
            new(r, t + bandY), new(l, t + bandY), new(l, t), new(r, t), new(0, 1, 0), SideShade, Direction.North);
        Add(quads, new(Half, -Half, 0), new(Half, Half, 0), new(Half, Half, height), new(Half, -Half, height),
            new(r - band, tb), new(r - band, t), new(r, t), new(r, tb), new(1, 0, 0), SideShade, Direction.East);
        Add(quads, new(-Half, Half, 0), new(-Half, -Half, 0), new(-Half, -Half, height), new(-Half, Half, height),
            new(l + band, t), new(l + band, tb), new(l, tb), new(l, t), new(-1, 0, 0), SideShade, Direction.West);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// A block drawn from its sheet's straight runs, like a frill wall: the image holds the east-west run above the
    /// centre row and the north-south run below it. The north-south run shows two thin rims either side of a middle,
    /// and the middle is the top. The east-west run's front, below the rows in the top's colour, is every side.
    /// </summary>
    /// <param name="bottom">Height its sides start at, for a window standing on a low wall.</param>
    /// <param name="frontRow">Image row the sides start at, or -1 to read it from the east-west run.</param>
    /// <param name="topped">
    /// Whether its art draws a top. Without one, like a window, the whole east-west run is every side and the top
    /// takes one row a quarter of the way down it, inside the upper pane.
    /// </param>
    public static FoldMesh BuildRimmed(SpriteAnalysis a, Vector2 origin, float height, float bottom = 0f,
        int frontRow = -1, bool topped = true)
    {
        var quads = new List<FoldQuad>();
        var l = origin.X - 16f;
        var r = origin.X + 16f;
        var centre = (int) origin.Y;
        var across = a.Rows(0, centre);
        var along = a.Rows(centre, a.Height);
        var alongTop = along.Empty ? centre : along.Y0;
        var alongBottom = along.Empty ? centre + 32 : along.Y1;

        // The sides: the front of the east-west run, from the first row below its top band to its lowest opaque row.
        // The top band is the rows drawn in the top's own colour, the top seen at a slant.
        float foot = across.Empty ? centre : across.Y1;
        float front = frontRow >= 0 ? frontRow : across.Empty ? centre - 32 : across.Y0;
        if (topped && frontRow < 0 && !across.Empty)
        {
            var top = ColumnLum(a, (int) origin.X, alongTop, alongBottom);
            var row = across.Y0;
            while (row < across.Y1 - 1 && MathF.Abs(RowLum(a, row, (int) l, (int) r) - top) <= RimStep)
            {
                row++;
            }

            front = row;
        }

        if (topped)
        {
            // The rims of the north-south run, each at most a quarter of the tile wide, are left off the top.
            var (west, east) = Rims(a, (int) l, alongTop, alongBottom);
            Add(quads, new(-Half, -Half, height), new(Half, -Half, height), new(Half, Half, height),
                new(-Half, Half, height), new(l + west, alongBottom), new(r - east, alongBottom),
                new(r - east, alongTop), new(l + west, alongTop), new(0, 0, 1), 1f, Direction.Invalid);
        }
        else
        {
            var pane = MathF.Floor(front + (foot - front) / 4f);
            Add(quads, new(-Half, -Half, height), new(Half, -Half, height), new(Half, Half, height),
                new(-Half, Half, height), new(l, pane + 1), new(r, pane + 1), new(r, pane), new(l, pane),
                new(0, 0, 1), 1f, Direction.Invalid);
        }
        var z = bottom;
        Add(quads, new(-Half, -Half, z), new(Half, -Half, z), new(Half, -Half, height), new(-Half, -Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(0, -1, 0), SideShade, Direction.South);
        Add(quads, new(Half, Half, z), new(-Half, Half, z), new(-Half, Half, height), new(Half, Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(0, 1, 0), SideShade, Direction.North);
        Add(quads, new(Half, -Half, z), new(Half, Half, z), new(Half, Half, height), new(Half, -Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(1, 0, 0), SideShade, Direction.East);
        Add(quads, new(-Half, Half, z), new(-Half, -Half, z), new(-Half, -Half, height), new(-Half, Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(-1, 0, 0), SideShade, Direction.West);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// The row a low wall's front starts at: under the sharpest step to darker rows in the lower half of its art, the
    /// shadow under its lip.
    /// </summary>
    public static int ShadowRow(SpriteAnalysis a, int top, int bottom, int left, int right)
    {
        var best = -1;
        var drop = 0f;
        var above = RowLum(a, (top + bottom) / 2 - 1, left, right);
        for (var row = (top + bottom) / 2; row < bottom; row++)
        {
            var lum = RowLum(a, row, left, right);
            if (above >= 0f && lum >= 0f && above - lum > drop)
            {
                drop = above - lum;
                best = row;
            }

            above = lum;
        }

        return best;
    }

    /// <summary>Mean brightness of one row's opaque pixels between two columns, or -1 when none are opaque.</summary>
    private static float RowLum(SpriteAnalysis a, int row, int left, int right)
    {
        var sum = 0f;
        var count = 0;
        for (var column = left; column < right; column++)
        {
            if (!a.Opaque(column, row))
                continue;

            sum += a.Lum[row * a.Width + column];
            count++;
        }

        return count == 0 ? -1f : sum / count;
    }

    /// <summary>How far a column's mean brightness may stray from the middle's and still count as middle.</summary>
    private const float RimStep = 0.06f;

    /// <summary>The widest a rim may be, in pixels.</summary>
    private const int RimMax = 8;

    /// <summary>
    /// The width of the rim at each side of a straight run's image: the columns in from each edge whose brightness
    /// differs from the middle column's.
    /// </summary>
    private static (int West, int East) Rims(SpriteAnalysis a, int left, int top, int bottom)
    {
        var middle = ColumnLum(a, left + 16, top, bottom);
        var west = 0;
        while (west < RimMax && MathF.Abs(ColumnLum(a, left + west, top, bottom) - middle) > RimStep)
        {
            west++;
        }

        var east = 0;
        while (east < RimMax && MathF.Abs(ColumnLum(a, left + 31 - east, top, bottom) - middle) > RimStep)
        {
            east++;
        }

        return (west, east);
    }

    /// <summary>Mean brightness of one column's opaque pixels between two rows, or -1 when none are opaque.</summary>
    private static float ColumnLum(SpriteAnalysis a, int column, int top, int bottom)
    {
        var sum = 0f;
        var count = 0;
        for (var row = top; row < bottom; row++)
        {
            if (!a.Opaque(column, row))
                continue;

            sum += a.Lum[row * a.Width + column];
            count++;
        }

        return count == 0 ? -1f : sum / count;
    }

    /// <summary>
    /// A window whose art reaches above its tile. The rows above the lid line are the top. The rows below are the front
    /// and back faces, at the scale of the tile's own rows, so clear rows under the art leave the foot open for the low
    /// wall the flat view shows there.
    /// </summary>
    public static FoldMesh BuildTall(SpriteAnalysis a, Vector2 origin, float height)
    {
        var quads = new List<FoldQuad>();
        var l = origin.X - 16f;
        var r = origin.X + 16f;
        var b = origin.Y + 16f;
        var t = (float) a.Y0;
        var lid = SilhouetteFold.LidLine(a);
        var front = lid > 0 ? t + lid : t + (a.Y1 - t) / 3f;
        var foot = a.Y1;
        var baseZ = MathF.Max(0f, height * (b - foot) / (b - front));
        var band = MathF.Min(SideBand, r - l);

        Add(quads, new(-Half, -Half, height), new(Half, -Half, height), new(Half, Half, height), new(-Half, Half, height),
            new(l, front), new(r, front), new(r, t), new(l, t), new(0, 0, 1), 1f, Direction.Invalid);
        Add(quads, new(-Half, -Half, baseZ), new(Half, -Half, baseZ), new(Half, -Half, height), new(-Half, -Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(0, -1, 0), SideShade, Direction.South);
        Add(quads, new(Half, Half, baseZ), new(-Half, Half, baseZ), new(-Half, Half, height), new(Half, Half, height),
            new(l, foot), new(r, foot), new(r, front), new(l, front), new(0, 1, 0), SideShade, Direction.North);
        Add(quads, new(Half, -Half, baseZ), new(Half, Half, baseZ), new(Half, Half, height), new(Half, -Half, height),
            new(r - band, foot), new(r, foot), new(r, front), new(r - band, front), new(1, 0, 0), SideShade,
            Direction.East);
        Add(quads, new(-Half, Half, baseZ), new(-Half, -Half, baseZ), new(-Half, -Half, height), new(-Half, Half, height),
            new(l, foot), new(l + band, foot), new(l + band, front), new(l, front), new(-1, 0, 0), SideShade,
            Direction.West);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// Flat art painted on the open face of the wall tile it shares, like graffiti, at the height the flat view draws
    /// it: each image row keeps its height above the tile's bottom edge.
    /// </summary>
    public static FoldMesh BuildPainted(SpriteAnalysis a, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        var foot = origin.Y + 16f;
        var low = MathF.Min(a.Y1, foot);
        float X(float column) => (column - origin.X) / 32f;
        float Z(float row) => (foot - row) / 32f;
        var y = -Half - PaintGap;
        Add(quads, new(X(a.X0), y, Z(low)), new(X(a.X1), y, Z(low)), new(X(a.X1), y, Z(a.Y0)), new(X(a.X0), y, Z(a.Y0)),
            new(a.X0, low), new(a.X1, low), new(a.X1, a.Y0), new(a.X0, a.Y0), new(0, -1, 0), 1f, Direction.Invalid);
        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// Flat art of a wall face stood up along its tile's north edge, seen from both sides. It never turns to the view.
    /// </summary>
    public static FoldMesh BuildPanel(SpriteAnalysis a, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        var foot = origin.Y + 16f;
        float X(float column) => (column - origin.X) / 32f;
        float Z(float row) => (foot - row) / 32f;
        Add(quads, new(X(a.X0), Half, Z(a.Y1)), new(X(a.X1), Half, Z(a.Y1)), new(X(a.X1), Half, Z(a.Y0)),
            new(X(a.X0), Half, Z(a.Y0)), new(a.X0, a.Y1), new(a.X1, a.Y1), new(a.X1, a.Y0), new(a.X0, a.Y0),
            new(0, -1, 0), 1f, Direction.Invalid);
        Add(quads, new(X(a.X1), Half, Z(a.Y1)), new(X(a.X0), Half, Z(a.Y1)), new(X(a.X0), Half, Z(a.Y0)),
            new(X(a.X1), Half, Z(a.Y0)), new(a.X1, a.Y1), new(a.X0, a.Y1), new(a.X0, a.Y0), new(a.X1, a.Y0),
            new(0, 1, 0), 1f, Direction.Invalid);
        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>Flat art of a roof laid over its tile at <paramref name="height"/>, seen from below as a ceiling.</summary>
    public static FoldMesh BuildCeiling(SpriteAnalysis a, Vector2 origin, float height)
    {
        var quads = new List<FoldQuad>();
        float X(float column) => (column - origin.X) / 32f;
        float Y(float row) => (origin.Y - row) / 32f;
        Add(quads, new(X(a.X0), Y(a.Y1), height), new(X(a.X0), Y(a.Y0), height), new(X(a.X1), Y(a.Y0), height),
            new(X(a.X1), Y(a.Y1), height), new(a.X0, a.Y1), new(a.X0, a.Y0), new(a.X1, a.Y0), new(a.X1, a.Y1),
            new(0, 0, -1), CeilingShade, Direction.Invalid);
        return new FoldMesh(quads.ToArray(), true);
    }

    private const float CeilingShade = 0.8f;

    /// <summary>How far painted art stands off its wall's face, in tiles, so the wall never covers it.</summary>
    private const float PaintGap = 0.01f;

    /// <summary>Width in pixels of the edge columns a tall window's ends take from its front.</summary>
    private const float SideBand = 4f;

    /// <summary>
    /// A block on part of its tile, like a diagonal window: the outline is its collision polygon, the sprite lies over
    /// its top, and each side folds the top's border along that edge down the side.
    /// </summary>
    /// <param name="corners">The polygon's corners in tiles, round the tile centre.</param>
    public static FoldMesh BuildPrism(Vector2 origin, float height, Vector2[] corners)
    {
        var quads = new List<FoldQuad>();
        Vector2 Tex(Vector2 p) => new(origin.X + p.X * 32f, origin.Y - p.Y * 32f);

        var centre = Vector2.Zero;
        foreach (var p in corners)
        {
            centre += p;
        }

        centre /= corners.Length;
        if (DiagonalStyle != 0)
            return BuildDiagonal(origin, height, corners, centre);

        // The top as a fan of triangles from the first corner, each a quad with its last corner doubled.
        for (var i = 1; i + 1 < corners.Length; i++)
        {
            Vector2 a = corners[0], b = corners[i], c = corners[i + 1];
            Add(quads, new(a, height), new(b, height), new(c, height), new(c, height),
                Tex(a), Tex(b), Tex(c), Tex(c), new(0, 0, 1), 1f, Direction.Invalid);
        }

        var band = MathF.Min(Band, height * 32f);

        // The longest straight edge, east or west ones first, whose border band also dresses the slanted sides. Wall
        // art draws its top dark, so a north or south band reads as a hole.
        var straight = -1;
        var straightLength = 0f;
        for (var i = 0; i < corners.Length; i++)
        {
            var edge = corners[(i + 1) % corners.Length] - corners[i];
            var length = edge.Length() + (MathF.Abs(edge.X) < 0.01f ? 2f : 0f);
            if ((MathF.Abs(edge.X) < 0.01f || MathF.Abs(edge.Y) < 0.01f) && length > straightLength)
            {
                straight = i;
                straightLength = length;
            }
        }

        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var b = corners[(i + 1) % corners.Length];
            var edge = b - a;
            var normal = Outward(a, b);

            // The foot of the face reads the top's pixels a band in from the edge. A slanted edge's border is
            // stepped, so its face reads the band of a straight edge instead, like the walls beside it.
            var (sa, sb) = (a, b);
            var sourceNormal = normal;
            if (straight >= 0 && MathF.Abs(edge.X) > 0.01f && MathF.Abs(edge.Y) > 0.01f)
            {
                sa = corners[straight];
                sb = corners[(straight + 1) % corners.Length];
                sourceNormal = Outward(sa, sb);
            }

            var inward = new Vector2(-sourceNormal.X, sourceNormal.Y) * band;
            Add(quads, new(a, 0), new(b, 0), new(b, height), new(a, height),
                Tex(sa) + inward, Tex(sb) + inward, Tex(sb), Tex(sa), new(normal, 0), SideShade, EdgeSide(a, b, normal));
        }

        Vector2 Outward(Vector2 a, Vector2 b)
        {
            var edge = b - a;
            var n = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            return Vector2.Dot(n, (a + b) / 2f - centre) < 0f ? -n : n;
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// The outline of a sprite's opaque pixels as a convex polygon, in tiles round <paramref name="origin"/>, y up.
    /// Points on a straight run, like a stepped diagonal edge, are dropped.
    /// </summary>
    public static Vector2[] Outline(SpriteAnalysis analysis, Vector2 origin)
    {
        var points = new List<Vector2>();
        for (var r = analysis.Y0; r < analysis.Y1; r++)
        {
            if (analysis.Count[r] == 0)
                continue;

            int first = -1, last = -1;
            for (var c = analysis.X0; c < analysis.X1; c++)
            {
                if (!analysis.Mask[r * analysis.Width + c])
                    continue;

                if (first < 0)
                    first = c;
                last = c + 1;
            }

            points.Add(new Vector2(first, r));
            points.Add(new Vector2(first, r + 1));
            points.Add(new Vector2(last, r));
            points.Add(new Vector2(last, r + 1));
        }

        points.Sort((a, b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var hull = new List<Vector2>();
        for (var pass = 0; pass < 2; pass++)
        {
            var start = hull.Count;
            for (var i = 0; i < points.Count; i++)
            {
                var p = points[pass == 0 ? i : points.Count - 1 - i];
                while (hull.Count >= start + 2 && Cross(hull[^2], hull[^1], p) <= 0.5f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(p);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        var corners = new Vector2[hull.Count];
        for (var i = 0; i < hull.Count; i++)
        {
            corners[i] = new Vector2((hull[i].X - origin.X) / 32f, (origin.Y - hull[i].Y) / 32f);
        }

        return corners;

        static float Cross(Vector2 o, Vector2 a, Vector2 b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }
    }

    /// <summary>
    /// The tile side a prism's edge lies on when it spans that whole side, so it hides against a block there.
    /// Invalid otherwise.
    /// </summary>
    public static Direction EdgeSide(Vector2 a, Vector2 b, Vector2 normal)
    {
        if ((b - a).Length() < 0.99f)
            return Direction.Invalid;

        if (MathF.Abs(a.X - b.X) < 0.01f && MathF.Abs(MathF.Abs(a.X) - Half) < 0.01f)
            return normal.X > 0f ? Direction.East : Direction.West;

        if (MathF.Abs(a.Y - b.Y) < 0.01f && MathF.Abs(MathF.Abs(a.Y) - Half) < 0.01f)
            return normal.Y > 0f ? Direction.North : Direction.South;

        return Direction.Invalid;
    }

    /// <summary>Debug: which diagonal window look to build. 0 top laid flat, 1 wedge with the sprite on its faces, 2 thin pane.</summary>
    public static int DiagonalStyle;

    /// <summary>Thickness of a diagonal pane, in tiles.</summary>
    private const float PaneDepth = 2f / 32f;

    /// <summary>
    /// A diagonal block whose sprite is drawn as a front, like a wall's: a wedge with the whole sprite on each
    /// standing face and a plain top, or a thin pane along the slanted edge with the sprite on both faces.
    /// </summary>
    private static FoldMesh BuildDiagonal(Vector2 origin, float height, Vector2[] corners, Vector2 centre)
    {
        var quads = new List<FoldQuad>();
        float l = origin.X - 16f, r = origin.X + 16f, t = origin.Y - 16f, b = origin.Y + 16f;

        // The whole sprite on a standing face from a to c, the right way round as seen from outside.
        void AddFace(Vector2 a, Vector2 c, Vector2 normal)
        {
            var right = new Vector2(-normal.Y, normal.X);
            if (Vector2.Dot(c - a, right) < 0f)
                (a, c) = (c, a);

            Add(quads, new(a, 0), new(c, 0), new(c, height), new(a, height),
                new(l, b), new(r, b), new(r, t), new(l, t), new(normal, 0), SideShade, Direction.Invalid);
        }

        Vector2 Outward(Vector2 a, Vector2 c)
        {
            var edge = c - a;
            var n = Vector2.Normalize(new Vector2(edge.Y, -edge.X));
            return Vector2.Dot(n, (a + c) / 2f - centre) < 0f ? -n : n;
        }

        var plain = new Vector2(origin.X, origin.Y + 12f);
        if (DiagonalStyle == 1)
        {
            for (var i = 1; i + 1 < corners.Length; i++)
            {
                Vector2 a = corners[0], m = corners[i], c = corners[i + 1];
                Add(quads, new(a, height), new(m, height), new(c, height), new(c, height),
                    plain, plain, plain, plain, new(0, 0, 1), 1f, Direction.Invalid);
            }

            for (var i = 0; i < corners.Length; i++)
            {
                var a = corners[i];
                var c = corners[(i + 1) % corners.Length];
                AddFace(a, c, Outward(a, c));
            }

            return new FoldMesh(quads.ToArray(), true);
        }

        // The pane stands along the edge that runs across the tile at a slant.
        for (var i = 0; i < corners.Length; i++)
        {
            var a = corners[i];
            var c = corners[(i + 1) % corners.Length];
            if (MathF.Abs(a.X - c.X) < 0.01f || MathF.Abs(a.Y - c.Y) < 0.01f)
                continue;

            var n = Outward(a, c);
            var inset = n * (PaneDepth / 2f);
            AddFace(a + inset, c + inset, n);
            AddFace(a - inset, c - inset, -n);
            Add(quads, new(a + inset, height), new(c + inset, height), new(c - inset, height), new(a - inset, height),
                plain, plain, plain, plain, new(0, 0, 1), 1f, Direction.Invalid);
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// A door. The sprite is its south and north faces at full height, and the top and the ends fold the sprite's top
    /// rows and side columns in from each face, meeting halfway through the door. A door set between walls is a tile
    /// deep. A free-standing one, like a curtain, hangs as a thin sheet through the middle of its tile.
    /// </summary>
    /// <param name="top">First art row, for a door drawn taller than its tile. The tile's top edge by default.</param>
    /// <param name="bottom">Row under the last art row. The tile's bottom edge by default.</param>
    public static FoldMesh BuildDoor(Vector2 origin, float height, float depth = 1f, float? top = null,
        float? bottom = null)
    {
        var quads = new List<FoldQuad>();
        var l = origin.X - 16f;
        var r = origin.X + 16f;
        var t = top ?? origin.Y - 16f;
        var b = bottom ?? origin.Y + 16f;
        var fold = Band / 2f * depth;
        var d = depth / 2f;

        Add(quads, new(-Half, -d, 0), new(Half, -d, 0), new(Half, -d, height), new(-Half, -d, height),
            new(l, b), new(r, b), new(r, t), new(l, t), new(0, -1, 0), SideShade, Direction.South);
        Add(quads, new(Half, d, 0), new(-Half, d, 0), new(-Half, d, height), new(Half, d, height),
            new(l, b), new(r, b), new(r, t), new(l, t), new(0, 1, 0), SideShade, Direction.North);

        Add(quads, new(Half, -d, 0), new(Half, 0, 0), new(Half, 0, height), new(Half, -d, height),
            new(r, b), new(r - fold, b), new(r - fold, t), new(r, t), new(1, 0, 0), SideShade, Direction.East);
        Add(quads, new(Half, 0, 0), new(Half, d, 0), new(Half, d, height), new(Half, 0, height),
            new(l + fold, b), new(l, b), new(l, t), new(l + fold, t), new(1, 0, 0), SideShade, Direction.East);
        Add(quads, new(-Half, d, 0), new(-Half, 0, 0), new(-Half, 0, height), new(-Half, d, height),
            new(r, b), new(r - fold, b), new(r - fold, t), new(r, t), new(-1, 0, 0), SideShade, Direction.West);
        Add(quads, new(-Half, 0, 0), new(-Half, -d, 0), new(-Half, -d, height), new(-Half, 0, height),
            new(l + fold, b), new(l, b), new(l, t), new(l + fold, t), new(-1, 0, 0), SideShade, Direction.West);

        Add(quads, new(-Half, -d, height), new(Half, -d, height), new(Half, 0, height), new(-Half, 0, height),
            new(l, t), new(r, t), new(r, t + fold), new(l, t + fold), new(0, 0, 1), 1f, Direction.Invalid);
        Add(quads, new(-Half, 0, height), new(Half, 0, height), new(Half, d, height), new(-Half, d, height),
            new(l, t + fold), new(r, t + fold), new(r, t), new(l, t), new(0, 0, 1), 1f, Direction.Invalid);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>A table before its image has been read: the whole tile is its top, with no drawn front.</summary>
    public static FoldMesh BuildTable(Vector2 origin, float height)
    {
        var b = origin.Y + 16f;
        return BuildTable(height, origin.X - 16f, origin.X + 16f, origin.Y - 16f, b, b, b);
    }

    /// <summary>
    /// A table read from its sprite. The rows down to the first narrow row are its top and its edge, the darker rows
    /// under the top. The top lies on a slab over the tile, and the edge runs round the slab's sides. Every open side
    /// gets a rail under the slab and the corners get legs, coloured from the drawn front's rail and legs. A leg stands
    /// only where no table joins on either side of its corner. A table with no legs drawn under its edge, like a
    /// counter, is a solid block with the edge on its sides from the floor up.
    /// </summary>
    public static FoldMesh BuildTable(SpriteAnalysis a, Vector2 origin, float height)
    {
        if (a.Empty)
            return BuildTable(origin, height);

        var width = a.X1 - a.X0;
        var front = a.Y1;
        for (var row = a.Y0 + TopMin; row < a.Y1; row++)
        {
            if (a.Count[row] >= width * TopFill)
                continue;

            front = row;
            break;
        }

        // A piece joined on one side draws its legs and rail elsewhere, so the colour is the darkest pixel of the whole
        // drawn front, or of the whole sprite when it draws no front.
        var leg = (front < a.Y1 ? Darkest(a, a.X0, a.X1, front, a.Y1) : null) ?? Darkest(a, a.X0, a.X1, a.Y0, a.Y1);
        var edge = Edge(a, front) ?? front;

        // The edge's rows are rounded off at their ends. The strip takes the columns they all fill.
        float el = a.X0, er = a.X1;
        for (var row = edge; row < front; row++)
        {
            var (c0, c1) = SilhouetteFold.Span(a, row);
            el = MathF.Max(el, c0);
            er = MathF.Min(er, c1);
        }

        // A top with its corners rounded off gets a solid cap under it.
        var rounded = !a.Opaque(a.X0, a.Y0) || !a.Opaque(a.X1 - 1, a.Y0) || !a.Opaque(a.X0, edge - 1)
                      || !a.Opaque(a.X1 - 1, edge - 1);
        var cap = rounded ? leg : null;
        return er - el < 1f
            ? BuildTable(height, a.X0, a.X1, a.Y0, front, front, a.Y1, leg, cap: cap)
            : BuildTable(height, a.X0, a.X1, a.Y0, edge, front, a.Y1, leg, el, er, cap);
    }

    /// <summary>Centre of the darkest opaque pixel in the given box, or null when it is clear.</summary>
    private static Vector2? Darkest(SpriteAnalysis a, int c0, int c1, int r0, int r1)
    {
        Vector2? best = null;
        var lum = float.MaxValue;
        for (var r = r0; r < r1; r++)
        {
            for (var c = c0; c < c1; c++)
            {
                if (!a.Opaque(c, r) || a.Lum[r * a.Width + c] >= lum)
                    continue;

                lum = a.Lum[r * a.Width + c];
                best = new Vector2(c + 0.5f, r + 0.5f);
            }
        }

        return best;
    }

    /// <summary>Highest brightness of an edge row, as a share of the top's.</summary>
    private const float EdgeShade = 0.6f;

    /// <summary>
    /// The first row of a table's edge: from the middle of the top down, the first row at most
    /// <see cref="EdgeShade"/> as bright as the rows above the middle. Null when the top runs on to the front.
    /// </summary>
    private static int? Edge(SpriteAnalysis a, int front)
    {
        float Row(int r)
        {
            float s = 0f;
            var n = 0;
            for (var c = a.X0; c < a.X1; c++)
            {
                if (!a.Opaque(c, r))
                    continue;

                s += a.Lum[r * a.Width + c];
                n++;
            }

            return n == 0 ? 0f : s / n;
        }

        var mid = (a.Y0 + front) / 2;
        float top = 0f;
        for (var r = a.Y0 + 1; r < mid; r++)
        {
            top += Row(r);
        }

        top /= Math.Max(1, mid - a.Y0 - 1);
        for (var r = mid; r < front; r++)
        {
            if (Row(r) <= top * EdgeShade)
                return r;
        }

        return null;
    }

    /// <param name="l">First column of the top.</param>
    /// <param name="r">Column after the top's last.</param>
    /// <param name="t">First row of the top.</param>
    /// <param name="edge">Row after the top's last, where its edge starts. The front row when it has none.</param>
    /// <param name="front">Row after the edge's last, where the drawn front starts.</param>
    /// <param name="b">Row after the drawn front's last.</param>
    /// <param name="leg">The pixel every face of a leg shows, from the drawn legs. Null when none are drawn.</param>
    /// <param name="el">First column the edge's rows all fill.</param>
    /// <param name="er">Column after the last the edge's rows all fill.</param>
    /// <param name="cap">The pixel a solid cap under the top shows, when the top's corners are rounded off.</param>
    private static FoldMesh BuildTable(float height, float l, float r, float t, float edge, float front, float b,
        Vector2? leg = null, float el = 0f, float er = 0f, Vector2? cap = null)
    {
        var quads = new List<FoldQuad>();
        var drawn = front < b - 0.5f;
        var edged = edge < front - 0.5f;
        var solid = edged && !drawn;
        var foot = solid ? 0f : height - (edged ? Math.Clamp((front - edge) / 32f, TableTop, height / 2f) : TableTop);
        var band = TableTop * 32f;

        // Pixels of the top map onto the whole tile.
        float X(float c) => (c - l) / (r - l) - Half;
        float Y(float row) => Half - (row - t) / (edge - t);

        // The drawn front's rail rows and the leg rows below them colour the inferred rails and legs.
        var railTop = drawn ? front + 1f : t;
        var railBottom = drawn ? MathF.Min(front + 1f + RailHeight, b) : t + band;
        var legTop = drawn ? MathF.Min(railBottom, b - 1f) : t;
        var legBottom = drawn ? b : t + band;

        foreach (var corner in solid ? Array.Empty<Direction>() : Corners)
        {
            var east = corner is Direction.NorthEast or Direction.SouthEast;
            var north = corner is Direction.NorthEast or Direction.NorthWest;
            var c0 = east ? r - LegInset - LegWidth : l + LegInset;
            var r0 = north ? t + LegInset : edge - LegInset - LegWidth;
            var start = quads.Count;
            // A drawn leg's outline would stripe every face of the post, so the post takes one solid colour.
            AddPost(quads, X(c0), X(c0 + LegWidth), Y(r0 + LegWidth), Y(r0), 0f, foot,
                leg ?? new Vector2(c0, legBottom), leg ?? new Vector2(c0 + LegWidth, legTop), corner);
            Under(quads, start);
        }

        // Rails run between the legs just under the top, flush with the legs' outer faces.
        if (!solid)
        {
            var railLow = foot - RailHeight / 32f;
            float xw = X(l + LegInset), xe = X(r - LegInset), ys = Y(edge - LegInset), yn = Y(t + LegInset);
            Vector2 rb0 = new(l, railBottom), rb1 = new(r, railBottom), rt1 = new(r, railTop), rt0 = new(l, railTop);
            var railStart = quads.Count;
            Add(quads, new(xe, yn, railLow), new(xw, yn, railLow), new(xw, yn, foot), new(xe, yn, foot),
                rb0, rb1, rt1, rt0, new(0, 1, 0), SideShade, Direction.North);
            Add(quads, new(xe, ys, railLow), new(xe, yn, railLow), new(xe, yn, foot), new(xe, ys, foot),
                rb0, rb1, rt1, rt0, new(1, 0, 0), SideShade, Direction.East);
            Add(quads, new(xw, yn, railLow), new(xw, ys, railLow), new(xw, ys, foot), new(xw, yn, foot),
                rb0, rb1, rt1, rt0, new(-1, 0, 0), SideShade, Direction.West);
            Add(quads, new(xw, ys, railLow), new(xe, ys, railLow), new(xe, ys, foot), new(xw, ys, foot),
                rb0, rb1, rt1, rt0, new(0, -1, 0), SideShade, Direction.South);
            Under(quads, railStart);
        }

        // The slab's sides show the drawn edge all round. Without one they fold the top's border down.
        Vector2 s0 = new(el, front), s1 = new(er, front), s2 = new(er, edge), s3 = new(el, edge);
        if (!edged)
        {
            s0 = new(l, front - band);
            s1 = new(r, front - band);
            s2 = new(r, front);
            s3 = new(l, front);
        }

        Add(quads, new(-Half, -Half, foot), new(Half, -Half, foot), new(Half, -Half, height),
            new(-Half, -Half, height), s0, s1, s2, s3, new(0, -1, 0), SideShade, Direction.South);

        if (edged)
        {
            Add(quads, new(Half, Half, foot), new(-Half, Half, foot), new(-Half, Half, height),
                new(Half, Half, height), s0, s1, s2, s3, new(0, 1, 0), SideShade, Direction.North);
            Add(quads, new(Half, -Half, foot), new(Half, Half, foot), new(Half, Half, height),
                new(Half, -Half, height), s0, s1, s2, s3, new(1, 0, 0), SideShade, Direction.East);
            Add(quads, new(-Half, Half, foot), new(-Half, -Half, foot), new(-Half, -Half, height),
                new(-Half, Half, height), s0, s1, s2, s3, new(-1, 0, 0), SideShade, Direction.West);
        }
        else
        {
            Add(quads, new(Half, Half, foot), new(-Half, Half, foot), new(-Half, Half, height),
                new(Half, Half, height), new(r, t + band), new(l, t + band), new(l, t), new(r, t), new(0, 1, 0),
                SideShade, Direction.North);
            Add(quads, new(Half, -Half, foot), new(Half, Half, foot), new(Half, Half, height),
                new(Half, -Half, height), new(r - band, edge), new(r - band, t), new(r, t), new(r, edge),
                new(1, 0, 0), SideShade, Direction.East);
            Add(quads, new(-Half, Half, foot), new(-Half, -Half, foot), new(-Half, -Half, height),
                new(-Half, Half, height), new(l + band, t), new(l + band, edge), new(l, edge), new(l, t),
                new(-1, 0, 0), SideShade, Direction.West);
        }

        // Where the top's corners are rounded off, the slab under them shows in the legs' colour, not the floor.
        if (cap is { } c)
        {
            var start = quads.Count;
            var zc = height - CapDrop;
            Add(quads, new(-Half, -Half, zc), new(Half, -Half, zc), new(Half, Half, zc), new(-Half, Half, zc),
                c, c, c, c, new(0, 0, 1), SideShade, Direction.Invalid);
            Under(quads, start);
        }

        Add(quads, new(-Half, -Half, height), new(Half, -Half, height), new(Half, Half, height), new(-Half, Half, height),
            new(l, edge), new(r, edge), new(r, t), new(l, t), new(0, 0, 1), 1f, Direction.Invalid);

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>How far under the top the solid cap below it lies, in tiles.</summary>
    private const float CapDrop = 0.5f / 32f;

    /// <summary>Thickness of a table's top, in tiles.</summary>
    private const float TableTop = 3f / 32f;

    /// <summary>Width of a table leg and its inset from the top's edge, in pixels of the top.</summary>
    private const float LegWidth = 3f;
    private const float LegInset = 1f;

    /// <summary>Height of a table's rail, in pixels.</summary>
    private const float RailHeight = 2f;

    /// <summary>Rows a table's top spans at least, and the share of the width its rows must fill.</summary>
    private const int TopMin = 4;
    private const float TopFill = 0.9f;

    private static readonly Direction[] Corners =
        { Direction.NorthEast, Direction.SouthEast, Direction.SouthWest, Direction.NorthWest };

    /// <summary>A square post from z0 to z1, every side showing one patch of the image.</summary>
    private static void AddPost(List<FoldQuad> quads, float x0, float x1, float y0, float y1, float z0, float z1,
        Vector2 texBottom, Vector2 texTop, Direction side)
    {
        Vector2 t0 = texBottom, t1 = new(texTop.X, texBottom.Y), t2 = texTop, t3 = new(texBottom.X, texTop.Y);
        Add(quads, new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1),
            t0, t1, t2, t3, new(0, -1, 0), SideShade, side);
        Add(quads, new(x1, y1, z0), new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1),
            t0, t1, t2, t3, new(0, 1, 0), SideShade, side);
        Add(quads, new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1),
            t0, t1, t2, t3, new(1, 0, 0), SideShade, side);
        Add(quads, new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1),
            t0, t1, t2, t3, new(-1, 0, 0), SideShade, side);
    }

    /// <summary>Sinks the sort centres of parts under a table's top, so they always draw before it.</summary>
    private static void Under(List<FoldQuad> quads, int start)
    {
        for (var i = start; i < quads.Count; i++)
        {
            var q = quads[i];
            q.Centre.Z -= 10f;
            quads[i] = q;
        }
    }

    private static void Add(List<FoldQuad> quads, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3, Vector3 normal, float shade, Direction side)
    {
        quads.Add(new FoldQuad
        {
            P0 = p0, P1 = p1, P2 = p2, P3 = p3,
            T0 = t0, T1 = t1, T2 = t2, T3 = t3,
            Normal = normal,
            Centre = (p0 + p1 + p2 + p3) / 4f,
            Shade = shade,
            Side = side,
        });
    }
}
