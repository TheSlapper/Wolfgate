using System.Numerics;
using Content.Client.GameTicking.Managers;
using Content.Shared._WG.CameraModes;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._WG.CameraModes;

/// <summary>A map's sky as first person draws it this moment.</summary>
public struct SkyLook
{
    public Vector3 Zenith;
    public Vector3 Horizon;

    /// <summary>The haze far land fades into: the horizon's colour, dimmed as the land's light goes.</summary>
    public Vector3 Ground;
    public Vector3 Sun;
    public Vector3 SunDirection;
    public float SunSize;
    public float SunBlur;

    /// <summary>The sun is a black hole: a dark disc in bright rings.</summary>
    public bool Hole;
    public Vector3 Cloud;
    public float Cover;
    public Vector2 Drift;

    /// <summary>Cloud patches across the sky, and how long a cloud is against its width.</summary>
    public Vector2 CloudShape;
    public float Stars;
}

public sealed partial class CameraModesSystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private ClientGameTicker _ticker = default!;
    [Dependency] private MetaDataSystem _metadata = default!;

    /// <summary>How far the far land shows on a map with no sky of its own, in tiles.</summary>
    private const float PlainReach = 60f;

    /// <summary>Share of the ambient light the haze takes on a map with no sky of its own.</summary>
    private const float PlainHaze = 0.6f;

    private static readonly ResPath SkiesFile = new("/Prototypes/_WG/CameraModes/skies.yml");

    private ProtoId<CameraSkyPrototype>? _skyPreview;

    /// <summary>Debug: shows one sky on every map for this client, or goes back to each map's own.</summary>
    public bool PreviewSky(string? sky)
    {
        if (sky != null && !_prototypes.HasIndex<CameraSkyPrototype>(sky))
            return false;

        _skyPreview = sky;
        return true;
    }

    /// <summary>Debug: reads the skies file again on this client, so an edit shows without a restart.</summary>
    public int ReloadSkies()
    {
        var changed = new Dictionary<Type, HashSet<string>>();
        if (!_resourceManager.ContentFileExists(SkiesFile))
            return 0;

        _prototypes.LoadString(_resourceManager.ContentFileReadAllText(SkiesFile), true, changed);
        if (changed.Count > 0)
            _prototypes.ReloadPrototypes(changed);

        var count = 0;
        foreach (var ids in changed.Values)
        {
            count += ids.Count;
        }

        return count;
    }

    /// <summary>The sky a map shows: the one being previewed, or its own.</summary>
    private bool TrySkyPrototype(EntityUid? map, out CameraSkyPrototype sky, out float starDistance)
    {
        sky = default!;
        starDistance = 0f;
        if (!_cfg.GetCVar(CameraModeCVars.Sky))
            return false;

        var own = CompOrNull<CameraSkyComponent>(map);
        starDistance = own?.StarDistance ?? 0f;
        var id = _skyPreview ?? own?.Sky;
        if (id == null || !_prototypes.TryIndex(id.Value, out var found))
            return false;

        sky = found;
        return true;
    }

    /// <summary>
    /// The sky of a map with one, at the map's time of day and under its weather. Weather shows as how far the map's
    /// light is from what its day alone gives.
    /// </summary>
    public bool TrySky(EntityUid? map, out SkyLook look)
    {
        look = default;
        if (map is not { } uid || !TrySkyPrototype(uid, out var sky, out var starDistance))
            return false;

        var day = 1f;
        var phase = 0.5f;
        var tint = Vector3.One;
        if (TryComp(uid, out LightCycleComponent? cycle) && cycle.Enabled)
        {
            var time = (float) (_timing.CurTime + cycle.Offset - _ticker.RoundStartTimeSpan
                                - _metadata.GetPauseTime(uid)).TotalSeconds;
            var length = MathF.Max(1f, (float) cycle.Duration.TotalSeconds);
            phase = time / length - MathF.Floor(time / length);
            var level = (float) SharedLightCycleSystem.CalculateLightLevel(cycle, time);
            day = Math.Clamp((level - cycle.MinLightLevel) / MathF.Max(1f - cycle.MinLightLevel, 0.01f), 0f, 1f);
            if (TryComp(uid, out MapLightComponent? light))
            {
                var clear = SharedLightCycleSystem.GetColor((uid, cycle), cycle.OriginalColor, time);
                var now = light.AmbientLightColor;
                tint = new Vector3(Share(now.R, clear.R), Share(now.G, clear.G), Share(now.B, clear.B));
            }
        }

        var overcast = Math.Clamp((1f - MathF.Min(tint.X, MathF.Min(tint.Y, tint.Z))) * sky.Overcast, 0f, 1f);
        // The sun rises in the east at a quarter of the day and stands highest at half, leaning south.
        var turn = (phase - 0.25f) * MathF.Tau;
        var sun = Vector3.Normalize(new Vector3(MathF.Cos(turn), -0.35f, MathF.Sin(turn) * 0.9f));
        var up = Math.Clamp(sun.Z * 5f + 0.5f, 0f, 1f);
        look.Zenith = Rgb(Color.InterpolateBetween(sky.ZenithNight, sky.ZenithDay, day)) * tint;
        look.Horizon = Rgb(Color.InterpolateBetween(sky.HorizonNight, sky.HorizonDay, day)) * tint;
        look.Ground = look.Horizon * (sky.NightHaze + (1f - sky.NightHaze) * day);
        look.Sun = Rgb(sky.SunColor) * (up * (1f - overcast * 0.9f));
        look.SunDirection = sun;
        look.SunSize = starDistance > 0f ? sky.SunSize * CameraSkyPrototype.SunSizeDistance / starDistance : sky.SunSize;
        look.SunBlur = sky.SunBlur;
        look.Hole = sky.Star == CameraSkyStar.BlackHole;
        look.Cloud = Rgb(sky.CloudColor) * tint * (sky.NightCloud + (1f - sky.NightCloud) * day);
        look.Cover = sky.CloudCover + (1f - sky.CloudCover) * overcast;
        look.Drift = sky.CloudDrift;
        look.CloudShape = new Vector2(sky.CloudScale, MathF.Max(sky.CloudStretch, 0.01f));
        look.Stars = sky.Stars * (1f - day) * (1f - day) * (1f - overcast);
        return true;
    }

    /// <summary>The colour far land fades into on a map: its sky's haze over the land, or a share of its light.</summary>
    public Vector3 HorizonHaze(EntityUid? map)
    {
        if (TrySky(map, out var look))
            return look.Ground;

        return Rgb(TryComp(map, out MapLightComponent? light) ? light.AmbientLightColor : Color.White) * PlainHaze;
    }

    /// <summary>How far off the far land is all haze on a map, in tiles.</summary>
    public float HorizonReach(EntityUid? map)
    {
        return TrySkyPrototype(map, out var sky, out _) ? sky.FarLandReach : PlainReach;
    }

    private static float Share(float now, float clear)
    {
        return Math.Clamp(now / MathF.Max(clear, 1f / 255f), 0f, 1f);
    }

    /// <summary>A colour as the shaders work with it.</summary>
    private static Vector3 Rgb(Color color)
    {
        var linear = Color.FromSrgb(color);
        return new Vector3(linear.R, linear.G, linear.B);
    }
}
