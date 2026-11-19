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
    /// <param name="joined">
    /// The image is one tile of a fence that runs on into the next. Each panel shows the part of it across the tile
    /// where it stands, so the pattern carries on from panel to panel.
    /// </param>
    public static FoldMesh Build(SpriteAnalysis a, Box2[] strips, bool joined = false)
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

            var (from, to) = alongX ? (x0, x1) : (y0, y1);
            var (u0, u1) = joined ? (Column(a, from), Column(a, to)) : (a.X0, a.X1);
            AddPanel(quads, a, top, height, x0, x1, y0, y1, alongX, u0, u1);
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>The image column at a place across its tile, from -0.5 to 0.5 tiles.</summary>
    private static float Column(SpriteAnalysis a, float at)
    {
        return a.X0 + (at + 0.5f) * (a.X1 - a.X0);
    }

    /// <summary>A panel whose two long faces show the fence rows from column u0 to u1 stretched along its length.</summary>
    private static void AddPanel(List<FoldQuad> quads, SpriteAnalysis a, int top, float h, float x0, float x1,
        float y0, float y1, bool alongX, float u0, float u1)
    {
        Vector2 tl = new(u0, top), tr = new(u1, top), bl = new(u0, a.Y1), br = new(u1, a.Y1);
        if (alongX)
        {
            SilhouetteFold.Add(quads, new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, h), new(x0, y0, h),
                bl, br, tr, tl, new(0, -1, 0), 1f);
            SilhouetteFold.Add(quads, new(x1, y1, 0), new(x0, y1, 0), new(x0, y1, h), new(x1, y1, h),
                br, bl, tl, tr, new(0, 1, 0), 1f);
            AddEdge(quads, a, top, new(x0, y1, 0), new(x0, y0, 0), new(x0, y0, h), new(x0, y1, h), new(-1, 0, 0), u0 + 0.5f);
            AddEdge(quads, a, top, new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, h), new(x1, y0, h), new(1, 0, 0), u1 - 0.5f);
        }
        else
        {
            SilhouetteFold.Add(quads, new(x1, y0, 0), new(x1, y1, 0), new(x1, y1, h), new(x1, y0, h),
                bl, br, tr, tl, new(1, 0, 0), 1f);
            SilhouetteFold.Add(quads, new(x0, y1, 0), new(x0, y0, 0), new(x0, y0, h), new(x0, y1, h),
                br, bl, tl, tr, new(-1, 0, 0), 1f);
            AddEdge(quads, a, top, new(x0, y0, 0), new(x1, y0, 0), new(x1, y0, h), new(x0, y0, h), new(0, -1, 0), u0 + 0.5f);
            AddEdge(quads, a, top, new(x1, y1, 0), new(x0, y1, 0), new(x0, y1, h), new(x1, y1, h), new(0, 1, 0), u1 - 0.5f);
        }

        // The top edge in the colour of the top rail.
        var t = new Vector2((u0 + u1) / 2f, top + 0.5f);
        SilhouetteFold.Add(quads, new(x0, y0, h), new(x1, y0, h), new(x1, y1, h), new(x0, y1, h),
            t, t, t, t, new(0, 0, 1), 1f);
    }

    /// <summary>An end of a panel in the colours of one column of the image.</summary>
    private static void AddEdge(List<FoldQuad> quads, SpriteAnalysis a, int top, Vector3 p0, Vector3 p1, Vector3 p2,
        Vector3 p3, Vector3 normal, float c)
    {
        SilhouetteFold.Add(quads, p0, p1, p2, p3, new(c, a.Y1), new(c, a.Y1), new(c, top), new(c, top), normal, 1f);
    }
}
