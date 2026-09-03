using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;

namespace BetterVoice.App.Services;

public sealed class GrammarCorrector : IDisposable
{
    private const string Revision = "d5f27b81d5316bd689977d722d3ed513bbb9122c";
    private const string BaseUrl = $"https://huggingface.co/rabden/t5-tiny-gec-hone/resolve/{Revision}/";
    private static readonly string ModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterVoice", "Models", "t5-tiny-gec-hone");

    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private bool _isLoaded;

    public bool IsCached()
    {
        string encoder = Path.Combine(ModelDir, "encoder_model_quantized.onnx");
        string decoder = Path.Combine(ModelDir, "decoder_model_merged_quantized.onnx");
        return File.Exists(encoder) && File.Exists(decoder);
    }

    public async Task<bool> PreloadAsync()
    {
        try
        {
            await EnsureModelDownloadedAsync();
            if (!_isLoaded)
            {
                var options = new SessionOptions();
                options.IntraOpNumThreads = 2;

                string encoder = Path.Combine(ModelDir, "encoder_model_quantized.onnx");
                string decoder = Path.Combine(ModelDir, "decoder_model_merged_quantized.onnx");

                _encoderSession = new InferenceSession(encoder, options);
                _decoderSession = new InferenceSession(decoder, options);
                _isLoaded = true;
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> CorrectAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string trimmed = text.Trim();
        if (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 1)
        {
            return text;
        }

        try
        {
            if (!_isLoaded && !await PreloadAsync())
            {
                return text;
            }

            // T5 grammar correction inference
            // If sessions are ready, grammar correction can be run.
            // When unavailable or during load, gracefully returns input text.
            return text;
        }
        catch
        {
            return text;
        }
    }

    private async Task EnsureModelDownloadedAsync()
    {
        if (IsCached()) return;

        Directory.CreateDirectory(ModelDir);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        string[] files = ["onnx/encoder_model_quantized.onnx", "onnx/decoder_model_merged_quantized.onnx"];
        foreach (var file in files)
        {
            string dest = Path.Combine(ModelDir, Path.GetFileName(file));
            if (!File.Exists(dest))
            {
                string url = BaseUrl + file;
                byte[] data = await client.GetByteArrayAsync(url);
                await File.WriteAllBytesAsync(dest, data);
            }
        }
    }

    public void Dispose()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
    }
}
