using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterVoice.Core;

namespace BetterVoice.App.Services;

public sealed class AppSettings
{
    public string? SelectedMicrophoneId { get; set; }
    public RecordingTriggerMode QuickTriggerMode { get; set; } = RecordingTriggerMode.Hold;
    public int QuickHoldDelayMilliseconds { get; set; } = 140;
    public double CircleMinimumAngleDegrees { get; set; } = 340;
    public string TranscriptionLanguageCode { get; set; } = "en";
    public bool DeveloperCleanupEnabled { get; set; } = true;
    public bool GrammarCorrectionEnabled { get; set; } = false;
    public List<string> RecentTranscripts { get; set; } = [];
}

public sealed class SettingsManager
{
    private readonly string _settingsFilePath;
    private readonly object _lock = new();
    public AppSettings Current { get; private set; }
    public event Action<AppSettings>? SettingsChanged;

    public SettingsManager(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BetterVoice", "settings.json");
        string? appData = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrEmpty(appData)) Directory.CreateDirectory(appData);
        Current = Load();
    }

    public AppSettings Load()
    {
        lock (_lock)
        {
            if (!File.Exists(_settingsFilePath))
            {
                return new AppSettings();
            }

            try
            {
                string json = File.ReadAllText(_settingsFilePath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
    }

    public void Save()
    {
        AppSettings snapshot;
        lock (_lock)
        {
            try
            {
                string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch
            {
                // ignored
            }
            snapshot = Current;
        }
        SettingsChanged?.Invoke(snapshot);
    }

    public void AddRecentTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        lock (_lock)
        {
            Current.RecentTranscripts.Insert(0, text.Trim());
            if (Current.RecentTranscripts.Count > 10)
            {
                Current.RecentTranscripts.RemoveRange(10, Current.RecentTranscripts.Count - 10);
            }
            Save();
        }
    }
}
