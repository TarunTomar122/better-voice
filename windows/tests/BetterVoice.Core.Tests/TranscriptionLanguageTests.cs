using System.Linq;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class TranscriptionLanguageTests
{
    [Fact]
    public void TestDefaultsToEnglishWhenNothingIsStored()
    {
        Assert.Equal(TranscriptionLanguage.English, TranscriptionLanguage.FromStoredCode(null));
    }

    [Fact]
    public void TestFallsBackToEnglishWhenTheStoredCodeIsUnknown()
    {
        Assert.Equal(TranscriptionLanguage.English, TranscriptionLanguage.FromStoredCode("klingon"));
    }

    [Fact]
    public void TestRestoresEveryOfferedLanguageFromItsStoredCode()
    {
        foreach (var language in TranscriptionLanguage.All)
        {
            Assert.Equal(language, TranscriptionLanguage.FromStoredCode(language.Code));
        }
    }

    [Fact]
    public void TestEnglishStaysOnTheEnglishOnlyModel()
    {
        Assert.True(TranscriptionLanguage.English.UsesEnglishOnlyModel);
    }

    [Fact]
    public void TestEveryOtherLanguageNeedsTheMultilingualModel()
    {
        foreach (var language in TranscriptionLanguage.All.Where(l => l != TranscriptionLanguage.English))
        {
            Assert.False(language.UsesEnglishOnlyModel);
        }
    }

    [Fact]
    public void TestOnlyEnglishAllowsTheEnglishOnlyGrammarModel()
    {
        Assert.True(TranscriptionLanguage.English.AllowsGrammarCorrection);
        foreach (var language in TranscriptionLanguage.All.Where(l => l != TranscriptionLanguage.English))
        {
            Assert.False(language.AllowsGrammarCorrection);
        }
    }

    [Fact]
    public void TestAutomaticCarriesNoScriptHint()
    {
        Assert.Null(TranscriptionLanguage.Automatic.ScriptHintCode);
        Assert.Equal("en", TranscriptionLanguage.English.ScriptHintCode);
    }

    [Fact]
    public void TestCodesAreUniqueAndNamesAreNotEmpty()
    {
        Assert.Equal(
            TranscriptionLanguage.All.Select(l => l.Code).Distinct().Count(),
            TranscriptionLanguage.All.Count);

        foreach (var language in TranscriptionLanguage.All)
        {
            Assert.False(string.IsNullOrEmpty(language.Name));
        }
    }
}
