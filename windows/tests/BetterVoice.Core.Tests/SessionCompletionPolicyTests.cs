using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class SessionCompletionPolicyTests
{
    [Fact]
    public void TestShortEmptySessionIsDiscardedAsAnAccidentalShortcut()
    {
        Assert.Equal(
            SessionCompletionDisposition.DiscardAccidental,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 1.2));
    }

    [Fact]
    public void TestLongEmptySessionIsKeptWithoutBeingAnError()
    {
        Assert.Equal(
            SessionCompletionDisposition.SaveEmpty,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: false, duration: 4.0));
    }

    [Fact]
    public void TestTranscriptOrContextIsDelivered()
    {
        Assert.Equal(
            SessionCompletionDisposition.Deliver,
            SessionCompletionPolicy.Evaluate(hasTranscript: true, hasContext: false, duration: 0.2));

        Assert.Equal(
            SessionCompletionDisposition.Deliver,
            SessionCompletionPolicy.Evaluate(hasTranscript: false, hasContext: true, duration: 0.2));
    }
}
