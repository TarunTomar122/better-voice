using System;
using System.IO;

namespace BetterVoice.Integration.Tests;

internal static class TestPaths
{
    public static string SettingsFile() => Path.Combine(
        Path.GetTempPath(),
        "BetterVoice.Tests",
        Environment.ProcessId.ToString(),
        Guid.NewGuid().ToString("N"),
        "settings.json");
}
