using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using BetterVoice.App.Audio;
using BetterVoice.App.Native;
using BetterVoice.App.Services;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Integration.Tests;

public class RealWorldVoiceAndSubsystemsTests
{
    [Fact]
    public async Task TestRealWhisperTranscriberWithDeveloperTextCleanup()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), "bettervoice_speech_test.wav");
        Assert.True(File.Exists(wavPath), "Spoken test WAV file should exist");

        var settingsManager = new SettingsManager();
        settingsManager.Current.DeveloperCleanupEnabled = true;
        settingsManager.Current.TranscriptionLanguageCode = "en";

        using var transcriber = new LocalTranscriber(settingsManager);
        string transcript = await transcriber.TranscribeAsync(wavPath, DeveloperAppProfile.General);

        Assert.False(string.IsNullOrWhiteSpace(transcript), "Transcript should not be empty");

        // Verify that Whisper decoded the audio and DeveloperTextCleanup applied proper developer casing!
        Assert.Contains("BetterVoice", transcript, StringComparison.OrdinalIgnoreCase);

        // DeveloperTextCleanup should have cased JavaScript, JSON, or API properly
        bool hasDeveloperCasing = transcript.Contains("JavaScript") || transcript.Contains("JSON") || transcript.Contains("API");
        Assert.True(hasDeveloperCasing, $"Expected developer casing in transcript: {transcript}");
    }

    [Fact]
    public void TestRealScreenCaptureWithTargetHighlight()
    {
        string tempPng = Path.Combine(Path.GetTempPath(), $"circle_capture_test_{Guid.NewGuid()}.png");
        try
        {
            var gesture = new CircleGesture(new PointD(350, 250), 55);
            ScreenshotCapture.Capture(gesture, tempPng);

            Assert.True(File.Exists(tempPng), "Screenshot file should be created");
            var fileInfo = new FileInfo(tempPng);
            Assert.True(fileInfo.Length > 5000, "Screenshot should be non-trivial size");

            using var img = Image.FromFile(tempPng);
            Assert.True(img.Width > 0 && img.Height > 0, "Valid image dimensions");
        }
        finally
        {
            if (File.Exists(tempPng))
            {
                File.Delete(tempPng);
            }
        }
    }

    [Fact]
    public void TestAudioCaptureDeviceEnumeration()
    {
        var devices = AudioRecorder.GetInputDevices();
        Assert.NotNull(devices);
        // Should not crash even if no mics are connected or default mic is active
    }

    [Fact]
    public void TestCurrentAppContextDetection()
    {
        var context = TextInsertion.GetCurrentContext();
        Assert.False(string.IsNullOrEmpty(context.ProcessName));
    }

    [Fact]
    public void TestSettingsAndVocabularyRoundTrip()
    {
        var settings = new SettingsManager();
        settings.Current.CircleMinimumAngleDegrees = 345;
        settings.Current.QuickTriggerMode = RecordingTriggerMode.DoubleTap;
        settings.Save();

        var reloaded = new SettingsManager();
        Assert.Equal(345, reloaded.Current.CircleMinimumAngleDegrees);
        Assert.Equal(RecordingTriggerMode.DoubleTap, reloaded.Current.QuickTriggerMode);

        // Reset to default
        settings.Current.CircleMinimumAngleDegrees = 340;
        settings.Current.QuickTriggerMode = RecordingTriggerMode.Hold;
        settings.Save();
    }
}
