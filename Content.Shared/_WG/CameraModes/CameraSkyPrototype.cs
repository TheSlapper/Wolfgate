using System.Numerics;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._WG.CameraModes;

/// <summary>What a sky shows where its sun stands.</summary>
public enum CameraSkyStar : byte
{
    Sun,
    BlackHole,
}

/// <summary>
/// A sky first person draws over a map: a gradient with a sun or black hole, clouds and stars that follows the map's
/// day and weather, and the far land under it.
/// </summary>
[Prototype("wgCameraSky")]
public sealed partial class CameraSkyPrototype : IPrototype, IInheritingPrototype
{
    /// <summary>The distance from the star at which <see cref="SunSize"/> is the sun's size.</summary>
    public const float SunSizeDistance = 10000f;

    [IdDataField]
    public string ID { get; private set; } = default!;

    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<CameraSkyPrototype>))]
    public string[]? Parents { get; private set; }

    [AbstractDataField]
    public bool Abstract { get; private set; }

    /// <summary>The sky straight up at midday.</summary>
    [DataField]
    public Color ZenithDay = Color.FromHex("#3b6ea8");

    /// <summary>The sky at the horizon at midday, and the haze far land fades into.</summary>
    [DataField]
    public Color HorizonDay = Color.FromHex("#a9c4dc");

    /// <summary>The sky straight up at midnight.</summary>
    [DataField]
    public Color ZenithNight = Color.FromHex("#03050a");

    /// <summary>The sky at the horizon at midnight.</summary>
    [DataField]
    public Color HorizonNight = Color.FromHex("#0a0e18");

    [DataField]
    public CameraSkyStar Star;

    /// <summary>The sun's colour, or the colour of a black hole's rings.</summary>
    [DataField]
    public Color SunColor = Color.FromHex("#fff4d6");

    /// <summary>
    /// Radius of the sun's disc in radians, as seen from <see cref="SunSizeDistance"/>. A nearer world sees it larger.
    /// </summary>
    [DataField]
    public float SunSize = 0.05f;

    /// <summary>How far the sun's disc is blurred into the haze round it: 0 a sharp disc, 1 a glow with no edge.</summary>
    [DataField]
    public float SunBlur = 0.2f;

    [DataField]
    public Color CloudColor = Color.White;

    /// <summary>Share of the sky under cloud in clear weather.</summary>
    [DataField]
    public float CloudCover = 0.3f;

    /// <summary>How fast the clouds move, in cloud patches a second.</summary>
    [DataField]
    public Vector2 CloudDrift = new(0.004f, 0.001f);

    /// <summary>How many cloud patches fit across the sky. Higher is smaller clouds.</summary>
    [DataField]
    public float CloudScale = 0.9f;

    /// <summary>How long a cloud is against its width: 1 round puffs, higher long streaks.</summary>
    [DataField]
    public float CloudStretch = 3.45f;

    /// <summary>Brightness of clouds at midnight against midday.</summary>
    [DataField]
    public float NightCloud = 0.04f;

    /// <summary>How strongly a weather that dims the map's light covers the sky with cloud.</summary>
    [DataField]
    public float Overcast = 2.5f;

    /// <summary>How many stars a clear night shows, from 0 to 1.</summary>
    [DataField]
    public float Stars = 0.5f;

    /// <summary>Whether the land carries on past the ground first person draws, as coarse far shapes.</summary>
    [DataField]
    public bool FarLand = true;

    /// <summary>How far off the far land is all haze, in tiles.</summary>
    [DataField]
    public float FarLandReach = 60f;

    /// <summary>Share of the horizon's colour the haze over the land keeps at midnight.</summary>
    [DataField]
    public float NightHaze = 0.2f;
}
