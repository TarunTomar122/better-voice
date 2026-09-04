using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class ModifierDoubleTapDetectorTests
{
    [Fact]
    public void TestDoubleTapWithinIntervalToggles()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.True(detector.ModifierChanged(active: true, now: 0.2));
    }

    [Fact]
    public void TestSingleTapDoesNotToggle()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.False(detector.ModifierChanged(active: true, now: 2));
    }

    [Fact]
    public void TestHoldDoesNotToggle()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.5));
    }

    [Fact]
    public void TestModifierComboCancelsPendingTap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        detector.NonModifierKeyPressed();
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.False(detector.ModifierChanged(active: true, now: 0.2));
    }

    [Fact]
    public void TestSlowSecondTapStartsFresh()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.False(detector.ModifierChanged(active: true, now: 1));
        Assert.False(detector.ModifierChanged(active: false, now: 1.1));
    }

    [Fact]
    public void TestResetClearsArmedTap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        detector.Reset();
        Assert.False(detector.ModifierChanged(active: true, now: 0.2));
    }

    [Fact]
    public void TestRepeatedModifierEventDoesNotShortenHold()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: true, now: 0.2));
        Assert.False(detector.ModifierChanged(active: false, now: 0.3));
        Assert.False(detector.ModifierChanged(active: true, now: 0.4));
    }

    [Fact]
    public void TestSecondTapReleaseDoesNotArmAnotherTap()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.True(detector.ModifierChanged(active: true, now: 0.2));
        Assert.False(detector.ModifierChanged(active: false, now: 0.3));
        Assert.False(detector.ModifierChanged(active: true, now: 0.4));
    }
}

public class ModifierToggleTapDetectorTests
{
    [Fact]
    public void TestShortTapToggles()
    {
        var detector = new ModifierToggleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.True(detector.ModifierChanged(active: false, now: 0.1));
    }

    [Fact]
    public void TestHoldDoesNotToggle()
    {
        var detector = new ModifierToggleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: false, now: 0.5));
    }

    [Fact]
    public void TestModifierComboCancelsTap()
    {
        var detector = new ModifierToggleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        detector.NonModifierKeyPressed();
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
    }

    [Fact]
    public void TestRepeatedModifierEventDoesNotShortenHold()
    {
        var detector = new ModifierToggleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(detector.ModifierChanged(active: true, now: 0.2));
        Assert.False(detector.ModifierChanged(active: false, now: 0.3));
    }
}

public class RecordingTriggerModeTests
{
    [Fact]
    public void TestCommandOptionUsesPartialStateOnlyForItsOwnModifiers()
    {
        var state = new ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: false,
            control: false,
            shift: false);

        Assert.False(state.Active);
        Assert.True(state.Partial);
    }

    [Fact]
    public void TestOptionBindingDoesNotBecomePartialWhenCommandIsHeld()
    {
        var state = new ModifierBindingState(
            bindingCommand: false,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: true,
            control: false,
            shift: false);

        Assert.False(state.Active);
        Assert.False(state.Partial);
    }

    [Fact]
    public void TestModifierChordWithExtraModifierIsNeitherActiveNorPartial()
    {
        var state = new ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: false,
            control: false,
            shift: true);

        Assert.False(state.Active);
        Assert.False(state.Partial);
    }

    [Fact]
    public void TestFullModifierChordIsActiveAndNotPartial()
    {
        var state = new ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: true,
            control: false,
            shift: false);

        Assert.True(state.Active);
        Assert.False(state.Partial);
    }

    [Fact]
    public void TestQuickModesForModifierOnlyBinding()
    {
        Assert.Equal(
            [RecordingTriggerMode.Hold, RecordingTriggerMode.Toggle, RecordingTriggerMode.DoubleTap],
            RecordingTriggerModeExtensions.AvailableModes(forQuick: true, modifierOnly: true));
    }

    [Fact]
    public void TestQuickModesForKeyComboBinding()
    {
        Assert.Equal(
            [RecordingTriggerMode.Hold, RecordingTriggerMode.Toggle],
            RecordingTriggerModeExtensions.AvailableModes(forQuick: true, modifierOnly: false));
    }

    [Fact]
    public void TestLongModesForModifierOnlyBinding()
    {
        Assert.Equal(
            [RecordingTriggerMode.Toggle, RecordingTriggerMode.DoubleTap],
            RecordingTriggerModeExtensions.AvailableModes(forQuick: false, modifierOnly: true));
    }

    [Fact]
    public void TestHoldDetailIncludesMilliseconds()
    {
        Assert.Contains("200 ms", RecordingTriggerMode.Hold.Detail("Alt", 200));
    }
}

public class ModifierChordEngagementTests
{
    private static ModifierBindingState CommandOption(
        bool command,
        bool option,
        bool control = false,
        bool shift = false)
    {
        return new ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: command,
            option: option,
            control: control,
            shift: shift);
    }

    [Fact]
    public void TestSingleKeyOfAChordDoesNotCountAsPressed()
    {
        var engagement = new ModifierChordEngagement();
        Assert.False(engagement.IsPressed(CommandOption(command: true, option: false)));
        Assert.False(engagement.IsPressed(CommandOption(command: false, option: false)));
        Assert.False(engagement.IsPressed(CommandOption(command: false, option: true)));
    }

    [Fact]
    public void TestFullChordThenSequentialReleaseStaysPressedUntilIdle()
    {
        var engagement = new ModifierChordEngagement();
        Assert.False(engagement.IsPressed(CommandOption(command: true, option: false)));
        Assert.True(engagement.IsPressed(CommandOption(command: true, option: true)));
        Assert.True(engagement.IsPressed(CommandOption(command: true, option: false)));
        Assert.False(engagement.IsPressed(CommandOption(command: false, option: false)));
    }

    [Fact]
    public void TestCommandOnlyDoubleTapDoesNotToggleACommandOptionShortcut()
    {
        var engagement = new ModifierChordEngagement();
        var detector = new ModifierDoubleTapDetector();

        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: false)),
            now: 0));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: false, option: false)),
            now: 0.1));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: false)),
            now: 0.2));
    }

    [Fact]
    public void TestFullChordDoubleTapStillFiresWhenKeysReleaseOneAtATime()
    {
        var engagement = new ModifierChordEngagement();
        var detector = new ModifierDoubleTapDetector();

        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: false)),
            now: 0));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: true)),
            now: 0.02));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: false)),
            now: 0.08));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: false, option: false)),
            now: 0.12));
        Assert.False(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: false)),
            now: 0.20));
        Assert.True(detector.ModifierChanged(
            active: engagement.IsPressed(CommandOption(command: true, option: true)),
            now: 0.24));
    }

    [Fact]
    public void TestOptionOnlyToggleStillFiresOnAShortTap()
    {
        var engagement = new ModifierChordEngagement();
        var detector = new ModifierToggleTapDetector();
        var optionDown = new ModifierBindingState(
            bindingCommand: false,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: false,
            option: true,
            control: false,
            shift: false);
        var idle = new ModifierBindingState(
            bindingCommand: false,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: false,
            option: false,
            control: false,
            shift: false);

        Assert.False(detector.ModifierChanged(active: engagement.IsPressed(optionDown), now: 0));
        Assert.True(detector.ModifierChanged(active: engagement.IsPressed(idle), now: 0.1));
    }

    [Fact]
    public void TestResetClearsALatchedFullChord()
    {
        var engagement = new ModifierChordEngagement();
        Assert.True(engagement.IsPressed(CommandOption(command: true, option: true)));
        engagement.Reset();
        Assert.False(engagement.IsPressed(CommandOption(command: true, option: false)));
    }
}

public class ModifierTapCancellationTests
{
    private static readonly RecordingModifierSnapshot None = new(
        Command: false, Option: false, Control: false, Shift: false);
    private static readonly RecordingModifierSnapshot CommandOnly = new(
        Command: true, Option: false, Control: false, Shift: false);

    [Fact]
    public void TestCapsLockOrFnAloneDoNotCancelATap()
    {
        Assert.False(ModifierTapHelper.ShouldCancelModifierTap(bindingEngaged: false, leftover: None));
    }

    [Fact]
    public void TestUnboundCommandCancelsAnOptionTap()
    {
        Assert.True(ModifierTapHelper.ShouldCancelModifierTap(bindingEngaged: false, leftover: CommandOnly));
    }

    [Fact]
    public void TestLeftoverModifiersDoNotCancelWhileTheBindingIsStillDown()
    {
        var commandAndOption = new RecordingModifierSnapshot(
            Command: true, Option: true, Control: false, Shift: false);
        Assert.False(ModifierTapHelper.ShouldCancelModifierTap(bindingEngaged: true, leftover: commandAndOption));
    }

    [Fact]
    public void TestOptionDoubleTapStillArmsWhenOnlyNonChordFlagsWouldRemain()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.False(ModifierTapHelper.ShouldCancelModifierTap(bindingEngaged: false, leftover: None));
        Assert.False(detector.ModifierChanged(active: false, now: 0.1));
        Assert.True(detector.ModifierChanged(active: true, now: 0.2));
    }

    [Fact]
    public void TestOptionDoubleTapResetsWhenAnUnboundModifierRemains()
    {
        var detector = new ModifierDoubleTapDetector();
        Assert.False(detector.ModifierChanged(active: true, now: 0));
        Assert.True(ModifierTapHelper.ShouldCancelModifierTap(bindingEngaged: false, leftover: CommandOnly));
        detector.Reset();
        Assert.False(detector.ModifierChanged(active: true, now: 0.2));
    }
}
