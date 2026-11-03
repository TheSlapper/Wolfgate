using System.Linq;
using Content.Client._WF.Stylesheets;
using Content.Shared._WF.Voice;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._WF.Voice;

/// <summary>Creator card with the choice of voice or message sound, the base voice, its sliders and a Test button.</summary>
public sealed partial class SpeechVoicePicker : PanelContainer
{
    private const int ModeVoice = 0;
    private const int ModeSound = 1;

    /// <summary>Any setting changed. The voice passed is a new copy.</summary>
    public event Action<SpeechVoice>? OnVoiceChanged;

    /// <summary>The Test button was pressed with this text.</summary>
    public event Action<string>? OnTestPressed;

    private SpeechVoice _voice = new();
    private readonly List<Action> _refreshers = new();

    private readonly OptionButton _mode;
    private readonly Label _modeHint;
    private readonly BoxContainer _settings;
    private readonly OptionButton _type;
    private readonly OptionButton _melody;
    private readonly LineEdit _testText;

    public SpeechVoicePicker()
    {
        StyleClasses.Add(StyleWolfgate.StyleClassCreatorCard);
        HorizontalExpand = true;

        var box = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        AddChild(box);

        var modeRow = NewRow("speech-voice-mode");
        _mode = new OptionButton { MinWidth = 220 };
        _mode.AddItem(Loc.GetString("speech-voice-mode-voice"), ModeVoice);
        _mode.AddItem(Loc.GetString("speech-voice-mode-sound"), ModeSound);
        _mode.OnItemSelected += args =>
        {
            _mode.SelectId(args.Id);
            Change(v => v.Enabled = args.Id == ModeVoice);
            UpdateMode();
        };
        modeRow.AddChild(_mode);
        box.AddChild(modeRow);

        _modeHint = new Label { Text = Loc.GetString("speech-voice-mode-sound-hint") };
        box.AddChild(_modeHint);

        _settings = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
        };
        box.AddChild(_settings);

        var typeRow = NewRow("speech-voice-type");
        _type = new OptionButton { MinWidth = 220 };
        foreach (var type in Enum.GetValues<SpeechVoiceType>())
        {
            _type.AddItem(Loc.GetString($"speech-voice-type-{type.ToString().ToLowerInvariant()}"), (int) type);
        }

        _type.OnItemSelected += args =>
        {
            _type.SelectId(args.Id);
            Change(v => v.Type = (SpeechVoiceType) args.Id);
        };
        typeRow.AddChild(_type);
        _settings.AddChild(typeRow);

        _melody = AddChoice("speech-voice-melody",
            Enum.GetValues<SpeechVoiceMelody>().Select(m => ((int) m, m.ToString().ToLowerInvariant())),
            (v, id) => v.Melody = (SpeechVoiceMelody) id);

        AddSlider(_settings, "speech-voice-pitch", SpeechVoice.MinPitch, SpeechVoice.MaxPitch,
            v => v.Pitch, (v, x) => v.Pitch = x, true);
        AddSlider(_settings, "speech-voice-pace", SpeechVoice.MinPace, SpeechVoice.MaxPace,
            v => v.Pace, (v, x) => v.Pace = x, true);
        AddSlider(_settings, "speech-voice-pitch-variance", 0f, SpeechVoice.MaxPitchVariance,
            v => v.PitchVariance, (v, x) => v.PitchVariance = x, true);
        AddSlider(_settings, "speech-voice-intonation", 0f, SpeechVoice.MaxScale,
            v => v.Intonation, (v, x) => v.Intonation = x, true);
        AddSlider(_settings, "speech-voice-size", SpeechVoice.MinSize, SpeechVoice.MaxSize,
            v => v.Size, (v, x) => v.Size = x, true);

        var testRow = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };
        _testText = new LineEdit
        {
            HorizontalExpand = true,
            PlaceHolder = Loc.GetString("speech-voice-test-placeholder"),
        };
        var test = new Button { Text = Loc.GetString("speech-voice-test") };
        test.OnPressed += _ => OnTestPressed?.Invoke(string.IsNullOrWhiteSpace(_testText.Text)
            ? Loc.GetString("speech-voice-test-placeholder")
            : _testText.Text);
        testRow.AddChild(_testText);
        testRow.AddChild(test);
        _settings.AddChild(testRow);
    }

    /// <summary>Shows a profile's voice without raising <see cref="OnVoiceChanged"/>.</summary>
    public void SetVoice(SpeechVoice voice)
    {
        _voice = voice;

        _mode.SelectId(voice.Enabled ? ModeVoice : ModeSound);
        UpdateMode();
        _type.SelectId((int) voice.Type);
        _melody.SelectId((int) voice.Melody);

        foreach (var refresh in _refreshers)
        {
            refresh();
        }
    }

    /// <summary>A character on the message sound has no use for the voice's settings.</summary>
    private void UpdateMode()
    {
        _settings.Visible = _voice.Enabled;
        _modeHint.Visible = !_voice.Enabled;
    }

    private void Change(Action<SpeechVoice> change)
    {
        var voice = _voice.Clone();
        change(voice);
        _voice = voice;
        OnVoiceChanged?.Invoke(voice);
    }

    /// <param name="tooltip">Hover text for the label, or null for none.</param>
    private static BoxContainer NewRow(LocId label, string? tooltip = null)
    {
        var row = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 6,
        };

        var name = new Label
        {
            Text = Loc.GetString(label),
            VAlign = Label.VAlignMode.Center,
            MinWidth = 160,
            StyleClasses = { StyleWolfgate.StyleClassCreatorFieldLabel },
        };

        if (tooltip != null)
        {
            name.ToolTip = tooltip;
            name.MouseFilter = MouseFilterMode.Stop; // labels ignore the mouse by default, so no hover without this
        }

        row.AddChild(name);
        return row;
    }

    /// <summary>A labelled dropdown with hover text from <c>{label}-tooltip</c>.</summary>
    /// <param name="names">Each choice's id and the lower-case name in its Fluent id, <c>{label}-{name}</c>.</param>
    private OptionButton AddChoice(LocId label, IEnumerable<(int Id, string Name)> names, Action<SpeechVoice, int> set)
    {
        var tooltip = Loc.GetString($"{label}-tooltip");
        var row = NewRow(label, tooltip);
        var button = new OptionButton { MinWidth = 220, ToolTip = tooltip };
        foreach (var (id, name) in names)
        {
            button.AddItem(Loc.GetString($"{label}-{name}"), id);
        }

        button.OnItemSelected += args =>
        {
            button.SelectId(args.Id);
            Change(v => set(v, args.Id));
        };

        row.AddChild(button);
        _settings.AddChild(row);
        return button;
    }

    /// <summary>A labelled slider row, with hover text from <c>{label}-tooltip</c> if that string exists.</summary>
    /// <param name="multiplier">Shows the value itself as a percent, so 100% is the voice's own setting.</param>
    private void AddSlider(BoxContainer box,
        LocId label,
        float min,
        float max,
        Func<SpeechVoice, float> get,
        Action<SpeechVoice, float> set,
        bool multiplier = false)
    {
        string? tooltip = Loc.TryGetString($"{label}-tooltip", out var text) ? text : null;
        var row = NewRow(label, tooltip);
        var slider = new Slider { MinValue = min, MaxValue = max, HorizontalExpand = true };
        if (tooltip != null)
            slider.ToolTip = tooltip;
        var value = new Label { MinWidth = 50, Align = Label.AlignMode.Right };
        row.AddChild(slider);
        row.AddChild(value);
        box.AddChild(row);

        void ShowValue()
        {
            var shown = multiplier ? slider.Value : (slider.Value - min) / (max - min);
            value.Text = Loc.GetString("ui-options-value-percent", ("value", shown));
        }

        slider.OnValueChanged += _ =>
        {
            ShowValue();
            Change(v => set(v, slider.Value));
        };

        _refreshers.Add(() =>
        {
            slider.SetValueWithoutEvent(get(_voice));
            ShowValue();
        });
    }
}
