using Content.Shared.Movement.Components;
using Content.Shared.Movement.Systems;
using Robust.Shared.Serialization;

namespace Content.Shared._WG.CameraModes;

/// <summary>Turns a player's view and movement frame to where the first-person view looks.</summary>
public sealed partial class CameraLookSystem : EntitySystem
{
    [Dependency] private SharedMoverController _mover = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeAllEvent<CameraLookEvent>(OnLook);
    }

    private void OnLook(CameraLookEvent args, EntitySessionEventArgs session)
    {
        if (session.SenderSession.AttachedEntity is not { } uid || !float.IsFinite((float) args.Rotation.Theta))
            return;

        Look(uid, args.Rotation);
    }

    /// <summary>Sets the turn of an entity's view at once, with no swing.</summary>
    public void Look(Entity<InputMoverComponent?> ent, Angle rotation)
    {
        if (_mover.CameraRotationLocked || !Resolve(ent, ref ent.Comp, false))
            return;

        rotation = rotation.Reduced();
        if (ent.Comp.RelativeRotation.EqualsApprox(rotation) && ent.Comp.TargetRelativeRotation.EqualsApprox(rotation))
            return;

        ent.Comp.RelativeRotation = rotation;
        ent.Comp.TargetRelativeRotation = rotation;
        Dirty(ent);
    }
}

/// <summary>Raised by a client as a predictive event when its first-person view has turned.</summary>
[Serializable, NetSerializable]
public sealed class CameraLookEvent : EntityEventArgs
{
    /// <summary>The view's turn from the grid the player stands on.</summary>
    public Angle Rotation;
}
