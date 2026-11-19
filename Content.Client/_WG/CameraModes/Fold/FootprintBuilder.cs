using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds a thick cut-out over an entity's collision area: the whole picture on its front and back, and walls across
/// the depth following its outline. For large props whose outline fits no other shape, like a pile of cars.
/// </summary>
public static class FootprintBuilder
{
    private const float PixelsPerTile = 32f;

    /// <summary>How far inside a run of edge pixels its wall samples, so it never picks up the pixel past the end.</summary>
    private const float In = 0.25f;

    private const float TopShade = 0.9f;
    private const float SideShade = 0.75f;
    private const float UnderShade = 0.6f;

    /// <param name="footprint">The collision box in tiles, in the frame the image faces south in.</param>
    public static FoldMesh BuildCutout(SpriteAnalysis a, Box2 footprint)
    {
        var quads = new List<FoldQuad>();
        var mid = (a.X0 + a.X1) / 2f;
        float y0 = footprint.Bottom, y1 = footprint.Top;
        float X(float c) => footprint.Center.X + (c - mid) / PixelsPerTile;
        float Z(float r) => (a.Y1 - r) / PixelsPerTile;

        // Across the depth, a top over each run of pixels with clear above and an underside under each with clear below.
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var row = r;
            ExtrudeBuilder.Runs(a, row, -1, (c0, c1) => SilhouetteFold.Add(quads,
                new(X(c0), y0, Z(row)), new(X(c1), y0, Z(row)), new(X(c1), y1, Z(row)), new(X(c0), y1, Z(row)),
                new(c0 + In, row + 0.5f), new(c1 - In, row + 0.5f), new(c1 - In, row + 0.5f), new(c0 + In, row + 0.5f),
                new(0, 0, 1), TopShade));
            ExtrudeBuilder.Runs(a, row, 1, (c0, c1) => SilhouetteFold.Add(quads,
                new(X(c0), y1, Z(row + 1)), new(X(c1), y1, Z(row + 1)), new(X(c1), y0, Z(row + 1)),
                new(X(c0), y0, Z(row + 1)),
                new(c0 + In, row + 0.5f), new(c1 - In, row + 0.5f), new(c1 - In, row + 0.5f), new(c0 + In, row + 0.5f),
                new(0, 0, -1), UnderShade));
        }

        // A wall across the depth beside each run of pixels with clear to the left or right.
        for (var c = a.X0; c < a.X1; c++)
        {
            var col = c;
            ExtrudeBuilder.Columns(a, col, -1, (r0, r1) => SilhouetteFold.Add(quads,
                new(X(col), y1, Z(r1)), new(X(col), y0, Z(r1)), new(X(col), y0, Z(r0)), new(X(col), y1, Z(r0)),
                new(col + 0.5f, r1 - In), new(col + 0.5f, r1 - In), new(col + 0.5f, r0 + In), new(col + 0.5f, r0 + In),
                new(-1, 0, 0), SideShade));
            ExtrudeBuilder.Columns(a, col, 1, (r0, r1) => SilhouetteFold.Add(quads,
                new(X(col + 1), y0, Z(r1)), new(X(col + 1), y1, Z(r1)), new(X(col + 1), y1, Z(r0)),
                new(X(col + 1), y0, Z(r0)),
                new(col + 0.5f, r1 - In), new(col + 0.5f, r1 - In), new(col + 0.5f, r0 + In), new(col + 0.5f, r0 + In),
                new(1, 0, 0), SideShade));
        }

        // The front and back last, so they cover the walls where quads share a depth. Both read the image the same
        // way across, so their outlines meet the walls.
        Vector2 bl = new(a.X0, a.Y1), br = new(a.X1, a.Y1), tr = new(a.X1, a.Y0), tl = new(a.X0, a.Y0);
        SilhouetteFold.Add(quads, new(X(a.X0), y0, 0), new(X(a.X1), y0, 0), new(X(a.X1), y0, Z(a.Y0)),
            new(X(a.X0), y0, Z(a.Y0)), bl, br, tr, tl, new(0, -1, 0), 1f);
        SilhouetteFold.Add(quads, new(X(a.X0), y1, 0), new(X(a.X1), y1, 0), new(X(a.X1), y1, Z(a.Y0)),
            new(X(a.X0), y1, Z(a.Y0)), bl, br, tr, tl, new(0, 1, 0), 1f);
        return new FoldMesh(quads.ToArray(), true);
    }
}
