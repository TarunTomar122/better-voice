using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BetterVoice.App.Services;

public sealed class GrammarCorrector : IDisposable
{
    private const string Revision = "d5f27b81d5316bd689977d722d3ed513bbb9122c";
    private const string BaseUrl = $"https://huggingface.co/rabden/t5-tiny-gec-hone/resolve/{Revision}/";
    private const int EosTokenId = 1;
    private const int DecoderStartTokenId = 0;
    private const int DecoderLayers = 4;
    private const int DecoderHeads = 4;
    private const int DecoderHeadSize = 64;
    private const int MaximumInputTokens = 512;
    private const int MaximumGeneratedTokens = 64;

    private static readonly string DefaultModelDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BetterVoice", "Models", "t5-tiny-gec-hone");

    private readonly string _modelDir;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private InferenceSession? _encoderSession;
    private InferenceSession? _decoderSession;
    private T5Tokenizer? _tokenizer;
    private bool _isLoaded;

    public GrammarCorrector(string? modelDirectory = null)
    {
        _modelDir = modelDirectory ?? DefaultModelDir;
    }

    public bool IsCached() => RequiredFiles.All(file => File.Exists(Path.Combine(_modelDir, file.LocalName)));

    public async Task<bool> PreloadAsync()
    {
        await _loadLock.WaitAsync();
        try
        {
            if (_isLoaded) return true;

            await EnsureModelDownloadedAsync();
            var options = new SessionOptions { IntraOpNumThreads = 2 };
            _encoderSession = new InferenceSession(Path.Combine(_modelDir, "encoder_model_quantized.onnx"), options);
            _decoderSession = new InferenceSession(Path.Combine(_modelDir, "decoder_model_merged_quantized.onnx"), options);
            _tokenizer = T5Tokenizer.Load(Path.Combine(_modelDir, "tokenizer.json"));
            _isLoaded = true;
            return true;
        }
        catch
        {
            DisposeSessions();
            return false;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public async Task<string> CorrectAsync(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return text;
        string trimmed = text.Trim();
        if (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 1) return text;

        try
        {
            if (!_isLoaded && !await PreloadAsync()) return text;

            int[] inputIds = _tokenizer!.Encode(trimmed, MaximumInputTokens);
            if (inputIds.Length == 0) return text;

            long[] encoderIds = inputIds.Select(id => (long)id).ToArray();
            long[] attentionValues = Enumerable.Repeat(1L, inputIds.Length).ToArray();
            var encoderInputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(encoderIds, [1, inputIds.Length])),
                NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(attentionValues, [1, inputIds.Length]))
            };

            using var encoderResults = _encoderSession!.Run(encoderInputs);
            Tensor<float> encoderHiddenStates = encoderResults.First(result => result.Name == "last_hidden_state").AsTensor<float>();
            List<int> generated = [];
            Tensor<float>[] past = CreateEmptyDecoderCache();
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue>? previousDecoderResults = null;
            int nextInputId = DecoderStartTokenId;
            bool completed = false;

            try
            {
                for (int step = 0; step < MaximumGeneratedTokens; step++)
                {
                    var decoderInputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(new[] { (long)nextInputId }, [1, 1])),
                        NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenStates),
                        NamedOnnxValue.CreateFromTensor("encoder_attention_mask", new DenseTensor<long>(attentionValues, [1, inputIds.Length]))
                    };
                    for (int layer = 0; layer < DecoderLayers; layer++)
                    {
                        decoderInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{layer}.decoder.key", past[layer * 2]));
                        decoderInputs.Add(NamedOnnxValue.CreateFromTensor($"past_key_values.{layer}.decoder.value", past[layer * 2 + 1]));
                    }

                    var decoderResults = _decoderSession!.Run(decoderInputs);
                    previousDecoderResults?.Dispose();
                    previousDecoderResults = decoderResults;

                    Tensor<float> logits = decoderResults.First(result => result.Name == "logits").AsTensor<float>();
                    nextInputId = ArgMaxLastToken(logits);
                    if (nextInputId == EosTokenId)
                    {
                        completed = true;
                        break;
                    }
                    generated.Add(nextInputId);

                    past = Enumerable.Range(0, DecoderLayers)
                        .SelectMany(layer => new[]
                        {
                            decoderResults.First(result => result.Name == $"present.{layer}.decoder.key").AsTensor<float>(),
                            decoderResults.First(result => result.Name == $"present.{layer}.decoder.value").AsTensor<float>()
                        })
                        .ToArray();
                }
            }
            finally
            {
                previousDecoderResults?.Dispose();
            }

            if (!completed || generated.Count == 0) return text;
            string corrected = _tokenizer.Decode(generated).Trim();
            return string.IsNullOrWhiteSpace(corrected) ? text : corrected;
        }
        catch
        {
            // Grammar cleanup is optional; raw transcription must always remain deliverable.
            return text;
        }
    }

    private static int ArgMaxLastToken(Tensor<float> logits)
    {
        int vocabularySize = logits.Dimensions[^1];
        int offset = checked((int)logits.Length) - vocabularySize;
        int bestIndex = 0;
        float bestValue = float.NegativeInfinity;
        for (int index = 0; index < vocabularySize; index++)
        {
            float value = logits.GetValue(offset + index);
            if (value > bestValue)
            {
                bestValue = value;
                bestIndex = index;
            }
        }
        return bestIndex;
    }

    private static Tensor<float>[] CreateEmptyDecoderCache() => Enumerable.Range(0, DecoderLayers * 2)
        .Select(_ => (Tensor<float>)new DenseTensor<float>([1, DecoderHeads, 0, DecoderHeadSize]))
        .ToArray();

    private async Task EnsureModelDownloadedAsync()
    {
        Directory.CreateDirectory(_modelDir);
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };

        foreach (var file in RequiredFiles)
        {
            string destination = Path.Combine(_modelDir, file.LocalName);
            if (File.Exists(destination)) continue;

            string temporary = destination + $".{Guid.NewGuid():N}.download";
            try
            {
                await using Stream source = await client.GetStreamAsync(BaseUrl + file.RemotePath);
                await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                {
                    await source.CopyToAsync(target);
                    await target.FlushAsync();
                }
                File.Move(temporary, destination, false);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }

    private static readonly (string RemotePath, string LocalName)[] RequiredFiles =
    [
        ("onnx/encoder_model_quantized.onnx", "encoder_model_quantized.onnx"),
        ("onnx/decoder_model_merged_quantized.onnx", "decoder_model_merged_quantized.onnx"),
        ("tokenizer.json", "tokenizer.json")
    ];

    private void DisposeSessions()
    {
        _encoderSession?.Dispose();
        _decoderSession?.Dispose();
        _encoderSession = null;
        _decoderSession = null;
        _tokenizer = null;
        _isLoaded = false;
    }

    public void Dispose()
    {
        DisposeSessions();
        _loadLock.Dispose();
    }
}

internal sealed class T5Tokenizer
{
    private readonly Dictionary<string, (int Id, double Score)> _vocabulary;
    private readonly string[] _tokensById;
    private readonly int _maximumTokenLength;
    private readonly int _unknownId;

    private T5Tokenizer(Dictionary<string, (int Id, double Score)> vocabulary, string[] tokensById, int unknownId)
    {
        _vocabulary = vocabulary;
        _tokensById = tokensById;
        _unknownId = unknownId;
        _maximumTokenLength = vocabulary.Keys.Max(token => token.Length);
    }

    public static T5Tokenizer Load(string tokenizerPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(tokenizerPath));
        JsonElement model = document.RootElement.GetProperty("model");
        int unknownId = model.GetProperty("unk_id").GetInt32();
        JsonElement vocabularyJson = model.GetProperty("vocab");
        var vocabulary = new Dictionary<string, (int Id, double Score)>(vocabularyJson.GetArrayLength(), StringComparer.Ordinal);
        var tokensById = new string[vocabularyJson.GetArrayLength()];

        int id = 0;
        foreach (JsonElement entry in vocabularyJson.EnumerateArray())
        {
            string token = entry[0].GetString() ?? string.Empty;
            double score = entry[1].GetDouble();
            vocabulary[token] = (id, score);
            tokensById[id] = token;
            id++;
        }
        return new T5Tokenizer(vocabulary, tokensById, unknownId);
    }

    public int[] Encode(string text, int maximumTokens)
    {
        string normalized = Regex.Replace(text.Normalize(NormalizationForm.FormKC), @"\s+", " ").Trim();
        if (normalized.Length == 0) return [];
        string sentencePieceText = "▁" + normalized.Replace(' ', '▁');

        int length = sentencePieceText.Length;
        var bestScore = Enumerable.Repeat(double.NegativeInfinity, length + 1).ToArray();
        var previous = Enumerable.Repeat(-1, length + 1).ToArray();
        var tokenIds = Enumerable.Repeat(_unknownId, length + 1).ToArray();
        bestScore[0] = 0;

        for (int start = 0; start < length; start++)
        {
            if (double.IsNegativeInfinity(bestScore[start])) continue;
            bool matched = false;
            int maxLength = Math.Min(_maximumTokenLength, length - start);
            for (int tokenLength = 1; tokenLength <= maxLength; tokenLength++)
            {
                string candidate = sentencePieceText.Substring(start, tokenLength);
                if (!_vocabulary.TryGetValue(candidate, out var token)) continue;
                matched = true;
                int end = start + tokenLength;
                double score = bestScore[start] + token.Score;
                if (score > bestScore[end])
                {
                    bestScore[end] = score;
                    previous[end] = start;
                    tokenIds[end] = token.Id;
                }
            }

            if (!matched && bestScore[start] - 100 < bestScore[start + 1]) continue;
            if (!matched)
            {
                bestScore[start + 1] = bestScore[start] - 100;
                previous[start + 1] = start;
                tokenIds[start + 1] = _unknownId;
            }
        }

        if (previous[length] < 0) return [];
        var encoded = new List<int>();
        for (int cursor = length; cursor > 0; cursor = previous[cursor]) encoded.Add(tokenIds[cursor]);
        encoded.Reverse();
        if (encoded.Count >= maximumTokens) encoded.RemoveRange(maximumTokens - 1, encoded.Count - maximumTokens + 1);
        encoded.Add(EosTokenId);
        return encoded.ToArray();
    }

    public string Decode(IEnumerable<int> tokenIds)
    {
        var builder = new StringBuilder();
        foreach (int id in tokenIds)
        {
            if (id < 0 || id >= _tokensById.Length || id <= 2) continue;
            string token = _tokensById[id];
            if (token.StartsWith("<extra_id_", StringComparison.Ordinal)) continue;
            builder.Append(token);
        }
        return builder.ToString().Replace('▁', ' ').Trim();
    }

    private const int EosTokenId = 1;
}
