using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BetterVoice.App.Audio;

public sealed class AudioRecorder : IDisposable
{
    private WasapiRecorder? _capture;
    private WaveFileWriter? _writer;
    private TaskCompletionSource _stopCompletion = CompletedStop();
    private string? _currentFilePath;
    private bool _isRecording;

    public event Action<float>? LevelChanged;
    public event Action? RecordingFinished;

    public bool IsRecording => _isRecording;
    public string? CurrentFilePath => _currentFilePath;

    public static List<(string Id, string Name)> GetInputDevices()
    {
        var devices = new List<(string Id, string Name)>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var endpoint in endpoints)
            {
                devices.Add((endpoint.ID, endpoint.FriendlyName));
            }
        }
        catch
        {
            // An empty list lets the UI retain the system-default fallback.
        }
        return devices;
    }

    public void Start(string outputWavPath, string? deviceId = null)
    {
        if (_isRecording) StopAsync().GetAwaiter().GetResult();

        _capture?.Dispose();
        _writer?.Dispose();
        _capture = null;
        _writer = null;

        _currentFilePath = outputWavPath;
        string? dir = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var enumerator = new MMDeviceEnumerator();
        MMDevice? selectedDevice = null;
        try
        {
            selectedDevice = string.IsNullOrEmpty(deviceId)
                ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console)
                : enumerator.GetDevice(deviceId);
        }
        catch when (!string.IsNullOrEmpty(deviceId))
        {
            selectedDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        }

        var targetFormat = new WaveFormat(16_000, 16, 1);
        var builder = new WasapiRecorderBuilder()
            .WithSharedMode()
            .WithEventSync()
            .WithFormat(targetFormat)
            .WithMmcssThreadPriority("Capture");
        if (selectedDevice != null) builder.WithDevice(selectedDevice);
        _capture = builder.Build();
        selectedDevice?.Dispose();

        _writer = new WaveFileWriter(outputWavPath, targetFormat);
        _stopCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _isRecording = true;

        try
        {
            _capture.StartRecording();
        }
        catch
        {
            _isRecording = false;
            _writer.Dispose();
            _writer = null;
            _stopCompletion.TrySetResult();
            throw;
        }
    }

    public async Task StopAsync()
    {
        if (!_isRecording || _capture == null) return;

        _capture.StopRecording();
        await _stopCompletion.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    private void OnDataAvailable(ReadOnlySpan<byte> data, AudioClientBufferFlags flags, long devicePosition, long qpcPosition)
    {
        if (_writer == null || data.IsEmpty) return;

        try
        {
            _writer.Write(data);

            float peak = 0;
            double sumSquares = 0;
            int sampleCount = data.Length / sizeof(short);
            for (int offset = 0; offset + 1 < data.Length; offset += sizeof(short))
            {
                short sample = (short)(data[offset] | data[offset + 1] << 8);
                float normalized = Math.Abs(sample / 32768f);
                if (normalized > peak) peak = normalized;
                sumSquares += normalized * normalized;
            }
            float rms = sampleCount > 0 ? (float)Math.Sqrt(sumSquares / sampleCount) : 0;
            LevelChanged?.Invoke(Math.Min(1f, peak * 0.6f + rms));
        }
        catch
        {
            // Preserve an empty/partial recording rather than crashing the app.
            _capture?.StopRecording();
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        try
        {
            _writer?.Dispose();
            _writer = null;
            _isRecording = false;
            RecordingFinished?.Invoke();

            if (e.Exception != null) _stopCompletion.TrySetException(e.Exception);
            else _stopCompletion.TrySetResult();
        }
        catch (Exception ex)
        {
            _stopCompletion.TrySetException(ex);
        }
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Disposal must not turn device shutdown into an app crash.
        }
        _capture?.Dispose();
        _writer?.Dispose();
    }

    private static TaskCompletionSource CompletedStop()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}
