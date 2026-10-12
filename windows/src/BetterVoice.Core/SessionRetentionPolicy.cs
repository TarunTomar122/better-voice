using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace BetterVoice.Core;

public readonly record struct StoredSession(string Name, DateTime ModifiedAt, long Bytes);

public static class SessionNaming
{
    private static readonly Regex SessionNameRegex = new(
        @"^\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}Z-[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\z",
        RegexOptions.Compiled);

    public static bool IsBetterVoiceSessionName(string name) => SessionNameRegex.IsMatch(name);
}

public sealed class SessionRetentionPolicy
{
    public TimeSpan MaxAge { get; }
    public long MaxBytes { get; }

    public SessionRetentionPolicy(TimeSpan maxAge, long maxBytes)
    {
        MaxAge = maxAge;
        MaxBytes = maxBytes;
    }

    public bool CanStore(long additionalBytes, long usedBytes) =>
        additionalBytes >= 0 && usedBytes >= 0 && additionalBytes <= MaxBytes - usedBytes;

    public HashSet<string> SessionsToRemove(IReadOnlyList<StoredSession> sessions, DateTime now)
    {
        var removed = new HashSet<string>(
            sessions.Where(s => now - s.ModifiedAt > MaxAge).Select(s => s.Name));

        var kept = sessions.Where(s => !removed.Contains(s.Name)).ToList();

        long totalBytes = 0;
        foreach (var s in kept)
        {
            if (s.Bytes > 0)
            {
                totalBytes = totalBytes > long.MaxValue - s.Bytes ? long.MaxValue : totalBytes + s.Bytes;
            }
        }

        foreach (var session in kept.OrderBy(s => s.ModifiedAt))
        {
            if (totalBytes <= MaxBytes)
            {
                break;
            }

            removed.Add(session.Name);
            totalBytes -= session.Bytes;
        }

        return removed;
    }
}
