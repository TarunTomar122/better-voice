using System;
using System.Collections.Generic;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class CircleGestureDetectorTests
{
    [Fact]
    public void TestConfiguredShortcutsExposeQuickAndLongModifierStates()
    {
        var configuration = new RecordingShortcutConfiguration(
            RecordingModifier.Command,
            RecordingLongShortcut.CommandShift);

        var (quick1, _, other1) = configuration.ActiveStates(command: true, option: false, control: false, shift: false);
        Assert.True(quick1);
        Assert.False(other1);

        var (_, long2, other2) = configuration.ActiveStates(command: true, option: false, control: false, shift: true);
        Assert.True(long2);
        Assert.False(other2);

        var (_, _, other3) = configuration.ActiveStates(command: true, option: true, control: false, shift: false);
        Assert.True(other3);
    }

    [Fact]
    public void TestCircleThresholdDefaultsTo340DegreesAndClampsUserValues()
    {
        var detector = new CircleGestureDetector();
        Assert.Equal(340, detector.MinimumAngleDegrees);
        Assert.Equal(300, new CircleGestureDetector(290).MinimumAngleDegrees);
        Assert.Equal(359, new CircleGestureDetector(370).MinimumAngleDegrees);

        detector.SetMinimumAngleDegrees(355);
        Assert.Equal(355, detector.MinimumAngleDegrees);
        detector.SetMinimumAngleDegrees(999);
        Assert.Equal(359, detector.MinimumAngleDegrees);
    }

    [Fact]
    public void TestOptionHoldStartsAfterDelayAndStopsOnRelease()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], shortcut.FlagsChanged(command: false, option: true));
        Assert.Equal([RecordingShortcutAction.StartPushToTalk], shortcut.PushToTalkDelayElapsed());
        Assert.Equal([RecordingShortcutAction.StopPushToTalk], shortcut.FlagsChanged(command: false, option: false));
    }

    [Fact]
    public void TestOtherModifierDoesNotStopActivePushToTalk()
    {
        var shortcut = new RecordingShortcutState();

        _ = shortcut.FlagsChanged(command: false, option: true);
        _ = shortcut.PushToTalkDelayElapsed();
        Assert.Empty(shortcut.FlagsChanged(command: false, option: true, otherModifier: true));
        Assert.Equal([RecordingShortcutAction.StopPushToTalk], shortcut.FlagsChanged(command: false, option: false));
    }

    [Fact]
    public void TestOptionTapCancelsBeforeRecordingStarts()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], shortcut.FlagsChanged(command: false, option: true));
        Assert.Equal([RecordingShortcutAction.CancelPendingPushToTalk], shortcut.FlagsChanged(command: false, option: false));
        Assert.Empty(shortcut.PushToTalkDelayElapsed());
    }

    [Fact]
    public void TestCommandOptionBeforeDelayStartsLongFormOnly()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], shortcut.FlagsChanged(command: false, option: true));
        Assert.Equal(
            [RecordingShortcutAction.CancelPendingPushToTalk, RecordingShortcutAction.ToggleLongForm],
            shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.PushToTalkDelayElapsed());
        Assert.Empty(shortcut.FlagsChanged(command: false, option: false));
    }

    [Fact]
    public void TestAddingCommandPromotesPushToTalkWithoutStopping()
    {
        var shortcut = new RecordingShortcutState();

        _ = shortcut.FlagsChanged(command: false, option: true);
        _ = shortcut.PushToTalkDelayElapsed();
        Assert.Equal([RecordingShortcutAction.PromoteToLongForm], shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.FlagsChanged(command: false, option: false));
    }

    [Fact]
    public void TestCommandOptionTogglesOncePerChord()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Equal([RecordingShortcutAction.ToggleLongForm], shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.FlagsChanged(command: false, option: false));
        Assert.Equal([RecordingShortcutAction.ToggleLongForm], shortcut.FlagsChanged(command: true, option: true));
    }

    [Fact]
    public void TestCommandAloneDoesNotActivateLongShortcut()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Empty(shortcut.FlagsChanged(command: true, option: false));
        Assert.Empty(shortcut.FlagsChanged(command: false, option: false));
    }

    [Fact]
    public void TestCompleteCommandOptionChordCanToggleOnAndOff()
    {
        var shortcut = new RecordingShortcutState();

        Assert.Empty(shortcut.FlagsChanged(command: true, option: false));
        Assert.Equal([RecordingShortcutAction.ToggleLongForm], shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.FlagsChanged(command: true, option: true));
        Assert.Empty(shortcut.FlagsChanged(command: false, option: false));
        Assert.Equal([RecordingShortcutAction.ToggleLongForm], shortcut.FlagsChanged(command: true, option: true));
    }

    [Fact]
    public void TestTrailSegmentsSkipPausesAndPointerJumps()
    {
        Assert.Empty(TrailSegments.Calculate([], []));
        Assert.Empty(TrailSegments.Calculate([new PointD(0, 0)], [0]));
        Assert.Equal(
            [new TrailSegment(0, 1)],
            TrailSegments.Calculate([new PointD(0, 0), new PointD(30, 0)], [0, 0.15]));
        Assert.Empty(
            TrailSegments.Calculate([new PointD(0, 0), new PointD(4, 3)], [0, 0.25]));
        Assert.Empty(
            TrailSegments.Calculate([new PointD(0, 0), new PointD(240, 0)], [0, 0.016]));
    }

    [Fact]
    public void TestRecognizesClosedCircle()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;
        var center = new PointD(300, 200);

        for (int index = 0; index < 48; index++)
        {
            double angle = (double)index / 47.0 * 2.0 * Math.PI;
            result = detector.Add(
                new PointD(center.X + 52 * Math.Cos(angle), center.Y + 52 * Math.Sin(angle)),
                (double)index / 60.0) ?? result;
        }

        Assert.NotNull(result);
        Assert.InRange(result.Value.Center.X, center.X - 3, center.X + 3);
        Assert.InRange(result.Value.Center.Y, center.Y - 3, center.Y + 3);
        Assert.InRange(result.Value.Radius, 52 - 3, 52 + 3);
    }

    [Fact]
    public void TestRecognizesSlowLooseLoop()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;
        var center = new PointD(900, 500);

        for (int index = 0; index < 150; index++)
        {
            double angle = (double)index / 149.0 * 2.0 * Math.PI;
            double wobble = 1.0 + 0.1 * Math.Sin(angle * 3);
            result = detector.Add(
                new PointD(
                    center.X + 110 * wobble * Math.Cos(angle),
                    center.Y + 82 * wobble * Math.Sin(angle)),
                index * 0.02) ?? result;
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void TestRecognizesSlowLooseLoopAfterPointerMovement()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        for (int index = 0; index < 60; index++)
        {
            _ = detector.Add(
                new PointD(300 + index * 5, 240 + (index % 7)),
                index * 0.02);
        }

        var center = new PointD(900, 500);
        for (int index = 0; index < 150; index++)
        {
            double angle = (double)index / 149.0 * 2.0 * Math.PI;
            double wobble = 1.0 + 0.1 * Math.Sin(angle * 3);
            result = detector.Add(
                new PointD(
                    center.X + 110 * wobble * Math.Cos(angle),
                    center.Y + 82 * wobble * Math.Sin(angle)),
                1.2 + index * 0.02) ?? result;
        }

        Assert.NotNull(result);
    }

    [Fact]
    public void TestRejectsStraightLine()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        for (int index = 0; index < 48; index++)
        {
            result = detector.Add(new PointD(index * 4, 200), (double)index / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void TestRejectsIrregularClosedLoop()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        for (int index = 0; index < 96; index++)
        {
            double angle = (double)index / 95.0 * 2.0 * Math.PI;
            double radius = 70 * (1 + 0.55 * Math.Sin(angle * 2));
            result = detector.Add(
                new PointD(400 + radius * Math.Cos(angle), 300 + radius * Math.Sin(angle)),
                (double)index / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void TestRejectsPartialArc()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        for (int index = 0; index < 48; index++)
        {
            double angle = (double)index / 47.0 * (4.0 * Math.PI / 3.0);
            result = detector.Add(
                new PointD(600 + 60 * Math.Cos(angle), 400 + 60 * Math.Sin(angle)),
                (double)index / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void TestRejectsHook()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;
        var points = new List<PointD>();

        for (int index = 0; index < 48; index++)
        {
            double progress = (double)index / 47.0;
            points.Add(new PointD(
                600 + 100 * progress,
                400 + 60 * Math.Sin(progress * Math.PI) + 18 * progress));
        }

        for (int index = 0; index < points.Count; index++)
        {
            result = detector.Add(points[index], (double)index / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void TestRejectsZigzag()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        for (int index = 0; index < 60; index++)
        {
            result = detector.Add(
                new PointD(
                    500 + index * 2,
                    400 + (index % 2 == 0 ? 35 : -35)),
                (double)index / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void TestLongContinuousLoopCapturesOnceUntilPointerLeaves()
    {
        var detector = new CircleGestureDetector();
        var center = new PointD(400, 300);
        int captures = 0;

        for (int index = 0; index < 240; index++)
        {
            double angle = (double)index / 47.0 * 2.0 * Math.PI;
            if (detector.Add(
                new PointD(center.X + 70 * Math.Cos(angle), center.Y + 70 * Math.Sin(angle)),
                (double)index / 60.0) is not null)
            {
                captures++;
            }
        }

        Assert.Equal(1, captures);
    }
}
