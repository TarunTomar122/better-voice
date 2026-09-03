using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using BetterVoice.Core;

namespace BetterVoice.App.Services;

public sealed class AppSettings
{
    public string? SelectedMicrophoneId { get; set; }
    public double CircleMinimumAngleDegrees { get; set; } = 340;
    public RecordingTriggerMode QuickTriggerMode { get; set; } = RecordingTriggerMode.Hold;
    public RecordingTriggerMode LongTriggerMode { get; set; } = RecordingTriggerMode.Toggle;
    public int QuickHoldDelayMilliseconds { get; set; } = 140;
    public bool GrammarCorrectionEnabled { get; set; } = false;
    public bool DeveloperCleanupEnabled { get; set; } = true;
    public string TranscriptionLanguageCode { get; set; } = TranscriptionLanguage.EnglishCode;
    public List<string> RecentTranscripts { get; set; } = [];
}

public sealed class SettingsManager
{
    private static readonly string SettingsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "BetterVoice");

    private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    public AppSettings Current { get; private set; }

    public SettingsManager()
    {
        Current = Load();
    }

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsFile))
            {
                string json = File.ReadAllText(SettingsFile);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // fallback to default
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            string json = JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(SettingsFile, json);
        }
        catch
        {
            // ignored
        }
    }

    public void AddRecentTranscript(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Current.RecentTranscripts.Insert(0, text.Trim());
        if (Current.RecentTranscripts.Count > 10)
        {
            Current.RecentTranscripts.RemoveRange(10, Current.RecentTranscripts.Count - 10);
        }
        Save();
    }
}
