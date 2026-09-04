using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using BetterVoice.App.Native;
using BetterVoice.App.Services;
using BetterVoice.Core;
using Xunit;
using Xunit.Abstractions;

namespace BetterVoice.Integration.Tests;

/// <summary>
/// Rigorous test harness for BetterVoice Windows.
/// Covers:
/// 1. Extreme boundary conditions (NaN, Infinity, empty collections, single-point inputs, massive strings).
/// 2. High concurrency & thread safety (parallel execution of text cleanup, concurrent detectors, concurrent settings).
/// 3. Adversarial & edge-case inputs (malformed JSON, Unicode surrogate pairs, pathological mouse paths, rapid key bouncing).
/// 4. Stress & torture tests under extreme load (high-frequency mouse sampling, high-throughput text processing).
/// 5. Regressions, flaws, and edge-case bug characterizations.
/// </summary>
public class HarnessStressTests
{
    private readonly ITestOutputHelper _output;

    public HarnessStressTests(ITestOutputHelper output)
    {
        _output = output;
    }

    // =========================================================================
    // SECTION 1: EXTREME BOUNDARY CONDITIONS
    // =========================================================================

    [Fact]
    public void Boundary_CircleGestureDetector_HandlesNaNAndInfinityPoints_WithoutCrashing()
    {
        var detector = new CircleGestureDetector();

        // Feed NaN and Infinity coordinates at different points
        CircleGesture? g1 = detector.Add(new PointD(double.NaN, 100.0), 0.016);
        CircleGesture? g2 = detector.Add(new PointD(100.0, double.NaN), 0.032);
        CircleGesture? g3 = detector.Add(new PointD(double.NaN, double.NaN), 0.048);
        CircleGesture? g4 = detector.Add(new PointD(double.PositiveInfinity, 100.0), 0.064);
        CircleGesture? g5 = detector.Add(new PointD(100.0, double.NegativeInfinity), 0.080);
        CircleGesture? g6 = detector.Add(new PointD(double.NegativeInfinity, double.PositiveInfinity), 0.096);

        Assert.Null(g1);
        Assert.Null(g2);
        Assert.Null(g3);
        Assert.Null(g4);
        Assert.Null(g5);
        Assert.Null(g6);

        // Reset should clear any invalid state cleanly
        detector.Reset();

        // Valid circle after NaN points should still be recognized
        var center = new PointD(500, 500);
        CircleGesture? validGesture = null;
        for (int i = 0; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            validGesture = detector.Add(new PointD(center.X + 50 * Math.Cos(a), center.Y + 50 * Math.Sin(a)), (double)i / 60.0) ?? validGesture;
        }

        Assert.NotNull(validGesture);
    }

    [Fact]
    public void Boundary_CircleGestureDetector_HandlesExtremeAndReversedTimestamps()
    {
        var detector = new CircleGestureDetector();

        // Negative time
        Assert.Null(detector.Add(new PointD(100, 100), -50.0));

        // Time zero
        Assert.Null(detector.Add(new PointD(105, 105), 0.0));

        // Jumps backwards in time
        Assert.Null(detector.Add(new PointD(110, 110), -10.0));

        // Extreme timestamp boundaries
        Assert.Null(detector.Add(new PointD(115, 115), double.MinValue));
        Assert.Null(detector.Add(new PointD(120, 120), double.MaxValue));

        // NaN time should not throw
        Assert.Null(detector.Add(new PointD(125, 125), double.NaN));

        detector.Reset();
    }

    [Fact]
    public void Boundary_CircleGestureDetector_SubThresholdSamples_AlwaysReturnsNull()
    {
        var detector = new CircleGestureDetector();

        // Single sample
        Assert.Null(detector.Add(new PointD(200, 200), 0.01));

        // 17 samples (< 18 required for recognition)
        for (int i = 1; i < 18; i++)
        {
            double a = (double)i / 17.0 * 2.0 * Math.PI;
            CircleGesture? res = detector.Add(new PointD(200 + 40 * Math.Cos(a), 200 + 40 * Math.Sin(a)), i * 0.016);
            Assert.Null(res);
        }
    }

    [Fact]
    public void Boundary_CircleGestureDetector_SubPixelAndMicroGestures_Rejected()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        // Micro-circle with radius 5 px (width 10, height 10 < 28 min dimension)
        for (int i = 0; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            result = detector.Add(new PointD(300 + 5 * Math.Cos(a), 300 + 5 * Math.Sin(a)), (double)i / 60.0) ?? result;
        }

        Assert.Null(result);

        detector.Reset();
        result = null;

        // Circle with meanRadius = 13 px (< 14 min mean radius requirement)
        for (int i = 0; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            result = detector.Add(new PointD(300 + 13.5 * Math.Cos(a), 300 + 13.5 * Math.Sin(a)), (double)i / 60.0) ?? result;
        }

        Assert.Null(result);
    }

    [Fact]
    public void Boundary_CircleGestureDetector_ExtremeAspectRatios_Rejected()
    {
        var detector = new CircleGestureDetector();

        // Ultra-flat ellipse: width = 200, height = 30 (aspect ~ 6.6 > 2.2 cutoff)
        CircleGesture? flatResult = null;
        for (int i = 0; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            flatResult = detector.Add(new PointD(500 + 100 * Math.Cos(a), 500 + 15 * Math.Sin(a)), (double)i / 60.0) ?? flatResult;
        }
        Assert.Null(flatResult);

        detector.Reset();

        // Ultra-tall ellipse: width = 30, height = 200 (aspect ~ 0.15 < 0.45 cutoff)
        CircleGesture? tallResult = null;
        for (int i = 0; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            tallResult = detector.Add(new PointD(500 + 15 * Math.Cos(a), 500 + 100 * Math.Sin(a)), (double)i / 60.0) ?? tallResult;
        }
        Assert.Null(tallResult);
    }

    [Fact]
    public void Boundary_CircleGestureDetector_MinimumAngleClamping()
    {
        // Value below 300 clamps to 300
        var d1 = new CircleGestureDetector(100);
        Assert.Equal(300, d1.MinimumAngleDegrees);

        // Value above 359 clamps to 359
        var d2 = new CircleGestureDetector(720);
        Assert.Equal(359, d2.MinimumAngleDegrees);

        // Exact boundary values
        var d3 = new CircleGestureDetector(300);
        Assert.Equal(300, d3.MinimumAngleDegrees);

        var d4 = new CircleGestureDetector(359);
        Assert.Equal(359, d4.MinimumAngleDegrees);

        // Negative infinity
        var d5 = new CircleGestureDetector(double.NegativeInfinity);
        Assert.Equal(300, d5.MinimumAngleDegrees);
    }

    [Fact]
    public void Boundary_TrailSegments_EmptyAndMismatchedInputs_ReturnsEmpty()
    {
        // Empty
        Assert.Empty(TrailSegments.Calculate([], []));

        // Single point
        Assert.Empty(TrailSegments.Calculate([new PointD(1, 1)], [0.0]));

        // Mismatched counts
        var points = new List<PointD> { new(1, 1), new(2, 2), new(3, 3) };
        var timesShort = new List<double> { 0.0, 0.016 };
        Assert.Empty(TrailSegments.Calculate(points, timesShort));

        var timesLong = new List<double> { 0.0, 0.016, 0.032, 0.048 };
        Assert.Empty(TrailSegments.Calculate(points, timesLong));
    }

    [Fact]
    public void Boundary_TrailSegments_NaNAndInfiniteCoordinates_FilteredGracefully()
    {
        var points = new List<PointD>
        {
            new(100, 100),
            new(double.NaN, 105),
            new(110, double.PositiveInfinity),
            new(115, 115)
        };
        var times = new List<double> { 0.0, 0.016, 0.032, 0.048 };

        // Should not throw and NaN distances should not generate segments
        var segments = TrailSegments.Calculate(points, times);
        Assert.NotNull(segments);
        // Any segment containing a NaN or Inf point must NOT be connected
        foreach (var seg in segments)
        {
            Assert.False(double.IsNaN(points[seg.From].X) || double.IsNaN(points[seg.From].Y));
            Assert.False(double.IsNaN(points[seg.To].X) || double.IsNaN(points[seg.To].Y));
            Assert.False(double.IsInfinity(points[seg.From].X) || double.IsInfinity(points[seg.From].Y));
            Assert.False(double.IsInfinity(points[seg.To].X) || double.IsInfinity(points[seg.To].Y));
        }
    }

    [Fact]
    public void Boundary_TrailSegments_IdenticalPointsAndZeroGaps()
    {
        int count = 100;
        var points = Enumerable.Repeat(new PointD(250, 250), count).ToList();
        var times = Enumerable.Range(0, count).Select(i => i * 0.01).ToList();

        var segments = TrailSegments.Calculate(points, times);
        Assert.Equal(count - 1, segments.Count);
        for (int i = 0; i < segments.Count; i++)
        {
            Assert.Equal(i, segments[i].From);
            Assert.Equal(i + 1, segments[i].To);
        }
    }

    [Fact]
    public void Boundary_DeveloperTextCleanup_NullEmptyAndMassiveInput()
    {
        // Null string
        Assert.Null(DeveloperTextCleanup.Apply(null!));

        // Empty string
        Assert.Equal(string.Empty, DeveloperTextCleanup.Apply(string.Empty));

        // Whitespace only
        Assert.Equal("   \t\r\n  ", DeveloperTextCleanup.Apply("   \t\r\n  "));

        // Single character non-matching
        Assert.Equal("x", DeveloperTextCleanup.Apply("x"));

        // Single character matching ("ai" in Terminal profile)
        Assert.Equal("AI", DeveloperTextCleanup.Apply("ai", DeveloperAppProfile.Terminal));

        // Massive input (200,000 characters)
        string chunk = "Inspect the postgres database, test postgresql, use nextjs and graphql with aws s3 and ec2. ";
        var sb = new StringBuilder();
        while (sb.Length < 200_000)
        {
            sb.Append(chunk);
        }
        string massiveInput = sb.ToString();

        string cleaned = DeveloperTextCleanup.Apply(massiveInput, DeveloperAppProfile.Terminal);
        Assert.Contains("Postgres", cleaned);
        Assert.Contains("PostgreSQL", cleaned);
        Assert.Contains("Next.js", cleaned);
        Assert.Contains("GraphQL", cleaned);
        Assert.Contains("AWS", cleaned);
        Assert.Contains("S3", cleaned);
        Assert.Contains("EC2", cleaned);
    }

    [Fact]
    public void Boundary_SessionCompletionPolicy_AllDispositionBoundaries()
    {
        // 1. Has transcript -> always Deliver regardless of duration or context
        Assert.Equal(
            SessionCompletionDisposition.Deliver,
            SessionCompletionPolicy.Evaluate(hasTranscript: true, hasContext: false, duration: 0.1));
        Assert.Equal(
            SessionCompletionDisposition.Deliver,
            SessionCompletionPolicy.Evaluate(hasTranscript: true, hasContext: true, duration: 0.0));

        // 2. Has context (screenshot) -> always Deliver even without transcript
        Assert.Equal(
            SessionCompletionDisposition.Deliver,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: true, duration: 0.2));

        // 3. No transcript, no context, duration < accidentalThreshold (default 2.5) -> DiscardAccidental
        Assert.Equal(
            SessionCompletionDisposition.DiscardAccidental,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 0.0));
        Assert.Equal(
            SessionCompletionDisposition.DiscardAccidental,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 2.499));
        Assert.Equal(
            SessionCompletionDisposition.DiscardAccidental,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: -1.0));

        // 4. No transcript, no context, duration >= accidentalThreshold -> SaveEmpty
        Assert.Equal(
            SessionCompletionDisposition.SaveEmpty,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 2.500));
        Assert.Equal(
            SessionCompletionDisposition.SaveEmpty,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 100.0));

        // 5. Extreme durations: NaN, PositiveInfinity
        Assert.Equal(
            SessionCompletionDisposition.SaveEmpty,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: double.NaN));
        Assert.Equal(
            SessionCompletionDisposition.SaveEmpty,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: double.PositiveInfinity));
    }

    [Fact]
    public void Boundary_SessionRetentionPolicy_ZeroAndExtremeQuotas()
    {
        var policy = new SessionRetentionPolicy(TimeSpan.FromDays(7), maxBytes: 1000);

        // CanStore boundary conditions
        Assert.True(policy.CanStore(additionalBytes: 0, usedBytes: 0));
        Assert.True(policy.CanStore(additionalBytes: 1000, usedBytes: 0));
        Assert.True(policy.CanStore(additionalBytes: 500, usedBytes: 500));
        Assert.False(policy.CanStore(additionalBytes: 501, usedBytes: 500));
        Assert.False(policy.CanStore(additionalBytes: 1001, usedBytes: 0));

        // Negative numbers
        Assert.False(policy.CanStore(additionalBytes: -1, usedBytes: 100));
        Assert.False(policy.CanStore(additionalBytes: 100, usedBytes: -1));

        // Over-quota already
        Assert.False(policy.CanStore(additionalBytes: 1, usedBytes: 1500));

        // Empty sessions collection
        var now = DateTime.UtcNow;
        var removed = policy.SessionsToRemove([], now);
        Assert.Empty(removed);
    }

    [Fact]
    public void Boundary_QuickNoteHoldDelay_ClampingBoundaries()
    {
        Assert.Equal(50, QuickNoteHoldDelay.Clamp(int.MinValue));
        Assert.Equal(50, QuickNoteHoldDelay.Clamp(-1));
        Assert.Equal(50, QuickNoteHoldDelay.Clamp(0));
        Assert.Equal(50, QuickNoteHoldDelay.Clamp(49));
        Assert.Equal(50, QuickNoteHoldDelay.Clamp(50));
        Assert.Equal(140, QuickNoteHoldDelay.Clamp(140));
        Assert.Equal(500, QuickNoteHoldDelay.Clamp(500));
        Assert.Equal(500, QuickNoteHoldDelay.Clamp(501));
        Assert.Equal(500, QuickNoteHoldDelay.Clamp(int.MaxValue));
    }

    // =========================================================================
    // SECTION 2: HIGH-CONCURRENCY & THREAD SAFETY
    // =========================================================================

    [Fact]
    public void Concurrency_DeveloperTextCleanup_ParallelExecution_MultiThreadedStress()
    {
        int threads = 32;
        int iterationsPerThread = 500;
        var exceptions = new ConcurrentBag<Exception>();

        string[] testPhrases =
        [
            "Please check the javascript and typescript code with nextjs and mongodb.",
            "Deploy the rest api to ec2 with kubectl and inspect json.",
            "Running npm install and npx with bitbucket and github repositories.",
            "Reviewing python with pytorch, tensorflow, and onnx models.",
            "Connecting to postgres via supabase with graphql and jwt authentication."
        ];

        var profiles = new[]
        {
            DeveloperAppProfile.General,
            DeveloperAppProfile.Terminal,
            DeveloperAppProfile.Editor,
            DeveloperAppProfile.Ai
        };

        var overrides = new List<(string, string)>
        {
            ("cube cuttle", "kubectl"),
            ("dock er", "Docker"),
            ("k eight s", "K8s")
        };

        Parallel.For(0, threads, new ParallelOptions { MaxDegreeOfParallelism = threads }, _ =>
        {
            try
            {
                var rand = new Random(Thread.CurrentThread.ManagedThreadId);
                for (int i = 0; i < iterationsPerThread; i++)
                {
                    string phrase = testPhrases[rand.Next(testPhrases.Length)];
                    var profile = profiles[rand.Next(profiles.Length)];

                    string result = DeveloperTextCleanup.Apply(phrase, profile, overrides);
                    Assert.False(string.IsNullOrEmpty(result));

                    // Verify invariant: terms should never remain lowercase
                    if (phrase.Contains("javascript")) Assert.Contains("JavaScript", result);
                    if (phrase.Contains("typescript")) Assert.Contains("TypeScript", result);
                    if (phrase.Contains("mongodb")) Assert.Contains("MongoDB", result);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void Concurrency_CircleGestureDetector_ParallelInstances_Isolation()
    {
        int workerCount = 16;
        var exceptions = new ConcurrentBag<Exception>();
        var detectedGesturesCount = new int[workerCount];

        Parallel.For(0, workerCount, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, workerIndex =>
        {
            try
            {
                var detector = new CircleGestureDetector();
                var center = new PointD(100 * (workerIndex + 1), 100 * (workerIndex + 1));
                int detected = 0;

                // Each thread runs 20 separate gesture sequences
                for (int cycle = 0; cycle < 20; cycle++)
                {
                    detector.Reset();
                    double baseTime = cycle * 2.0;

                    for (int i = 0; i < 48; i++)
                    {
                        double a = (double)i / 47.0 * 2.0 * Math.PI;
                        var pt = new PointD(center.X + 45 * Math.Cos(a), center.Y + 45 * Math.Sin(a));
                        var gesture = detector.Add(pt, baseTime + (double)i / 60.0);
                        if (gesture.HasValue)
                        {
                            detected++;
                            Assert.InRange(gesture.Value.Center.X, center.X - 5, center.X + 5);
                            Assert.InRange(gesture.Value.Center.Y, center.Y - 5, center.Y + 5);
                        }
                    }
                }

                detectedGesturesCount[workerIndex] = detected;
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        // Every worker should have detected gestures independently without interference
        for (int i = 0; i < workerCount; i++)
        {
            Assert.True(detectedGesturesCount[i] > 0, $"Worker {i} detected {detectedGesturesCount[i]} gestures");
        }
    }

    [Fact]
    public void Concurrency_TrailSegments_ConcurrentEvaluation()
    {
        int threads = 16;
        var exceptions = new ConcurrentBag<Exception>();

        var testPoints = new List<PointD>();
        var testTimes = new List<double>();
        for (int i = 0; i < 1_000; i++)
        {
            testPoints.Add(new PointD(i * 2.0, (i % 30) * 1.5));
            testTimes.Add(i * 0.016);
        }

        Parallel.For(0, threads, _ =>
        {
            try
            {
                for (int iter = 0; iter < 100; iter++)
                {
                    var segs = TrailSegments.Calculate(testPoints, testTimes);
                    Assert.NotNull(segs);
                    Assert.True(segs.Count > 0);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void Concurrency_SettingsManager_ConcurrentLoads()
    {
        int threads = 16;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, threads, _ =>
        {
            try
            {
                for (int iter = 0; iter < 50; iter++)
                {
                    var manager = new SettingsManager(TestPaths.SettingsFile());
                    Assert.NotNull(manager.Current);
                }
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    [Fact]
    public void Concurrency_RecordingShortcutState_IndependentInstancesInParallel()
    {
        int parallelTasks = 32;
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, parallelTasks, _ =>
        {
            try
            {
                var state = new RecordingShortcutState();

                // Sequence: Hold option -> start -> other modifier -> release
                var a1 = state.FlagsChanged(command: false, option: true);
                Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], a1);

                var a2 = state.PushToTalkDelayElapsed();
                Assert.Equal([RecordingShortcutAction.StartPushToTalk], a2);

                var a3 = state.FlagsChanged(command: false, option: true, otherModifier: true);
                Assert.Empty(a3); // Other modifier doesn't stop active PTT while option held

                var a4 = state.FlagsChanged(command: false, option: false);
                Assert.Equal([RecordingShortcutAction.StopPushToTalk], a4);
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    // =========================================================================
    // SECTION 3: ADVERSARIAL & EDGE-CASE INPUTS
    // =========================================================================

    [Fact]
    public void Adversarial_VocabularyFile_MalformedAndCorruptedJson_HandledGracefully()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"bv_vocab_test_{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // 1. Non-existent file
            Assert.Empty(VocabularyFile.Terms(Path.Combine(tempDir, "missing.json")));

            // 2. Empty file (0 bytes)
            string emptyFile = Path.Combine(tempDir, "empty.json");
            File.WriteAllText(emptyFile, string.Empty);
            Assert.Empty(VocabularyFile.Terms(emptyFile));

            // 3. Syntax error / truncated JSON
            string malformedFile = Path.Combine(tempDir, "malformed.json");
            File.WriteAllText(malformedFile, "{\"terms\": { \"cube cuttle\": \"kubectl\"");
            Assert.Empty(VocabularyFile.Terms(malformedFile));

            // 4. Non-JSON binary content
            string binaryFile = Path.Combine(tempDir, "binary.json");
            File.WriteAllBytes(binaryFile, [0x00, 0xFF, 0xFE, 0x80, 0x12]);
            Assert.Empty(VocabularyFile.Terms(binaryFile));

            // 5. JSON root is array, not object
            string arrayRoot = Path.Combine(tempDir, "array_root.json");
            File.WriteAllText(arrayRoot, "[1, 2, 3]");
            Assert.Empty(VocabularyFile.Terms(arrayRoot));

            // 6. "terms" is null or array
            string nullTerms = Path.Combine(tempDir, "null_terms.json");
            File.WriteAllText(nullTerms, "{\"terms\": null}");
            Assert.Empty(VocabularyFile.Terms(nullTerms));

            string arrayTerms = Path.Combine(tempDir, "array_terms.json");
            File.WriteAllText(arrayTerms, "{\"terms\": [\"a\", \"b\"]}");
            Assert.Empty(VocabularyFile.Terms(arrayTerms));

            // 7. Valid string terms with empty strings to verify filter
            string stringTerms = Path.Combine(tempDir, "string_terms.json");
            File.WriteAllText(stringTerms, """
            {
              "terms": {
                "emptyKey": "",
                "": "emptyValue",
                "validKey": "validReplacement",
                "longerValidKey": "longestReplacement"
              }
            }
            """);

            var parsed = VocabularyFile.Terms(stringTerms);
            Assert.Equal(2, parsed.Count);
            // Must be sorted by descending key length
            Assert.Equal("longerValidKey", parsed[0].Key);
            Assert.Equal("longestReplacement", parsed[0].Value);
            Assert.Equal("validKey", parsed[1].Key);
            Assert.Equal("validReplacement", parsed[1].Value);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public void Adversarial_VocabularyFile_UnicodeAndRegexMetacharactersInTerms()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bv_vocab_regex_{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, """
            {
              "terms": {
                "c++": "C++",
                "node.js": "Node.js",
                "a.*b": "clean_regex",
                "price $100": "Price $100",
                "🔥 fire": "FIRE",
                "résumé": "Resume"
              }
            }
            """);

            var terms = VocabularyFile.Terms(tempFile);
            Assert.Equal(6, terms.Count);

            // Verify they feed into DeveloperTextCleanup without crashing regex compiler
            string cleaned = DeveloperTextCleanup.Apply("I love c++ and node.js with 🔥 fire and résumé", overrides: terms);
            Assert.Contains("C++", cleaned);
            Assert.Contains("Node.js", cleaned);
            Assert.Contains("FIRE", cleaned);
            Assert.Contains("Resume", cleaned);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Adversarial_DeveloperTextCleanup_RegexEscapeProtectsAgainstWildcardInjection()
    {
        // User provides regex wildcard tokens in override source: ".+" and ".*"
        var overrides = new List<(string Source, string Replacement)>
        {
            (".+", "HACKED_PLUS"),
            (".*", "HACKED_STAR")
        };

        // If regex injection occurred, ".+" would match and replace the whole sentence!
        string text = "This is a normal sentence containing literal .+ and .* symbols.";
        string result = DeveloperTextCleanup.Apply(text, DeveloperAppProfile.General, overrides);

        // Verify: ordinary words MUST NOT be swallowed by wildcards!
        Assert.Contains("This is a normal sentence containing literal", result);
        Assert.Contains("HACKED_PLUS", result); // Literal .+ was replaced
        Assert.Contains("HACKED_STAR", result); // Literal .* was replaced
        Assert.Contains("symbols.", result);
    }

    [Fact]
    public void Adversarial_DeveloperTextCleanup_UnicodeSurrogatesAndCombiningMarks()
    {
        // Text with surrogate pairs (emojis), combining characters, and RTL text
        string input = "Build 🤖 with chatgpt and nextjs 🎉. Test with ca\u0301fe and rest api in Terminal.";
        string cleaned = DeveloperTextCleanup.Apply(input, DeveloperAppProfile.Terminal);

        Assert.Contains("ChatGPT", cleaned);
        Assert.Contains("Next.js", cleaned);
        Assert.Contains("REST", cleaned);
        Assert.Contains("API", cleaned);
        Assert.Contains("🤖", cleaned);
        Assert.Contains("🎉", cleaned);

        // RTL Arabic text with embedded English tech terms
        string rtlInput = "تثبيت حزمة npm وربطها مع github ثم تفعيل ssl";
        string rtlCleaned = DeveloperTextCleanup.Apply(rtlInput, DeveloperAppProfile.Terminal);
        Assert.Contains("npm", rtlCleaned);
        Assert.Contains("GitHub", rtlCleaned);
        Assert.Contains("SSL", rtlCleaned);
    }

    [Fact]
    public void Adversarial_DeveloperTextCleanup_AmbiguousTermsProfileFiltering()
    {
        // Ambiguous terms: "rest", "rag", "crud", "whisper", "parakeet", "ai"
        string sentence = "Please take a rest and whisper to the parakeet. Wipe with a rag and avoid crud.";

        // General profile MUST NOT capitalize ambiguous everyday English words
        string generalResult = DeveloperTextCleanup.Apply(sentence, DeveloperAppProfile.General);
        Assert.Contains("take a rest", generalResult);
        Assert.Contains("whisper to", generalResult);
        Assert.Contains("with a rag", generalResult);
        Assert.Contains("avoid crud", generalResult);

        // Terminal profile SHOULD capitalize developer acronyms
        string terminalSentence = "Deploy the rest api, query the rag vector db, and inspect crud operations.";
        string terminalResult = DeveloperTextCleanup.Apply(terminalSentence, DeveloperAppProfile.Terminal);
        Assert.Contains("REST", terminalResult);
        Assert.Contains("RAG", terminalResult);
        Assert.Contains("CRUD", terminalResult);

        // Editor profile
        string editorResult = DeveloperTextCleanup.Apply(terminalSentence, DeveloperAppProfile.Editor);
        Assert.Contains("REST", editorResult);
        Assert.Contains("RAG", editorResult);
        Assert.Contains("CRUD", editorResult);
    }

    [Fact]
    public void Adversarial_DeveloperTextCleanup_CodeIdentifiersAndFileExtensions_NotCorrupted()
    {
        // Filenames, URLs, and code constructs should NOT be falsely cased
        string input = "Open file.json, check data.api.com, read schema.xml, and run Next.js script.";
        string result = DeveloperTextCleanup.Apply(input, DeveloperAppProfile.Terminal);

        // "file.json" should NOT become "file.JSON" (extension protected)
        Assert.Contains("file.json", result);

        // "data.api.com" should NOT become "data.API.com" (domain name protected)
        Assert.Contains("data.api.com", result);

        // "schema.xml" should NOT become "schema.XML"
        Assert.Contains("schema.xml", result);

        // "Next.js" preserved
        Assert.Contains("Next.js", result);
    }

    [Fact]
    public void Adversarial_DeveloperTextCleanup_SpokenAcronymsOnlyOnDeveloperProfiles()
    {
        string spoken = "I need to run n p m install and check g i t h u b and parse j s o n via c l i";

        // General profile: does NOT expand spoken acronyms
        string general = DeveloperTextCleanup.Apply(spoken, DeveloperAppProfile.General);
        Assert.Contains("n p m", general);
        Assert.Contains("g i t h u b", general);
        Assert.Contains("j s o n", general);

        // Terminal profile: DOES expand spoken acronyms
        string terminal = DeveloperTextCleanup.Apply(spoken, DeveloperAppProfile.Terminal);
        Assert.Contains("npm", terminal);
        Assert.Contains("GitHub", terminal);
        Assert.Contains("JSON", terminal);
        Assert.Contains("CLI", terminal);

        // Editor profile: DOES expand
        string editor = DeveloperTextCleanup.Apply(spoken, DeveloperAppProfile.Editor);
        Assert.Contains("npm", editor);
        Assert.Contains("GitHub", editor);

        // Ai profile: DOES expand
        string ai = DeveloperTextCleanup.Apply(spoken, DeveloperAppProfile.Ai);
        Assert.Contains("npm", ai);
        Assert.Contains("GitHub", ai);
    }

    [Fact]
    public void Adversarial_CircleGestureDetector_PathologicalPaths_Rejected()
    {
        var detector = new CircleGestureDetector();

        // 1. Linear oscillation / high-frequency mouse jitter (shaking)
        detector.Reset();
        CircleGesture? jitterResult = null;
        for (int i = 0; i < 60; i++)
        {
            jitterResult = detector.Add(new PointD(500 + (i % 2 == 0 ? 40 : -40), 500), (double)i / 60.0) ?? jitterResult;
        }
        Assert.Null(jitterResult);

        // 2. Archimedean spiral (expanding outwards, non-constant radius)
        detector.Reset();
        CircleGesture? spiralResult = null;
        for (int i = 0; i < 60; i++)
        {
            double a = (double)i / 59.0 * 2.0 * Math.PI;
            double r = 15 + i * 1.5; // radius expands from 15 to 105
            spiralResult = detector.Add(new PointD(400 + r * Math.Cos(a), 400 + r * Math.Sin(a)), (double)i / 60.0) ?? spiralResult;
        }
        Assert.Null(spiralResult);

        // 3. Pause midway (> 0.45s causes buffer reset)
        detector.Reset();
        CircleGesture? pausedResult = null;
        for (int i = 0; i < 24; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            _ = detector.Add(new PointD(400 + 50 * Math.Cos(a), 400 + 50 * Math.Sin(a)), i * 0.016);
        }
        // Big pause 0.5s > 0.45s
        for (int i = 24; i < 48; i++)
        {
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            pausedResult = detector.Add(new PointD(400 + 50 * Math.Cos(a), 400 + 50 * Math.Sin(a)), 0.50 + i * 0.016) ?? pausedResult;
        }
        // Since samples were cleared after the 0.5s gap, only 24 samples remain -> less than full loop
        Assert.Null(pausedResult);
    }

    [Fact]
    public void Adversarial_CircleGestureDetector_CooldownAndExitEnforcement()
    {
        var detector = new CircleGestureDetector();
        var center = new PointD(500, 500);
        double radius = 60.0;

        // 1. Draw valid circle #1
        CircleGesture? g1 = null;
        double t = 0;
        for (int i = 0; i < 48; i++)
        {
            t = (double)i / 60.0;
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            g1 = detector.Add(new PointD(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)), t) ?? g1;
        }
        Assert.NotNull(g1);

        // 2. Immediately draw circle #2 within cooldown window (t + 0.65s) -> must be rejected
        CircleGesture? g2 = null;
        for (int i = 0; i < 48; i++)
        {
            double t2 = t + 0.05 + (double)i / 60.0; // within 0.65 cooldown
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            g2 = detector.Add(new PointD(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)), t2) ?? g2;
        }
        Assert.Null(g2);

        // 3. Draw circle #3 after cooldown (t + 1.0s), BUT mouse pointer stays strictly INSIDE 1.5x radius
        // The detector requires mouse to exit 1.5x radius before recognizing another gesture in the same spot!
        CircleGesture? g3 = null;
        double t3Base = t + 1.0;
        for (int i = 0; i < 48; i++)
        {
            double t3 = t3Base + (double)i / 60.0;
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            g3 = detector.Add(new PointD(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)), t3) ?? g3;
        }
        Assert.Null(g3); // Blocked because pointer never exited 1.5x radius!

        // 4. Pointer exits outside 1.5 * radius (60 * 1.5 = 90 px, move to distance 150 px)
        detector.Add(new PointD(center.X + 150, center.Y), t3Base + 1.0);

        // 5. Now draw circle #4 -> Recognized!
        CircleGesture? g4 = null;
        double t4Base = t3Base + 1.1;
        for (int i = 0; i < 48; i++)
        {
            double t4 = t4Base + (double)i / 60.0;
            double a = (double)i / 47.0 * 2.0 * Math.PI;
            g4 = detector.Add(new PointD(center.X + radius * Math.Cos(a), center.Y + radius * Math.Sin(a)), t4) ?? g4;
        }
        Assert.NotNull(g4);
    }

    [Fact]
    public void Adversarial_RecordingShortcutState_RapidModifierKeyBouncing()
    {
        // Simulates mechanical switch chatter / bounce: 1,000 rapid key transitions in 5ms
        var state = new RecordingShortcutState();

        for (int i = 0; i < 1_000; i++)
        {
            bool isDown = (i % 2 == 1);
            var actions = state.FlagsChanged(command: false, option: isDown);
            Assert.NotNull(actions);
        }

        // Final release must restore clean idle state
        var finalRelease = state.FlagsChanged(command: false, option: false);
        Assert.NotNull(finalRelease);

        // Next clean hold must schedule PTT normally
        var freshSchedule = state.FlagsChanged(command: false, option: true);
        Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], freshSchedule);
    }

    [Fact]
    public void Adversarial_RecordingShortcutState_UnboundModifiersCancellation()
    {
        var state = new RecordingShortcutState();

        // 1. Pending PTT cancelled by other modifier (e.g. Shift or Ctrl)
        var a1 = state.FlagsChanged(command: false, option: true);
        Assert.Equal([RecordingShortcutAction.SchedulePushToTalk], a1);

        var a2 = state.FlagsChanged(command: false, option: true, otherModifier: true);
        Assert.Equal([RecordingShortcutAction.CancelPendingPushToTalk], a2);

        // 2. Active PTT cancelled when other modifier is pressed and option is released
        state = new RecordingShortcutState();
        _ = state.FlagsChanged(command: false, option: true);
        _ = state.PushToTalkDelayElapsed(); // In PushToTalk mode

        var a3 = state.FlagsChanged(command: false, option: false, otherModifier: true);
        Assert.Equal([RecordingShortcutAction.StopPushToTalk], a3);
    }

    [Fact]
    public void Adversarial_ModifierDoubleTap_InterruptedByTyping()
    {
        var detector = new ModifierDoubleTapDetector();
        double now = 10.0;

        // 1. First tap: Down at 10.0, Up at 10.08 (80ms tap)
        Assert.False(detector.ModifierChanged(active: true, now));
        now += 0.08;
        Assert.False(detector.ModifierChanged(active: false, now));

        // 2. User types a regular key (e.g. 'A')
        detector.NonModifierKeyPressed();

        // 3. Second tap occurs at 10.20 (within double tap interval 0.40s)
        now += 0.12;
        bool triggered = detector.ModifierChanged(active: true, now);

        // MUST NOT trigger because typing interrupted the combo!
        Assert.False(triggered);
    }

    [Fact]
    public void Adversarial_ModifierDoubleTap_ExcessiveHoldDuration_NotTreatedAsTap()
    {
        var detector = new ModifierDoubleTapDetector();
        double now = 20.0;

        // Hold modifier for 300ms (> MaxTapDuration of 250ms)
        detector.ModifierChanged(active: true, now);
        now += 0.30;
        detector.ModifierChanged(active: false, now);

        // Second tap arrives quickly at now + 50ms
        now += 0.05;
        bool triggered = detector.ModifierChanged(active: true, now);

        // MUST NOT trigger because first press was a sustained hold, not a quick tap
        Assert.False(triggered);
    }

    [Fact]
    public void Adversarial_ModifierChordEngagement_WinAltTransitions()
    {
        var engagement = new ModifierChordEngagement();

        // Binding: Command (Win) + Option (Alt)
        // 1. Initial idle: no keys pressed
        var s0 = new ModifierBindingState(bindingCommand: true, bindingOption: true, bindingControl: false, bindingShift: false,
                                          command: false, option: false, control: false, shift: false);
        Assert.False(engagement.IsPressed(s0));

        // 2. Press Win only (partial engagement) -> should NOT be pressed yet
        var s1 = new ModifierBindingState(bindingCommand: true, bindingOption: true, bindingControl: false, bindingShift: false,
                                          command: true, option: false, control: false, shift: false);
        Assert.True(s1.Partial);
        Assert.False(engagement.IsPressed(s1));

        // 3. Press Alt while Win held -> FULL CHORD ACTIVE!
        var s2 = new ModifierBindingState(bindingCommand: true, bindingOption: true, bindingControl: false, bindingShift: false,
                                          command: true, option: true, control: false, shift: false);
        Assert.True(s2.Active);
        Assert.True(engagement.IsPressed(s2));

        // 4. Release Alt while Win still held -> chord remains engaged during release transition
        Assert.True(engagement.IsPressed(s1));

        // 5. Release Win as well -> all released
        Assert.False(engagement.IsPressed(s0));

        // 6. Unbound key pressed (e.g. Shift) breaks engagement
        var sUnbound = new ModifierBindingState(bindingCommand: true, bindingOption: true, bindingControl: false, bindingShift: false,
                                                command: true, option: true, control: false, shift: true);
        Assert.False(engagement.IsPressed(sUnbound));
    }

    [Fact]
    public void Adversarial_SessionNaming_RegexValidation()
    {
        // Valid sessions
        Assert.True(SessionNaming.IsBetterVoiceSessionName("2026-08-23T15-16-45Z-b005b883-9cb8-83d9-aa8a-2ff461f04c13"));
        Assert.True(SessionNaming.IsBetterVoiceSessionName("1999-12-31T23-59-59Z-00000000-0000-0000-0000-000000000000"));

        // Invalid: missing timestamp, bad separators, directory traversal
        Assert.False(SessionNaming.IsBetterVoiceSessionName(""));
        Assert.False(SessionNaming.IsBetterVoiceSessionName("../2026-08-23T15-16-45Z-b005b883-9cb8-83d9-aa8a-2ff461f04c13"));
        Assert.False(SessionNaming.IsBetterVoiceSessionName("2026-08-23T15:16:45Z-b005b883-9cb8-83d9-aa8a-2ff461f04c13")); // colon instead of dash
        Assert.False(SessionNaming.IsBetterVoiceSessionName("2026-08-23T15-16-45Z-invalid-guid-here-0000-000000000000"));
        Assert.False(SessionNaming.IsBetterVoiceSessionName("prefix-2026-08-23T15-16-45Z-b005b883-9cb8-83d9-aa8a-2ff461f04c13"));
    }

    // =========================================================================
    // SECTION 4: STRESS & TORTURE TESTS UNDER EXTREME LOAD
    // =========================================================================

    [Fact]
    public void Stress_CircleGestureDetector_OneMillionSamples_SustainedThroughput()
    {
        var detector = new CircleGestureDetector();
        int totalSamples = 1_000_000;
        var center = new PointD(500, 500);
        int recognizedGestures = 0;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < totalSamples; i++)
        {
            double a = (double)(i % 48) / 47.0 * 2.0 * Math.PI;
            var pt = new PointD(center.X + 50 * Math.Cos(a), center.Y + 50 * Math.Sin(a));
            if (detector.Add(pt, (double)i / 60.0) != null)
            {
                recognizedGestures++;
            }
        }
        sw.Stop();

        double elapsedSec = sw.Elapsed.TotalSeconds;
        double throughput = totalSamples / elapsedSec;
        double usPerSample = (sw.Elapsed.TotalMilliseconds * 1000.0) / totalSamples;

        _output.WriteLine($"[STRESS] Processed {totalSamples:N0} samples in {sw.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"[STRESS] Throughput: {throughput:N0} samples/sec | Latency: {usPerSample:F3} µs/sample");

        Assert.True(throughput > 500_000, $"Throughput {throughput:N0} samples/sec should exceed 500k samples/sec");
        Assert.True(usPerSample < 2.0, $"Per-sample latency {usPerSample:F3} µs should be sub-2 microseconds");
    }

    [Fact]
    public void Stress_DeveloperTextCleanup_FiftyMegabytes_HighThroughput()
    {
        string paragraph = "Deploy the rest api to ec2 with postgresql and nextjs on github. Use docker and kubernetes with aws s3 and chatgpt. ";
        var sb = new StringBuilder();
        while (sb.Length < 1_000_000)
        {
            sb.Append(paragraph);
        }
        string oneMbText = sb.ToString();

        var overrides = new List<(string, string)>
        {
            ("cube cuttle", "kubectl"),
            ("dock er", "Docker"),
            ("k eight s", "K8s")
        };

        // Process 50MB of text (50 x 1MB)
        int iterations = 50;
        long totalChars = (long)oneMbText.Length * iterations;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            _ = DeveloperTextCleanup.Apply(oneMbText, DeveloperAppProfile.Terminal, overrides);
        }
        sw.Stop();

        double elapsedSec = sw.Elapsed.TotalSeconds;
        double charsPerSec = totalChars / elapsedSec;
        double mbPerSec = (totalChars * sizeof(char)) / (1024.0 * 1024.0) / elapsedSec;

        _output.WriteLine($"[STRESS] Processed {totalChars:N0} characters in {sw.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"[STRESS] Throughput: {charsPerSec:N0} chars/sec ({mbPerSec:F2} MB/sec)");

        Assert.True(charsPerSec > 1_000_000, $"Throughput {charsPerSec:N0} chars/sec must exceed 1M chars/sec");
    }

    [Fact]
    public void Stress_TrailSegments_HundredThousandPoints_Performance()
    {
        int count = 100_000;
        var points = new List<PointD>(count);
        var times = new List<double>(count);

        for (int i = 0; i < count; i++)
        {
            points.Add(new PointD(i * 0.5, (i % 50) * 1.2));
            times.Add(i * 0.016);
        }

        var sw = Stopwatch.StartNew();
        var segments = TrailSegments.Calculate(points, times);
        sw.Stop();

        _output.WriteLine($"[STRESS] TrailSegments calculated {segments.Count:N0} segments for {count:N0} points in {sw.Elapsed.TotalMilliseconds:F2} ms");
        Assert.True(sw.Elapsed.TotalMilliseconds < 50.0, "Should compute 100k trail segments in < 50ms");
    }

    [Fact]
    public void Stress_SessionRetentionPolicy_FiftyThousandSessions_QuotaEnforcement()
    {
        var now = DateTime.UtcNow;
        int count = 50_000;
        var sessions = new List<StoredSession>(count);
        var rand = new Random(1337);

        for (int i = 0; i < count; i++)
        {
            sessions.Add(new StoredSession(
                $"2026-08-23T15-16-45Z-{Guid.NewGuid()}",
                now.AddDays(-rand.NextDouble() * 30.0),
                rand.Next(50_000, 2_000_000)));
        }

        long maxQuota = 500_000_000; // 500MB
        var policy = new SessionRetentionPolicy(TimeSpan.FromDays(7), maxQuota);

        var sw = Stopwatch.StartNew();
        var removed = policy.SessionsToRemove(sessions, now);
        sw.Stop();

        _output.WriteLine($"[STRESS] Evaluated {count:N0} sessions: marked {removed.Count:N0} for removal in {sw.Elapsed.TotalMilliseconds:F2} ms");

        // Verify kept sessions satisfy quota and age invariants
        var kept = sessions.Where(s => !removed.Contains(s.Name)).ToList();
        long keptTotalBytes = kept.Sum(s => s.Bytes);

        Assert.True(keptTotalBytes <= maxQuota, $"Kept bytes {keptTotalBytes:N0} must be <= quota {maxQuota:N0}");
        foreach (var s in kept)
        {
            Assert.True(now - s.ModifiedAt <= TimeSpan.FromDays(7), "No expired session should be kept");
        }
        Assert.True(sw.Elapsed.TotalMilliseconds < 200.0, "50k session retention check must complete in < 200ms");
    }

    // =========================================================================
    // SECTION 5: FLAW & REGRESSION CHARACTERIZATIONS
    // =========================================================================

    /// <summary>
    /// VERIFIES RESOLUTION: CircleGestureDetector correctly rejects irregular peanut/figure-8 shaped loops.
    /// </summary>
    [Fact]
    public void Flaw_CircleGestureDetector_IrregularPeanutShape_IsRejected()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? result = null;

        // Path with radius oscillating wildly by 55%: r = 70 * (1 + 0.55 * sin(2*theta))
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

    /// <summary>
    /// VERIFIES RESOLUTION: CircleGestureDetector correctly rejects sharp non-circular geometries
    /// such as triangles using the restored variance and path-length/circumference ratio checks.
    /// </summary>
    [Fact]
    public void Flaw_CircleGestureDetector_SharpTriangle_IsRejected()
    {
        var detector = new CircleGestureDetector();
        CircleGesture? triangleResult = null;

        for (int i = 0; i < 60; i++)
        {
            PointD pt = i switch
            {
                < 20 => new PointD(300 + i * 5, 300),
                < 40 => new PointD(400 - (i - 20) * 2.5, 300 + (i - 20) * 4.3),
                _ => new PointD(350 - (i - 40) * 2.5, 386 - (i - 40) * 4.3)
            };
            triangleResult = detector.Add(pt, (double)i / 60.0) ?? triangleResult;
        }

        Assert.Null(triangleResult);
    }

    /// <summary>
    /// VERIFIES RESOLUTION: VocabularyFile.Terms guards with prop.Value.ValueKind == JsonValueKind.String.
    /// Valid string terms are preserved even if non-string properties are present.
    /// </summary>
    [Fact]
    public void Flaw_VocabularyFile_NonStringValue_KeepsValidTerms()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"bv_vocab_flaw_{Guid.NewGuid()}.json");
        try
        {
            File.WriteAllText(tempFile, """
            {
              "terms": {
                "kubectl": "Kubectl",
                "nonStringKey": 12345
              }
            }
            """);

            var terms = VocabularyFile.Terms(tempFile);
            Assert.Single(terms);
            Assert.Equal("kubectl", terms[0].Key);
            Assert.Equal("Kubectl", terms[0].Value);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    /// <summary>
    /// VERIFIES RESOLUTION: SettingsManager.AddRecentTranscript is synchronized with a thread lock.
    /// Concurrent mutations on RecentTranscripts execute without any race condition exceptions.
    /// </summary>
    [Fact]
    public void Flaw_SettingsManager_AddRecentTranscript_ThreadSafe()
    {
        var settings = new SettingsManager(TestPaths.SettingsFile());
        var exceptions = new ConcurrentBag<Exception>();

        Parallel.For(0, 30, i =>
        {
            try
            {
                settings.AddRecentTranscript($"Item #{i}");
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
    }

    /// <summary>
    /// VERIFIES RESOLUTION: SessionNaming.SessionNameRegex uses '\z' instead of '$'.
    /// Trailing newlines or whitespace are strictly rejected.
    /// </summary>
    [Fact]
    public void Flaw_SessionNaming_RegexRejectsTrailingNewline()
    {
        string nameWithTrailingNewline = "2026-08-23T15-16-45Z-b005b883-9cb8-83d9-aa8a-2ff461f04c13\n";
        bool matches = SessionNaming.IsBetterVoiceSessionName(nameWithTrailingNewline);
        Assert.False(matches, "A trailing newline must be rejected by \\z");
    }

    /// <summary>
    /// VERIFIES RESOLUTION: SessionsToRemove guards against 64-bit integer overflow.
    /// Even when session bytes approach long.MaxValue, no OverflowException is thrown.
    /// </summary>
    [Fact]
    public void Flaw_SessionRetentionPolicy_SumOverflowProtection()
    {
        var policy = new SessionRetentionPolicy(TimeSpan.FromDays(7), 10_000_000);
        var now = DateTime.UtcNow;

        var giantSessions = new List<StoredSession>
        {
            new("session1", now, long.MaxValue - 100),
            new("session2", now, 200)
        };

        var removed = policy.SessionsToRemove(giantSessions, now);
        Assert.NotNull(removed);
        Assert.Contains("session1", removed);
    }

    // =========================================================================
    // SECTION 6: APP PROFILE & LOCALE SUBSYSTEM CHECKS
    // =========================================================================

    [Theory]
    [InlineData("cmd.exe", "cmd", DeveloperAppProfile.Terminal)]
    [InlineData("powershell.exe", "powershell", DeveloperAppProfile.Terminal)]
    [InlineData("pwsh.exe", "pwsh", DeveloperAppProfile.Terminal)]
    [InlineData("windowsterminal.exe", "WindowsTerminal", DeveloperAppProfile.Terminal)]
    [InlineData("alacritty.exe", "alacritty", DeveloperAppProfile.Terminal)]
    [InlineData("code.exe", "code", DeveloperAppProfile.Editor)]
    [InlineData("devenv.exe", "devenv", DeveloperAppProfile.Editor)]
    [InlineData("cursor.exe", "cursor", DeveloperAppProfile.Editor)]
    [InlineData("chatgpt.exe", "chatgpt", DeveloperAppProfile.Ai)]
    [InlineData("notepad.exe", "notepad", DeveloperAppProfile.General)]
    [InlineData(null, null, DeveloperAppProfile.General)]
    public void Context_DeveloperAppProfileInference_DetectsExpectedProfiles(
        string? processId, string? appName, DeveloperAppProfile expected)
    {
        var profile = DeveloperAppProfileExtensions.Infer(processId, appName);
        Assert.Equal(expected, profile);
    }

    [Fact]
    public void Context_TranscriptionLanguage_EnglishAndAutoCapabilities()
    {
        var en = TranscriptionLanguage.English;
        Assert.True(en.UsesEnglishOnlyModel);
        Assert.True(en.AllowsGrammarCorrection);
        Assert.Equal("en", en.ScriptHintCode);

        var auto = TranscriptionLanguage.Automatic;
        Assert.False(auto.UsesEnglishOnlyModel);
        Assert.False(auto.AllowsGrammarCorrection);
        Assert.Null(auto.ScriptHintCode);

        // Fallback for unknown code
        var fallback = TranscriptionLanguage.FromStoredCode("unknown_language_code");
        Assert.Equal(TranscriptionLanguage.English, fallback);
    }
}
