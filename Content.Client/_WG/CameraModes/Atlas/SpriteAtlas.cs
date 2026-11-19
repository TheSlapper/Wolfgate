using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using System.Numerics;

namespace Content.Client._WG.CameraModes.Atlas;

/// <summary>A square cell of the atlas holding one sprite image.</summary>
public sealed class AtlasSlot
{
    /// <summary>Top-left corner of the cell in the atlas, in pixels.</summary>
    public int X;
    public int Y;

    /// <summary>Side of the cell in pixels, a multiple of 16.</summary>
    public int Size;

    /// <summary>Height of the atlas the cell sits in, in pixels. Its width is twice that.</summary>
    public int AtlasSize;

    /// <summary>Whether the image in the cell matches the key it is stored under.</summary>
    public bool Drawn;

    public int LastUsed;
}

/// <summary>
/// One render target holding the sprite images the meshes sample, packed in rows of equal-size square cells.
/// Images are shared by every entity whose sprite looks the same. The right half holds each cell's glow: its unshaded
/// layers alone, which stay bright in the dark.
/// </summary>
public sealed class SpriteAtlas : IDisposable
{
    private const int StartSize = 2048;
    private const int MaxSize = 4096;
    private const int Step = 16;
    private const int StaleFrames = 300;

    /// <summary>Height of the target in pixels.</summary>
    public int Size { get; private set; } = StartSize;

    /// <summary>Width of the target: the images, then their glow.</summary>
    public int Width => Size * 2;

    public IRenderTexture Target { get; private set; }

    private readonly IClyde _clyde;
    private readonly Dictionary<ulong, AtlasSlot> _slots = new();
    private readonly Dictionary<int, Stack<AtlasSlot>> _free = new();
    private readonly List<(int Y, int CellSize, int Next)> _shelves = new();
    private readonly List<ulong> _stale = new();
    private int _top;
    private bool _grow;

    /// <summary>Cells holding an image.</summary>
    public int Count => _slots.Count;

    public SpriteAtlas(IClyde clyde)
    {
        _clyde = clyde;
        Target = Create(Size);
    }

    private IRenderTexture Create(int size)
    {
        return _clyde.CreateRenderTarget(
            (size * 2, size),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = false },
            nameof(SpriteAtlas));
    }

    /// <summary>The cell for a key when it holds a drawn image, without allocating one.</summary>
    public AtlasSlot? Drawn(ulong key, int frame)
    {
        if (!_slots.TryGetValue(key, out var slot) || !slot.Drawn)
            return null;

        slot.LastUsed = frame;
        return slot;
    }

    /// <summary>
    /// The cell for a key, allocating one when needed. When the atlas is full, cells unused for
    /// <see cref="StaleFrames"/> frames are freed first, then the cell not used this frame that fits best is taken
    /// over. Null when every cell is in use this frame.
    /// </summary>
    public AtlasSlot? Get(ulong key, int size, int frame)
    {
        if (_slots.TryGetValue(key, out var slot))
        {
            slot.LastUsed = frame;
            return slot;
        }

        var cell = (size + Step - 1) / Step * Step;
        if (!TryAllocate(cell, out slot)
            && (Evict(frame, StaleFrames) == 0 || !TryAllocate(cell, out slot))
            && !TryTakeOver(cell, frame, out slot))
        {
            // More images are in view than the atlas holds. It grows once, at the start of the next frame.
            if (Size < MaxSize)
                _grow = true;

            return null;
        }

        slot.Drawn = false;
        slot.LastUsed = frame;
        _slots[key] = slot;
        return slot;
    }

    /// <summary>Starts the frame. After a frame that ran out of room, the atlas grows and every image is redrawn.</summary>
    public void BeginFrame()
    {
        if (!_grow)
            return;

        _grow = false;
        Target.Dispose();
        Size = MaxSize;
        Target = Create(Size);
        _slots.Clear();
        _free.Clear();
        _shelves.Clear();
        _top = 0;
    }

    /// <summary>Texture coordinates of a pixel of a cell. Render targets store their rows bottom up.</summary>
    public static Vector2 Uv(AtlasSlot slot, Vector2 pixel)
    {
        return new Vector2((slot.X + pixel.X) / (slot.AtlasSize * 2f), 1f - (slot.Y + pixel.Y) / slot.AtlasSize);
    }

    /// <summary>
    /// Takes a freed cell of the size, else the next place on a shelf of that size, else starts a new shelf below
    /// the last.
    /// </summary>
    private bool TryAllocate(int cell, out AtlasSlot slot)
    {
        if (_free.TryGetValue(cell, out var free) && free.TryPop(out slot!))
            return true;

        for (var i = 0; i < _shelves.Count; i++)
        {
            var shelf = _shelves[i];
            if (shelf.CellSize != cell || shelf.Next + cell > Size)
                continue;

            slot = new AtlasSlot { X = shelf.Next, Y = shelf.Y, Size = cell, AtlasSize = Size };
            _shelves[i] = (shelf.Y, cell, shelf.Next + cell);
            return true;
        }

        if (_top + cell > Size)
        {
            slot = default!;
            return false;
        }

        slot = new AtlasSlot { X = 0, Y = _top, Size = cell, AtlasSize = Size };
        _shelves.Add((_top, cell, cell));
        _top += cell;
        return true;
    }

    /// <summary>
    /// Takes over a cell no object has used this frame: the smallest that fits, then the longest unused. Free cells
    /// of other sizes count too.
    /// </summary>
    private bool TryTakeOver(int cell, int frame, out AtlasSlot slot)
    {
        AtlasSlot? best = null;
        var bestKey = 0ul;
        foreach (var (key, candidate) in _slots)
        {
            if (candidate.LastUsed >= frame || candidate.Size < cell)
                continue;

            if (best == null || candidate.Size < best.Size
                || candidate.Size == best.Size && candidate.LastUsed < best.LastUsed)
            {
                best = candidate;
                bestKey = key;
            }
        }

        foreach (var (freeSize, stack) in _free)
        {
            if (freeSize < cell || stack.Count == 0 || best != null && freeSize >= best.Size)
                continue;

            slot = stack.Pop();
            return true;
        }

        if (best == null)
        {
            slot = default!;
            return false;
        }

        _slots.Remove(bestKey);
        slot = best;
        return true;
    }

    /// <summary>Frees cells no object has used for more than the given frames. Returns how many were freed.</summary>
    private int Evict(int frame, int age)
    {
        _stale.Clear();
        foreach (var (key, slot) in _slots)
        {
            if (frame - slot.LastUsed > age)
                _stale.Add(key);
        }

        foreach (var key in _stale)
        {
            var slot = _slots[key];
            _slots.Remove(key);
            if (!_free.TryGetValue(slot.Size, out var free))
                _free[slot.Size] = free = new Stack<AtlasSlot>();

            free.Push(slot);
        }

        return _stale.Count;
    }

    public void Dispose()
    {
        Target.Dispose();
    }
}
