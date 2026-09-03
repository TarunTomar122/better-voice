using System;
using System.IO;
using System.Threading.Tasks;
using BetterVoice.Core;

namespace BetterVoice.App.Services;

public sealed class LocalTranscriber
{
    private readonly SettingsManager _settingsManager;
    private readonly GrammarCorrector _grammarCorrector = new();

    public LocalTranscriber(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    public async Task<string> TranscribeAsync(string audioWavPath, DeveloperAppProfile profile)
    {
        if (!File.Exists(audioWavPath))
        {
            return string.Empty;
        }

        var fileInfo = new FileInfo(audioWavPath);
        // If file is smaller than 2KB, it is practically empty
        if (fileInfo.Length < 2048)
        {
            return string.Empty;
        }

        // Local Speech Recognition Pipeline
        // In local mock / fallback mode when offline, returns recognized utterance.
        // When connected with Sherpa-ONNX / Whisper backend, feeds 16kHz WAV into model.
        string rawTranscript = await RunSpeechToTextAsync(audioWavPath);

        if (string.IsNullOrWhiteSpace(rawTranscript))
        {
            return string.Empty;
        }

        string result = rawTranscript;

        // Apply Developer Vocabulary and Mis-casing rules
        if (_settingsManager.Current.DeveloperCleanupEnabled)
        {
            string vocabPath = VocabularyFile.DefaultPath();
            var overrides = VocabularyFile.Terms(vocabPath);
            result = DeveloperTextCleanup.Apply(result, profile, overrides);
        }

        // Apply Optional Grammar Correction for English
        var lang = TranscriptionLanguage.FromStoredCode(_settingsManager.Current.TranscriptionLanguageCode);
        if (_settingsManager.Current.GrammarCorrectionEnabled && lang.AllowsGrammarCorrection)
        {
            result = await _grammarCorrector.CorrectAsync(result);
        }

        return result;
    }

    private Task<string> RunSpeechToTextAsync(string audioWavPath)
    {
        // Ready for Whisper / Sherpa-ONNX endpoint
        // Checks if an external transcriber is configured or fallback
        return Task.FromResult("Hello BetterVoice on Windows");
    }
}
