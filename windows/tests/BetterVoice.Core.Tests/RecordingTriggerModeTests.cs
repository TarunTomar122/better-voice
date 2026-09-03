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
}
