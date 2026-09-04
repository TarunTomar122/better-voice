using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BetterVoice.Core;

namespace BetterVoice.App.Native;

public sealed class InputMonitor : IDisposable
{
    private readonly struct KeyEvent
    {
        public uint VkCode { get; }
        public bool IsKeyDown { get; }
        public double Timestamp { get; }

        public KeyEvent(uint vkCode, bool isKeyDown, double timestamp)
        {
            VkCode = vkCode;
            IsKeyDown = isKeyDown;
            Timestamp = timestamp;
        }
    }

    private readonly Channel<KeyEvent> _eventChannel = Channel.CreateUnbounded<KeyEvent>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = true
    });

    private IntPtr _hookHandle = IntPtr.Zero;
    private Win32Api.HookProc? _hookDelegate;
    private readonly CancellationTokenSource _cts = new();
    private Task? _processingTask;
    private System.Threading.Timer? _mouseTimer;

    private readonly CircleGestureDetector _gestureDetector;
    private RecordingShortcutState _shortcutState = new();
    private ModifierDoubleTapDetector _quickDoubleTapDetector = new();
    private ModifierToggleTapDetector _quickToggleTapDetector = new();
    private ModifierChordEngagement _longChordEngagement = new();
    private bool _longChordWasPressed;

    private bool _altDown;
    private bool _winDown;
    private bool _ctrlDown;
    private bool _shiftDown;

    private PointD? _lastMousePoint;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    public event Action<RecordingShortcutAction>? ActionTriggered;
    public event Action<CircleGesture>? CircleGestureDetected;
    public event Action<PointD>? MouseMoved;

    public RecordingTriggerMode QuickTriggerMode { get; set; } = RecordingTriggerMode.Hold;
    public int HoldDelayMilliseconds { get; set; } = QuickNoteHoldDelay.DefaultMilliseconds;

    public InputMonitor(double circleSensitivity = 340)
    {
        _gestureDetector = new CircleGestureDetector(circleSensitivity);
    }

    public void Start()
    {
        _hookDelegate = HookCallback;
        using var curProcess = Process.GetCurrentProcess();
        using var curModule = curProcess.MainModule;
        IntPtr modHandle = Win32Api.GetModuleHandle(curModule?.ModuleName);

        _hookHandle = Win32Api.SetWindowsHookEx(
            Win32Api.WH_KEYBOARD_LL,
            _hookDelegate,
            modHandle,
            0);

        _processingTask = Task.Run(ProcessEventsAsync);
    }

    public void StartMouseTracking()
    {
        _lastMousePoint = null;
        _mouseTimer?.Dispose();
        _mouseTimer = new System.Threading.Timer(SampleMouse, null, 0, 16);
    }

    public void StopMouseTracking()
    {
        _mouseTimer?.Dispose();
        _mouseTimer = null;
        _lastMousePoint = null;
        _gestureDetector.Reset();
    }

    public void SetCircleSensitivity(double minimumAngleDegrees) =>
        _gestureDetector.SetMinimumAngleDegrees(minimumAngleDegrees);

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && lParam != IntPtr.Zero)
        {
            var hookStruct = Marshal.PtrToStructure<Win32Api.KBDLLHOOKSTRUCT>(lParam);
            int msg = (int)wParam;
            bool isKeyDown = msg == Win32Api.WM_KEYDOWN || msg == Win32Api.WM_SYSKEYDOWN;
            bool isKeyUp = msg == Win32Api.WM_KEYUP || msg == Win32Api.WM_SYSKEYUP;

            if (isKeyDown || isKeyUp)
            {
                double now = _stopwatch.Elapsed.TotalSeconds;
                _eventChannel.Writer.TryWrite(new KeyEvent(hookStruct.vkCode, isKeyDown, now));
            }
        }

        return Win32Api.CallNextHookEx(_hookHandle, nCode, wParam, lParam);
    }

    private async Task ProcessEventsAsync()
    {
        var reader = _eventChannel.Reader;
        while (await reader.WaitToReadAsync(_cts.Token))
        {
            while (reader.TryRead(out var ev))
            {
                HandleKeyEvent(ev);
            }
        }
    }

    private void HandleKeyEvent(KeyEvent ev)
    {
        bool isModifier = false;
        switch (ev.VkCode)
        {
            case Win32Api.VK_LMENU:
            case Win32Api.VK_RMENU:
                _altDown = ev.IsKeyDown;
                isModifier = true;
                break;
            case Win32Api.VK_LWIN:
            case Win32Api.VK_RWIN:
                _winDown = ev.IsKeyDown;
                isModifier = true;
                break;
            case Win32Api.VK_LCONTROL:
            case Win32Api.VK_RCONTROL:
                _ctrlDown = ev.IsKeyDown;
                isModifier = true;
                break;
            case Win32Api.VK_LSHIFT:
            case Win32Api.VK_RSHIFT:
                _shiftDown = ev.IsKeyDown;
                isModifier = true;
                break;
        }

        if (!isModifier && ev.IsKeyDown)
        {
            _quickDoubleTapDetector.NonModifierKeyPressed();
            _quickToggleTapDetector.NonModifierKeyPressed();
        }

        // On Windows: Alt is QuickModifier, Win+Alt is LongShortcut
        bool quickActive = _altDown;
        bool longActive = _winDown && _altDown;
        bool otherModifier = _ctrlDown || _shiftDown;

        if (QuickTriggerMode == RecordingTriggerMode.Hold)
        {
            var actions = _shortcutState.FlagsChangedForActive(quickActive: quickActive, longActive: longActive, otherModifier: otherModifier);
            foreach (var action in actions)
            {
                ActionTriggered?.Invoke(action);
            }
        }
        else
        {
            var binding = new ModifierBindingState(
                bindingCommand: true,
                bindingOption: true,
                bindingControl: false,
                bindingShift: false,
                command: _winDown,
                option: _altDown,
                control: _ctrlDown,
                shift: _shiftDown);
            bool longChordPressed = _longChordEngagement.IsPressed(binding);

            if (longActive && !_longChordWasPressed)
            {
                ActionTriggered?.Invoke(RecordingShortcutAction.ToggleLongForm);
                _quickDoubleTapDetector.Reset();
                _quickToggleTapDetector.Reset();
            }
            _longChordWasPressed = longChordPressed;

            if (!longChordPressed && QuickTriggerMode == RecordingTriggerMode.DoubleTap)
            {
                if (_quickDoubleTapDetector.ModifierChanged(quickActive, ev.Timestamp))
                {
                    ActionTriggered?.Invoke(RecordingShortcutAction.ToggleLongForm);
                }
            }
            else if (!longChordPressed && QuickTriggerMode == RecordingTriggerMode.Toggle)
            {
                if (_quickToggleTapDetector.ModifierChanged(quickActive, ev.Timestamp))
                {
                    ActionTriggered?.Invoke(RecordingShortcutAction.ToggleLongForm);
                }
            }
        }
    }

    private void SampleMouse(object? state)
    {
        if (!Win32Api.GetCursorPos(out var pt)) return;
        var current = new PointD(pt.X, pt.Y);
        if (_lastMousePoint.HasValue && _lastMousePoint.Value == current)
        {
            return;
        }

        _lastMousePoint = current;
        double now = _stopwatch.Elapsed.TotalSeconds;

        MouseMoved?.Invoke(current);

        var gesture = _gestureDetector.Add(current, now);
        if (gesture.HasValue)
        {
            CircleGestureDetected?.Invoke(gesture.Value);
        }
    }

    public void NotifyPushToTalkDelayElapsed()
    {
        var actions = _shortcutState.PushToTalkDelayElapsed();
        foreach (var action in actions)
        {
            ActionTriggered?.Invoke(action);
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        StopMouseTracking();

        if (_hookHandle != IntPtr.Zero)
        {
            Win32Api.UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }
}
