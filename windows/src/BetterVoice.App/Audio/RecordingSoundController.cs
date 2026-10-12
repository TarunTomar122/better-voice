using System;
using System.IO;
using System.Media;
using BetterVoice.Core;

namespace BetterVoice.App.Audio;

public sealed class RecordingSoundController
{
    public void Play(RecordingSoundCue cue)
    {
        try
        {
            if (cue == RecordingSoundCue.Started)
            {
                SystemSounds.Asterisk.Play();
            }
            else
            {
                SystemSounds.Beep.Play();
            }
        }
        catch
        {
            // Sound play should never crash the app
        }
    }
}
