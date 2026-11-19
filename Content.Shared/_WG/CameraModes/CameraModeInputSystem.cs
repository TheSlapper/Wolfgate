using Content.Shared.Movement.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Shared._WG.CameraModes;

/// <summary>Turns a player's movement input to match the isometric view.</summary>
public sealed partial class CameraModeInputSystem : EntitySystem
{
    [Dependency] private INetConfigurationManager _netConfig = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private ISharedPlayerManager _player = default!;

    /// <summary>How far the isometric view turns from the stock view.</summary>
    public static readonly Angle IsoTurn = Angle.FromDegrees(45);

    /// <summary>The turn to add to the input frame of an entity, or of the player driving it.</summary>
    public Angle InputTurn(EntityUid uid)
    {
        if (TryComp<MovementRelayTargetComponent>(uid, out var relay))
            uid = relay.Source;

        if (!_player.TryGetSessionByEntity(uid, out var session))
            return Angle.Zero;

        bool iso;
        if (_net.IsClient)
        {
            if (session != _player.LocalSession)
                return Angle.Zero;

            iso = _netConfig.GetCVar(CameraModeCVars.IsoInput);
        }
        else
        {
            iso = _netConfig.GetClientCVar(session.Channel, CameraModeCVars.IsoInput);
        }

        return iso ? IsoTurn : Angle.Zero;
    }
}
