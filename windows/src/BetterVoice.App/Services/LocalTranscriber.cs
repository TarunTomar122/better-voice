using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using BetterVoice.Core;
using Whisper.net;

namespace BetterVoice.App.Services;

public sealed class LocalTranscriber : IDisposable
{
    private static readonly Regex NoSpeechMarker = new(
        @"\[(?:BLANK_AUDIO|SILENCE|NOISE|MUSIC|APPLAUSE|LAUGHTER)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private readonly SettingsManager _settingsManager;
    private readonly GrammarCorrector _grammarCorrector = new();
    private readonly SemaphoreSlim _modelDownloadLock = new(1, 1);
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

        var language = TranscriptionLanguage.FromStoredCode(_settingsManager.Current.TranscriptionLanguageCode);
        string modelPath = GetModelPath(language);
        if (!await EnsureModelDownloadedAsync(modelPath, language.UsesEnglishOnlyModel)) return string.Empty;

        string raw = await RunWhisperAsync(audioWavPath, modelPath);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        string result = CleanWhisperOutput(raw);
        if (string.IsNullOrWhiteSpace(result))
        {
            return string.Empty;
        }

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

    public static string CleanWhisperOutput(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string cleaned = NoSpeechMarker.Replace(text, " ");
        return string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
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

    public static string GetModelPath() => GetModelPath(TranscriptionLanguage.English);

    public static string GetModelPath(TranscriptionLanguage language)
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BetterVoice", "Models", language.UsesEnglishOnlyModel ? "ggml-tiny.en.bin" : "ggml-tiny.bin");
    }

    private async Task<bool> EnsureModelDownloadedAsync(string modelPath, bool englishOnly)
    {
        if (File.Exists(modelPath)) return true;

        await _modelDownloadLock.WaitAsync();
        try
        {
            if (File.Exists(modelPath)) return true;

            string? modelDirectory = Path.GetDirectoryName(modelPath);
            if (!string.IsNullOrEmpty(modelDirectory)) Directory.CreateDirectory(modelDirectory);

            const string revision = "5359861c739e955e79d9a303bcbc70fb988958b1";
            string modelName = englishOnly ? "ggml-tiny.en.bin" : "ggml-tiny.bin";
            string url = $"https://huggingface.co/ggerganov/whisper.cpp/resolve/{revision}/{modelName}";
            string temporary = modelPath + $".{Guid.NewGuid():N}.download";
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
                await using Stream source = await client.GetStreamAsync(url);
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await source.CopyToAsync(target);
                    await target.FlushAsync();
                }
                File.Move(temporary, modelPath, false);
                return true;
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
        catch
        {
            return false;
        }
        finally
        {
            _modelDownloadLock.Release();
        }
    }

    public void Dispose()
    {
        _factory?.Dispose();
        _grammarCorrector.Dispose();
        _modelDownloadLock.Dispose();
    }
}
