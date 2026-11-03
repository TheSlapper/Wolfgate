using System.IO;
using Content.Shared._WF.Photography;
using Content.Shared.GameTicking;
using Robust.Client.Graphics;
using Robust.Shared.Graphics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace Content.Client._WF.Photography;

/// <summary>Downloads photo images by hash once and keeps them as full-size and thumbnail textures.</summary>
public sealed partial class PhotoImageSystem : EntitySystem
{
    /// <summary>A downloaded photo.</summary>
    public sealed record PhotoTextures(Texture Full, Texture Thumbnail);

    private readonly Dictionary<string, PhotoTextures> _textures = new();
    private readonly HashSet<string> _requested = new();

    /// <summary>Raised when an image arrives, with its hash.</summary>
    public event Action<string>? ImageLoaded;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<PhotoImageEvent>(OnImage);
        SubscribeNetworkEvent<RoundRestartCleanupEvent>(_ => Reset());
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Reset();
    }

    /// <summary>The textures for a hash, asking the server for them the first time.</summary>
    public PhotoTextures? Get(string hash)
    {
        if (hash.Length == 0)
            return null;

        if (_textures.TryGetValue(hash, out var textures))
            return textures;

        if (_requested.Add(hash))
            RaiseNetworkEvent(new PhotoImageRequestEvent(hash));

        return null;
    }

    private void OnImage(PhotoImageEvent ev)
    {
        if (ev.Png == null || _textures.ContainsKey(ev.Hash) || Load(ev.Png) is not { } textures)
            return;

        _textures[ev.Hash] = textures;
        ImageLoaded?.Invoke(ev.Hash);
    }

    private PhotoTextures? Load(byte[] png)
    {
        try
        {
            using var image = Image.Load<Rgba32>(new MemoryStream(png));
            var parameters = TextureLoadParameters.Default with { SampleParameters = TextureSampleParameters.Default };
            var full = Texture.LoadFromImage(image, "photo", parameters);

            var size = PhotoRules.ThumbnailPixels;
            using var small = image.Clone(context => context.Resize(size, size));
            var thumbnail = Texture.LoadFromImage(small, "photo-thumbnail", parameters);
            return new PhotoTextures(full, thumbnail);
        }
        catch (Exception e)
        {
            Log.Warning($"Unreadable photo image: {e.Message}");
            return null;
        }
    }

    private void Reset()
    {
        foreach (var textures in _textures.Values)
        {
            (textures.Full as OwnedTexture)?.Dispose();
            (textures.Thumbnail as OwnedTexture)?.Dispose();
        }

        _textures.Clear();
        _requested.Clear();
    }
}
