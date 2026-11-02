namespace Content.Server._WF.Fauna;

/// <summary>
/// Makes whatever this projectile hits jitter or slow down.
/// </summary>
[RegisterComponent]
public sealed partial class FaunaProjectileEffectComponent : Component
{
    [DataField]
    public TimeSpan Jitter = TimeSpan.Zero;

    [DataField]
    public TimeSpan Slow = TimeSpan.Zero;
}
