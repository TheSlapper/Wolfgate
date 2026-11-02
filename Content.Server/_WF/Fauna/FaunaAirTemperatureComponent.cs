namespace Content.Server._WF.Fauna;

/// <summary>
/// Heats or cools the air the living mob stands in, until the air reaches <see cref="Target"/>.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class FaunaAirTemperatureComponent : Component
{
    /// <summary>
    /// Watts. Negative cools.
    /// </summary>
    [DataField]
    public float Power = 25000f;

    /// <summary>
    /// Kelvin.
    /// </summary>
    [DataField]
    public float Target = 473.15f;

    /// <summary>
    /// Above this temperature <see cref="BoostPower"/> replaces <see cref="Power"/>.
    /// </summary>
    [DataField]
    public float? BoostFrom;

    [DataField]
    public float BoostPower;

    [DataField]
    public TimeSpan Interval = TimeSpan.FromSeconds(1);

    [DataField, AutoPausedField]
    public TimeSpan NextTick;
}
