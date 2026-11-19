using System.Numerics;
using Content.Client.Parallax;
using Content.Shared.Maps;
using Content.Shared.Weather;
using Content.Shared._WG.Caverns;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using Robust.Shared.Map.Components;

namespace Content.Client._WG.CameraModes.Render;

public sealed partial class CameraModeMeshOverlay
{
    /// <summary>Size of the sky panorama: the full turn across, straight down to straight up.</summary>
    private const int SkyWidth = 4096;
    private const int SkyHeight = 2048;

    /// <summary>
    /// The mask's colour for see-through water under the sky: green alone, a hole to the eye and a floor to the
    /// weather.
    /// </summary>
    private static readonly Color WeatherFloor = new(0f, 1f, 0f, 1f);

    /// <summary>The tile mask is drawn at this share of the stock picture's size.</summary>
    private const float MaskScale = 0.25f;

    /// <summary>Shortest time between redraws of a sky that scrolls or has weather, in seconds.</summary>
    private const float SkyRedrawSeconds = 0.05f;

    /// <summary>Size of the panorama a map's own sky is painted into, and the shortest time between its redraws.</summary>
    private const int OwnSkyWidth = 1024;
    private const int OwnSkyHeight = 512;
    private const float OwnSkyRedrawSeconds = 0.1f;

    private IRenderTexture? _ownSkyTarget;
    private float _ownSkyTime = float.MinValue;
    private EntityUid? _ownSkyMap;
    private int _skyKey;
    private float _skyTime;

    private IRenderTexture? _maskTarget;
    private List<Entity<MapGridComponent>> _maskGrids = new();

    /// <summary>
    /// Paints the sky round the first-person eye as a panorama fixed to world bearings: across it the full turn,
    /// down it from straight up to straight down. The map's background layers tile it, without the ones anchored to
    /// the world. A cavern leaves it black.
    /// </summary>
    private IRenderTexture DrawSky(DrawingHandleWorld handle, in OverlayDrawArgs args)
    {
        if (_system.TrySky(args.MapUid, out var look))
            return DrawOwnSky(handle, args.MapUid, look);

        _skyTarget ??= _clyde.CreateRenderTarget((SkyWidth, SkyHeight),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            nameof(CameraModeMeshOverlay));

        var cavern = _entities.HasComponent<WGCavernLayerComponent>(args.MapUid);
        var layers = cavern ? Array.Empty<ParallaxLayerPrepared>() : _parallax.GetParallaxLayers(args.MapId);
        var clear = Color.Black;
        // Panorama pixels to one screen pixel, so the sky shows at the size the flat view draws it.
        var scale = SkyWidth / MathF.Tau / _focal;
        var time = (float) _timing.RealTime.TotalSeconds;

        // The panorama is redrawn only when what it shows changes, and while it scrolls, at most once per
        // SkyRedrawSeconds.
        var moving = false;
        var key = HashCode.Combine(args.MapUid, scale, clear, layers.Length);
        foreach (var layer in layers)
        {
            key = HashCode.Combine(key, layer.Texture, layer.Config.Scale);
            moving |= layer.Config.Tiled && layer.Config.Scrolling != Vector2.Zero;
        }

        if (key == _skyKey && (!moving || time - _skyTime < SkyRedrawSeconds))
            return _skyTarget;

        _skyKey = key;
        _skyTime = time;
        handle.RenderInRenderTarget(_skyTarget, DrawLayers, clear);
        return _skyTarget;

        void DrawLayers()
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(null);
            foreach (var layer in layers)
            {
                var size = layer.Texture.Size * layer.Config.Scale;
                // A layer anchored to the world is ground under the tiles, like a planet's bedrock.
                if (size.X < 1f || size.Y < 1f || layer.Config.Slowness == 0f)
                    continue;

                if (!layer.Config.Tiled)
                {
                    var once = size * scale;
                    handle.DrawTextureRect(layer.Texture,
                        Box2.FromDimensions(new Vector2(SkyWidth - once.X, SkyHeight - once.Y) / 2f, once));
                    continue;
                }

                // A layer that scrolls moves by its share of its own size.
                var world = size / EyeManager.PixelsPerMeter;
                var shift = layer.Config.Scrolling * time / world;
                TileSky(handle, layer.Texture, size * scale, shift, Color.White);
            }
        }
    }

    /// <summary>Paints a map's own sky into its panorama, a few times a second.</summary>
    private IRenderTexture DrawOwnSky(DrawingHandleWorld handle, EntityUid? map, SkyLook look)
    {
        _ownSkyTarget ??= _clyde.CreateRenderTarget((OwnSkyWidth, OwnSkyHeight),
            new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8Srgb),
            new TextureSampleParameters { Filter = true },
            nameof(CameraModeMeshOverlay));

        var time = (float) _timing.RealTime.TotalSeconds;
        if (map == _ownSkyMap && time - _ownSkyTime < OwnSkyRedrawSeconds)
            return _ownSkyTarget;

        _ownSkyMap = map;
        _ownSkyTime = time;
        var size = new Vector2(OwnSkyWidth, OwnSkyHeight);
        _skyShader.SetParameter("SKY_SIZE", size);
        _skyShader.SetParameter("SKY_ZENITH", look.Zenith);
        _skyShader.SetParameter("SKY_HORIZON", look.Horizon);
        _skyShader.SetParameter("SKY_GROUND", look.Ground);
        _skyShader.SetParameter("SKY_SUN", look.Sun);
        _skyShader.SetParameter("SKY_SUN_DIR", look.SunDirection);
        _skyShader.SetParameter("SKY_SUN_SIZE", look.SunSize);
        _skyShader.SetParameter("SKY_SUN_BLUR", look.SunBlur);
        _skyShader.SetParameter("SKY_HOLE", look.Hole ? 1f : 0f);
        _skyShader.SetParameter("SKY_CLOUD", look.Cloud);
        _skyShader.SetParameter("SKY_COVER", look.Cover);
        _skyShader.SetParameter("SKY_CLOUD_SHAPE", look.CloudShape);
        _skyShader.SetParameter("SKY_SHIFT", look.Drift * (float) _timing.CurTime.TotalSeconds);
        _skyShader.SetParameter("SKY_STARS", look.Stars);
        handle.RenderInRenderTarget(_ownSkyTarget, () =>
        {
            handle.SetTransform(Matrix3x2.Identity);
            handle.UseShader(_skyShader);
            handle.DrawRect(Box2.FromDimensions(Vector2.Zero, size), Color.White);
            handle.UseShader(null);
        }, Color.Black);
        return _ownSkyTarget;
    }

    /// <summary>
    /// Tiles an image over the whole panorama. A whole number of images goes round the turn, so the sky has no seam.
    /// </summary>
    /// <param name="shift">How far the images are moved, as a share of one image.</param>
    private static void TileSky(DrawingHandleWorld handle, Texture texture, Vector2 tile, Vector2 shift, Color color)
    {
        var across = MathF.Max(1f, MathF.Round(SkyWidth / MathF.Max(tile.X, 1f)));
        var width = SkyWidth / across;
        var height = width * tile.Y / MathF.Max(tile.X, 1f);
        var x = (shift.X % 1f + 1f) % 1f * width - width;
        var y = (shift.Y % 1f + 1f) % 1f * height - height;
        for (var at = x; at < SkyWidth; at += width)
        {
            for (var down = y; down < SkyHeight; down += height)
            {
                handle.DrawTextureRect(texture, Box2.FromDimensions(new Vector2(at, down), new Vector2(width, height)),
                    color);
            }
        }
    }

    /// <summary>
    /// Marks every tile in the stock picture, at a quarter of its size, except tiles that take the map's atmosphere.
    /// Where the first-person view meets the floor on no tile, or runs out of the picture over no tile, it sees open
    /// space. Over a level below, a see-through tile is a hole too, as CE shows that level through it.
    /// </summary>
    private IRenderTexture DrawMask(DrawingHandleWorld handle, Vector2i size, in OverlayDrawArgs args)
    {
        var maskSize = new Vector2i(Math.Max(1, (int) (size.X * MaskScale)), Math.Max(1, (int) (size.Y * MaskScale)));
        if (_maskTarget == null || _maskTarget.Size != maskSize)
        {
            _maskTarget?.Dispose();
            _maskTarget = _clyde.CreateRenderTarget(maskSize,
                new RenderTargetFormatParameters(RenderTargetColorFormat.Rgba8),
                new TextureSampleParameters { Filter = false },
                nameof(CameraModeMeshOverlay));
        }

        var floored = _system.HasLevelBelow(args.MapUid);
        var toMask = _worldToLocal * Matrix3x2.CreateScale(maskSize.X / _viewSize.X, maskSize.Y / _viewSize.Y);
        var box = args.WorldAABB;
        var mapId = args.MapId;
        handle.RenderInRenderTarget(_maskTarget, () =>
        {
            handle.UseShader(null);
            _maskGrids.Clear();
            _mapSystem.FindGridsIntersecting(mapId, box, ref _maskGrids, true);
            foreach (var grid in _maskGrids)
            {
                handle.SetTransform(_transformSystem.GetWorldMatrix(grid) * toMask);
                var tileSize = grid.Comp.TileSize;
                var tiles = _mapSystem.GetTilesIntersecting(grid, grid, box);
                while (tiles.MoveNext(out var tile))
                {
                    var surface = false;
                    if (_tileDefs[tile.Tile.TypeId] is ContentTileDefinition def)
                    {
                        surface = floored && def.Transparent;
                        if (def.MapAtmosphere || surface && !def.Weather)
                            continue;
                    }

                    var corner = (Vector2) tile.GridIndices * tileSize;
                    handle.DrawRect(Box2.FromDimensions(corner, new Vector2(tileSize)),
                        surface ? WeatherFloor : Color.White);
                }
            }

            handle.SetTransform(Matrix3x2.Identity);
        }, Color.Black);

        return _maskTarget;
    }
}
