using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterVoice.Core;

public enum RecordingShortcutAction
{
    SchedulePushToTalk,
    CancelPendingPushToTalk,
    StartPushToTalk,
    StopPushToTalk,
    ToggleLongForm,
    PromoteToLongForm
}

public enum RecordingModifier
{
    Option,
    Command,
    Control,
    Shift
}

public static class RecordingModifierExtensions
{
    public static string GetName(this RecordingModifier modifier) => modifier switch
    {
        RecordingModifier.Option => "Alt / Option",
        RecordingModifier.Command => "Win / Command",
        RecordingModifier.Control => "Control",
        RecordingModifier.Shift => "Shift",
        _ => modifier.ToString()
    };

    public static string GetSymbol(this RecordingModifier modifier) => modifier switch
    {
        RecordingModifier.Option => "Alt",
        RecordingModifier.Command => "Win",
        RecordingModifier.Control => "Ctrl",
        RecordingModifier.Shift => "Shift",
        _ => modifier.ToString()
    };
}

public enum RecordingLongShortcut
{
    CommandOption,
    CommandShift,
    OptionShift,
    ControlOption
}

public static class RecordingLongShortcutExtensions
{
    public static string GetName(this RecordingLongShortcut shortcut) => shortcut switch
    {
        RecordingLongShortcut.CommandOption => "Win + Alt",
        RecordingLongShortcut.CommandShift => "Win + Shift",
        RecordingLongShortcut.OptionShift => "Alt + Shift",
        RecordingLongShortcut.ControlOption => "Ctrl + Alt",
        _ => shortcut.ToString()
    };

    public static string GetLabel(this RecordingLongShortcut shortcut) => shortcut switch
    {
        RecordingLongShortcut.CommandOption => "Win+Alt",
        RecordingLongShortcut.CommandShift => "Win+Shift",
        RecordingLongShortcut.OptionShift => "Alt+Shift",
        RecordingLongShortcut.ControlOption => "Ctrl+Alt",
        _ => shortcut.ToString()
    };

    public static HashSet<RecordingModifier> Modifiers(this RecordingLongShortcut shortcut) => shortcut switch
    {
        RecordingLongShortcut.CommandOption => [RecordingModifier.Command, RecordingModifier.Option],
        RecordingLongShortcut.CommandShift => [RecordingModifier.Command, RecordingModifier.Shift],
        RecordingLongShortcut.OptionShift => [RecordingModifier.Option, RecordingModifier.Shift],
        RecordingLongShortcut.ControlOption => [RecordingModifier.Control, RecordingModifier.Option],
        _ => []
    };
}

public record struct RecordingShortcutConfiguration(
    RecordingModifier QuickModifier = RecordingModifier.Option,
    RecordingLongShortcut LongShortcut = RecordingLongShortcut.CommandOption)
{
    public static readonly RecordingShortcutConfiguration Standard = new(
        RecordingModifier.Option,
        RecordingLongShortcut.CommandOption);

    public (bool Quick, bool Long, bool Other) ActiveStates(
        bool command,
        bool option,
        bool control,
        bool shift)
    {
        var active = new HashSet<RecordingModifier>();
        if (command) active.Add(RecordingModifier.Command);
        if (option) active.Add(RecordingModifier.Option);
        if (control) active.Add(RecordingModifier.Control);
        if (shift) active.Add(RecordingModifier.Shift);

        var recognized = new HashSet<RecordingModifier>(LongShortcut.Modifiers()) { QuickModifier };

        bool quick = active.Contains(QuickModifier);
        bool longActive = LongShortcut.Modifiers().IsSubsetOf(active);
        bool other = active.Except(recognized).Any();

        return (quick, longActive, other);
    }
}

public struct RecordingShortcutState
{
    private enum Mode
    {
        Idle,
        PendingPushToTalk,
        PushToTalk,
        SuppressUntilOptionRelease
    }

    private Mode _mode = Mode.Idle;

    public RecordingShortcutState()
    {
    }

    public List<RecordingShortcutAction> FlagsChanged(bool command, bool option, bool otherModifier = false) =>
        Process(quickActive: option, longActive: command && option, otherModifier: otherModifier);

    public List<RecordingShortcutAction> FlagsChangedForActive(bool quickActive, bool longActive, bool otherModifier = false) =>
        Process(quickActive: quickActive, longActive: longActive, otherModifier: otherModifier);

    private List<RecordingShortcutAction> Process(bool quickActive, bool longActive, bool otherModifier)
    {
        if (_mode == Mode.PushToTalk && otherModifier)
        {
            if (quickActive) return [];
            _mode = Mode.Idle;
            return [RecordingShortcutAction.StopPushToTalk];
        }

        if (_mode == Mode.PendingPushToTalk && otherModifier)
        {
            _mode = Mode.Idle;
            return [RecordingShortcutAction.CancelPendingPushToTalk];
        }

        if (otherModifier) return [];

        if (longActive)
        {
            switch (_mode)
            {
                case Mode.Idle:
                    _mode = Mode.SuppressUntilOptionRelease;
                    return [RecordingShortcutAction.ToggleLongForm];
                case Mode.PendingPushToTalk:
                    _mode = Mode.SuppressUntilOptionRelease;
                    return [RecordingShortcutAction.CancelPendingPushToTalk, RecordingShortcutAction.ToggleLongForm];
                case Mode.PushToTalk:
                    _mode = Mode.SuppressUntilOptionRelease;
                    return [RecordingShortcutAction.PromoteToLongForm];
                case Mode.SuppressUntilOptionRelease:
                    return [];
            }
        }

        if (!quickActive && _mode == Mode.SuppressUntilOptionRelease)
        {
            _mode = Mode.Idle;
            return [];
        }

        if (quickActive && _mode == Mode.Idle)
        {
            _mode = Mode.PendingPushToTalk;
            return [RecordingShortcutAction.SchedulePushToTalk];
        }

        if (!quickActive && _mode == Mode.PushToTalk)
        {
            _mode = Mode.Idle;
            return [RecordingShortcutAction.StopPushToTalk];
        }

        if (!quickActive && _mode == Mode.PendingPushToTalk)
        {
            _mode = Mode.Idle;
            return [RecordingShortcutAction.CancelPendingPushToTalk];
        }

        return [];
    }

    public List<RecordingShortcutAction> PushToTalkDelayElapsed()
    {
        if (_mode != Mode.PendingPushToTalk) return [];
        _mode = Mode.PushToTalk;
        return [RecordingShortcutAction.StartPushToTalk];
    }
}
