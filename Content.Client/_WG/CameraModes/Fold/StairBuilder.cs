using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Builds a flight of stairs that climbs its tile from the south edge to the next level at the north edge. The sprite's
/// rows from bottom to top are the treads from the lowest step to the highest.
/// </summary>
public static class StairBuilder
{
    private const float Half = 0.5f;
    private const int Steps = 8;
    private const float RiserShade = 0.7f;
    private const float SideShade = 0.8f;
    private const float BackShade = 0.6f;

    /// <summary>The stairs, facing south, climbing <paramref name="height"/> tiles over their tile.</summary>
    public static FoldMesh Build(SpriteAnalysis a, Vector2 origin, float height)
    {
        var quads = new List<FoldQuad>();
        float l = a.X0;
        float r = a.X1;
        var band = (a.Y1 - a.Y0) / (float) Steps;
        for (var i = 0; i < Steps; i++)
        {
            var low = a.Y1 - i * band;
            var high = low - band;
            var y0 = -Half + i / (float) Steps;
            var y1 = y0 + 1f / Steps;
            var z0 = height * i / Steps;
            var z1 = height * (i + 1) / Steps;

            SilhouetteFold.Add(quads, new(-Half, y0, z1), new(Half, y0, z1), new(Half, y1, z1), new(-Half, y1, z1),
                new(l, low), new(r, low), new(r, high), new(l, high), new(0, 0, 1), 1f);
            SilhouetteFold.Add(quads, new(-Half, y0, z0), new(Half, y0, z0), new(Half, y0, z1), new(-Half, y0, z1),
                new(l, low), new(r, low), new(r, high), new(l, high), new(0, -1, 0), RiserShade);
            SilhouetteFold.Add(quads, new(Half, y0, 0), new(Half, y1, 0), new(Half, y1, z1), new(Half, y0, z1),
                new(r - 1, low), new(r - 1, high), new(r, high), new(r, low), new(1, 0, 0), SideShade);
            SilhouetteFold.Add(quads, new(-Half, y1, 0), new(-Half, y0, 0), new(-Half, y0, z1), new(-Half, y1, z1),
                new(l, high), new(l, low), new(l + 1, low), new(l + 1, high), new(-1, 0, 0), SideShade);
        }

        var top = a.Y0 + band;
        SilhouetteFold.Add(quads, new(Half, Half, 0), new(-Half, Half, 0), new(-Half, Half, height), new(Half, Half, height),
            new(r, top), new(l, top), new(l, a.Y0), new(r, a.Y0), new(0, 1, 0), BackShade);

        return new FoldMesh(quads.ToArray(), true);
    }
}
