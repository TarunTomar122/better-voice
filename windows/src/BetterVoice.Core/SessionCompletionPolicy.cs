using System;

namespace BetterVoice.Core;

public enum SessionCompletionDisposition
{
    DiscardAccidental,
    SaveEmpty,
    Deliver
}

public static class SessionCompletionPolicy
{
    public static SessionCompletionDisposition Evaluate(
        bool hasTranscript,
        bool hasContext,
        double duration,
        double accidentalThreshold = 2.5)
    {
        if (hasTranscript || hasContext)
        {
            return SessionCompletionDisposition.Deliver;
        }

        return duration < accidentalThreshold
            ? SessionCompletionDisposition.DiscardAccidental
            : SessionCompletionDisposition.SaveEmpty;
    }
}
