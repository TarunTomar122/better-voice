using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterVoice.Core;

/// <summary>
/// The dictation language the user picked.
/// </summary>
public sealed record TranscriptionLanguage(string Code, string Name)
{
    public const string AutomaticCode = "auto";
    public const string EnglishCode = "en";

    public bool UsesEnglishOnlyModel => Code == EnglishCode;
    public bool AllowsGrammarCorrection => UsesEnglishOnlyModel;
    public string? ScriptHintCode => Code == AutomaticCode ? null : Code;

    public static readonly TranscriptionLanguage Automatic = new(AutomaticCode, "Automatic");
    public static readonly TranscriptionLanguage English = new(EnglishCode, "English");

    public static readonly IReadOnlyList<TranscriptionLanguage> All =
    [
        Automatic,
        English,
        new("pt", "Português"),
        new("es", "Español"),
        new("fr", "Français"),
        new("de", "Deutsch"),
        new("it", "Italiano"),
        new("nl", "Nederlands"),
        new("da", "Dansk"),
        new("sv", "Svenska"),
        new("fi", "Suomi"),
        new("et", "Eesti"),
        new("lv", "Latviešu"),
        new("lt", "Lietuvių"),
        new("pl", "Polski"),
        new("cs", "Čeština"),
        new("sk", "Slovenčina"),
        new("sl", "Slovenščina"),
        new("hr", "Hrvatski"),
        new("bs", "Bosanski"),
        new("hu", "Magyar"),
        new("ro", "Română"),
        new("mt", "Malti"),
        new("el", "Ελληνικά"),
        new("bg", "Български"),
        new("ru", "Русский"),
        new("uk", "Українська"),
        new("be", "Беларуская"),
        new("sr", "Српски")
    ];

    public static TranscriptionLanguage FromStoredCode(string? storedCode) =>
        All.FirstOrDefault(l => l.Code == storedCode) ?? English;
}
