using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using BetterVoice.App.Audio;
using BetterVoice.App.Native;
using BetterVoice.App.Overlay;
using BetterVoice.App.Services;
using BetterVoice.App.UI;
using BetterVoice.Core;

namespace BetterVoice.App;

public sealed class AppController : IDisposable
{
    private readonly SettingsManager _settingsManager = new();
    private readonly AudioRecorder _recorder = new();
    private readonly RecordingSoundController _soundController = new();
    private readonly LocalTranscriber _transcriber;
    private readonly InputMonitor _inputMonitor;

    private readonly TrailOverlayWindow _trailOverlay = new();
    private readonly RecordingHUDWindow _hud = new();
    private SetupWindow? _setupWindow;

    private bool _isRecording;
    private DateTime? _recordingStartedAt;
    private TextInsertion.AppContext _recordingContext;
    private string? _currentSessionDir;
    private bool _hasGestureScreenshot;

    public SettingsManager Settings => _settingsManager;

    public AppController()
    {
        _transcriber = new LocalTranscriber(_settingsManager);
        _inputMonitor = new InputMonitor(_settingsManager.Current.CircleMinimumAngleDegrees)
        {
            QuickTriggerMode = _settingsManager.Current.QuickTriggerMode,
            HoldDelayMilliseconds = _settingsManager.Current.QuickHoldDelayMilliseconds
        };

        _inputMonitor.ActionTriggered += OnShortcutAction;
        _inputMonitor.CircleGestureDetected += OnCircleDetected;
        _inputMonitor.MouseMoved += OnMouseMoved;
        _settingsManager.SettingsChanged += OnSettingsChanged;

        _recorder.LevelChanged += level =>
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _hud.UpdateLevel(level));
        };
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        _inputMonitor.QuickTriggerMode = settings.QuickTriggerMode;
        _inputMonitor.HoldDelayMilliseconds = settings.QuickHoldDelayMilliseconds;
        _inputMonitor.SetCircleSensitivity(settings.CircleMinimumAngleDegrees);
    }

    public void Start()
    {
        _trailOverlay.Show();
        _inputMonitor.Start();
    }

    public void ShowSettings()
    {
        if (_setupWindow == null || !_setupWindow.IsLoaded)
        {
            _setupWindow = new SetupWindow(_settingsManager);
        }
        _setupWindow.Show();
        _setupWindow.Activate();
    }

    private void OnShortcutAction(RecordingShortcutAction action)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(async () =>
        {
            switch (action)
            {
                case RecordingShortcutAction.SchedulePushToTalk:
                    // After hold delay, transition to push-to-talk
                    await Task.Delay(_settingsManager.Current.QuickHoldDelayMilliseconds);
                    if (!_isRecording)
                    {
                        _inputMonitor.NotifyPushToTalkDelayElapsed();
                    }
                    break;

                case RecordingShortcutAction.StartPushToTalk:
                case RecordingShortcutAction.ToggleLongForm:
                    if (!_isRecording)
                    {
                        StartSession();
                    }
                    else if (action == RecordingShortcutAction.ToggleLongForm)
                    {
                        await StopSessionAsync();
                    }
                    break;

                case RecordingShortcutAction.StopPushToTalk:
                    if (_isRecording)
                    {
                        await StopSessionAsync();
                    }
                    break;

                case RecordingShortcutAction.CancelPendingPushToTalk:
                    // Tap was too short or cancelled
                    break;
            }
        });
    }

    private void StartSession()
    {
        _isRecording = true;
        _recordingStartedAt = DateTime.UtcNow;
        _recordingContext = TextInsertion.GetCurrentContext();
        _hasGestureScreenshot = false;

        string sessionId = $"{DateTime.UtcNow:yyyy-MM-ddTHH-mm-ssZ}-{Guid.NewGuid()}";
        _currentSessionDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterVoice", "Sessions", sessionId);
        Directory.CreateDirectory(_currentSessionDir);

        string audioPath = Path.Combine(_currentSessionDir, "audio.wav");

        _soundController.Play(RecordingSoundCue.Started);
        _trailOverlay.ClearTrail();
        _inputMonitor.StartMouseTracking();

        string micName = "Default Microphone";
        _recorder.Start(audioPath, _settingsManager.Current.SelectedMicrophoneId);

        _hud.SetState("Listening...", micName, isRecording: true);
        _hud.Show();
    }

    private async Task StopSessionAsync()
    {
        if (!_isRecording) return;
        _isRecording = false;

        _soundController.Play(RecordingSoundCue.Finished);
        _inputMonitor.StopMouseTracking();
        _hud.SetState("Transcribing...", "BetterVoice", isRecording: false);

        string? audioPath = _recorder.CurrentFilePath;
        await _recorder.StopAsync();

        double duration = _recordingStartedAt.HasValue
            ? (DateTime.UtcNow - _recordingStartedAt.Value).TotalSeconds
            : 0;

        // Use the app that was active when dictation began; transcription and
        // overlays must not redirect delivery to a later foreground window.
        var context = _recordingContext;

        string transcript = string.Empty;
        if (!string.IsNullOrEmpty(audioPath) && File.Exists(audioPath))
        {
            transcript = await _transcriber.TranscribeAsync(audioPath, context.Profile);
        }

        var disposition = SessionCompletionPolicy.Evaluate(
            hasTranscript: !string.IsNullOrWhiteSpace(transcript),
            hasContext: _hasGestureScreenshot,
            duration: duration);

        if (disposition == SessionCompletionDisposition.Deliver && !string.IsNullOrWhiteSpace(transcript))
        {
            await TextInsertion.InsertTextAsync(transcript, context);
            _settingsManager.AddRecentTranscript(transcript);
        }

        _trailOverlay.ClearTrail();
        _hud.Hide();
    }

    private void OnMouseMoved(PointD point)
    {
        if (_isRecording)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => _trailOverlay.AddPoint(point));
        }
    }

    private void OnCircleDetected(CircleGesture gesture)
    {
        if (!_isRecording || string.IsNullOrEmpty(_currentSessionDir)) return;

        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _trailOverlay.HighlightCircle(gesture);
            string screenshotPath = Path.Combine(_currentSessionDir, "context.png");
            try
            {
                ScreenshotCapture.Capture(gesture, screenshotPath);
                _hasGestureScreenshot = true;
                _hud.SetState("Target Circled", "Context Saved", isRecording: true);
            }
            catch
            {
                // ignored
            }
        });
    }

    public void Dispose()
    {
        _inputMonitor.Dispose();
        _settingsManager.SettingsChanged -= OnSettingsChanged;
        _recorder.Dispose();
        _trailOverlay.Close();
        _hud.Close();
        _setupWindow?.Close();
    }
}
