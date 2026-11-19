using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

public static partial class SilhouetteFold
{
    /// <summary>Shade of the underside of a top that reaches past the front below it.</summary>
    private const float UnderShade = 0.6f;

    /// <summary>How far a wall reaches past its cell so it overlaps the next one, in tiles.</summary>
    private const float Lap = 0.5f / PixelsPerTile;

    /// <summary>
    /// The colours a back is drawn in: the most common shade inside the front rows, and the most common shade on their
    /// outline. A shade is a pixel's brightness and colourfulness. Each is the centre of a pixel that has it.
    /// </summary>
    private static (Vector2 Casing, Vector2 Border) BackColours(SpriteAnalysis a, int l)
    {
        var inside = new Dictionary<int, (int Count, Vector2 Pixel)>();
        var outline = new Dictionary<int, (int Count, Vector2 Pixel)>();
        for (var r = l; r < a.Y1; r++)
        {
            for (var c = a.X0; c < a.X1; c++)
            {
                if (!a.Opaque(c, r))
                    continue;

                var i = r * a.Width + c;
                var key = (int) MathF.Round(a.Lum[i] * 255f) * 1024 + (int) MathF.Round(a.Chroma[i] * 255f);
                var edge = !a.Opaque(c - 1, r) || !a.Opaque(c + 1, r) || !a.Opaque(c, r - 1) || !a.Opaque(c, r + 1);
                var counts = edge ? outline : inside;
                counts[key] = counts.TryGetValue(key, out var seen)
                    ? (seen.Count + 1, seen.Pixel)
                    : (1, new Vector2(c + 0.5f, r + 0.5f));
            }
        }

        static Vector2? Most(Dictionary<int, (int Count, Vector2 Pixel)> counts)
        {
            Vector2? best = null;
            var most = 0;
            foreach (var (count, pixel) in counts.Values)
            {
                if (count <= most)
                    continue;

                most = count;
                best = pixel;
            }

            return best;
        }

        var border = Most(outline) ?? new Vector2(a.X0 + 0.5f, a.Y1 - 0.5f);
        return (Most(inside) ?? border, border);
    }

    /// <summary>
    /// Folds a sprite, or one step of its top, as cells. Each front pixel below the lid line reaches back through
    /// the depth slices whose lid pixel above it is opaque, so a clear part of the top stays clear through the whole
    /// solid. The lid rows are the tops of the cells, the front rows their front. The back is the casing colour, with a
    /// border in the colours of the faces it meets.
    /// </summary>
    /// <param name="bottom">Pixel row of the whole sprite's base, which sits at the lift.</param>
    private static void Carve(List<FoldQuad> quads, SpriteAnalysis a, int lid, int bottom, Vector2 origin, float lift,
        float yf, float yb, (Vector2 Casing, Vector2 Border) back)
    {
        var l = a.Y0 + lid;
        var slices = Math.Max(1, lid);
        if (l >= a.Y1)
            return;

        var d = (yb - yf) / slices;
        float X(float c) => (c - origin.X) / PixelsPerTile;
        float Y(float s) => yf + s * d;
        float Z(float r) => (bottom - r) / PixelsPerTile + lift;
        // A wall's back end reaches past its cell into the next slice, but never out of the back.
        float Behind(int s) => Y(s) + (s < slices ? Lap : 0f);
        var zl = Z(l);

        // The lid pixel over slice s is lid row l - 1 - s. The front slice always stands, so the front keeps its art.
        bool Lid(int c, int s) => lid > 0 && a.Opaque(c, l - 1 - s);
        bool Solid(int c, int s, int r) =>
            s >= 0 && s < slices && r >= l && a.Opaque(c, r) && (s == 0 || lid == 0 || Lid(c, s));

        // The front, one quad over the front rows. Clear pixels stay clear.
        Add(quads, new(X(a.X0), yf, Z(a.Y1)), new(X(a.X1), yf, Z(a.Y1)), new(X(a.X1), yf, zl), new(X(a.X0), yf, zl),
            new(a.X0, a.Y1), new(a.X1, a.Y1), new(a.X1, l), new(a.X0, l), new(0, -1, 0), 1f);

        // The top: the lid art in one sheet with an underside, so no seam runs through it. Clear pixels stay clear.
        if (lid > 0)
        {
            Add(quads, new(X(a.X0), yf, zl), new(X(a.X1), yf, zl), new(X(a.X1), yb, zl), new(X(a.X0), yb, zl),
                new(a.X0, l), new(a.X1, l), new(a.X1, a.Y0), new(a.X0, a.Y0), new(0, 0, 1), 1f);
            Add(quads, new(X(a.X0), yf, zl - 0.002f), new(X(a.X1), yf, zl - 0.002f), new(X(a.X1), yb, zl - 0.002f),
                new(X(a.X0), yb, zl - 0.002f), new(a.X0, l), new(a.X1, l), new(a.X1, a.Y0), new(a.X0, a.Y0),
                new(0, 0, -1), UnderShade);
        }

        // A front column with no lid pixel over its first slice is closed in the colour of its top pixel.
        for (var c = a.X0; c < a.X1; c++)
        {
            if (!Solid(c, 0, l) || Lid(c, 0))
                continue;

            var t = new Vector2(c + 0.5f, l + 0.5f);
            AddFlat(quads, new(X(c) - Lap, Y(0), zl), new(X(c + 1) + Lap, Y(0), zl),
                new(X(c + 1) + Lap, Behind(1), zl), new(X(c) - Lap, Behind(1), zl), t, new(0, 0, 1));
        }

        for (var r = l; r < a.Y1; r++)
        {
            // Each wall reaches half a pixel up and back past its cell, so no gap opens where it meets the next.
            float zt = Z(r), zb = Z(r + 1), zu = MathF.Min(zt + Lap, zl);
            for (var c = a.X0; c < a.X1; c++)
            {
                var t = new Vector2(c + 0.5f, r + 0.5f);
                // Walls on each open side of a cell, merged along the depth, in the colour of the front pixel.
                Spans(slices, s => Solid(c, s, r) && !Solid(c - 1, s, r), (s0, s1) =>
                    AddFlat(quads, new(X(c), Behind(s1), zb), new(X(c), Y(s0), zb), new(X(c), Y(s0), zu),
                        new(X(c), Behind(s1), zu), t, new(-1, 0, 0)));
                Spans(slices, s => Solid(c, s, r) && !Solid(c + 1, s, r), (s0, s1) =>
                    AddFlat(quads, new(X(c + 1), Y(s0), zb), new(X(c + 1), Behind(s1), zb),
                        new(X(c + 1), Behind(s1), zu), new(X(c + 1), Y(s0), zu), t, new(1, 0, 0)));
                if (r > l)
                {
                    Spans(slices, s => Solid(c, s, r) && !Solid(c, s, r - 1), (s0, s1) =>
                        AddFlat(quads, new(X(c) - Lap, Y(s0), zt), new(X(c + 1) + Lap, Y(s0), zt),
                            new(X(c + 1) + Lap, Behind(s1), zt), new(X(c) - Lap, Behind(s1), zt), t, new(0, 0, 1)));
                }

                Spans(slices, s => Solid(c, s, r) && !Solid(c, s, r + 1), (s0, s1) =>
                    AddFlat(quads, new(X(c) - Lap, Y(s0), zb), new(X(c + 1) + Lap, Y(s0), zb),
                        new(X(c + 1) + Lap, Behind(s1), zb), new(X(c) - Lap, Behind(s1), zb), t, new(0, 0, -1)));

                // A face looking forward where the slice in front is empty, in that row of the front art.
                for (var s = 1; s < slices; s++)
                {
                    if (!Solid(c, s, r) || Solid(c, s - 1, r))
                        continue;

                    Add(quads, new(X(c), Y(s), zb), new(X(c + 1), Y(s), zb), new(X(c + 1), Y(s), zt),
                        new(X(c), Y(s), zt), new(c, r + 1), new(c + 1, r + 1), new(c + 1, r), new(c, r),
                        new(0, -1, 0), 1f);
                }
            }
        }

        // The back of each cell with nothing behind it: the casing, bordered where the next cell's back is not level
        // with it. A border cell takes the colour of the face it meets: the top's pixel above it, or else the front
        // pixel of its row, which its side wall shows.
        bool Back(int c, int s, int r) => Solid(c, s, r) && !Solid(c, s + 1, r);
        for (var s = 0; s < slices; s++)
        {
            var slice = s;
            Rects(a.X0, a.X1, l, a.Y1, (c, r) =>
            {
                if (!Back(c, slice, r))
                    return 0;

                if (Back(c - 1, slice, r) && Back(c + 1, slice, r) && Back(c, slice, r - 1) && Back(c, slice, r + 1))
                    return 1;

                var top = r == l && Lid(c, slice) ? l - 1 - slice : r;
                return 2 + top * a.Width + c;
            }, (c0, c1, r0, r1, key) =>
            {
                var t = key == 1
                    ? back.Casing
                    : new Vector2((key - 2) % a.Width + 0.5f, (key - 2) / a.Width + 0.5f);
                var y = Y(slice + 1);
                var zt = MathF.Min(Z(r0) + Lap, zl);
                Add(quads, new(X(c1) + Lap, y, Z(r1)), new(X(c0) - Lap, y, Z(r1)), new(X(c0) - Lap, y, zt),
                    new(X(c1) + Lap, y, zt), t, t, t, t, new(0, 1, 0), BackShade);
            });
        }
    }

    /// <summary>Calls back with each run of indices below the count where the test holds.</summary>
    private static void Spans(int count, Func<int, bool> test, Action<int, int> span)
    {
        for (var i = 0; i < count;)
        {
            if (!test(i))
            {
                i++;
                continue;
            }

            var end = i + 1;
            while (end < count && test(end))
                end++;

            span(i, end);
            i = end;
        }
    }

    /// <summary>
    /// Calls back with rectangles of cells that share a key other than 0: runs along the first axis, each grown along
    /// the second while the same run with the same key repeats.
    /// </summary>
    private static void Rects(int u0, int u1, int v0, int v1, Func<int, int, int> key,
        Action<int, int, int, int, int> rect)
    {
        var done = new bool[(u1 - u0) * (v1 - v0)];
        for (var v = v0; v < v1; v++)
        {
            for (var u = u0; u < u1;)
            {
                var k = key(u, v);
                if (k == 0 || done[(v - v0) * (u1 - u0) + u - u0])
                {
                    u++;
                    continue;
                }

                var end = u + 1;
                while (end < u1 && key(end, v) == k && !done[(v - v0) * (u1 - u0) + end - u0])
                    end++;

                // Grow down while the next row has the same run, bounded by cells of another key on both ends.
                var bottom = v + 1;
                while (bottom < v1 && Same(bottom))
                    bottom++;

                for (var vv = v; vv < bottom; vv++)
                {
                    for (var uu = u; uu < end; uu++)
                    {
                        done[(vv - v0) * (u1 - u0) + uu - u0] = true;
                    }
                }

                rect(u, end, v, bottom, k);
                u = end;

                bool Same(int row)
                {
                    for (var uu = u; uu < end; uu++)
                    {
                        if (key(uu, row) != k || done[(row - v0) * (u1 - u0) + uu - u0])
                            return false;
                    }

                    return (u == u0 || key(u - 1, row) != k) && (end == u1 || key(end, row) != k);
                }
            }
        }
    }
}
