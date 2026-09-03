using System;
using System.Collections.Generic;

namespace BetterVoice.Core;

/// <summary>
/// How a recording shortcut starts and stops.
/// </summary>
public enum RecordingTriggerMode
{
    Hold,
    Toggle,
    DoubleTap
}

public static class RecordingTriggerModeExtensions
{
    public static string GetName(this RecordingTriggerMode mode) => mode switch
    {
        RecordingTriggerMode.Hold => "Hold to record",
        RecordingTriggerMode.Toggle => "Press to toggle",
        RecordingTriggerMode.DoubleTap => "Double-tap to toggle",
        _ => mode.ToString()
    };

    public static string GetPickerLabel(this RecordingTriggerMode mode) => mode switch
    {
        RecordingTriggerMode.Hold => "Hold",
        RecordingTriggerMode.Toggle => "Shortcut",
        RecordingTriggerMode.DoubleTap => "Double-tap",
        _ => mode.ToString()
    };

    public static string GetQuickPickerLabel(this RecordingTriggerMode mode) => mode switch
    {
        RecordingTriggerMode.Hold => "Hold",
        RecordingTriggerMode.Toggle => "Tap",
        RecordingTriggerMode.DoubleTap => "Double-tap",
        _ => mode.ToString()
    };

    public static List<RecordingTriggerMode> AvailableModes(bool forQuick, bool modifierOnly)
    {
        if (forQuick)
        {
            return modifierOnly
                ? [RecordingTriggerMode.Hold, RecordingTriggerMode.Toggle, RecordingTriggerMode.DoubleTap]
                : [RecordingTriggerMode.Hold, RecordingTriggerMode.Toggle];
        }

        return modifierOnly
            ? [RecordingTriggerMode.Toggle, RecordingTriggerMode.DoubleTap]
            : [RecordingTriggerMode.Toggle];
    }

    public static string Detail(this RecordingTriggerMode mode, string bindingLabel, int holdDelayMilliseconds)
    {
        switch (mode)
        {
            case RecordingTriggerMode.Hold:
                int delay = QuickNoteHoldDelay.Clamp(holdDelayMilliseconds);
                return $"Hold {bindingLabel} for {delay} ms to record. Release it to finish.";
            case RecordingTriggerMode.Toggle:
                return $"Press {bindingLabel} once to start and again to finish.";
            case RecordingTriggerMode.DoubleTap:
                return $"Double-tap {bindingLabel} to start. Double-tap again to finish.";
            default:
                return string.Empty;
        }
    }
}

public static class QuickNoteHoldDelay
{
    public const int DefaultMilliseconds = 140;
    public const int MinimumMilliseconds = 50;
    public const int MaximumMilliseconds = 500;

    public static int Clamp(int milliseconds) =>
        Math.Min(Math.Max(milliseconds, MinimumMilliseconds), MaximumMilliseconds);
}

public readonly record struct ModifierBindingState
{
    public bool Active { get; }
    public bool Partial { get; }

    public ModifierBindingState(bool active, bool partial)
    {
        Active = active;
        Partial = partial;
    }

    public ModifierBindingState(
        bool bindingCommand,
        bool bindingOption,
        bool bindingControl,
        bool bindingShift,
        bool command,
        bool option,
        bool control,
        bool shift)
    {
        bool bindingMatch = bindingCommand == command &&
                            bindingOption == option &&
                            bindingControl == control &&
                            bindingShift == shift;
        Active = bindingMatch;

        int requiredCount = (bindingCommand ? 1 : 0) + (bindingOption ? 1 : 0) +
                            (bindingControl ? 1 : 0) + (bindingShift ? 1 : 0);

        bool hasRequiredModifier = (bindingCommand && command) || (bindingOption && option) ||
                                  (bindingControl && control) || (bindingShift && shift);

        bool hasUnboundModifier = (!bindingCommand && command) || (!bindingOption && option) ||
                                 (!bindingControl && control) || (!bindingShift && shift);

        Partial = !Active && requiredCount > 1 && hasRequiredModifier && !hasUnboundModifier;
    }
}

public struct ModifierChordEngagement
{
    private bool _reachedFullChord;

    public void Reset()
    {
        _reachedFullChord = false;
    }

    public bool IsPressed(ModifierBindingState state)
    {
        if (state.Active)
        {
            _reachedFullChord = true;
        }
        else if (!state.Partial)
        {
            _reachedFullChord = false;
        }

        return _reachedFullChord && (state.Active || state.Partial);
    }
}

public readonly record struct RecordingModifierSnapshot(bool Command, bool Option, bool Control, bool Shift)
{
    public bool IsEmpty => !Command && !Option && !Control && !Shift;
}

public static class ModifierTapHelper
{
    public static bool ShouldCancelModifierTap(bool bindingEngaged, RecordingModifierSnapshot leftover) =>
        !bindingEngaged && !leftover.IsEmpty;
}

public struct ModifierDoubleTapDetector
{
    public const double MaxTapDuration = 0.25;
    public const double DoubleTapInterval = 0.40;

    private bool _modifierPressed;
    private double? _modifierDownAt;
    private double? _firstTapReleasedAt;
    private bool _comboInterrupted;
    private bool _ignoreRelease;

    public void Reset()
    {
        _modifierPressed = false;
        _modifierDownAt = null;
        _firstTapReleasedAt = null;
        _comboInterrupted = false;
        _ignoreRelease = false;
    }

    public void NonModifierKeyPressed()
    {
        _comboInterrupted = true;
        _firstTapReleasedAt = null;
    }

    public bool ModifierChanged(bool active, double now)
    {
        if (active)
        {
            if (_modifierPressed) return false;

            if (_firstTapReleasedAt is { } armedAt)
            {
                if (now - armedAt <= DoubleTapInterval)
                {
                    Reset();
                    _modifierPressed = true;
                    _modifierDownAt = now;
                    _ignoreRelease = true;
                    return true;
                }
                _firstTapReleasedAt = null;
            }

            _comboInterrupted = false;
            _modifierPressed = true;
            _modifierDownAt = now;
            return false;
        }

        if (!_modifierPressed) return false;
        _modifierPressed = false;
        double? downAt = _modifierDownAt;
        _modifierDownAt = null;

        if (_ignoreRelease)
        {
            _ignoreRelease = false;
            _firstTapReleasedAt = null;
            _comboInterrupted = false;
            return false;
        }

        if (downAt is null) return false;
        double held = now - downAt.Value;
        if (_comboInterrupted || held > MaxTapDuration)
        {
            _firstTapReleasedAt = null;
            _comboInterrupted = false;
            return false;
        }

        _firstTapReleasedAt = now;
        return false;
    }
}

public struct ModifierToggleTapDetector
{
    private bool _modifierPressed;
    private double? _modifierDownAt;
    private bool _comboInterrupted;

    public void Reset()
    {
        _modifierPressed = false;
        _modifierDownAt = null;
        _comboInterrupted = false;
    }

    public void NonModifierKeyPressed()
    {
        _comboInterrupted = true;
    }

    public bool ModifierChanged(bool active, double now)
    {
        if (active)
        {
            if (_modifierPressed) return false;
            _comboInterrupted = false;
            _modifierPressed = true;
            _modifierDownAt = now;
            return false;
        }

        if (!_modifierPressed) return false;
        _modifierPressed = false;
        double? downAt = _modifierDownAt;
        _modifierDownAt = null;

        if (downAt is null) return false;
        double held = now - downAt.Value;
        if (_comboInterrupted || held > ModifierDoubleTapDetector.MaxTapDuration)
        {
            _comboInterrupted = false;
            return false;
        }

        return true;
    }
}
