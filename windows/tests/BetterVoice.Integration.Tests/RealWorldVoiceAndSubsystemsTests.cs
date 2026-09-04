using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using BetterVoice.App.Audio;
using BetterVoice.App.Native;
using BetterVoice.App.Services;
using BetterVoice.Core;
using NAudio.Wave;
using Xunit;

namespace BetterVoice.Integration.Tests;

public class RealWorldVoiceAndSubsystemsTests
{
    [Fact]
    public async Task TestRealWhisperTranscriberWithDeveloperTextCleanup()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), "bettervoice_speech_test.wav");
        Assert.True(File.Exists(wavPath), "Spoken test WAV file should exist");

        var settingsManager = new SettingsManager(TestPaths.SettingsFile());
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

    [Theory]
    [InlineData("he go to school yesterday", "He went to school yesterday.")]
    [InlineData("she dont like apples", "She doesn't like apples.")]
    public async Task TestRealGrammarCorrection(string input, string expected)
    {
        using var corrector = new GrammarCorrector();
        string corrected = await corrector.CorrectAsync(input);
        Assert.Equal(expected, corrected);
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
    public async Task TestAudioCaptureDurationMatchesWallClock()
    {
        if (AudioRecorder.GetInputDevices().Count == 0) return;

        string outputPath = Path.Combine(Path.GetTempPath(), $"bettervoice_capture_{Guid.NewGuid():N}.wav");
        try
        {
            using var recorder = new AudioRecorder();
            recorder.Start(outputPath);
            await Task.Delay(1_000);
            await recorder.StopAsync();

            using var reader = new WaveFileReader(outputPath);
            Assert.Equal(16_000, reader.WaveFormat.SampleRate);
            Assert.Equal(1, reader.WaveFormat.Channels);
            Assert.InRange(reader.TotalTime.TotalSeconds, 0.75, 1.35);
        }
        finally
        {
            if (File.Exists(outputPath)) File.Delete(outputPath);
        }
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
        string settingsPath = TestPaths.SettingsFile();
        var settings = new SettingsManager(settingsPath);
        int changeNotifications = 0;
        settings.SettingsChanged += _ => changeNotifications++;
        settings.Current.CircleMinimumAngleDegrees = 345;
        settings.Current.QuickTriggerMode = RecordingTriggerMode.DoubleTap;
        settings.Save();

        Assert.Equal(1, changeNotifications);
        var reloaded = new SettingsManager(settingsPath);
        Assert.Equal(345, reloaded.Current.CircleMinimumAngleDegrees);
        Assert.Equal(RecordingTriggerMode.DoubleTap, reloaded.Current.QuickTriggerMode);

        // Reset to default
        settings.Current.CircleMinimumAngleDegrees = 340;
        settings.Current.QuickTriggerMode = RecordingTriggerMode.Hold;
        settings.Save();
    }

    [Fact]
    public void TestTranscriberSelectsEnglishAndMultilingualModels()
    {
        string english = LocalTranscriber.GetModelPath(TranscriptionLanguage.English);
        string multilingual = LocalTranscriber.GetModelPath(TranscriptionLanguage.Automatic);

        Assert.EndsWith("ggml-tiny.en.bin", english, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith("ggml-tiny.bin", multilingual, StringComparison.OrdinalIgnoreCase);
        Assert.NotEqual(english, multilingual);
    }

    [Theory]
    [InlineData("[BLANK_AUDIO]", "")]
    [InlineData(" [silence]  [NOISE] ", "")]
    [InlineData("Hello [MUSIC] world", "Hello world")]
    public void TestTranscriberRemovesWhisperNoSpeechMarkers(string raw, string expected)
    {
        Assert.Equal(expected, LocalTranscriber.CleanWhisperOutput(raw));
    }
}
