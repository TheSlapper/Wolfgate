using SixLabors.ImageSharp.PixelFormats;

namespace Content.Client._WG.CameraModes.Fold;

/// <summary>Opacity and brightness of each pixel of one sprite image, with its opaque bounds.</summary>
public sealed class SpriteAnalysis
{
    public readonly int Width;
    public readonly int Height;

    /// <summary>Pixels at least half opaque, row by row.</summary>
    public readonly bool[] Mask;

    /// <summary>Brightness of each pixel, 0 to 1.</summary>
    public readonly float[] Lum;

    /// <summary>Colourfulness of each pixel: its brightest channel less its darkest, 0 to 1.</summary>
    public readonly float[] Chroma;

    /// <summary>Opaque pixels in each row.</summary>
    public readonly int[] Count;

    /// <summary>Opaque bounds: columns X0 to X1 and rows Y0 to Y1, each end exclusive.</summary>
    public readonly int X0;
    public readonly int X1;
    public readonly int Y0;
    public readonly int Y1;

    public bool Empty => X1 <= X0;

    /// <summary>Reads premultiplied pixels, top row first.</summary>
    public SpriteAnalysis(int width, int height, ReadOnlySpan<Rgba32> pixels)
    {
        Width = width;
        Height = height;
        Mask = new bool[width * height];
        Lum = new float[width * height];
        Chroma = new float[width * height];
        Count = new int[height];
        X0 = width;
        Y0 = height;

        for (var r = 0; r < height; r++)
        {
            for (var c = 0; c < width; c++)
            {
                var i = r * width + c;
                var p = pixels[i];
                if (p.A < 128)
                    continue;

                Mask[i] = true;
                Lum[i] = (0.2126f * p.R + 0.7152f * p.G + 0.0722f * p.B) / p.A;
                Chroma[i] = (float) (Math.Max(p.R, Math.Max(p.G, p.B)) - Math.Min(p.R, Math.Min(p.G, p.B))) / p.A;
                Count[r]++;
                X0 = Math.Min(X0, c);
                X1 = Math.Max(X1, c + 1);
                Y0 = Math.Min(Y0, r);
                Y1 = Math.Max(Y1, r + 1);
            }
        }
    }

    private SpriteAnalysis(SpriteAnalysis a, int x0, int x1, int y0, int y1)
    {
        Width = a.Width;
        Height = a.Height;
        Mask = new bool[Width * Height];
        Lum = a.Lum;
        Chroma = a.Chroma;
        Count = new int[Height];
        X0 = Width;
        Y0 = Height;
        for (var r = Math.Max(a.Y0, y0); r < Math.Min(a.Y1, y1); r++)
        {
            for (var c = x0; c < x1; c++)
            {
                if (!a.Opaque(c, r))
                    continue;

                Mask[r * Width + c] = true;
                Count[r]++;
                X0 = Math.Min(X0, c);
                X1 = Math.Max(X1, c + 1);
                Y0 = Math.Min(Y0, r);
                Y1 = Math.Max(Y1, r + 1);
            }
        }
    }

    /// <summary>A copy with every row from the given one down left out.</summary>
    public SpriteAnalysis Above(int y1)
    {
        return new SpriteAnalysis(this, 0, Width, 0, y1);
    }

    /// <summary>A copy of the given columns only.</summary>
    public SpriteAnalysis Columns(int x0, int x1)
    {
        return new SpriteAnalysis(this, x0, x1, 0, Height);
    }

    /// <summary>A copy of the given rows only.</summary>
    public SpriteAnalysis Rows(int y0, int y1)
    {
        return new SpriteAnalysis(this, 0, Width, y0, y1);
    }

    /// <summary>Whether the pixel is opaque. False outside the image.</summary>
    public bool Opaque(int c, int r)
    {
        return c >= 0 && c < Width && r >= 0 && r < Height && Mask[r * Width + c];
    }
}
