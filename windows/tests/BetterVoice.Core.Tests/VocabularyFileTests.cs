using System.IO;
using System.Linq;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class VocabularyFileTests
{
    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), $"vocabulary-{System.Guid.NewGuid()}.json");

    [Fact]
    public void TestReadsTheTermsObject()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, """{"terms": {"cube cuttle": "kubectl", "engine x": "nginx"}}""");
            var terms = VocabularyFile.Terms(path).ToDictionary(x => x.Key, x => x.Value);

            Assert.Equal("kubectl", terms["cube cuttle"]);
            Assert.Equal("nginx", terms["engine x"]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TestSortsLongerSourcesFirstSoPhrasesWinOverTheWordsInside()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, """{"terms": {"engine": "engine", "engine x": "nginx"}}""");
            var terms = VocabularyFile.Terms(path);

            Assert.Equal("engine x", terms.First().Key);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TestIgnoresTheNotesKey()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, """{"_readme": ["a note"], "terms": {"engine x": "nginx"}}""");
            var terms = VocabularyFile.Terms(path);

            Assert.Single(terms);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TestDropsEntriesWithAnEmptySourceOrReplacement()
    {
        string path = TempFile();
        try
        {
            File.WriteAllText(path, """{"terms": {"": "nginx", "engine x": "", "psequel": "psql"}}""");
            var terms = VocabularyFile.Terms(path).Select(x => x.Key).ToList();

            Assert.Equal(["psequel"], terms);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void TestWritesATemplateOnlyWhenTheFileIsAbsent()
    {
        string path = TempFile();
        try
        {
            VocabularyFile.CreateTemplateIfMissing(path);
            File.WriteAllText(path, """{"terms": {"mine": "kept"}}""");
            VocabularyFile.CreateTemplateIfMissing(path);

            var terms = VocabularyFile.Terms(path).Select(x => x.Key).ToList();
            Assert.Equal(["mine"], terms);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
