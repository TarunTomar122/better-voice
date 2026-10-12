using BetterVoice.Core;
using Xunit;

namespace BetterVoice.Core.Tests;

public class RecordingSoundCueTests
{
    [Fact]
    public void TestListeningUsesTheSofterPurrCue()
    {
        Assert.Equal("Started", RecordingSoundCue.Started.SystemSoundName());
    }

    [Fact]
    public void TestListeningAndFinishedCuesAreDistinctSystemSounds()
    {
        Assert.NotEqual(
            RecordingSoundCue.Started.SystemSoundName(),
            RecordingSoundCue.Finished.SystemSoundName());
    }
}
