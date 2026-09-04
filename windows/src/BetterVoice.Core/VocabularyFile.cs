using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterVoice.Core;

/// <summary>
/// A replacement map the user edits by hand, so misheard terms can be corrected.
/// </summary>
public static class VocabularyFile
{
    public const string FileName = "vocabulary.json";
    private const string TermsKey = "terms";
    private const string NotesKey = "_readme";

    public static string DefaultPath()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "BetterVoice", FileName);
    }

    public static List<(string Key, string Value)> Terms(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return [];
        }

        try
        {
            string json = File.ReadAllText(filePath);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty(TermsKey, out var termsElem) || termsElem.ValueKind != JsonValueKind.Object)
            {
                return [];
            }

            var list = new List<(string Key, string Value)>();
            foreach (var prop in termsElem.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }
                string key = prop.Name;
                string val = prop.Value.GetString() ?? string.Empty;
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(val))
                {
                    list.Add((key, val));
                }
            }

            return list
                .OrderByDescending(x => x.Key.Length)
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void CreateTemplateIfMissing(string filePath)
    {
        if (File.Exists(filePath))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(filePath, Template);
    }

    private const string Template = """
    {
      "_readme": [
        "Corrections for terms BetterVoice mishears. The key is what comes out,",
        "the value is what you meant. Phrases of several words are allowed.",
        "Saving takes effect on your next recording, no restart needed.",
        "",
        "Example: \"cube cuttle\": \"kubectl\"",
        "",
        "One rule worth respecting: never use an ordinary word as a key.",
        "Writing \"read me\": \"README\" would rewrite every sentence that",
        "contains read me. Matching is whole-word and case-insensitive, and it",
        "skips filenames, domains and paths.",
        "",
        "This file is ignored while Developer vocabulary is off."
      ],
      "terms": {
      }
    }
    """;
}
