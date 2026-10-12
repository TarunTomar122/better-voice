using System.Text;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class DeveloperTextCleanupTests
{
    [Fact]
    public void TestCommonTermsUseDeveloperCasing()
    {
        Assert.Equal(
            "use GitHub with a JavaScript API and JSON",
            DeveloperTextCleanup.Apply("use github with a javascript api and json"));
    }

    [Fact]
    public void TestTerminalProfileCollapsesSpokenAcronyms()
    {
        Assert.Equal(
            "run npm install then inspect the JSON",
            DeveloperTextCleanup.Apply("run n p m install then inspect the j s o n", DeveloperAppProfile.Terminal));
    }

    [Fact]
    public void TestGeneralProfileKeepsSpokenAcronyms()
    {
        Assert.Equal(
            "a p i access",
            DeveloperTextCleanup.Apply("a p i access", DeveloperAppProfile.General));
    }

    [Fact]
    public void TestGeneralProfileDoesNotUppercaseOrdinaryWords()
    {
        Assert.Equal(
            "take a rest and whisper to the parakeet",
            DeveloperTextCleanup.Apply("take a rest and whisper to the parakeet"));
    }

    [Fact]
    public void TestSentenceEndingTermsStillGetDeveloperCasing()
    {
        Assert.Equal(
            "i pushed the fix to GitHub.",
            DeveloperTextCleanup.Apply("i pushed the fix to github."));

        Assert.Equal(
            "Inspect the JSON. Then call the API.",
            DeveloperTextCleanup.Apply("Inspect the json. Then call the api.", DeveloperAppProfile.Editor));
    }

    [Fact]
    public void TestDomainsAndPathsKeepTheirExactSpelling()
    {
        Assert.Equal(
            "visit github.com for the api-first code",
            DeveloperTextCleanup.Apply("visit github.com for the api-first code"));

        Assert.Equal(
            "leave api._private and api.éclair unchanged",
            DeveloperTextCleanup.Apply("leave api._private and api.éclair unchanged"));
    }

    [Fact]
    public void TestDeveloperProfilePreservesFileExtensions()
    {
        Assert.Equal(
            "run cat package.json",
            DeveloperTextCleanup.Apply("run cat package.json", DeveloperAppProfile.Terminal));
    }

    [Fact]
    public void TestPunctuationAndWordingStayUntouched()
    {
        Assert.Equal(
            "Please use SwiftUI, not Swift.",
            DeveloperTextCleanup.Apply("Please use SwiftUI, not Swift."));
    }

    [Fact]
    public void TestUserTermsAreApplied()
    {
        Assert.Equal(
            "deploy with kubectl",
            DeveloperTextCleanup.Apply("deploy with cube cuttle", overrides: [("cube cuttle", "kubectl")]));
    }

    [Fact]
    public void TestUserTermsRunFirstSoTheBuiltInCasingStillSeesThem()
    {
        Assert.Equal(
            "push it to GitHub",
            DeveloperTextCleanup.Apply("push it to get hub", overrides: [("get hub", "github")]));
    }

    [Fact]
    public void TestAUserTermOverridesTheBuiltInSpellingForTheSameSource()
    {
        Assert.Equal(
            "read the Json",
            DeveloperTextCleanup.Apply("read the json", overrides: [("json", "Json")]));
    }

    [Fact]
    public void TestUserTermsKeepTheWholeWordAndPathProtections()
    {
        Assert.Equal(
            "open src/psequel/main.go",
            DeveloperTextCleanup.Apply("open src/psequel/main.go", overrides: [("psequel", "psql")]));
    }

    [Fact]
    public void TestAccentedWordsThatBeginWithATermAreLeftAlone()
    {
        Assert.Equal(
            "o apiário fica no sítio",
            DeveloperTextCleanup.Apply("o apiário fica no sítio"));
    }

    [Fact]
    public void TestTermsNextToAccentedWordsAreStillCased()
    {
        Assert.Equal(
            "a última resposta é JSON",
            DeveloperTextCleanup.Apply("a última resposta é json"));
    }

    [Fact]
    public void TestExtendedLatinLettersAlsoStopAMatch()
    {
        Assert.Equal(
            "ideš domov",
            DeveloperTextCleanup.Apply("ideš domov"));
    }

    [Fact]
    public void TestDecomposedAccentsAlsoStopAMatch()
    {
        string decomposed = "a idéia do apiário".Normalize(NormalizationForm.FormD);
        Assert.Equal(decomposed, DeveloperTextCleanup.Apply(decomposed));
    }

    [Fact]
    public void TestTheTextKeepsItsOriginalNormalizationForm()
    {
        string decomposed = "a última resposta é json".Normalize(NormalizationForm.FormD);
        string expected = "a última resposta é JSON".Normalize(NormalizationForm.FormD);
        Assert.Equal(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(DeveloperTextCleanup.Apply(decomposed)));
    }
}
