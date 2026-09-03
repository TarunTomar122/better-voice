using System;
using System.Collections.Generic;
using System.IO;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace BetterVoice.App.Audio;

public sealed class AudioRecorder : IDisposable
{
    private WasapiCapture? _capture;
    private WaveFileWriter? _writer;
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
            var enumerator = new MMDeviceEnumerator();
            var endpoints = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active);
            foreach (var ep in endpoints)
            {
                devices.Add((ep.ID, ep.FriendlyName));
            }
        }
        catch
        {
            // fallback
        }
        return devices;
    }

    public void Start(string outputWavPath, string? deviceId = null)
    {
        Stop();

        _currentFilePath = outputWavPath;
        string? dir = Path.GetDirectoryName(outputWavPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        MMDevice? device = null;
        var enumerator = new MMDeviceEnumerator();
        if (!string.IsNullOrEmpty(deviceId))
        {
            try
            {
                device = enumerator.GetDevice(deviceId);
            }
            catch
            {
                device = null;
            }
        }

        device ??= enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);
        _capture = new WasapiCapture(device);

        // 16kHz, 16-bit, Mono PCM format is ideal for local speech recognition
        var targetFormat = new WaveFormat(16000, 16, 1);
        _writer = new WaveFileWriter(outputWavPath, targetFormat);

        _capture.DataAvailable += (s, e) =>
        {
            if (_writer == null || e.BytesRecorded == 0) return;

            // Compute audio peak and RMS level
            float maxSample = 0;
            double sumSquares = 0;
            int sampleCount = 0;

            var waveFormat = _capture.WaveFormat;
            if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
            {
                for (int i = 0; i < e.BytesRecorded; i += 4)
                {
                    float sample = BitConverter.ToSingle(e.Buffer, i);
                    float abs = Math.Abs(sample);
                    if (abs > maxSample) maxSample = abs;
                    sumSquares += sample * sample;
                    sampleCount++;
                }

                // Resample to 16kHz 16-bit mono
                byte[] pcm16 = ConvertFloatToPcm16(e.Buffer, e.BytesRecorded, waveFormat.Channels);
                _writer.Write(pcm16, 0, pcm16.Length);
            }
            else if (waveFormat.BitsPerSample == 16)
            {
                for (int i = 0; i < e.BytesRecorded; i += 2)
                {
                    short sample = BitConverter.ToInt16(e.Buffer, i);
                    float normalized = Math.Abs(sample / 32768.0f);
                    if (normalized > maxSample) maxSample = normalized;
                    sumSquares += normalized * normalized;
                    sampleCount++;
                }

                // Direct write
                _writer.Write(e.Buffer, 0, e.BytesRecorded);
            }

            float rms = sampleCount > 0 ? (float)Math.Sqrt(sumSquares / sampleCount) : 0;
            float combined = Math.Min(1.0f, (maxSample * 0.6f) + (rms * 0.4f) * 2.5f);
            LevelChanged?.Invoke(combined);
        };

        _capture.RecordingStopped += (s, e) =>
        {
            _writer?.Dispose();
            _writer = null;
            _isRecording = false;
            RecordingFinished?.Invoke();
        };

        _isRecording = true;
        _capture.StartRecording();
    }

    public void Stop()
    {
        if (_isRecording && _capture != null)
        {
            _capture.StopRecording();
        }
    }

    private static byte[] ConvertFloatToPcm16(byte[] floatBuffer, int byteCount, int channels)
    {
        int floatCount = byteCount / 4;
        int monoCount = floatCount / channels;
        var pcm16 = new byte[monoCount * 2];

        int outIndex = 0;
        for (int i = 0; i < floatCount; i += channels)
        {
            // Average channels to mono
            float sum = 0;
            for (int ch = 0; ch < channels; ch++)
            {
                sum += BitConverter.ToSingle(floatBuffer, (i + ch) * 4);
            }
            float avg = sum / channels;
            short val = (short)Math.Clamp((int)(avg * 32767.0f), short.MinValue, short.MaxValue);

            pcm16[outIndex++] = (byte)(val & 0xFF);
            pcm16[outIndex++] = (byte)((val >> 8) & 0xFF);
        }

        return pcm16;
    }

    public void Dispose()
    {
        Stop();
        _capture?.Dispose();
        _writer?.Dispose();
    }
}
