using System;
using System.Collections.Generic;
using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class SessionRetentionPolicyTests
{
    [Fact]
    public void TestRemovesExpiredThenOldestSessionsUntilUnderSizeLimit()
    {
        var now = DateTime.UnixEpoch.AddSeconds(1_000_000);
        var day = TimeSpan.FromDays(1);
        var policy = new SessionRetentionPolicy(maxAge: TimeSpan.FromDays(7), maxBytes: 500);

        var sessions = new List<StoredSession>
        {
            new("expired", now.Subtract(TimeSpan.FromDays(8)), 100),
            new("oldest", now.Subtract(TimeSpan.FromDays(3)), 300),
            new("newest", now.Subtract(day), 300)
        };

        var toRemove = policy.SessionsToRemove(sessions, now);
        Assert.Contains("expired", toRemove);
        Assert.Contains("oldest", toRemove);
        Assert.DoesNotContain("newest", toRemove);
    }

    [Fact]
    public void TestRejectsAFileThatWouldExceedTheStorageLimit()
    {
        var policy = new SessionRetentionPolicy(maxAge: TimeSpan.FromSeconds(1), maxBytes: 500);
        Assert.True(policy.CanStore(additionalBytes: 100, usedBytes: 400));
        Assert.False(policy.CanStore(additionalBytes: 101, usedBytes: 400));
    }

    [Fact]
    public void TestOnlyRecognizesGeneratedSessionFolderNames()
    {
        Assert.True(SessionNaming.IsBetterVoiceSessionName(
            "2026-08-23T15-16-45Z-C81A6E98-FD94-4FC6-AF2C-8928EBD938B1"));
        Assert.False(SessionNaming.IsBetterVoiceSessionName("my-important-folder"));
        Assert.False(SessionNaming.IsBetterVoiceSessionName("backup-C81A6E98-FD94-4FC6-AF2C-8928EBD938B1"));
    }
}
