using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using BetterVoice.Core;
using Whisper.net;

namespace BetterVoice.App.Services;

public sealed class LocalTranscriber : IDisposable
{
    private readonly SettingsManager _settingsManager;
    private readonly GrammarCorrector _grammarCorrector = new();
    private WhisperFactory? _factory;
    private string? _loadedModelPath;

    public LocalTranscriber(SettingsManager settingsManager)
    {
        _settingsManager = settingsManager;
    }

    private WhisperFactory GetOrCreateFactory(string modelPath)
    {
        if (_factory != null && _loadedModelPath == modelPath)
        {
            return _factory;
        }

        _factory?.Dispose();
        _factory = WhisperFactory.FromPath(modelPath);
        _loadedModelPath = modelPath;
        return _factory;
    }

    public async Task<string> TranscribeAsync(string audioWavPath, DeveloperAppProfile profile)
    {
        if (!File.Exists(audioWavPath)) return string.Empty;
        var info = new FileInfo(audioWavPath);
        if (info.Length < 2048) return string.Empty;

        string modelPath = GetModelPath();
        if (!File.Exists(modelPath))
        {
            return string.Empty;
        }

        string raw = await RunWhisperAsync(audioWavPath, modelPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string result = raw.Trim();

        if (_settingsManager.Current.DeveloperCleanupEnabled)
        {
            string vocabPath = VocabularyFile.DefaultPath();
            var overrides = VocabularyFile.Terms(vocabPath);
            result = DeveloperTextCleanup.Apply(result, profile, overrides);
        }

        var lang = TranscriptionLanguage.FromStoredCode(_settingsManager.Current.TranscriptionLanguageCode);
        if (_settingsManager.Current.GrammarCorrectionEnabled && lang.AllowsGrammarCorrection)
        {
            result = await _grammarCorrector.CorrectAsync(result);
        }

        return result;
    }

    private async Task<string> RunWhisperAsync(string wavPath, string modelPath)
    {
        try
        {
            var factory = GetOrCreateFactory(modelPath);
            var lang = TranscriptionLanguage.FromStoredCode(_settingsManager.Current.TranscriptionLanguageCode);
            string whisperLang = lang.UsesEnglishOnlyModel ? "en" : (lang.Code == TranscriptionLanguage.AutomaticCode ? "auto" : lang.Code);

            using var processor = factory.CreateBuilder()
                .WithLanguage(whisperLang)
                .Build();

            await using var fileStream = File.OpenRead(wavPath);
            var sb = new StringBuilder();

            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    sb.Append(segment.Text).Append(' ');
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Whisper transcription error: {ex}");
            return string.Empty;
        }
    }

    public static string GetModelPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterVoice", "Models", "ggml-tiny.en.bin");
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _grammarCorrector.Dispose();
    }
}
