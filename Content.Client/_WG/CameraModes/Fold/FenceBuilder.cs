using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds a railing: one thin upright panel along each strip of its collision shape, showing the fence rows of its
/// front image.
/// </summary>
public static class FenceBuilder
{
    private const float PixelsPerTile = 32f;

    /// <summary>Thickness of a fence panel, in tiles.</summary>
    private const float Thickness = 2f / PixelsPerTile;

    /// <summary>Share of the image's width a row must fill to be part of the fence rather than a post above it.</summary>
    private const float RailFill = 0.6f;

    /// <param name="strips">The collision strips, in tiles, in the frame the image faces south in.</param>
    public static FoldMesh Build(SpriteAnalysis a, Box2[] strips)
    {
        // The fence rows run from the first wide row to the bottom of the image.
        var top = a.Y0;
        while (top < a.Y1 && a.Count[top] < (a.X1 - a.X0) * RailFill)
            top++;

        if (top >= a.Y1)
            top = a.Y0;

        var height = (a.Y1 - top) / PixelsPerTile;
        var quads = new List<FoldQuad>();
        foreach (var strip in strips)
        {
            var alongX = strip.Width >= strip.Height;
            var c = strip.Center;
            float x0, x1, y0, y1;
            if (alongX)
            {
                (x0, x1) = (strip.Left, strip.Right);
                (y0, y1) = (c.Y - Thickness / 2f, c.Y + Thickness / 2f);
            }
            else
            {
                (x0, x1) = (c.X - Thickness / 2f, c.X + Thickness / 2f);
                (y0, y1) = (strip.Bottom, strip.Top);
            }

            AddPanel(quads, a, top, height, x0, x1, y0, y1, alongX);
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>A panel whose two long faces show the fence rows stretched along its length.</summary>
    private static void AddPanel(List<FoldQuad> quads, SpriteAnalysis a, int top, float h, float x0, float x1,
        float y0, float y1, bool alongX)
    {
        Vector2 tl = new(a.X0, top), tr = new(a.X1, top), bl = new(a.X0, a.Y1), br = new(a.X1, a.Y1);
        if (alongX)
        {
            SilhouetteFold.Add(quads, new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, h), new(x0, y0, h),
                bl, br, tr, tl, new(0, -1, 0), 1f);
            SilhouetteFold.Add(quads, new(x1, y1, 0), new(x0, y1, 0), new(x0, y1, h), new(x1, y1, h),
                br, bl, tl, tr, new(0, 1, 0), 1f);
            AddEdge(quads, a, top, new(x0, y1, 0), new(x0, y0, 0), new(x0, y0, h), new(x0, y1, h), new(-1, 0, 0), true);
            AddEdge(quads, a, top, new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, h), new(x1, y0, h), new(1, 0, 0), false);
        }
        else
        {
            SilhouetteFold.Add(quads, new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, h), new(x1, y0, h),
                bl, br, tr, tl, new(1, 0, 0), 1f);
            SilhouetteFold.Add(quads, new(x0, y1, 0), new(x0, y0, 0), new(x0, y0, h), new(x0, y1, h),
                br, bl, tl, tr, new(-1, 0, 0), 1f);
            AddEdge(quads, a, top, new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, h), new(x0, y0, h), new(0, -1, 0), true);
            AddEdge(quads, a, top, new(x1, y1, 0), new(x0, y1, 0), new(x0, y1, h), new(x1, y1, h), new(0, 1, 0), false);
        }

        // The top edge in the colour of the top rail.
        var t = new Vector2((a.X0 + a.X1) / 2f, top + 0.5f);
        SilhouetteFold.Add(quads, new(x0, y0, h), new(x1, y0, h), new(x1, y1, h), new(x0, y1, h),
            t, t, t, t, new(0, 0, 1), 1f);
    }

    /// <summary>An end of a panel in the colours of the image's first or last column.</summary>
    private static void AddEdge(List<FoldQuad> quads, SpriteAnalysis a, int top, Vector3 p0, Vector3 p1, Vector3 p2,
        Vector3 p3, Vector3 normal, bool first)
    {
        var c = first ? a.X0 + 0.5f : a.X1 - 0.5f;
        SilhouetteFold.Add(quads, p0, p1, p2, p3, new(c, a.Y1), new(c, a.Y1), new(c, top), new(c, top), normal, 1f);
    }
}
