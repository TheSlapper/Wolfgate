using Content.Client.Options.UI;
using Content.Shared._WG.CameraModes;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Configuration;

namespace Content.Client._WG.CameraModes.UI;

/// <summary>One checkbox per view, bound to the single view setting. Ticking one unticks the others.</summary>
public sealed class CameraModeOption : BaseOptionCVar<int>
{
    private readonly (CameraMode Mode, CheckBox Box)[] _boxes;

    /// <param name="boxes">Each view with its checkbox.</param>
    public CameraModeOption(OptionsTabControlRow controller, IConfigurationManager cfg,
        params (CameraMode Mode, CheckBox Box)[] boxes)
        : base(controller, cfg, CameraModeCVars.Mode)
    {
        _boxes = boxes;
        foreach (var (_, box) in boxes)
        {
            var self = box;
            box.OnToggled += args =>
            {
                if (args.Pressed)
                {
                    foreach (var (_, other) in _boxes)
                    {
                        if (other != self)
                            other.Pressed = false;
                    }
                }

                ValueChanged();
            };
        }
    }

    /// <summary>The view ticked in the menu, saved or not.</summary>
    public int Selected => Value;

    protected override int Value
    {
        get
        {
            foreach (var (mode, box) in _boxes)
            {
                if (box.Pressed)
                    return (int) mode;
            }

            return (int) CameraMode.Off;
        }
        set
        {
            foreach (var (mode, box) in _boxes)
            {
                box.Pressed = value == (int) mode;
            }
        }
    }
}
