using System.Numerics;
using Content.Shared._WG.CameraModes;

namespace Content.Shared.Movement.Systems;

public abstract partial class SharedMoverController
{
    [Dependency] private CameraModeInputSystem _cameraModeInput = default!;

    /// <summary>Turns a wish direction so the up key walks up the screen in the isometric view.</summary>
    public Vector2 CameraModeWish(EntityUid uid, Vector2 wishDir)
    {
        return wishDir == Vector2.Zero ? wishDir : _cameraModeInput.InputTurn(uid).RotateVec(wishDir);
    }
}
