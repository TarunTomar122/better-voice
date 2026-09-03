using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using BetterVoice.App.Audio;
using BetterVoice.App.Services;
using BetterVoice.Core;

namespace BetterVoice.App.UI;

public partial class SetupWindow : Window
{
    private readonly SettingsManager _settingsManager;
    private bool _isInitializing = true;

    public SetupWindow(SettingsManager settingsManager)
    {
        InitializeComponent();
        _settingsManager = settingsManager;
        LoadSettings();
        _isInitializing = false;
    }

    private void LoadSettings()
    {
        // Populate Microphones
        var devices = AudioRecorder.GetInputDevices();
        MicComboBox.Items.Clear();
        MicComboBox.Items.Add(new ComboBoxItem { Content = "Default System Microphone", Tag = (string?)null });
        int selectedIndex = 0;

        for (int i = 0; i < devices.Count; i++)
        {
            var item = new ComboBoxItem { Content = devices[i].Name, Tag = devices[i].Id };
            MicComboBox.Items.Add(item);
            if (devices[i].Id == _settingsManager.Current.SelectedMicrophoneId)
            {
                selectedIndex = i + 1;
            }
        }
        MicComboBox.SelectedIndex = selectedIndex;

        // Populate Trigger Mode
        TriggerModeComboBox.SelectedIndex = (int)_settingsManager.Current.QuickTriggerMode;

        // Hold Delay
        HoldDelaySlider.Value = _settingsManager.Current.QuickHoldDelayMilliseconds;
        HoldDelayLabel.Text = $"{_settingsManager.Current.QuickHoldDelayMilliseconds} ms";

        // Circle angle sensitivity
        AngleSlider.Value = _settingsManager.Current.CircleMinimumAngleDegrees;
        AngleLabel.Text = $"{(int)_settingsManager.Current.CircleMinimumAngleDegrees}°";

        // Languages
        LanguageComboBox.Items.Clear();
        int langIndex = 0;
        for (int i = 0; i < TranscriptionLanguage.All.Count; i++)
        {
            var lang = TranscriptionLanguage.All[i];
            LanguageComboBox.Items.Add(new ComboBoxItem { Content = $"{lang.Name} ({lang.Code})", Tag = lang.Code });
            if (lang.Code == _settingsManager.Current.TranscriptionLanguageCode)
            {
                langIndex = i;
            }
        }
        LanguageComboBox.SelectedIndex = langIndex;

        // Toggles
        DeveloperCleanupCheckBox.IsChecked = _settingsManager.Current.DeveloperCleanupEnabled;
        GrammarCorrectionCheckBox.IsChecked = _settingsManager.Current.GrammarCorrectionEnabled;
    }

    private void OnMicChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (MicComboBox.SelectedItem is ComboBoxItem item)
        {
            _settingsManager.Current.SelectedMicrophoneId = item.Tag as string;
            _settingsManager.Save();
        }
    }

    private void OnTriggerModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        _settingsManager.Current.QuickTriggerMode = (RecordingTriggerMode)TriggerModeComboBox.SelectedIndex;
        _settingsManager.Save();
    }

    private void OnHoldDelayChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        int val = (int)e.NewValue;
        HoldDelayLabel.Text = $"{val} ms";
        _settingsManager.Current.QuickHoldDelayMilliseconds = val;
        _settingsManager.Save();
    }

    private void OnAngleChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isInitializing) return;
        int val = (int)e.NewValue;
        AngleLabel.Text = $"{val}°";
        _settingsManager.Current.CircleMinimumAngleDegrees = val;
        _settingsManager.Save();
    }

    private void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing) return;
        if (LanguageComboBox.SelectedItem is ComboBoxItem item && item.Tag is string code)
        {
            _settingsManager.Current.TranscriptionLanguageCode = code;
            _settingsManager.Save();
        }
    }

    private void OnDeveloperCleanupToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settingsManager.Current.DeveloperCleanupEnabled = DeveloperCleanupCheckBox.IsChecked ?? true;
        _settingsManager.Save();
    }

    private void OnGrammarToggled(object sender, RoutedEventArgs e)
    {
        if (_isInitializing) return;
        _settingsManager.Current.GrammarCorrectionEnabled = GrammarCorrectionCheckBox.IsChecked ?? false;
        _settingsManager.Save();
    }

    private void OnOpenVocabularyClicked(object sender, RoutedEventArgs e)
    {
        string path = VocabularyFile.DefaultPath();
        VocabularyFile.CreateTemplateIfMissing(path);
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // fallback
        }
    }

    private void OnOpenSessionsClicked(object sender, RoutedEventArgs e)
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterVoice", "Sessions");
        Directory.CreateDirectory(dir);
        try
        {
            Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
        }
        catch
        {
            // fallback
        }
    }
}
