using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Map;

namespace Content.Client._WG.CameraModes.UI;

/// <summary>
/// Covers the screen while the mouse turns the first-person view. Mouse motion goes to the view, and every key and
/// button goes on to the game as if pressed over the main viewport. Lookups of the world under the mouse go to the
/// main viewport too, so aiming, swings and drags act at the screen centre.
/// </summary>
public sealed class MouselookCapture : Control, IViewportControl
{
    private const float DotRadius = 1.5f;

    private readonly CameraModesSystem _system;
    private readonly IInputManager _input;

    public MouselookCapture(CameraModesSystem system, IInputManager input)
    {
        _system = system;
        _input = input;
        MouseFilter = MouseFilterMode.Stop;
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);
        _system.TurnLook(args.Relative * UIScale);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled)
            return;

        _input.ViewportKeyEvent(_system.MainViewport(), args);
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);
        if (args.Handled)
            return;

        _input.ViewportKeyEvent(_system.MainViewport(), args);
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        if (_system.MainViewport() is not { } viewport)
            return;

        // Marks the point clicks act on.
        var centre = viewport.GlobalPixelPosition + viewport.PixelSize / 2f - GlobalPixelPosition;
        handle.DrawCircle(centre, DotRadius * UIScale + 1f, Color.Black.WithAlpha(0.6f));
        handle.DrawCircle(centre, DotRadius * UIScale, Color.White.WithAlpha(0.9f));
    }

    public MapCoordinates ScreenToMap(Vector2 coords)
    {
        return _system.MainViewport()?.ScreenToMap(coords) ?? default;
    }

    public MapCoordinates PixelToMap(Vector2 point)
    {
        return _system.MainViewport()?.PixelToMap(point) ?? default;
    }

    public Vector2 WorldToScreen(Vector2 map)
    {
        return _system.MainViewport()?.WorldToScreen(map) ?? default;
    }

    public Matrix3x2 GetWorldToScreenMatrix()
    {
        return _system.MainViewport()?.GetWorldToScreenMatrix() ?? Matrix3x2.Identity;
    }

    public Matrix3x2 GetLocalToScreenMatrix()
    {
        return _system.MainViewport()?.GetLocalToScreenMatrix() ?? Matrix3x2.Identity;
    }
}
