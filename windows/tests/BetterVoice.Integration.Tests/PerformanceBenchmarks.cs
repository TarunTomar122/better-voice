using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BetterVoice.App.Native;
using BetterVoice.App.Services;
using BetterVoice.Core;
using Xunit;
using Xunit.Abstractions;

namespace BetterVoice.Integration.Tests;

public class PerformanceBenchmarks
{
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void Benchmark_CircleGestureDetector_Throughput()
    {
        var detector = new CircleGestureDetector();
        int iterations = 100_000;
        var center = new PointD(500, 500);

        // Warmup
        for (int i = 0; i < 1_000; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            detector.Add(new PointD(center.X + 50 * Math.Cos(a), center.Y + 50 * Math.Sin(a)), (double)i / 60.0);
        }
        detector.Reset();

        var sw = Stopwatch.StartNew();
        int gesturesFound = 0;
        for (int i = 0; i < iterations; i++)
        {
            double a = (double)(i % 48) / 47.0 * 2.0 * Math.PI;
            if (detector.Add(new PointD(center.X + 50 * Math.Cos(a), center.Y + 50 * Math.Sin(a)), (double)i / 60.0) != null)
            {
                gesturesFound++;
            }
        }
        sw.Stop();

        double totalMs = sw.Elapsed.TotalMilliseconds;
        double opsPerSec = iterations / sw.Elapsed.TotalSeconds;
        double usPerOp = (totalMs * 1000.0) / iterations;

        _output.WriteLine($"--- CIRCLE GESTURE DETECTOR PERFORMANCE ---");
        _output.WriteLine($"Total Samples: {iterations:N0}");
        _output.WriteLine($"Total Time: {totalMs:F2} ms");
        _output.WriteLine($"Latency per sample: {usPerOp:F3} µs (microseconds)");
        _output.WriteLine($"Throughput: {opsPerSec:N0} samples/second");
        _output.WriteLine($"Recognized Gestures: {gesturesFound:N0}");

        Assert.True(opsPerSec > 500_000, "Should process at least 500k samples/sec");
    }

    [Fact]
    public void Benchmark_DeveloperTextCleanup_Throughput()
    {
        string sampleText = "Please inspect the json and javascript code on github, then deploy with cube cuttle to the rest api and test with curl and npm install. ";
        var sb = new StringBuilder();
        for (int i = 0; i < 500; i++) sb.Append(sampleText);
        string bigText = sb.ToString(); // ~70,000 characters

        var overrides = new List<(string, string)> { ("cube cuttle", "kubectl") };

        // Warmup
        _ = DeveloperTextCleanup.Apply(sampleText, DeveloperAppProfile.Terminal, overrides);

        var sw = Stopwatch.StartNew();
        int runs = 50;
        for (int i = 0; i < runs; i++)
        {
            _ = DeveloperTextCleanup.Apply(bigText, DeveloperAppProfile.Terminal, overrides);
        }
        sw.Stop();

        long totalChars = (long)bigText.Length * runs;
        double totalSec = sw.Elapsed.TotalSeconds;
        double charsPerSec = totalChars / totalSec;
        double mbPerSec = (totalChars * sizeof(char)) / (1024.0 * 1024.0) / totalSec;

        _output.WriteLine($"--- DEVELOPER TEXT CLEANUP PERFORMANCE ---");
        _output.WriteLine($"Total Characters Processed: {totalChars:N0}");
        _output.WriteLine($"Total Time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"Throughput: {charsPerSec:N0} characters/sec ({mbPerSec:F2} MB/sec)");

        Assert.True(charsPerSec > 1_000_000, "Should process at least 1M chars/sec");
    }

    [Fact]
    public void Benchmark_TrailSegments_Throughput()
    {
        int pointCount = 10_000;
        var points = new List<PointD>(pointCount);
        var times = new List<double>(pointCount);

        for (int i = 0; i < pointCount; i++)
        {
            points.Add(new PointD(i * 1.5, (i % 20) * 2.0));
            times.Add(i * 0.016);
        }

        // Warmup
        _ = TrailSegments.Calculate(points.GetRange(0, 100), times.GetRange(0, 100));

        var sw = Stopwatch.StartNew();
        int iterations = 100;
        int totalSegments = 0;
        for (int i = 0; i < iterations; i++)
        {
            var segments = TrailSegments.Calculate(points, times);
            totalSegments += segments.Count;
        }
        sw.Stop();

        long totalPointsProcessed = (long)pointCount * iterations;
        double pointsPerSec = totalPointsProcessed / sw.Elapsed.TotalSeconds;

        _output.WriteLine($"--- TRAIL SEGMENTS PERFORMANCE ---");
        _output.WriteLine($"Total Points Processed: {totalPointsProcessed:N0}");
        _output.WriteLine($"Total Time: {sw.Elapsed.TotalMilliseconds:F2} ms");
        _output.WriteLine($"Throughput: {pointsPerSec:N0} points/second");

        Assert.True(pointsPerSec > 2_000_000, "Should process at least 2M points/sec");
    }

    [Fact]
    public void Benchmark_SessionRetention_Scaling()
    {
        var now = DateTime.UtcNow;
        int sessionCount = 10_000;
        var sessions = new List<StoredSession>(sessionCount);
        var rand = new Random(42);

        for (int i = 0; i < sessionCount; i++)
        {
            sessions.Add(new StoredSession(
                $"2026-08-23T15-16-45Z-{Guid.NewGuid()}",
                now.AddDays(-rand.NextDouble() * 14.0),
                rand.Next(10_000, 500_000)));
        }

        var policy = new SessionRetentionPolicy(TimeSpan.FromDays(7), 50_000_000);

        var sw = Stopwatch.StartNew();
        int removedCount = 0;
        for (int i = 0; i < 20; i++)
        {
            var removed = policy.SessionsToRemove(sessions, now);
            removedCount = removed.Count;
        }
        sw.Stop();

        _output.WriteLine($"--- SESSION RETENTION QUOTA SCALING ---");
        _output.WriteLine($"Session count per run: {sessionCount:N0}");
        _output.WriteLine($"Total Runs: 20");
        _output.WriteLine($"Average Time per 10k items: {sw.Elapsed.TotalMilliseconds / 20.0:F2} ms");
        _output.WriteLine($"Sessions marked for removal: {removedCount:N0}");

        Assert.True(sw.Elapsed.TotalMilliseconds / 20.0 < 50.0, "Should evaluate 10k sessions in < 50ms");
    }

    [Fact]
    public async Task Benchmark_Whisper_Inference_Latency_And_RTF()
    {
        string wavPath = Path.Combine(Path.GetTempPath(), "bettervoice_speech_test.wav");
        Assert.True(File.Exists(wavPath), "Spoken test WAV file should exist");

        var fileInfo = new FileInfo(wavPath);
        // 16kHz 16-bit mono = 32,000 bytes/sec
        double audioDurationSeconds = (fileInfo.Length - 44) / 32000.0;

        var settingsManager = new SettingsManager(TestPaths.SettingsFile());
        using var transcriber = new LocalTranscriber(settingsManager);

        // Warmup inference
        _ = await transcriber.TranscribeAsync(wavPath, DeveloperAppProfile.General);

        var sw = Stopwatch.StartNew();
        int iterations = 3;
        string lastResult = string.Empty;
        for (int i = 0; i < iterations; i++)
        {
            lastResult = await transcriber.TranscribeAsync(wavPath, DeveloperAppProfile.General);
        }
        sw.Stop();

        double avgInferenceMs = sw.Elapsed.TotalMilliseconds / iterations;
        double avgInferenceSec = avgInferenceMs / 1000.0;
        double rtf = avgInferenceSec / audioDurationSeconds;

        _output.WriteLine($"--- WHISPER.NET SPEECH RECOGNITION PERFORMANCE ---");
        _output.WriteLine($"Audio Length: {audioDurationSeconds:F2} seconds");
        _output.WriteLine($"Average Inference Time: {avgInferenceMs:F1} ms");
        _output.WriteLine($"Real-Time Factor (RTF): {rtf:F3}x ({(1.0 / rtf):F1}x faster than real time!)");
        _output.WriteLine($"Decoded Text: \"{lastResult}\"");

        Assert.True(rtf < 0.5, "Whisper.net should be at least 2x faster than real-time (RTF < 0.5)");
    }
}
