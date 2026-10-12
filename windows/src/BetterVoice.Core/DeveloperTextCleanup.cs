using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterVoice.Core;

/// <summary>
/// A conservative, zero-download pass for terms that speech models commonly mis-case.
/// Highly optimized with pre-compiled regex automata for sub-millisecond execution.
/// </summary>
public static class DeveloperTextCleanup
{
    private const string WordCharacters = @"\p{L}\p{M}0-9_./\-";
    private const string TrailingCharacters = @"\p{L}\p{M}0-9_/\-";

    private static readonly (string Source, string Replacement)[] RawTerms =
    [
        ("better voice", "BetterVoice"),
        ("javascript", "JavaScript"), ("typescript", "TypeScript"), ("swiftui", "SwiftUI"),
        ("nextjs", "Next.js"), ("next.js", "Next.js"), ("postgresql", "PostgreSQL"),
        ("postgres", "Postgres"), ("mongodb", "MongoDB"), ("supabase", "Supabase"),
        ("graphql", "GraphQL"), ("github", "GitHub"), ("gitlab", "GitLab"), ("bitbucket", "Bitbucket"),
        ("macos", "macOS"), ("ios", "iOS"), ("ipados", "iPadOS"), ("watchos", "watchOS"),
        ("xcode", "Xcode"), ("appkit", "AppKit"), ("coregraphics", "CoreGraphics"),
        ("avfoundation", "AVFoundation"), ("openai", "OpenAI"), ("chatgpt", "ChatGPT"),
        ("pytorch", "PyTorch"), ("tensorflow", "TensorFlow"), ("onnx", "ONNX"),
        ("parakeet", "Parakeet"), ("whisper", "Whisper"), ("fluid audio", "FluidAudio"),
        ("api", "API"), ("sdk", "SDK"), ("cli", "CLI"), ("ide", "IDE"), ("orm", "ORM"),
        ("cdn", "CDN"), ("dns", "DNS"), ("ssl", "SSL"), ("tls", "TLS"), ("ssh", "SSH"),
        ("html", "HTML"), ("css", "CSS"), ("xml", "XML"), ("sql", "SQL"), ("jwt", "JWT"),
        ("csv", "CSV"), ("pdf", "PDF"), ("svg", "SVG"), ("png", "PNG"), ("json", "JSON"),
        ("yaml", "YAML"), ("toml", "TOML"), ("uuid", "UUID"), ("http", "HTTP"), ("https", "HTTPS"),
        ("cors", "CORS"), ("crud", "CRUD"), ("rest", "REST"), ("grpc", "gRPC"),
        ("tcp", "TCP"), ("udp", "UDP"), ("vpn", "VPN"), ("cpu", "CPU"), ("gpu", "GPU"),
        ("npm", "npm"), ("npx", "npx"), ("aws", "AWS"), ("gcp", "GCP"), ("ec2", "EC2"),
        ("s3", "S3"), ("llm", "LLM"), ("gpt", "GPT"), ("rag", "RAG"), ("nlp", "NLP"),
        ("mps", "MPS"), ("ai", "AI")
    ];

    private static readonly (string Source, string Replacement)[] RawSpokenAcronyms =
    [
        ("n p m", "npm"), ("n p x", "npx"), ("g i t h u b", "GitHub"),
        ("j s o n", "JSON"), ("a p i", "API"), ("c l i", "CLI"), ("s d k", "SDK")
    ];

    private static readonly HashSet<string> AmbiguousTerms = new(StringComparer.OrdinalIgnoreCase)
    {
        "rest", "rag", "crud", "whisper", "parakeet", "ai"
    };

    private sealed class CompiledRule
    {
        public string Source { get; }
        public string Replacement { get; }
        public Regex Regex { get; }

        public CompiledRule(string source, string replacement)
        {
            Source = source;
            Replacement = replacement;
            string pattern = $@"(?i)(?<![{WordCharacters}]){Regex.Escape(source)}(?![{TrailingCharacters}])(?!\.[{TrailingCharacters}])";
            Regex = new Regex(pattern, RegexOptions.Compiled);
        }
    }

    private static readonly CompiledRule[] PrecompiledTerms;
    private static readonly CompiledRule[] PrecompiledSpokenAcronyms;
    private static readonly ConcurrentDictionary<string, Regex> CustomRegexCache = new();

    static DeveloperTextCleanup()
    {
        PrecompiledTerms = RawTerms.Select(t => new CompiledRule(t.Source, t.Replacement)).ToArray();
        PrecompiledSpokenAcronyms = RawSpokenAcronyms.Select(t => new CompiledRule(t.Source, t.Replacement)).ToArray();
    }

    public static string Apply(
        string text,
        DeveloperAppProfile profile = DeveloperAppProfile.General,
        IReadOnlyList<(string Source, string Replacement)>? overrides = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        string result = text;
        if (profile is DeveloperAppProfile.Terminal or DeveloperAppProfile.Editor or DeveloperAppProfile.Ai)
        {
            foreach (var rule in PrecompiledSpokenAcronyms)
            {
                result = rule.Regex.Replace(result, rule.Replacement);
            }
        }

        var userOverrides = overrides ?? [];
        foreach (var (source, replacement) in userOverrides)
        {
            var regex = CustomRegexCache.GetOrAdd(source, s =>
            {
                string pattern = $@"(?i)(?<![{WordCharacters}]){Regex.Escape(s)}(?![{TrailingCharacters}])(?!\.[{TrailingCharacters}])";
                return new Regex(pattern, RegexOptions.Compiled);
            });
            result = regex.Replace(result, replacement);
        }

        HashSet<string>? overridden = null;
        if (userOverrides.Count > 0)
        {
            overridden = new HashSet<string>(userOverrides.Select(o => o.Source), StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rule in PrecompiledTerms)
        {
            if (profile == DeveloperAppProfile.General && AmbiguousTerms.Contains(rule.Source))
            {
                continue;
            }

            if (overridden != null && overridden.Contains(rule.Source))
            {
                continue;
            }

            result = rule.Regex.Replace(result, rule.Replacement);
        }

        return result;
    }
}
