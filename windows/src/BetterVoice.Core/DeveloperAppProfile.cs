using System;
using System.Collections.Generic;
using System.Linq;

namespace BetterVoice.Core;

public enum DeveloperAppProfile
{
    General,
    Terminal,
    Editor,
    Ai
}

public static class DeveloperAppProfileExtensions
{
    private static readonly HashSet<string> TerminalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "terminal", "iterm2", "ghostty", "warp", "kitty", "wezterm",
        "windowsterminal", "cmd", "powershell", "pwsh", "conhost", "alacritty", "mintty"
    };

    private static readonly HashSet<string> EditorNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "xcode", "visual studio code", "cursor", "windsurf", "neovim", "nvim",
        "code", "devenv", "notepad++", "sublime_text", "clion", "rider", "idea64"
    };

    private static readonly HashSet<string> AiNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "chatgpt", "claude", "codex"
    };

    public static DeveloperAppProfile Infer(string? bundleOrProcessId, string? applicationName)
    {
        string? name = applicationName?.ToLowerInvariant();
        string? id = bundleOrProcessId?.ToLowerInvariant();

        if ((name != null && TerminalNames.Contains(name)) ||
            (id != null && (TerminalNames.Contains(id) || id.Contains("terminal") || id.Contains("powershell") || id.Contains("cmd"))))
        {
            return DeveloperAppProfile.Terminal;
        }

        if ((name != null && EditorNames.Contains(name)) ||
            (id != null && (EditorNames.Contains(id) || id.Contains("vscode") || id.Contains("code") || id.Contains("devenv"))))
        {
            return DeveloperAppProfile.Editor;
        }

        if (name != null && AiNames.Contains(name))
        {
            return DeveloperAppProfile.Ai;
        }

        return DeveloperAppProfile.General;
    }
}
