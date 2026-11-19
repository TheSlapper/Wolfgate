using System.Numerics;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>
/// Turns any sprite into a solid from its pixels alone. <see cref="Build"/> reads the outline and shading to pick a
/// shape: a card facing the view, a low box for top-down art, a round or domed solid, a half-round shell for a small
/// fitting, a seat, a console, a wall panel, trays and steps, or the default fold. The
/// default fold finds the lid line, the first sharp full-width edge below the top rim. The part below it is extruded
/// back along its own outline, and the lid rows lie flat on top.
/// </summary>
public static partial class SilhouetteFold
{
    /// <summary>Debug: which shape the last <see cref="Build"/> made, for the explain command.</summary>
    public static string Path = "";

    /// <summary>Brightness of the inferred back, which shows the casing colours of the side walls.</summary>
    private const float BackShade = 0.7f;

    /// <summary>Width of the frame round the inferred back, in pixels.</summary>
    private const float BackFrame = 2f;

    /// <summary>Rows a new look must reach above a kept solid before they stand as a panel.</summary>
    private const int GrowMin = 2;

    /// <summary>Thickness of a grown panel, in tiles.</summary>
    private const float GrowDepth = 1f / PixelsPerTile;

    private const float LidStep = 0.12f;
    private const float LidShare = 0.5f;
    private const float LidWidth = 0.7f;
    private const int LidMin = 4;

    /// <summary>Share of the sprite's width the first body row under a cap spans, and the foot rows left out below.</summary>
    private const float ShoulderFill = 0.95f;
    private const int ShoulderFeet = 3;

    /// <summary>Share of the sprite's width a bottom row stays under to count as feet.</summary>
    private const float FeetShare = 0.5f;

    /// <summary>Share of the sprite's width the top row of a dome stays under.</summary>
    private const float DomeTop = 0.6f;

    /// <summary>Rows the top must jump by between column runs to count as a step, and the narrowest run that stands.</summary>
    private const int StepRows = 4;
    private const int StepWidth = 4;

    /// <summary>Most pixels a dome widens by from one row to the next, past its first rows.</summary>
    private const int DomeStep = 4;
    private const int DomeRim = 2;

    /// <summary>Share of opaque pixels a turned image must share with a quarter turn of the south one.</summary>
    private const float TurnMatch = 0.85f;

    /// <summary>Fewest rows a console's cap spans.</summary>
    private const int CapMin = 2;
    private const int LidBand = 3;
    private const float PixelsPerTile = 32f;

    /// <summary>Height of the box a sprite drawn from above stands as.</summary>
    private const float TopDownHeight = 0.3f;

    /// <summary>Share of its tile a sprite must cover to count as drawn from above.</summary>
    private const float TopDownFill = 0.9f;

    /// <summary>Height of the wall blocks a wall panel is centred on.</summary>
    private const float WallHeight = 1f;

    /// <summary>Thickness of a panel on a wall face.</summary>
    private const float WallPanelDepth = 2f / PixelsPerTile;

    /// <summary>A sprite whose opaque bottom sits this far above its frame bottom hangs on a wall.</summary>
    private const int MountGap = 12;

    /// <summary>How far a console's face leans back, as a share of its height.</summary>
    private const float ConsoleLean = 0.55f;

    /// <summary>Rows a light band must span to count as a console's shelf, and how much lighter it must be.</summary>
    private const int ShelfRows = 3;
    private const float ShelfLift = 0.15f;

    /// <summary>Pixels more colourful than this are lit screens and lights, never a shelf.</summary>
    private const float ShelfChroma = 0.3f;

    /// <summary>Thinnest a console gets, in tiles.</summary>
    private const float ConsoleMinDepth = 0.35f;

    /// <summary>Depth of a full-width console from its foot to its back, in tiles.</summary>
    private const float ConsoleDepth = 0.9f;

    /// <summary>Narrowest sprite read as a round footprint, in pixels.</summary>
    private const int RoundMinWidth = 8;

    /// <summary>Share of the sprite's height a column must fill to be part of a round body.</summary>
    private const float RoundBody = 0.4f;

    /// <summary>Fewest rows the bottom outline of a round footprint rises at each end, and that as a share of its width.</summary>
    private const int RoundEdge = 2;
    private const float RoundEdgeShare = 0.08f;

    /// <summary>Deepest arc the bottom outline of a round footprint draws, as a share of its width.</summary>
    private const float RoundSag = 0.35f;

    /// <summary>Rows the bottom outline may stray from the arc of a round footprint.</summary>
    private const float RoundSlack = 1f;

    /// <summary>Widest a round footprint gets, in tiles.</summary>
    private const float RoundMax = 0.95f;

    /// <summary>
    /// Height of the lid in pixels, or 0 when the sprite has none. With a shift for each column, the lid edge is looked
    /// for along the sprite's bottom outline moved up, as the front edge of a round lid is drawn.
    /// </summary>
    public static int LidLine(SpriteAnalysis a, int[]? shift = null)
    {
        var w = a.X1 - a.X0;
        var h = a.Y1 - a.Y0;
        var lastEdge = a.Y0 + 1;
        var end = a.Y0 + (int) MathF.Floor(h * 0.75f);
        for (var r = a.Y0 + 2; r <= end; r++)
        {
            // Rounded top corners may leave the first rows narrow. After them the lid must span the sprite.
            if (a.Count[r] < w * LidWidth)
            {
                if (r - a.Y0 < 3)
                    continue;

                return 0;
            }


            var share = RowShare(a, r, shift);
            if (share < LidShare)
                continue;

            if (r - lastEdge >= LidBand && r - a.Y0 >= LidMin)
                return r - a.Y0;

            if (share >= 0.98f && r - a.Y0 >= LidMin && RowShare(a, r + 1, shift) < LidShare)
                return r - a.Y0;

            lastEdge = r;
        }

        return 0;
    }

    /// <summary>Most a darker front's brightness may be, as a share of the bright top's, for a shade line.</summary>
    private const float ShadeRatio = 0.6f;

    /// <summary>
    /// Height in pixels of a bright top drawn over a darker front, or null when there is none: the row where the rows
    /// above are brightest against the rows below, when the rows below are at most <see cref="ShadeRatio"/> as bright.
    /// The outline's first and last rows are left out.
    /// </summary>
    /// <param name="near">Also accept a front dark against only the rows just above it, under a dark rim.</param>
    public static int? ShadeLine(SpriteAnalysis a, bool near = false)
    {
        var h = a.Y1 - a.Y0;
        if (h < LidMin * 2)
            return null;

        var sum = new float[h + 1];
        var count = new int[h + 1];
        for (var r = a.Y0; r < a.Y1; r++)
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

            sum[r - a.Y0 + 1] = sum[r - a.Y0] + s;
            count[r - a.Y0 + 1] = count[r - a.Y0] + n;
        }

        float Mean(int from, int to) => count[to] > count[from]
            ? (sum[to] - sum[from]) / (count[to] - count[from])
            : 0f;

        int? best = null;
        float bestAbove = 0f, bestBelow = 0f;
        for (var r = LidMin; r <= (int) MathF.Floor(h * 0.75f); r++)
        {
            float above = Mean(1, r), below = Mean(r, h - 1);
            if (best == null || above - below > bestAbove - bestBelow)
            {
                best = r;
                bestAbove = above;
                bestBelow = below;
            }
        }

        if (best is not { } line || bestAbove <= 0f)
            return null;

        var bright = near ? MathF.Max(bestAbove, Mean(Math.Max(1, line - LidMin), line)) : bestAbove;
        return bestBelow <= bright * ShadeRatio ? line : null;
    }

    /// <summary>Lowest a shade line may sit to be the edge of a top, as a share of the sprite's height.</summary>
    private const float ShadeDepth = 0.6f;

    /// <summary>
    /// The sharp row a shade line sits on when it is the edge of a top rather than a dark base or a lit screen: the
    /// line or the row above it, in the top <see cref="ShadeDepth"/> of the sprite. Null when neither is sharp.
    /// </summary>
    private static int? ShadeEdge(SpriteAnalysis a, int line)
    {
        if (line > (a.Y1 - a.Y0) * ShadeDepth)
            return null;

        return RowShare(a, a.Y0 + line) >= LidShare ? line
            : RowShare(a, a.Y0 + line - 1) >= LidShare ? line - 1
            : null;
    }

    /// <summary>Least rise in a row's mean brightness over the row above for a lit front edge.</summary>
    private const float RiseMin = 0.2f;

    /// <summary>
    /// Height in pixels of a top whose rows are all edges, like a striped grille: the row in the top half whose mean
    /// brightness rises most over the row above, the lit edge of the front under the top's dark last row. Null when
    /// no row rises by <see cref="RiseMin"/>.
    /// </summary>
    private static int? RiseLine(SpriteAnalysis a)
    {
        var h = a.Y1 - a.Y0;
        float Mean(int r)
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

        int? best = null;
        var bestRise = RiseMin;
        for (var k = LidMin; k <= h / 2; k++)
        {
            var rise = Mean(a.Y0 + k) - Mean(a.Y0 + k - 1);
            if (rise < bestRise)
                continue;

            best = k;
            bestRise = rise;
        }

        return best;
    }

    /// <summary>
    /// Height of a cap in pixels, or 0 when there is none: the rows above the first one that spans the sprite, when
    /// every row below it down to the feet stays wide. A dome or rounded top drawn over a box is its lid.
    /// </summary>
    private static int Shoulder(SpriteAnalysis a)
    {
        var w = a.X1 - a.X0;
        var h = a.Y1 - a.Y0;
        var s = a.Y0;
        while (s < a.Y1 && a.Count[s] < w * ShoulderFill)
            s++;

        if (s - a.Y0 < LidMin || a.Y1 - s < h / 3)
            return 0;

        for (var r = s; r < a.Y1 - ShoulderFeet; r++)
        {
            if (a.Count[r] < w * LidWidth)
                return 0;
        }

        return s - a.Y0;
    }

    /// <summary>
    /// The sprite without its feet when it is a round body under a dome: a cap of rows that starts narrow, widens
    /// steadily and stays centred, over rows that span the sprite down to a base with rounded ends. Null when it
    /// isn't. Feet are only left out here, under a dome.
    /// </summary>
    private static SpriteAnalysis? Dome(SpriteAnalysis a)
    {
        var cap = Shoulder(a);
        var w = a.X1 - a.X0;
        if (cap == 0 || a.Count[a.Y0] > w * DomeTop)
            return null;

        // Past its rounded top rows a dome widens a little each row and meets the body, so something narrow set on a
        // box isn't one.
        var centre = (a.X0 + a.X1) / 2f;
        var last = 0;
        for (var r = a.Y0; r <= a.Y0 + cap; r++)
        {
            var (c0, c1) = Span(a, r);
            var step = a.Count[r] - last;
            if (MathF.Abs((c0 + c1) / 2f - centre) > 1f || step < -1 || r - a.Y0 >= DomeRim && step > DomeStep)
                return null;

            last = a.Count[r];
        }

        // A round body draws its base as an arc, so its last row is inset at both ends. A box's is not.
        var body = Footless(a);
        if (body.Y1 - BaseRows <= a.Y0 + cap)
            return null;

        var (s0, s1) = Span(body, body.Y1 - BaseRows);
        var (b0, b1) = Span(body, body.Y1 - 1);
        return b0 > s0 && b1 < s1 ? body : null;
    }

    /// <summary>
    /// The column runs of a top that steps: runs whose top rows differ by at least <see cref="StepRows"/> from the
    /// run beside them, each at least <see cref="StepWidth"/> wide. Narrower runs join a neighbour. Null when the top
    /// doesn't step.
    /// </summary>
    private static List<(int X0, int X1)>? Steps(SpriteAnalysis a)
    {
        int Top(int c)
        {
            for (var r = a.Y0; r < a.Y1; r++)
            {
                if (a.Opaque(c, r))
                    return r;
            }

            return a.Y1;
        }

        var runs = new List<(int X0, int X1, int Top)>();
        for (var c = a.X0; c < a.X1; c++)
        {
            var top = Top(c);
            if (runs.Count > 0 && Math.Abs(top - runs[^1].Top) < StepRows)
                runs[^1] = (runs[^1].X0, c + 1, Math.Min(runs[^1].Top, top));
            else
                runs.Add((c, c + 1, top));
        }

        for (var i = 0; i < runs.Count;)
        {
            if (runs.Count == 1 || runs[i].X1 - runs[i].X0 >= StepWidth)
            {
                i++;
                continue;
            }

            var into = i == 0 ? 1 : i - 1;
            var (x0, x1) = (Math.Min(runs[i].X0, runs[into].X0), Math.Max(runs[i].X1, runs[into].X1));
            runs[into] = (x0, x1, Math.Min(runs[i].Top, runs[into].Top));
            runs.RemoveAt(i);
            i = 0;
        }

        var cuts = new List<int>();
        for (var i = 1; i < runs.Count; i++)
        {
            cuts.Add(runs[i].X0);
        }

        // A top that slopes from one level to another chains into one run above. Flat stretches at least StepRows
        // apart are cut halfway across the slope between them, in place of a cut the chain left on the slope.
        var flats = Flats(a, Top);
        for (var i = 1; i < flats.Count; i++)
        {
            if (Math.Abs(flats[i].Top - flats[i - 1].Top) < StepRows)
                continue;

            var cut = (flats[i - 1].X1 + flats[i].X0) / 2;
            var near = cuts.FindAll(k => Math.Abs(k - cut) < StepWidth);
            if (near.Exists(k => Math.Abs(Top(k) - Top(k - 1)) >= 2))
                continue;

            cuts.RemoveAll(near.Contains);
            cuts.Add(cut);
        }

        if (cuts.Count == 0)
            return null;

        cuts.Sort();
        var steps = new List<(int X0, int X1)>();
        var from = a.X0;
        foreach (var cut in cuts)
        {
            steps.Add((from, cut));
            from = cut;
        }

        steps.Add((from, a.X1));
        return steps;
    }

    /// <summary>Runs of at least <see cref="StepWidth"/> columns whose tops sit on the same row.</summary>
    private static List<(int X0, int X1, int Top)> Flats(SpriteAnalysis a, Func<int, int> top)
    {
        var flats = new List<(int X0, int X1, int Top)>();
        for (var c = a.X0; c < a.X1;)
        {
            var end = c + 1;
            while (end < a.X1 && top(end) == top(c))
                end++;

            if (end - c >= StepWidth)
                flats.Add((c, end, top(c)));

            c = end;
        }

        return flats;
    }

    /// <summary>
    /// The low front part a side image shows: the columns at its front end whose tops step by a row at most and sit
    /// at least <see cref="StepRows"/> below the image's top. Gives its depth in pixels and the row of the front image
    /// where it starts, which is the sharpest edge near where a top that deep would begin. Null when the side image is
    /// the front one again or shows no such part.
    /// </summary>
    private static (int Edge, int Depth)? Tray(SpriteAnalysis a, SpriteAnalysis side, int lid)
    {
        if (side.Empty || Same(a, side))
            return null;

        int Top(int c)
        {
            for (var r = side.Y0; r < side.Y1; r++)
            {
                if (side.Opaque(c, r))
                    return r;
            }

            return side.Y1;
        }

        // Counts the low columns from one end whose tops stay within a row of the one before. The end column may be
        // a rounded corner.
        int Low(int from, int step)
        {
            var n = 0;
            var level = -1;
            for (var c = from; c >= side.X0 && c < side.X1; c += step)
            {
                var top = Top(c);
                if (top - side.Y0 < StepRows || top >= side.Y1 - TrayFront)
                    break;

                if (n > 1 && Math.Abs(top - level) > 1)
                    break;

                level = top;
                n++;
            }

            return n;
        }

        // The side image faces east, so the front is its right end. A low back hides behind the body.
        var depth = Low(side.X1 - 1, -1);
        if (Low(side.X0, 1) >= StepWidth || depth < StepWidth || depth > (side.X1 - side.X0) * TrayShare)
            return null;

        var guess = a.Y1 - TrayFront - depth;
        var first = Math.Max(guess - TrayReach, a.Y0 + lid + StepRows + TrayReach);
        var last = Math.Min(guess + TrayReach, a.Y1 - TrayFront - 2);
        if (first > last)
            return null;

        var edge = Math.Clamp(guess, first, last);
        var best = LidShare;
        for (var r = first; r <= last; r++)
        {
            var share = RowShare(a, r);
            if (share <= best)
                continue;

            best = share;
            edge = r;
        }

        return (edge, depth);
    }

    /// <summary>Whether the side frame shows a low front part, which the tray rule shapes.</summary>
    public static bool Trayed(SpriteAnalysis a, SpriteAnalysis side)
    {
        return Tray(a, side, LidLine(a)) != null;
    }

    /// <summary>Rows of a low front part that are its front face, and how far from its expected row its edge is sought.</summary>
    private const int TrayFront = 2;
    private const int TrayReach = 3;

    /// <summary>Most of a side image's width a low front part takes.</summary>
    private const float TrayShare = 0.6f;

    /// <summary>Share of its collision box's depth a lidded solid is at least as deep as.</summary>
    private const float FootprintFill = 0.75f;

    /// <summary>Share of its bounding box a lidless sprite fills below which it is a figure's card.</summary>
    private const float BoardFill = 0.6f;

    /// <summary>Longest straight side, as a share of the height, below which a lidless sprite is a figure's card.</summary>
    private const float CardStraight = 0.25f;

    /// <summary>Share of the sprite's bounding box that is opaque.</summary>
    private static float Fill(SpriteAnalysis a)
    {
        var opaque = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            opaque += a.Count[r];
        }

        return (float) opaque / ((a.X1 - a.X0) * (a.Y1 - a.Y0));
    }

    /// <summary>Rows above the last one that a rounded base is narrower than.</summary>
    private const int BaseRows = 4;

    /// <summary>How much of its drawn rows a dome rises by.</summary>
    private const float DomeRise = 0.5f;

    /// <summary>Pixels a body narrows by towards its base, on each side and in all, to count as tapered.</summary>
    private const int TaperSide = 2;
    private const int TaperAll = 4;

    /// <summary>Whether two images have the same outline.</summary>
    public static bool Same(SpriteAnalysis a, SpriteAnalysis b)
    {
        if (a.Width != b.Width || a.Height != b.Height)
            return false;

        for (var i = 0; i < a.Mask.Length; i++)
        {
            if (a.Mask[i] != b.Mask[i])
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether both sides close in from the widest rows down to the base, over at least a third of the height. Feet
    /// under the body are left out.
    /// </summary>
    private static bool Tapered(SpriteAnalysis a)
    {
        var widest = a.Y0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            if (a.Count[r] >= a.Count[widest])
                widest = r;
        }

        var wide = a.Count[widest];
        var b = a.Y1 - 1;
        while (b > widest && a.Count[b] < wide * FeetShare)
            b--;

        var (w0, w1) = Span(a, widest);
        var (b0, b1) = Span(a, b);
        if (b0 - w0 < TaperSide || w1 - b1 < TaperSide || b0 - w0 + w1 - b1 < TaperAll
            || (b - widest) * 3 < a.Y1 - a.Y0)
            return false;

        for (var r = widest + 1; r <= b; r++)
        {
            if (a.Count[r] > a.Count[r - 1])
                return false;
        }

        return true;
    }

    /// <summary>Share of the middle row's width the widest row of the bottom quarter must exceed it by to splay.</summary>
    private const float SplayShare = 0.5f;

    /// <summary>Whether the widest row in the bottom quarter spans well past the middle row.</summary>
    private static bool Splayed(SpriteAnalysis a)
    {
        var (m0, m1) = Span(a, (a.Y0 + a.Y1) / 2);
        var widest = 0;
        for (var r = a.Y1 - (a.Y1 - a.Y0) / 4; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            widest = Math.Max(widest, c1 - c0);
        }

        return widest - (m1 - m0) > (m1 - m0) * SplayShare;
    }

    /// <summary>Widest and tallest a hung sprite may be, in pixels, to stand out from its wall as a shell.</summary>
    private const int BulbSize = 11;

    /// <summary>Sides round half a row of a shell.</summary>
    private const int ShellSegments = 8;

    /// <summary>
    /// A sprite hung on the north wall as half a turned solid: each row a half circle as wide as the row, its flat back
    /// on the wall face, every point taking the pixel straight in front of it.
    /// </summary>
    private static FoldMesh BuildShell(SpriteAnalysis a, Vector2 origin, float lift)
    {
        var quads = new List<FoldQuad>();
        const float wall = 0.5f;
        var step = MathF.PI / ShellSegments;
        float Z(float r) => (a.Y1 - r) / PixelsPerTile + lift;

        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            if (c1 <= c0)
                continue;

            var mid = (c0 + c1) / 2f;
            var rad = (c1 - c0) / 2f;
            Vector3 At(float angle, float row) => new(
                (mid - rad * MathF.Cos(angle) - origin.X) / PixelsPerTile,
                wall - rad * MathF.Sin(angle) / PixelsPerTile,
                Z(row));
            Vector2 Tex(float angle, float row) => new(mid - rad * MathF.Cos(angle), row);

            for (var k = 0; k < ShellSegments; k++)
            {
                float a0 = k * step, a1 = (k + 1) * step, m = (a0 + a1) / 2f;
                Add(quads, At(a0, r + 1), At(a1, r + 1), At(a1, r), At(a0, r),
                    Tex(a0, r + 1), Tex(a1, r + 1), Tex(a1, r), Tex(a0, r),
                    new(-MathF.Cos(m), -MathF.Sin(m), 0f), 1f);
            }

            // Where a neighbouring row is narrower or missing, the step between them faces up or down.
            foreach (var (next, z, up) in new[] { (r - 1, r, true), (r + 1, r + 1, false) })
            {
                var (n0, n1) = next >= a.Y0 && next < a.Y1 ? Span(a, next) : (0, 0);
                var inner = n1 > n0 ? (n1 - n0) / 2f : 0f;
                if (inner >= rad)
                    continue;

                var t = new Vector2(mid, r + 0.5f);
                for (var k = 0; k < ShellSegments; k++)
                {
                    float a0 = k * step, a1 = (k + 1) * step;
                    Vector3 Rim(float angle, float radius) => new(
                        (mid - radius * MathF.Cos(angle) - origin.X) / PixelsPerTile,
                        wall - radius * MathF.Sin(angle) / PixelsPerTile,
                        Z(z));
                    AddFlat(quads, Rim(a0, inner), Rim(a1, inner), Rim(a1, rad), Rim(a0, rad), t,
                        new(0f, 0f, up ? 1f : -1f));
                }
            }
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>Most end columns trimmed off a round body, and the rise over its neighbour that marks one.</summary>
    private const int RoundTrim = 4;
    private const int RoundStep = 2;

    /// <summary>Most rows a round top is drawn over, and most pixels it widens by from one row to the next.</summary>
    private const int CrownRows = 6;
    private const int CrownStep = 4;

    /// <summary>Share of the widest row at which a round top has reached the body.</summary>
    private const float CrownReach = 0.85f;

    /// <summary>
    /// Rows at the top that widen a little at a time until they reach the body's width, the drawn top of a round
    /// solid, or 0 when the top does not do that.
    /// </summary>
    private static int Crown(SpriteAnalysis a)
    {
        var widest = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            widest = Math.Max(widest, c1 - c0);
        }

        var (t0, t1) = Span(a, a.Y0);
        var last = t1 - t0;
        var first = last;
        for (var r = a.Y0 + 1; r <= a.Y0 + CrownRows && r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            var width = c1 - c0;
            if (width < last || width - last > CrownStep)
                return 0;

            if (width >= widest * CrownReach)
                return width - first >= CrownStep ? r - a.Y0 : 0;

            last = width;
        }

        return 0;
    }

    /// <summary>
    /// Rows of a round top drawn in full: down to the first row below its widest where most pixels turn darker, the
    /// front edge of the flat face, within twice the rows it took to widen. Keeps the crown when there is no such row.
    /// </summary>
    private static int CrownFace(SpriteAnalysis a, int crown)
    {
        var widest = 0;
        var at = a.Y0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            if (c1 - c0 <= widest)
                continue;

            widest = c1 - c0;
            at = r;
        }

        var end = Math.Min(a.Y1 - 2, at + (at - a.Y0) + 2);
        for (var r = at + 1; r <= end; r++)
        {
            if (ShadeShare(a, r, true) >= SeatSplit)
                return r - a.Y0;
        }

        return crown;
    }

    /// <summary>Fewest rows a top edge must step out one pixel a row over, and the narrowest strip that makes.</summary>
    private const int SlantRows = 3;
    private const int SlantStrip = 3;

    /// <summary>
    /// The columns of the front when one end of the top steps out one pixel a row from the top row down, the edge of
    /// a side face drawn in perspective, or null when neither end does.
    /// </summary>
    private static (int X0, int X1)? Slanted(SpriteAnalysis a)
    {
        var (t0, t1) = Span(a, a.Y0);
        int left = 0, right = 0;
        for (var r = a.Y0 + 1; r < a.Y1; r++)
        {
            var (p0, p1) = Span(a, r - 1);
            var (c0, c1) = Span(a, r);
            var step = r - a.Y0;
            if (right == step - 1 && c1 - p1 == 1)
                right = step;

            if (left == step - 1 && p0 - c0 == 1)
                left = step;

            if (right < step && left < step)
                break;
        }

        if (right >= SlantRows && a.X1 - t1 >= SlantStrip)
            return (a.X0, t1);

        if (left >= SlantRows && t0 - a.X0 >= SlantStrip)
            return (t0, a.X1);

        return null;
    }

    /// <summary>Darkest a pixel may be to count towards a plain top's colour, as luminance plus colourfulness.</summary>
    private const float TopDark = 0.3f;

    /// <summary>
    /// The centre of a pixel in the commonest colour among the top rows, leaving out dark outline pixels, or of any
    /// top pixel when all are dark.
    /// </summary>
    private static Vector2 TopColour(SpriteAnalysis a, int rows)
    {
        var counts = new Dictionary<int, (int Count, Vector2 Pixel)>();
        var fallback = new Vector2(a.X0 + 0.5f, a.Y0 + 0.5f);
        for (var r = a.Y0; r < a.Y0 + rows; r++)
        {
            for (var c = a.X0; c < a.X1; c++)
            {
                if (!a.Opaque(c, r))
                    continue;

                var i = r * a.Width + c;
                fallback = new Vector2(c + 0.5f, r + 0.5f);
                // Luminance plus colourfulness, so a strong red is not taken for the dark outline.
                if (a.Lum[i] + a.Chroma[i] < TopDark)
                    continue;

                var key = (int) MathF.Round(a.Lum[i] * 255f) * 1024 + (int) MathF.Round(a.Chroma[i] * 255f);
                counts[key] = counts.TryGetValue(key, out var seen) ? (seen.Count + 1, seen.Pixel) : (1, fallback);
            }
        }

        var best = fallback;
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

    /// <summary>Share of the widest row the top row of an arch is at most.</summary>
    private const float ArchTop = 0.75f;

    /// <summary>Depth of an arched front as a share of its width, since no top is drawn to read it from.</summary>
    private const float ArchDepth = 0.5f;

    /// <summary>
    /// Whether the top rows widen without a break from a narrow top row until they are about as wide as the body,
    /// within a quarter of the height.
    /// </summary>
    private static bool ArchedTop(SpriteAnalysis a)
    {
        var widest = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            widest = Math.Max(widest, c1 - c0);
        }

        var (t0, t1) = Span(a, a.Y0);
        if (t1 - t0 > widest * ArchTop)
            return false;

        var last = t1 - t0;
        for (var r = a.Y0 + 1; r <= a.Y0 + (a.Y1 - a.Y0) / 4; r++)
        {
            var (c0, c1) = Span(a, r);
            if (c1 - c0 < last)
                return false;

            last = c1 - c0;
            if (last >= widest * CrownReach)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether the rows down to the lid line widen without a break and are still narrower than the body below, so the
    /// lid line is drawn across a rounded top rather than along the edge of a flat one.
    /// </summary>
    private static bool Arched(SpriteAnalysis a, int lid)
    {
        var widest = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            widest = Math.Max(widest, c1 - c0);
        }

        var (t0, t1) = Span(a, a.Y0);
        if (t1 - t0 > widest * ArchTop)
            return false;

        var last = t1 - t0;
        for (var r = a.Y0 + 1; r <= a.Y0 + lid; r++)
        {
            var (c0, c1) = Span(a, r);
            if (c1 - c0 < last)
                return false;

            last = c1 - c0;
        }

        return last < widest;
    }

    /// <summary>
    /// The longest run of rows over which one side of the outline stays in the same column, as a share of the height.
    /// Machines and cabinets have a straight upright side. Plants, statues and figures do not.
    /// </summary>
    public static float Straight(SpriteAnalysis a)
    {
        var h = a.Y1 - a.Y0;
        if (h <= 1)
            return 1f;

        var best = 1;
        int left = 1, right = 1;
        var (p0, p1) = Span(a, a.Y0);
        for (var r = a.Y0 + 1; r < a.Y1; r++)
        {
            var (c0, c1) = Span(a, r);
            left = c0 == p0 ? left + 1 : 1;
            right = c1 == p1 ? right + 1 : 1;
            best = Math.Max(best, Math.Max(left, right));
            (p0, p1) = (c0, c1);
        }

        return (float) best / h;
    }

    /// <summary>The sprite as one upright card that turns to face the view.</summary>
    private static FoldMesh BuildCard(SpriteAnalysis a, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        float xl = (a.X0 - origin.X) / PixelsPerTile, xr = (a.X1 - origin.X) / PixelsPerTile;
        var top = (a.Y1 - a.Y0) / PixelsPerTile;
        Add(quads, new(xl, 0f, 0f), new(xr, 0f, 0f), new(xr, 0f, top), new(xl, 0f, top),
            new(a.X0, a.Y1), new(a.X1, a.Y1), new(a.X1, a.Y0), new(a.X0, a.Y0), new(0, -1, 0), 1f);
        return new FoldMesh(quads.ToArray(), false);
    }

    /// <summary>The sprite without the stubby feet under its body: up to a few bottom rows under half its width.</summary>
    private static SpriteAnalysis Footless(SpriteAnalysis a)
    {
        var w = a.X1 - a.X0;
        var y = a.Y1;
        while (y > a.Y0 && a.Y1 - y < ShoulderFeet && a.Count[y - 1] < w * FeetShare)
            y--;

        return y == a.Y1 || y == a.Y0 ? a : a.Above(y);
    }

    /// <summary>Whether the sprite covers nearly all of one full tile and no more.</summary>
    public static bool TopDown(SpriteAnalysis a)
    {
        var w = a.X1 - a.X0;
        var h = a.Y1 - a.Y0;
        if (w < PixelsPerTile - 2 || h < PixelsPerTile - 2 || w > PixelsPerTile + 2 || h > PixelsPerTile + 2)
            return false;

        var opaque = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            opaque += a.Count[r];
        }

        return opaque >= TopDownFill * PixelsPerTile * PixelsPerTile;
    }

    /// <summary>Depth of the solid in tiles: from the lid when there is one, else a quarter of the width.</summary>
    public static float FoldDepth(SpriteAnalysis a, int lid)
    {
        if (lid > 0)
            return MathF.Min(0.95f, lid / PixelsPerTile * 2.5f);

        return Math.Clamp((int) MathF.Round((a.X1 - a.X0) / 4f), 2, 8) / PixelsPerTile;
    }

    /// <summary>
    /// Builds the solid with its front facing south and its base on the floor.
    /// </summary>
    /// <param name="origin">Pixel of the image at the entity's position.</param>
    /// <param name="frameBottom">Pixel row of the bottom of the sprite's frame.</param>
    /// <param name="onWall">
    /// The entity is mounted on the wall tile it stands on. It becomes a thin panel on the wall's south face.
    /// </param>
    /// <param name="hung">
    /// The sprite is drawn a tile north of the entity, on the wall there. It becomes a thin panel on that wall's face.
    /// </param>
    /// <param name="console">
    /// The entity is a console. Its face leans back from its foot, and the lid runs from the top of the face to the back.
    /// </param>
    /// <param name="keyboard">Rows a console's keyboard layer covers, which slope back as its shelf.</param>
    /// <param name="rest">
    /// Pixels the sprite may be drawn above its frame bottom because it stands on a surface. They are not a hang.
    /// </param>
    /// <param name="footprint">
    /// The entity's collision box in tiles, facing south. A lidless solid with one stands fixed in the world, centred
    /// in the box and as deep as it, but no deeper than the sprite is wide.
    /// </param>
    /// <param name="seat">Something a body sits on. It stands as legs, a seat and a backrest.</param>
    /// <param name="side">The east direction frame, when the sprite draws its own side frames.</param>
    /// <param name="tall">Drawn at mob height or above, like a plant or a statue that people walk behind.</param>
    public static FoldMesh Build(SpriteAnalysis a, Vector2 origin, float frameBottom, bool onWall, bool hung,
        bool console, (int Top, int Bottom)? keyboard = null, float rest = 0f, Box2? footprint = null,
        bool seat = false, SpriteAnalysis? side = null, bool tall = false)
    {
        // A panel's whole image stands on its wall, so it has no lid.
        var panel = onWall || hung;
        var lid = panel ? 0 : LidLine(a);
        // A grille's rows are all edges and hide its lid line. A lid line past half the height is not one.
        if (lid * 2 > a.Y1 - a.Y0 && RiseLine(a) is { } lit)
            lid = lit;

        // A lidless sprite drawn over the people around it has no box in it. It stays an upright card.
        if (tall && lid == 0 && !panel && !console && !seat)
        {
            Path = "tall card";
            return BuildCard(a, origin);
        }

        // A top that widens row by row down to the body is an arch, not a lid. The outline is pushed back whole.
        var arched = lid > 0 && !panel && !console && footprint != null && Arched(a, lid);
        if (arched)
            lid = 0;

        // A side face drawn at a slant beside the front is left off. The fold infers its own side there.
        if (lid > 0 && !panel && !console && side == null && Slanted(a) is { } face)
            a = a.Columns(face.X0, face.X1);

        // A frame larger than a tile reaches below the tile's south edge, where the floor line is.
        var floor = MathF.Min(frameBottom, origin.Y + PixelsPerTile / 2f);
        var gap = MathF.Max(0f, floor - a.Y1 - rest);

        // A lidless sprite that fills its whole tile is drawn from above. It becomes a low box with the sprite on top.
        if (lid == 0 && !panel && TopDown(a))
        {
            Path = "top-down box";
            return BlockBuilder.Build(origin, TopDownHeight);
        }

        console &= !panel;
        if (!console && !panel && gap <= MountGap && Round(a, out var rise, out var body, out var trimmed, side == null))
        {
            Path = trimmed ? "round trimmed" : "round";
            return BuildRound(a, rise, body, origin, keep: trimmed ? body : null);
        }

        if (!console && !panel && gap <= MountGap && footprint != null && Dome(a) is { } domed)
        {
            Path = "dome";
            // A lid line well below the cap is the edge of a flat round top drawn in full.
            var cap = Shoulder(a);
            return BuildRound(domed, new int[domed.X1 - domed.X0], (domed.X0, domed.X1), origin,
                lid >= cap * 2 ? 0 : cap);
        }

        // Sides that close in towards the base, like a bucket's, fit no box. Without a drawn top the sprite stays a
        // flat card.
        if (!console && !panel && !seat && lid == 0 && gap <= MountGap && Tapered(a))
        {
            Path = "tapered card";
            return BuildCard(a, origin);
        }

        if (console)
            lid = CapLine(a, lid);

        var standing = lid == 0 && !panel && !console && footprint != null;
        // A lidless top that widens row by row to the body is an arch, not a shoulder to fold back.
        arched |= standing && ArchedTop(a);
        if (standing && !arched)
            lid = Shoulder(a);

        // A lidless outline that leaves much of its box empty, like a figure's, or one with no straight upright side,
        // has no box in it. It stands as a card facing the view.
        if (standing && !seat
            && (lid == 0 && Fill(a) < BoardFill || !arched && side == null && Straight(a) < CardStraight))
        {
            Path = "figure card";
            return BuildCard(a, origin);
        }

        // A console is about as deep as its front is wide. A lidded solid is never much thinner than what it blocks.
        var depth = panel ? WallPanelDepth
            : console ? Math.Clamp(ConsoleDepth * (a.X1 - a.X0) / PixelsPerTile, ConsoleMinDepth, ConsoleDepth)
            : standing ? Math.Clamp(MathF.Min(footprint!.Value.Height,
                (a.X1 - a.X0) / PixelsPerTile * (arched ? ArchDepth : 1f)), 1f / PixelsPerTile, 1f)
            // A front less than half as wide as its box is deep, like a locker's, is no guide to how deep it looks.
            // It is never deeper than it is wide, so a narrow bin in a full-tile box stays a cube.
            : footprint is { } blocked && gap <= MountGap && blocked.Height > 0f
                ? MathF.Min(Math.Clamp(FoldDepth(a, lid),
                    a.X1 - a.X0 < blocked.Height * PixelsPerTile / 2f ? 0f : blocked.Height * FootprintFill,
                    blocked.Height), (a.X1 - a.X0) / PixelsPerTile)
            : MathF.Min(FoldDepth(a, lid), (a.X1 - a.X0) / PixelsPerTile);
        var quads = new List<FoldQuad>();
        var hz = depth / 2f;

        // Sprites drawn in the top of their frame hang on the north wall at their drawn height. Panels on a wall
        // keep that height, or sit halfway up the wall when drawn low.
        var lift = gap > MountGap ? gap / PixelsPerTile : 0f;
        var north = lift > 0f ? 0.5f - hz : standing ? footprint!.Value.Center.Y : 0f;
        if (panel)
        {
            lift = MathF.Max(lift, (WallHeight - (a.Y1 - a.Y0) / PixelsPerTile) / 2f);
            north = onWall ? -0.5f - hz : 0.5f - hz;
        }

        // A fitting this small on the wall, like a bulb, stands out from it as a half-round shell.
        if (hung && a.X1 - a.X0 <= BulbSize && a.Y1 - a.Y0 <= BulbSize)
        {
            Path = $"bulb lift={lift:0.00}";
            return BuildShell(a, origin, lift);
        }

        var tray = panel || console || lift > 0f || side == null ? null : Tray(a, side, lid);
        if (seat && !panel && lift == 0f && tray == null)
        {
            // Legs that splay far past the body, like a walker's, fit no seat shape. It stays a flat card.
            if (Splayed(a))
            {
                Path = "splayed card";
                return BuildCard(a, origin);
            }

            Path = "seat";
            return BuildSeat(a, origin);
        }

        Path = (panel ? "panel" : console ? "console" : standing ? lid > 0 ? "standing cap" : "standing"
                   : lid > 0 ? "lid fold" : "card")
               + $" lid={lid} lift={lift:0.00} depth={depth:0.00}";

        var yf = north - hz;
        var yb = north + hz;

        // A top that steps up and down across the sprite, like a tower beside a low deck, folds each step on its own
        // lid line, all with one front and one back.
        var steps = panel || console || lid == 0 && !standing ? null : Steps(a);
        // A bright top over a darker front, like a lathe's, folds where the shade changes rather than at an edge.
        var shade = panel || console || tray != null ? null : ShadeLine(a);
        var colours = BackColours(a, a.Y0 + lid);
        if (tray is { } front)
        {
            // The side image shows a low part in front of a taller back. The rows from its edge down are that part,
            // folded on its own lid line, or lying flat bar the last ones when it has none. The rows above fold as
            // the back, standing behind it from its top up.
            Path = $"tray edge={front.Edge - a.Y0} deep={front.Depth} lift={lift:0.00} depth={depth:0.00}";
            depth = MathF.Max(depth, (front.Depth + 2) / PixelsPerTile);
            yf = north - depth / 2f;
            yb = north + depth / 2f;
            var ym = yf + front.Depth / PixelsPerTile;
            var low = a.Rows(front.Edge, a.Y1);
            var lowLid = LidLine(low);
            if (lowLid == 0)
                lowLid = a.Y1 - TrayFront - front.Edge;

            Carve(quads, low, lowLid, a.Y1, origin, lift, yf, ym, colours);
            var back = a.Above(front.Edge);
            var plinth = (a.Y1 - front.Edge - lowLid) / PixelsPerTile;
            Carve(quads, back, lid, front.Edge, origin, lift + plinth, ym, yb, colours);

            // The back stands on a plinth as high as the low part, its sides in the colours of that part's two ends.
            var (p0, p1) = Span(low, low.Y0);
            float px0 = (p0 - origin.X) / PixelsPerTile, px1 = (p1 - origin.X) / PixelsPerTile;
            float z0 = lift, z1 = lift + plinth;
            var west = new Vector2(p0 + 0.5f, low.Y0 + 0.5f);
            var east = new Vector2(p1 - 0.5f, low.Y0 + 0.5f);
            AddFlat(quads, new(px0, yb, z0), new(px0, ym, z0), new(px0, ym, z1), new(px0, yb, z1), west, new(-1, 0, 0));
            AddFlat(quads, new(px1, ym, z0), new(px1, yb, z0), new(px1, yb, z1), new(px1, ym, z1), east, new(1, 0, 0));
            Add(quads, new(px1, yb, z0), new(px0, yb, z0), new(px0, yb, z1), new(px1, yb, z1),
                colours.Casing, colours.Casing, colours.Casing, colours.Casing, new(0, 1, 0), BackShade);
        }
        else if (steps != null)
        {
            Path = $"stepped x{steps.Count} lift={lift:0.00} depth={depth:0.00}" + (shade != null ? " shaded" : "");
            foreach (var (x0, x1) in steps)
            {
                var part = a.Columns(x0, x1);
                var top = (shade != null ? ShadeLine(part) : null) ?? LidLine(part);
                Carve(quads, part, top == 0 && standing ? Shoulder(part) : top, a.Y1, origin, lift, yf, yb, colours);
            }
        }
        else
        {
            // A line within a row of the lid line is that same edge, and the lid line stays.
            if (lid > 0 && (shade ?? ShadeLine(a, true)) is { } line && ShadeEdge(a, line) is { } edge
                && Math.Abs(edge - lid) > 1)
            {
                Path += $" shade={edge}";
                lid = edge;
            }

            if (console)
                Fold(quads, a, lid, a.Y1, origin, lift, yf, yb, true, keyboard);
            else
                Carve(quads, a, lid, a.Y1, origin, lift, yf, yb, colours);
        }

        return new FoldMesh(quads.ToArray(), lid > 0 || panel || lift > 0f || console || standing);
    }

    /// <summary>
    /// Folds a console between the given front and back: the rows above the lid line lie flat and the rows below stand
    /// as the front, with a wall on every outline edge and an inferred back.
    /// </summary>
    /// <param name="bottom">Pixel row of the whole sprite's base, which sits at the lift.</param>
    private static void Fold(List<FoldQuad> quads, SpriteAnalysis a, int lid, int bottom, Vector2 origin, float lift,
        float yf, float yb, bool console, (int Top, int Bottom)? keyboard)
    {
        var l = a.Y0 + lid;
        float X(float c) => (c - origin.X) / PixelsPerTile;
        float Z(float r) => (bottom - r) / PixelsPerTile + lift;
        var zl = Z(l);
        var xl = X(a.X0);
        var xr = X(a.X1);

        // A console stands on its foot, the bottom rows inset from the body. Its keyboard, or else a light shelf in its
        // sprite, slopes back and the rows above stand upright behind it. Without one, the whole face above the foot
        // leans.
        var foot = console ? Foot(a, l) : a.Y1;
        var shelf = console ? Keyboard(keyboard, l, foot) ?? Shelf(a, l, foot) : null;
        var bendTop = shelf?.Top ?? l;
        var bendBottom = shelf?.Bottom ?? foot;
        var lean = !console ? 0f
            : shelf != null ? Z(bendTop) - Z(bendBottom)
            : ConsoleLean * (zl - Z(foot));
        float Y(float r) => r >= bendBottom ? yf
            : r >= bendTop ? yf + lean * (bendBottom - r) / (bendBottom - bendTop)
            : yf + lean;
        var yl = Y(l);

        // Front face, one quad for each run between bends, bottom first.
        var bends = new[] { a.Y1, bendBottom, bendTop, l };
        for (var i = 0; i < bends.Length - 1; i++)
        {
            float rb = bends[i], rt = bends[i + 1];
            if (rb <= rt)
                continue;

            float yb0 = Y(rb), yt0 = Y(rt), zb0 = Z(rb), zt0 = Z(rt);
            var normal = Vector3.Normalize(new Vector3(0f, -(zt0 - zb0), yt0 - yb0));
            Add(quads, new(xl, yb0, zb0), new(xr, yb0, zb0), new(xr, yt0, zt0), new(xl, yt0, zt0),
                new(a.X0, rb), new(a.X1, rb), new(a.X1, rt), new(a.X0, rt), normal, 1f);
        }

        if (lid > 0)
        {
            // The lid rows lie flat from the front edge to the back, with an underside.
            Add(quads, new(xl, yl, zl), new(xr, yl, zl), new(xr, yb, zl), new(xl, yb, zl),
                new(a.X0, l), new(a.X1, l), new(a.X1, a.Y0), new(a.X0, a.Y0), new(0, 0, 1), 1f);
            Add(quads, new(xl, yl, zl - 0.002f), new(xr, yl, zl - 0.002f), new(xr, yb, zl - 0.002f),
                new(xl, yb, zl - 0.002f), new(a.X0, l), new(a.X1, l), new(a.X1, a.Y0), new(a.X0, a.Y0),
                new(0, 0, -1), 0.6f);
        }

        // One wall per outline pixel edge of the front, in that pixel's colour.
        for (var r = l; r < a.Y1; r++)
        {
            for (var c = a.X0; c < a.X1; c++)
            {
                if (!InFront(a, l, c, r))
                    continue;

                var t = new Vector2(c + 0.5f, r + 0.5f);
                float xa = X(c), xb = X(c + 1), zt = Z(r), zb = Z(r + 1), ft = Y(r), fb = Y(r + 1);
                if (!InFront(a, l, c - 1, r))
                    AddFlat(quads, new(xa, yb, zb), new(xa, fb, zb), new(xa, ft, zt), new(xa, yb, zt), t, new(-1, 0, 0));
                if (!InFront(a, l, c + 1, r))
                    AddFlat(quads, new(xb, fb, zb), new(xb, yb, zb), new(xb, yb, zt), new(xb, ft, zt), t, new(1, 0, 0));
                if (!InFront(a, l, c, r + 1))
                    AddFlat(quads, new(xa, fb, zb), new(xb, fb, zb), new(xb, yb, zb), new(xa, yb, zb), t, new(0, 0, -1));

                // The top wall closes the solid. Under the lid sheet it sits just below it and always draws first.
                if (!InFront(a, l, c, r - 1))
                {
                    var under = r == l && lid > 0;
                    var zw = under ? zt - 0.001f : zt;
                    AddFlat(quads, new(xa, ft, zw), new(xb, ft, zw), new(xb, yb, zw), new(xa, yb, zw), t, new(0, 0, 1));
                    if (under)
                    {
                        var q = quads[^1];
                        q.Centre.Z -= 10f;
                        quads[^1] = q;
                    }
                }
            }
        }

        // The back is inferred row by row across the front's width at that row: a frame in the colours of the row's
        // two edge pixels, and a middle in the colour of the casing just inside the left one.
        for (var r = l; r < a.Y1; r++)
        {
            var c0 = a.X0;
            while (c0 < a.X1 && !a.Opaque(c0, r))
                c0++;

            var c1 = a.X1 - 1;
            while (c1 > c0 && !a.Opaque(c1, r))
                c1--;

            if (c0 >= a.X1)
                continue;

            var frame = MathF.Min(BackFrame, (c1 + 1 - c0) / 3f);
            var mid = Math.Min(c0 + (int) MathF.Ceiling(frame), c1);
            while (mid > c0 && !a.Opaque(mid, r))
                mid--;

            var left = new Vector2(c0 + 0.5f, r + 0.5f);
            var right = new Vector2(c1 + 0.5f, r + 0.5f);
            var middle = new Vector2(mid + 0.5f, r + 0.5f);
            float zt = Z(r), zb = Z(r + 1), xa = X(c0), xm0 = X(c0 + frame), xm1 = X(c1 + 1 - frame), xb = X(c1 + 1);
            Add(quads, new(xm0, yb, zb), new(xa, yb, zb), new(xa, yb, zt), new(xm0, yb, zt),
                left, left, left, left, new(0, 1, 0), BackShade);
            Add(quads, new(xm1, yb, zb), new(xm0, yb, zb), new(xm0, yb, zt), new(xm1, yb, zt),
                middle, middle, middle, middle, new(0, 1, 0), BackShade);
            Add(quads, new(xb, yb, zb), new(xm1, yb, zb), new(xm1, yb, zt), new(xb, yb, zt),
                right, right, right, right, new(0, 1, 0), BackShade);
        }

    }

    /// <summary>
    /// Builds a sprite whose bottom outline bows like the front of a round footprint as a solid turned about its
    /// upright axis. Each column is moved down by its outline's rise, then each row is a circle as wide as the row.
    /// The back half shows the casing just inside each end column.
    /// </summary>
    /// <param name="cap">Rows of a dome's cap, which rise by only part of their drawn height.</param>
    /// <param name="keep">Columns the solid is cut to, leaving out a part trimmed off the body's side.</param>
    private static FoldMesh BuildRound(SpriteAnalysis a, int[] rise, (int X0, int X1) body, Vector2 origin,
        int cap = 0, (int X0, int X1)? keep = null)
    {
        var (k0, k1) = keep ?? (a.X0, a.X1);
        var w = a.X1 - a.X0;
        // A dome has no flat lid. Its cap rows are rings that rise by part of their drawn height. A round top drawn
        // widening row by row is the flat top seen at a slant, so it lies flat like a lid instead of peaking.
        var lid = cap > 0 ? 0 : LidLine(a, rise);
        Vector2? plain = null;
        if (lid == 0 && cap == 0)
        {
            // Its drawn rims and bands would lie flat as rings, so the top is one colour: its commonest light one.
            lid = Crown(a);
            if (lid > 0)
            {
                lid = CrownFace(a, lid);
                plain = TopColour(a, lid);
            }
        }

        var l = a.Y0 + lid;
        var shoulder = a.Y0 + cap;
        var drop = lid > 0 ? Valley(a, false, body) : rise;
        // Tiles of depth for each pixel of a row's radius.
        var deep = MathF.Min(1f / PixelsPerTile, RoundMax / (body.X1 - body.X0));
        var quads = new List<FoldQuad>();

        float X(float c) => (c - origin.X) / PixelsPerTile;
        float Z(float r) => r >= shoulder ? (a.Y1 - r) / PixelsPerTile
            : (a.Y1 - shoulder + (shoulder - r) * DomeRise) / PixelsPerTile;
        float Edge(int[] rows, int i) => i <= a.X0 ? rows[0]
            : i >= a.X1 ? rows[w - 1]
            : (rows[i - 1 - a.X0] + rows[i - a.X0]) / 2f;
        bool Solid(int c, int row) => row >= l && c >= k0 && c < k1 && a.Opaque(c, row - rise[c - a.X0]);

        // A rib one pixel in at each end between two rows of one width is drawn on the body, not cut into it.
        (int X0, int X1) Width(int row)
        {
            var own = Raw(row);
            if (row <= l || row >= a.Y1 - 1)
                return own;

            var up = Raw(row - 1);
            return up == Raw(row + 1) && own.X0 - up.X0 is 0 or 1 && up.X1 - own.X1 is 0 or 1 ? up : own;
        }

        (int X0, int X1) Raw(int row)
        {
            var c0 = a.X0;
            while (c0 < a.X1 && !Solid(c0, row))
                c0++;

            var c1 = a.X1;
            while (c1 > c0 && !Solid(c1 - 1, row))
                c1--;

            return (c0, c1);
        }

        // Half the depth of a row's circle at a column edge.
        float Half((int X0, int X1) span, float c)
        {
            var r = (span.X1 - span.X0) / 2f;
            var d = c - (span.X0 + span.X1) / 2f;
            return deep * MathF.Sqrt(MathF.Max(0f, r * r - d * d));
        }

        // Each run of rows of one width is a cylinder. Where it is wider than the run above, its top shows as a ledge.
        (int X0, int X1) above = default;
        for (var row = l; row < a.Y1;)
        {
            var span = Width(row);
            var end = row + 1;
            while (end < a.Y1 && Width(end) == span)
                end++;

            // The outline pixels at a run's two ends are not part of its surface. The end columns show the casing
            // just inside them, and each side of the back half carries that casing on round, row by row.
            var inset = span.X1 - span.X0 >= 4 ? 1 : 0;
            var left = span.X0 + inset;
            var right = span.X1 - 1 - inset;
            var middle = (span.X0 + span.X1) / 2;
            float zt = Z(row), zb = Z(end);
            for (var i = span.X0; i < span.X1; i++)
            {
                float xa = X(i), xb = X(i + 1), ha = Half(span, i), hb = Half(span, i + 1);
                float sa = Edge(rise, i), sb = Edge(rise, i + 1);
                var normal = Vector3.Normalize(new Vector3(ha - hb, xa - xb, 0f));
                float ta0 = i, tb0 = i + 1;
                if (i < left)
                    ta0 = tb0 = left + 0.5f;
                else if (i > right)
                    ta0 = tb0 = right + 0.5f;

                Add(quads, new(xa, -ha, zb), new(xb, -hb, zb), new(xb, -hb, zt), new(xa, -ha, zt),
                    new(ta0, end - sa), new(tb0, end - sb), new(tb0, row - sb), new(ta0, row - sa), normal, 1f);

                var casing = i < middle ? left : right;
                var casingTop = new Vector2(casing + 0.5f, row - rise[casing - a.X0]);
                var casingBottom = casingTop + new Vector2(0f, end - row);
                Add(quads, new(xb, hb, zb), new(xa, ha, zb), new(xa, ha, zt), new(xb, hb, zt),
                    casingBottom, casingBottom, casingTop, casingTop,
                    Vector3.Normalize(new Vector3(ha - hb, xb - xa, 0f)), 1f);

                if (row == l && lid > 0 && plain is { } colour)
                {
                    AddFlat(quads, new(xa, -ha, zt), new(xb, -hb, zt), new(xb, hb, zt), new(xa, ha, zt), colour,
                        new(0, 0, 1));
                    continue;
                }

                if (row == l && lid > 0)
                {
                    var ta = a.Y0 + MathF.Min(Edge(drop, i), sa);
                    var tb = a.Y0 + MathF.Min(Edge(drop, i + 1), sb);
                    Add(quads, new(xa, -ha, zt), new(xb, -hb, zt), new(xb, hb, zt), new(xa, ha, zt),
                        new(i, l - sa), new(i + 1, l - sb), new(i + 1, tb), new(i, ta), new(0, 0, 1), 1f);
                    continue;
                }

                float ua = Half(above, i), ub = Half(above, i + 1);
                if (ua >= ha && ub >= hb)
                    continue;

                // The topmost row is the dark outline, so the very top takes the colour of the row inside it.
                var shown = Math.Clamp(i, left, right);
                var face = row == l && row + 1 < a.Y1 ? row + 1 : row;
                var t = new Vector2(shown + 0.5f, face - rise[shown - a.X0] + 0.5f);
                if (ua <= 0f && ub <= 0f)
                {
                    AddFlat(quads, new(xa, -ha, zt), new(xb, -hb, zt), new(xb, hb, zt), new(xa, ha, zt), t, new(0, 0, 1));
                    continue;
                }

                AddFlat(quads, new(xa, -ha, zt), new(xb, -hb, zt), new(xb, -ub, zt), new(xa, -ua, zt), t, new(0, 0, 1));
                AddFlat(quads, new(xa, ua, zt), new(xb, ub, zt), new(xb, hb, zt), new(xa, ha, zt), t, new(0, 0, 1));
            }

            above = span;
            row = end;
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// Whether the bottom outline bows like the front of a round footprint: level across the middle and rising at both
    /// ends along the arc a circle as wide as the body would draw. A square step up at the ends is not an arc. The
    /// body leaves out thin parts that stick out at the sides. A bottom with a gap in it, like one on wheels or legs,
    /// is not round.
    /// </summary>
    /// <param name="trim">Also trim end columns that stop well above the floor, like a handle.</param>
    private static bool Round(SpriteAnalysis a, out int[] rise, out (int X0, int X1) body, out bool trimmed,
        bool trim = false)
    {
        trimmed = false;
        var need = (a.Y1 - a.Y0) * RoundBody;
        var x0 = a.X0;
        while (x0 < a.X1 && Column(a, x0) < need)
            x0++;

        var x1 = a.X1;
        while (x1 > x0 && Column(a, x1 - 1) < need)
            x1--;

        // A part on one side that ends well above the floor, like a handle, is not the body's end.
        for (var i = 0; trim && i < RoundTrim && x1 - x0 >= RoundMinWidth; i++)
        {
            var bottom = Valley(a, true, (x0, x1));
            int f = x0 - a.X0, l = x1 - a.X0 - 1;
            if (bottom[f] - bottom[f + 1] > RoundStep)
                x0++;
            else if (bottom[l] - bottom[l - 1] > RoundStep)
                x1--;
            else
                break;

            trimmed = true;
        }

        body = (x0, x1);
        var w = x1 - x0;
        rise = Array.Empty<int>();
        if (w < RoundMinWidth)
            return false;

        rise = Valley(a, true, body);
        var first = x0 - a.X0;
        for (var c = x0; c < x1; c++)
        {
            if (!a.Opaque(c, a.Y1 - 1 - rise[c - a.X0]))
                return false;
        }

        var edge = Math.Max(RoundEdge, (int) MathF.Round(w * RoundEdgeShare));
        if (rise[first] < edge || rise[first + w - 1] < edge)
            return false;

        // The arc is sized to pass through the two end columns.
        float Arc(int c)
        {
            var u = (c + 0.5f - w / 2f) / (w / 2f);
            return 1f - MathF.Sqrt(1f - u * u);
        }

        var sag = (rise[first] + rise[first + w - 1]) / 2f / Arc(0);
        if (sag > RoundSag * w)
            return false;

        var level = false;
        for (var c = 0; c < w; c++)
        {
            var rows = rise[first + c];
            level |= rows == 0;
            if (Math.Abs(rows - rise[first + w - 1 - c]) > 1 || MathF.Abs(rows - sag * Arc(c)) > RoundSlack)
                return false;
        }

        return level;
    }

    /// <summary>Opaque pixels in a column.</summary>
    private static int Column(SpriteAnalysis a, int c)
    {
        var count = 0;
        for (var r = a.Y0; r < a.Y1; r++)
        {
            if (a.Opaque(c, r))
                count++;
        }

        return count;
    }

    /// <summary>
    /// Rows each column's bottom pixel sits above the lowest row, or its top pixel below the highest, kept only where
    /// that grows from the middle of the body out to both ends. Columns outside the body take its end values.
    /// </summary>
    private static int[] Valley(SpriteAnalysis a, bool bottom, (int X0, int X1) body)
    {
        var rows = new int[a.X1 - a.X0];
        var first = body.X0 - a.X0;
        var last = body.X1 - a.X0 - 1;
        for (var c = first; c <= last; c++)
        {
            rows[c] = a.Height;
            for (var i = 0; i < a.Y1 - a.Y0; i++)
            {
                if (!a.Opaque(a.X0 + c, bottom ? a.Y1 - 1 - i : a.Y0 + i))
                    continue;

                rows[c] = i;
                break;
            }
        }

        var fromRight = new int[rows.Length];
        var least = a.Height;
        for (var c = last; c >= first; c--)
        {
            least = Math.Min(least, rows[c]);
            fromRight[c] = least;
        }

        least = a.Height;
        for (var c = first; c <= last; c++)
        {
            least = Math.Min(least, rows[c]);
            rows[c] = Math.Max(least, fromRight[c]);
        }

        for (var c = 0; c < rows.Length; c++)
        {
            if (c < first)
                rows[c] = rows[first];
            else if (c > last)
                rows[c] = rows[last];
        }

        return rows;
    }

    /// <summary>
    /// A kept solid under a new look that reaches past it, like a crate with its lid opened or a closet with its door
    /// open. The rows the new look adds above the kept top stand as a thin panel on the solid's back edge. Columns it
    /// adds beside the solid are a door hinged at that front corner.
    /// </summary>
    public static FoldMesh Grow(FoldMesh kept, SpriteAnalysis was, SpriteAnalysis now, Vector2 origin)
    {
        var quads = new List<FoldQuad>(kept.Quads);
        float front = float.MaxValue, back = float.MinValue;
        foreach (var q in kept.Quads)
        {
            front = MathF.Min(front, MathF.Min(MathF.Min(q.P0.Y, q.P1.Y), MathF.Min(q.P2.Y, q.P3.Y)));
            back = MathF.Max(back, MathF.Max(MathF.Max(q.P0.Y, q.P1.Y), MathF.Max(q.P2.Y, q.P3.Y)));
        }

        var lid = Lid(quads, was, now, origin, kept.Top, back);
        var left = Door(quads, was, now, origin, front, kept.Top, now.X0, was.X0);
        var right = Door(quads, was, now, origin, front, kept.Top, was.X1, now.X1);
        return lid || left || right ? new FoldMesh(quads.ToArray(), kept.Fixed) : kept;
    }

    /// <summary>Least height of a door, as a share of the kept solid's.</summary>
    private const float DoorMinHeight = 0.5f;

    /// <summary>
    /// Adds the door the new look draws in the given columns beside the kept solid, when they are at least
    /// <see cref="StepWidth"/> wide and half its height. The door is as long as the solid is wide and no taller,
    /// hinged at the front corner and swung forward far enough to cover the drawn width.
    /// </summary>
    private static bool Door(List<FoldQuad> quads, SpriteAnalysis was, SpriteAnalysis now, Vector2 origin,
        float front, float tall, int c0, int c1)
    {
        if (c1 - c0 < StepWidth)
            return false;

        int r0 = now.Y1, r1 = now.Y0;
        for (var r = now.Y0; r < now.Y1; r++)
        {
            for (var c = c0; c < c1; c++)
            {
                if (!now.Opaque(c, r))
                    continue;

                r0 = Math.Min(r0, r);
                r1 = Math.Max(r1, r + 1);
            }
        }

        if (r1 - r0 < (was.Y1 - was.Y0) * DoorMinHeight)
            return false;

        // The hinge is on the solid's side of the drawn door, the free end on the far side.
        var hingeLeft = c0 >= was.X0;
        float length = was.X1 - was.X0, drawn = c1 - c0;
        var forward = MathF.Sqrt(MathF.Max(0f, length * length - drawn * drawn)) / PixelsPerTile;
        float hinge = ((hingeLeft ? c0 : c1) - origin.X) / PixelsPerTile;
        float free = ((hingeLeft ? c1 : c0) - origin.X) / PixelsPerTile;
        var y = front - forward;
        var top = MathF.Min((r1 - r0) / PixelsPerTile, tall);
        float xl = MathF.Min(hinge, free), xr = MathF.Max(hinge, free);
        float yl = hingeLeft ? front : y, yr = hingeLeft ? y : front;
        var normal = Vector3.Normalize(new Vector3(yr - yl, xl - xr, 0f));
        Add(quads, new(xl, yl, 0f), new(xr, yr, 0f), new(xr, yr, top), new(xl, yl, top),
            new(c0, r1), new(c1, r1), new(c1, r0), new(c0, r0), normal, 1f);
        Add(quads, new(xr, yr, 0f), new(xl, yl, 0f), new(xl, yl, top), new(xr, yr, top),
            new(c1, r1), new(c0, r1), new(c0, r0), new(c1, r0), -normal, BackShade);
        return true;
    }

    /// <summary>Adds the rows the new look draws above the kept top as a thin panel on the solid's back edge.</summary>
    private static bool Lid(List<FoldQuad> quads, SpriteAnalysis was, SpriteAnalysis now, Vector2 origin,
        float zb, float back)
    {
        var rise = was.Y0 - now.Y0;
        if (rise < GrowMin)
            return false;

        var c0 = now.X1;
        var c1 = now.X0;
        for (var r = now.Y0; r < was.Y0; r++)
        {
            for (var c = now.X0; c < now.X1; c++)
            {
                if (!now.Opaque(c, r))
                    continue;

                c0 = Math.Min(c0, c);
                c1 = Math.Max(c1, c + 1);
            }
        }

        if (c1 <= c0)
            return false;

        float X(float c) => (c - origin.X) / PixelsPerTile;
        var zt = zb + rise / PixelsPerTile;
        var yf = back - GrowDepth;
        float xl = X(c0), xr = X(c1);
        Add(quads, new(xl, yf, zb), new(xr, yf, zb), new(xr, yf, zt), new(xl, yf, zt),
            new(c0, was.Y0), new(c1, was.Y0), new(c1, now.Y0), new(c0, now.Y0), new(0, -1, 0), 1f);
        Add(quads, new(xr, back, zb), new(xl, back, zb), new(xl, back, zt), new(xr, back, zt),
            new(c1, was.Y0), new(c0, was.Y0), new(c0, now.Y0), new(c1, now.Y0), new(0, 1, 0), BackShade);
        return true;
    }

    /// <summary>
    /// The top row of the sprite's foot: the bottom rows that share one span inset from the row above. Without one,
    /// the bottom quarter below the lid.
    /// </summary>
    private static int Foot(SpriteAnalysis a, int l)
    {
        var bottom = Span(a, a.Y1 - 1);
        var r = a.Y1 - 1;
        while (r > l && Span(a, r - 1) == bottom)
            r--;

        var above = Span(a, r - 1);
        if (a.Y1 - r >= 2 && r > l && above.Item1 < bottom.Item1 && above.Item2 > bottom.Item2)
            return r;

        return a.Y1 - (a.Y1 - l) / 4;
    }

    /// <summary>
    /// Whether the image turned a quarter is the south image rotated a quarter either way. Such art is drawn from
    /// above, since a sprite drawn from the front shows a different side when it turns.
    /// </summary>
    public static bool DrawnFromAbove(SpriteAnalysis south, SpriteAnalysis turned)
    {
        if (south.Empty || turned.Empty || south.Width != south.Height)
            return false;

        var n = south.Width;
        int both0 = 0, any0 = 0, both1 = 0, any1 = 0;
        for (var r = 0; r < n; r++)
        {
            for (var c = 0; c < n; c++)
            {
                var t = turned.Opaque(c, r);
                var a = south.Opaque(n - 1 - r, c);
                var b = south.Opaque(r, n - 1 - c);
                both0 += t && a ? 1 : 0;
                any0 += t || a ? 1 : 0;
                both1 += t && b ? 1 : 0;
                any1 += t || b ? 1 : 0;
            }
        }

        if (both0 >= TurnMatch * any0 || both1 >= TurnMatch * any1)
            return true;

        // Art redrawn for each turn still swaps its width and height when it is seen from above: a long bench is wide
        // facing south and just as tall facing east. A sprite seen from the front keeps its height when it turns.
        int sw = south.X1 - south.X0, sh = south.Y1 - south.Y0, tw = turned.X1 - turned.X0, th = turned.Y1 - turned.Y0;
        return sw >= sh && th >= tw && th - sh >= SwapMin && sw - tw >= SwapMin && Math.Abs(th - sw) <= SwapSlack;
    }

    /// <summary>Pixels a turned image's height must gain and its width lose for its outline to count as swapped.</summary>
    private const int SwapMin = 4;

    /// <summary>Pixels the long side may change by between the south and east images of art seen from above.</summary>
    private const int SwapSlack = 1;

    /// <summary>
    /// A console's lid is only its cap: the rows above the last sharp edge before the lid line, so a screen between
    /// that edge and the lid line stays upright.
    /// </summary>
    private static int CapLine(SpriteAnalysis a, int lid)
    {
        for (var r = a.Y0 + lid - 1; r >= a.Y0 + CapMin; r--)
        {
            if (RowShare(a, r) >= LidShare)
                return r - a.Y0;
        }

        return lid;
    }

    /// <summary>The keyboard rows between the lid and the foot, or null when too few are left.</summary>
    private static (int Top, int Bottom)? Keyboard((int Top, int Bottom)? keyboard, int l, int foot)
    {
        if (keyboard is not { } k)
            return null;

        var top = Math.Max(k.Top, l + 1);
        var bottom = Math.Min(k.Bottom, foot);
        return bottom - top >= ShelfRows ? (top, bottom) : null;
    }

    /// <summary>
    /// The tallest band of rows between the lid and the foot that is at least <see cref="ShelfLift"/> lighter than the
    /// face's median row, at least <see cref="ShelfRows"/> tall and below the first two rows. Rows that are mostly
    /// colour never count. In this art a light band facing the viewer is a surface facing up.
    /// </summary>
    private static (int Top, int Bottom)? Shelf(SpriteAnalysis a, int l, int foot)
    {
        var count = foot - l;
        if (count < ShelfRows + 2)
            return null;

        var lum = new float[count];
        var sorted = new float[count];
        for (var i = 0; i < count; i++)
        {
            var r = l + i;
            var sum = 0f;
            var n = 0;
            var opaque = 0;
            for (var c = a.X0; c < a.X1; c++)
            {
                if (!a.Opaque(c, r))
                    continue;

                opaque++;
                if (a.Chroma[r * a.Width + c] > ShelfChroma)
                    continue;

                sum += a.Lum[r * a.Width + c];
                n++;
            }

            // A row that is mostly colour is a screen or a light.
            lum[i] = n == 0 || n * 2 < opaque ? 0f : sum / n;
            sorted[i] = lum[i];
        }

        Array.Sort(sorted);
        var lit = sorted[count / 2] + ShelfLift;
        (int Top, int Bottom)? best = null;
        var start = -1;
        for (var i = 2; i <= count; i++)
        {
            var light = i < count && lum[i] > lit;
            if (light && start < 0)
                start = i;

            if (light || start < 0)
                continue;

            if (i - start >= ShelfRows && (best == null || i - start > best.Value.Bottom - best.Value.Top))
                best = (l + start, l + i);

            start = -1;
        }

        return best;
    }

    /// <summary>
    /// A seat read from its front image. From the bottom up: legs, the rows narrower than the seat, then the seat's
    /// front edge up to the first sharp edge, then the backrest. The seat lies flat at the top of its front edge and
    /// the backrest stands at its back, so each part keeps its own rows of the image.
    /// </summary>
    private static FoldMesh BuildSeat(SpriteAnalysis a, Vector2 origin)
    {
        var quads = new List<FoldQuad>();
        var w = a.X1 - a.X0;
        float X(float c) => (c - origin.X) / PixelsPerTile;
        float Z(float r) => (a.Y1 - r) / PixelsPerTile;

        var legTop = a.Y1;
        while (legTop > a.Y0 && a.Count[legTop - 1] < w * SeatFill)
            legTop--;

        // A narrow waist above the seat, like a bench's posts, splits it from the backrest above. Without one, the
        // seat's front edge runs up from the legs to the first sharp edge, within a few rows.
        // A single narrow row is a slot in the art, not a waist. The topmost waist is taken, since a gap lower down is
        // under the seat.
        var waist = a.Y0;
        for (var r = a.Y0 + 1; r < legTop - 1; r++)
        {
            if (a.Count[r] >= w * SeatFill || a.Count[r + 1] >= w * SeatFill)
                continue;

            waist = r + 1;
            while (waist + 1 < legTop && a.Count[waist + 1] < w * SeatFill)
                waist++;

            break;
        }

        var seatTop = Math.Max(a.Y0, legTop - SeatEdgeMax);
        if (waist > a.Y0 + SeatEdgeMin && waist < legTop - SeatEdgeMin)
        {
            seatTop = waist + 1;
        }
        else if (legTop == a.Y1)
        {
            // A seat solid to the floor, like a booth, has its seat in the bottom half, from the first row there that
            // changes shade right across, or else from the middle.
            seatTop = (a.Y0 + a.Y1) / 2;
            for (var r = seatTop; r < a.Y1 - SeatEdgeMin; r++)
            {
                if (ShadeShare(a, r) >= SeatSplit)
                {
                    seatTop = r;
                    break;
                }
            }
        }
        else
        {
            for (var r = legTop - SeatEdgeMin; r > legTop - SeatEdgeMax && r > a.Y0; r--)
            {
                if (RowShare(a, r) >= LidShare)
                {
                    seatTop = r;
                    break;
                }
            }
        }

        var depth = Math.Clamp(w / PixelsPerTile * SeatDepth, SeatMinDepth, SeatMaxDepth);
        float yf = -depth / 2f, yb = depth / 2f, yr = yb - SeatBack;
        var back = seatTop > a.Y0;

        // Legs at the front and the back, each a post as thick as the backrest.
        if (legTop < a.Y1)
        {
            var c = a.X0;
            while (c < a.X1)
            {
                if (!a.Opaque(c, a.Y1 - 1))
                {
                    c++;
                    continue;
                }

                var c0 = c;
                while (c < a.X1 && a.Opaque(c, a.Y1 - 1))
                    c++;

                AddBox(quads, X(c0), X(c), yf, yf + SeatBack, Z(a.Y1), Z(legTop), c0, c, legTop, a.Y1);
                AddBox(quads, X(c0), X(c), yb - SeatBack, yb, Z(a.Y1), Z(legTop), c0, c, legTop, a.Y1);
            }
        }

        // A seat drawn with its cushion seen from above, down to a dark outline row or else a line that changes shade
        // right across, lies that part flat as its top. The rows below are its front.
        var front = seatTop;
        for (var r = seatTop + SeatEdgeMin; r <= legTop - SeatEdgeMin; r++)
        {
            if (DarkShare(a, r) >= SeatOutline)
            {
                front = r;
                break;
            }
        }

        for (var r = seatTop + SeatEdgeMin; front == seatTop && r <= legTop - SeatEdgeMin; r++)
        {
            if (ShadeShare(a, r) >= SeatSplit)
                front = r;
        }

        // A bench drawn from above has a bright top over a darker front and no backrest. The bright rows lie flat as
        // its seat.
        if (back && legTop < a.Y1 && waist == a.Y0 && ShadeLine(a) is { } shade
            && shade > SeatEdgeMin && a.Y0 + shade < legTop - SeatEdgeMin)
        {
            seatTop = a.Y0;
            front = a.Y0 + shade;
            back = false;
        }

        // The seat: a block from the legs to its top, its front showing the front edge rows.
        var (s0, s1) = Span(a, Math.Min(legTop, a.Y1 - 1));
        if (s1 <= s0)
            (s0, s1) = (a.X0, a.X1);

        var seatBack = back ? yr : yb;
        AddBox(quads, X(s0), X(s1), yf, seatBack, Z(legTop), Z(front), s0, s1, front, legTop, front == seatTop);
        if (front > seatTop)
        {
            Add(quads, new(X(s0), yf, Z(front)), new(X(s1), yf, Z(front)), new(X(s1), seatBack, Z(front)),
                new(X(s0), seatBack, Z(front)), new(s0, front), new(s1, front), new(s1, seatTop), new(s0, seatTop),
                new(0, 0, 1), 1f);
        }

        // The backrest stands on the back of the seat.
        if (back)
        {
            var (b0, b1) = Span(a, seatTop - 1);
            if (b1 > b0)
                AddBox(quads, X(b0), X(b1), yr, yb, Z(front), Z(front) + (seatTop - a.Y0) / PixelsPerTile, b0, b1,
                    a.Y0, seatTop);
        }

        return new FoldMesh(quads.ToArray(), true);
    }

    /// <summary>
    /// A box whose front and back show a patch of the image, its top and bottom the patch's top row and its sides the
    /// patch's edge columns.
    /// </summary>
    /// <param name="top">Whether to close the box with a top in its first row's colours.</param>
    private static void AddBox(List<FoldQuad> quads, float x0, float x1, float y0, float y1, float z0, float z1,
        int c0, int c1, int r0, int r1, bool top = true)
    {
        Add(quads, new(x0, y0, z0), new(x1, y0, z0), new(x1, y0, z1), new(x0, y0, z1),
            new(c0, r1), new(c1, r1), new(c1, r0), new(c0, r0), new(0, -1, 0), 1f);
        Add(quads, new(x1, y1, z0), new(x0, y1, z0), new(x0, y1, z1), new(x1, y1, z1),
            new(c1, r1), new(c0, r1), new(c0, r0), new(c1, r0), new(0, 1, 0), BackShade);
        Add(quads, new(x0, y1, z0), new(x0, y0, z0), new(x0, y0, z1), new(x0, y1, z1),
            new(c0 + 0.5f, r1), new(c0 + 0.5f, r1), new(c0 + 0.5f, r0), new(c0 + 0.5f, r0), new(-1, 0, 0), 1f);
        Add(quads, new(x1, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x1, y0, z1),
            new(c1 - 0.5f, r1), new(c1 - 0.5f, r1), new(c1 - 0.5f, r0), new(c1 - 0.5f, r0), new(1, 0, 0), 1f);
        if (top)
        {
            Add(quads, new(x0, y0, z1), new(x1, y0, z1), new(x1, y1, z1), new(x0, y1, z1),
                new(c0, r0 + 0.5f), new(c1, r0 + 0.5f), new(c1, r0 + 0.5f), new(c0, r0 + 0.5f), new(0, 0, 1), 1f);
        }
    }

    /// <summary>Share of a row that must change shade from the row above to count as a shade line.</summary>
    private const float SeatSplit = 0.5f;

    /// <summary>Least change in luminance between a pixel and the one above that counts as a change of shade.</summary>
    private const float SeatShade = 0.06f;

    /// <summary>Share of a row that must be dark outline for it to be the line under a seat's cushion.</summary>
    private const float SeatOutline = 0.75f;

    /// <summary>Brightest a pixel may be to count as dark outline, as luminance.</summary>
    private const float OutlineDark = 0.2f;

    /// <summary>Share of a row's opaque pixels that are dark outline.</summary>
    private static float DarkShare(SpriteAnalysis a, int r)
    {
        var dark = 0;
        var all = 0;
        for (var c = a.X0; c < a.X1; c++)
        {
            if (!a.Opaque(c, r))
                continue;

            all++;
            if (a.Lum[r * a.Width + c] < OutlineDark)
                dark++;
        }

        return all == 0 ? 0f : (float) dark / all;
    }

    /// <summary>Share of a row's pixels, among those with a pixel above, whose shade differs from the one above.</summary>
    /// <param name="darker">Count only pixels darker than the one above.</param>
    private static float ShadeShare(SpriteAnalysis a, int r, bool darker = false)
    {
        var both = 0;
        var changed = 0;
        for (var c = a.X0; c < a.X1; c++)
        {
            if (!a.Opaque(c, r) || !a.Opaque(c, r - 1))
                continue;

            both++;
            var change = a.Lum[r * a.Width + c] - a.Lum[(r - 1) * a.Width + c];
            if (darker ? -change >= SeatShade : MathF.Abs(change) >= SeatShade)
                changed++;
        }

        return both == 0 ? 0f : (float) changed / both;
    }

    /// <summary>Share of the seat's width a row must fill to be part of the seat rather than its legs.</summary>
    private const float SeatFill = 0.6f;

    /// <summary>Fewest and most rows the seat's front edge spans.</summary>
    private const int SeatEdgeMin = 2;
    private const int SeatEdgeMax = 8;

    /// <summary>Depth of a seat as a share of its width, and its bounds, in tiles.</summary>
    private const float SeatDepth = 0.8f;
    private const float SeatMinDepth = 0.35f;
    private const float SeatMaxDepth = 0.7f;

    /// <summary>Thickness of a backrest and of a leg, in tiles.</summary>
    private const float SeatBack = 3f / PixelsPerTile;

    /// <summary>The first opaque column of a row and the column after its last.</summary>
    public static (int, int) Span(SpriteAnalysis a, int r)
    {
        var c0 = a.X0;
        while (c0 < a.X1 && !a.Opaque(c0, r))
            c0++;

        var c1 = a.X1;
        while (c1 > c0 && !a.Opaque(c1 - 1, r))
            c1--;

        return (c0, c1);
    }

    private static bool InFront(SpriteAnalysis a, int l, int c, int r)
    {
        return r >= l && a.Opaque(c, r);
    }

    /// <summary>
    /// Share of a row's pixels, among those with a pixel above, whose brightness jumps by more than
    /// <see cref="LidStep"/> from the one above. With a shift, each column's row is moved up by its shift.
    /// </summary>
    private static float RowShare(SpriteAnalysis a, int r, int[]? shift = null)
    {
        if (r >= a.Y1 || r < 1)
            return 0f;

        var both = 0;
        var sharp = 0;
        for (var c = a.X0; c < a.X1; c++)
        {
            var row = r - (shift?[c - a.X0] ?? 0);
            if (!a.Opaque(c, row) || !a.Opaque(c, row - 1))
                continue;

            both++;
            if (MathF.Abs(a.Lum[row * a.Width + c] - a.Lum[(row - 1) * a.Width + c]) > LidStep)
                sharp++;
        }

        return both == 0 ? 0f : (float) sharp / both;
    }

    private static void AddFlat(List<FoldQuad> quads, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector2 t,
        Vector3 normal)
    {
        Add(quads, p0, p1, p2, p3, t, t, t, t, normal, 1f);
    }

    /// <summary>Adds a quad that faces no grid side. Shared with the other builders.</summary>
    public static void Add(List<FoldQuad> quads, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3,
        Vector2 t0, Vector2 t1, Vector2 t2, Vector2 t3, Vector3 normal, float shade)
    {
        quads.Add(new FoldQuad
        {
            P0 = p0, P1 = p1, P2 = p2, P3 = p3,
            T0 = t0, T1 = t1, T2 = t2, T3 = t3,
            Normal = normal,
            Centre = (p0 + p1 + p2 + p3) / 4f,
            Shade = shade,
            Side = Direction.Invalid,
        });
    }
}

