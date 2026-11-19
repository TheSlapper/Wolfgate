using System.Numerics;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Robust.Client.GameObjects;

namespace Content.Client._WG.CameraModes;

public sealed partial class CameraModesSystem
{
    [Dependency] private SharedHandsSystem _hands = default!;

    /// <summary>Adds what the player holds in each hand to the first-person scene, as the solid it is on the floor.</summary>
    private void CollectHeld(Angle eyeRot)
    {
        if (!FirstPerson || _player.LocalEntity is not { } player || !TryComp<HandsComponent>(player, out var hands))
            return;

        var (position, rotation) = _transform.GetWorldPositionRotation(player);
        foreach (var hand in _hands.EnumerateHands(player, hands))
        {
            if (hand.HeldEntity is not { } held || hand.Location == HandLocation.Middle
                || !TryComp<SpriteComponent>(held, out var sprite))
            {
                continue;
            }

            if (!_objects.TryGetValue(held, out var obj))
            {
                obj = new SceneObject { Uid = held };
                _objects[held] = obj;
            }

            obj.Kind = SceneKind.Flat;
            obj.Level = 0;
            obj.Hand = hand.Location == HandLocation.Left ? 1 : 2;
            obj.Seen = Frame;
            obj.Draw = false;
            obj.Faded = false;
            obj.Alpha = 1f;
            // The lift marks the active hand, which is held a little higher.
            obj.Lift = hands.ActiveHand == hand ? 1f : 0f;
            obj.Surface = null;
            obj.Low = obj.OnWall = obj.Hung = obj.Console = obj.Turnable = false;
            obj.IsSurface = obj.OnSurface = obj.LieOn = obj.Seat = obj.Cladding = false;
            obj.Offset = Vector2.Zero;
            obj.Neighbours = DirectionFlag.None;
            obj.Position = position;
            obj.Grid = EntityUid.Invalid;
            obj.Tile = Vector2i.Zero;
            SizeSlot(obj, (held, sprite));
            Prepare(obj, (held, sprite), rotation, Angle.Zero, eyeRot);
            Drawn.Add(obj);
        }
    }
}
