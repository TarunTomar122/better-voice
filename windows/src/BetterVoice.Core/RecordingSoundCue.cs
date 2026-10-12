namespace BetterVoice.Core;

public enum RecordingSoundCue
{
    Started,
    Finished
}

public static class RecordingSoundCueExtensions
{
    public static string SystemSoundName(this RecordingSoundCue cue) => cue switch
    {
        RecordingSoundCue.Started => "Started",
        RecordingSoundCue.Finished => "Finished",
        _ => cue.ToString()
    };
}
