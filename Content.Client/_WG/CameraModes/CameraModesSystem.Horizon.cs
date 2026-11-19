using System.Numerics;
using Content.Shared._WG.CameraModes;
using Content.Shared.Maps;
using Content.Shared.Parallax.Biomes;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Utility;
using Robust.Shared.ContentPack;
using Robust.Shared.Graphics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Color = Robust.Shared.Maths.Color;
using DrawDepth = Content.Shared.DrawDepth.DrawDepth;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private SharedBiomeSystem _biome = default!;
    [Dependency] private IResourceManager _resourceManager = default!;

    /// <summary>Side of the far land picture in texels, and the tiles one texel covers.</summary>
    private const int HorizonSize = 128;
    private const int HorizonTexel = 1;

    /// <summary>The picture moves its centre once the eye is this many tiles from it, and by steps of this size.</summary>
    private const int HorizonStep = 16;

    /// <summary>Texels of the far land picture worked out each frame.</summary>
    private const int HorizonBudget = 512;

    /// <summary>The tallest thing the far land shows, in tiles. Heights are stored as a share of it.</summary>
    public const float HorizonHigh = BlockHeight;

    /// <summary>Art fainter than this doesn't count towards an image's colour.</summary>
    private const byte SolidAlpha = 32;

    private readonly Rgba32[] _horizonPixels = new Rgba32[HorizonSize * HorizonSize];
    private readonly Rgba32[] _horizonSwap = new Rgba32[HorizonSize * HorizonSize];
    private readonly Rgba32[] _horizonRise = new Rgba32[HorizonSize * HorizonSize];
    private readonly bool[] _horizonDone = new bool[HorizonSize * HorizonSize];
    private readonly bool[] _horizonDoneSwap = new bool[HorizonSize * HorizonSize];
    private int[]? _horizonOrder;
    private OwnedTexture? _horizonTexture;
    private OwnedTexture? _riseTexture;
    private EntityUid _horizonMap;
    private int _horizonSeed;
    private Vector2i _horizonCentre;
    private int _horizonNext;
    private readonly Dictionary<int, Rgba32> _tileColours = new();
    private readonly Dictionary<string, FarThing> _farThings = new();

    /// <summary>What a biome entity adds to the far land: a colour over its tile's floor, or a body standing on it.</summary>
    private record struct FarThing(Rgba32? Cover, Rgba32 Rise);

    /// <summary>Whether the far land picture holds the map the player is on.</summary>
    public bool HorizonOn { get; private set; }

    /// <summary>The far land's ground colours, a texel a tile.</summary>
    public Texture? HorizonTexture => _horizonTexture;

    /// <summary>What stands on the far land: each texel a colour, with its height as a share of the tallest in alpha.</summary>
    public Texture? HorizonRise => _riseTexture;

    /// <summary>The world point at the middle of the far land picture.</summary>
    public Vector2 HorizonCentre => _horizonCentre;

    /// <summary>World tiles the far land picture spans.</summary>
    public float HorizonTiles => HorizonSize * HorizonTexel;

    /// <summary>
    /// Keeps the far land picture round the first-person eye on a planet: the ground's colour and what stands on it,
    /// tile by tile, worked out from the map's biome a share each frame, with no chunk loaded.
    /// </summary>
    private void UpdateHorizon()
    {
        HorizonOn = false;
        if (!FirstPerson || !_cfg.GetCVar(CameraModeCVars.Horizon)
            || !TryComp(_player.LocalEntity, out TransformComponent? xform) || xform.MapUid is not { } map
            || !OpenGround(map) || !TryComp(map, out BiomeComponent? biome)
            || TrySkyPrototype(map, out var sky, out _) && !sky.FarLand)
        {
            DropHorizon();
            return;
        }

        _horizonTexture ??= _clyde.CreateBlankTexture<Rgba32>(new Vector2i(HorizonSize, HorizonSize),
            nameof(CameraModesSystem));
        _riseTexture ??= _clyde.CreateBlankTexture<Rgba32>(new Vector2i(HorizonSize, HorizonSize),
            nameof(CameraModesSystem));
        _horizonOrder ??= HorizonOrder();

        var eye = _transform.GetWorldPosition(xform);
        var changed = false;
        if (map != _horizonMap || biome.Seed != _horizonSeed)
        {
            _horizonMap = map;
            _horizonSeed = biome.Seed;
            _horizonCentre = SnapHorizon(eye);
            Array.Clear(_horizonPixels);
            Array.Clear(_horizonRise);
            Array.Clear(_horizonDone);
            _horizonNext = 0;
            changed = true;
        }
        else if (MathF.Abs(eye.X - _horizonCentre.X) > HorizonStep || MathF.Abs(eye.Y - _horizonCentre.Y) > HorizonStep)
        {
            var centre = SnapHorizon(eye);
            ShiftHorizon((centre - _horizonCentre) / HorizonTexel);
            _horizonCentre = centre;
            _horizonNext = 0;
            changed = true;
        }

        var left = HorizonBudget;
        for (; _horizonNext < _horizonOrder.Length && left > 0; _horizonNext++)
        {
            var index = _horizonOrder[_horizonNext];
            if (_horizonDone[index])
                continue;

            var texel = new Vector2i(index % HorizonSize, index / HorizonSize);
            var tile = _horizonCentre + (texel - new Vector2i(HorizonSize / 2, HorizonSize / 2)) * HorizonTexel
                       + new Vector2i(HorizonTexel / 2, HorizonTexel / 2);
            _horizonPixels[index] = GroundColour(biome, tile, out _horizonRise[index]);
            _horizonDone[index] = true;
            left--;
            changed = true;
        }

        if (changed)
        {
            _horizonTexture.SetSubImage<Rgba32>(Vector2i.Zero, new Vector2i(HorizonSize, HorizonSize), _horizonPixels);
            _riseTexture.SetSubImage<Rgba32>(Vector2i.Zero, new Vector2i(HorizonSize, HorizonSize), _horizonRise);
        }

        HorizonOn = true;
    }

    /// <summary>Frees the far land picture while nothing shows it.</summary>
    private void DropHorizon()
    {
        if (_horizonTexture == null && _riseTexture == null)
            return;

        _horizonTexture?.Dispose();
        _horizonTexture = null;
        _riseTexture?.Dispose();
        _riseTexture = null;
        _horizonMap = EntityUid.Invalid;
    }

    private static Vector2i SnapHorizon(Vector2 eye)
    {
        return new Vector2i((int) MathF.Round(eye.X / HorizonStep), (int) MathF.Round(eye.Y / HorizonStep)) * HorizonStep;
    }

    /// <summary>The picture's texels, nearest its middle first.</summary>
    private static int[] HorizonOrder()
    {
        var order = new int[HorizonSize * HorizonSize];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (a, b) => FromMiddle(a).CompareTo(FromMiddle(b)));
        return order;
    }

    private static int FromMiddle(int index)
    {
        var x = index % HorizonSize - HorizonSize / 2;
        var y = index / HorizonSize - HorizonSize / 2;
        return x * x + y * y;
    }

    /// <summary>Moves the picture's texels as its centre moves, keeping what stays inside it.</summary>
    private void ShiftHorizon(Vector2i by)
    {
        ShiftTexels(_horizonRise, by);
        Array.Clear(_horizonDoneSwap);
        for (var y = 0; y < HorizonSize; y++)
        {
            var fromY = y + by.Y;
            if (fromY < 0 || fromY >= HorizonSize)
                continue;

            for (var x = 0; x < HorizonSize; x++)
            {
                var fromX = x + by.X;
                if (fromX < 0 || fromX >= HorizonSize)
                    continue;

                _horizonDoneSwap[y * HorizonSize + x] = _horizonDone[fromY * HorizonSize + fromX];
            }
        }

        Array.Copy(_horizonDoneSwap, _horizonDone, _horizonDone.Length);
        ShiftTexels(_horizonPixels, by);
    }

    private void ShiftTexels(Rgba32[] texels, Vector2i by)
    {
        Array.Clear(_horizonSwap);
        for (var y = 0; y < HorizonSize; y++)
        {
            var fromY = y + by.Y;
            if (fromY < 0 || fromY >= HorizonSize)
                continue;

            for (var x = 0; x < HorizonSize; x++)
            {
                var fromX = x + by.X;
                if (fromX >= 0 && fromX < HorizonSize)
                    _horizonSwap[y * HorizonSize + x] = texels[fromY * HorizonSize + fromX];
            }
        }

        Array.Copy(_horizonSwap, texels, texels.Length);
    }

    /// <summary>
    /// The colour of the ground a biome puts on a tile: what covers it, like a lake, or else its floor. Clear where the
    /// biome has nothing. Also gives what the biome stands on the tile.
    /// </summary>
    private Rgba32 GroundColour(BiomeComponent biome, Vector2i tile, out Rgba32 rise)
    {
        rise = default;
        if (!_biome.TryGetBiomeTile(tile, biome.Layers, biome.Seed, null, out var floor) || floor.Value.IsEmpty)
            return default;

        if (_biome.TryGetEntity(tile, biome.Layers, floor.Value, biome.Seed, null, out var entity))
        {
            var thing = Far(entity);
            rise = thing.Rise;
            if (thing.Cover is { } covered)
                return covered;
        }

        if (_tileColours.TryGetValue(floor.Value.TypeId, out var colour))
            return colour;

        colour = _tileDefs[floor.Value.TypeId] is ContentTileDefinition { Sprite: { } sprite }
            ? ImageColour(sprite) ?? default
            : default;
        _tileColours[floor.Value.TypeId] = colour;
        return colour;
    }

    /// <summary>
    /// What a biome entity shows as from afar. A wall stands a block high. Art at floor depth colours its tile. The
    /// rest shows nothing.
    /// </summary>
    private FarThing Far(string prototype)
    {
        if (_farThings.TryGetValue(prototype, out var thing))
            return thing;

        thing = default;
        var dummy = Spawn(prototype, MapCoordinates.Nullspace);
        if (TryComp(dummy, out SpriteComponent? sprite))
        {
            foreach (var layer in sprite.AllLayers)
            {
                if (!layer.Visible || layer.ActualRsi is not { } rsi || layer.RsiState.Name is not { } state)
                    continue;

                var height = HasComp<OccluderComponent>(dummy) ? BlockHeight : 0f;
                var cover = height == 0f && sprite.DrawDepth <= (int) DrawDepth.FloorObjects;
                if ((height > 0f || cover) && ImageColour(rsi.Path / $"{state}.png") is { } colour)
                {
                    thing = cover
                        ? new FarThing(colour, default)
                        : new FarThing(null, new Rgba32(colour.R, colour.G, colour.B,
                            (byte) Math.Clamp(height / HorizonHigh * 255f, 1f, 255f)));
                }

                break;
            }
        }

        Del(dummy);
        _farThings[prototype] = thing;
        return thing;
    }

    /// <summary>The average colour of an image's solid pixels.</summary>
    private Rgba32? ImageColour(ResPath path)
    {
        if (!_resourceManager.TryContentFileRead(path, out var stream))
            return null;

        using (stream)
        {
            using var image = Image.Load<Rgba32>(stream);
            long r = 0, g = 0, b = 0, count = 0;
            foreach (var pixel in image.GetPixelSpan())
            {
                if (pixel.A < SolidAlpha)
                    continue;

                r += pixel.R;
                g += pixel.G;
                b += pixel.B;
                count++;
            }

            return count == 0 ? null : new Rgba32((byte) (r / count), (byte) (g / count), (byte) (b / count), 255);
        }
    }
}
