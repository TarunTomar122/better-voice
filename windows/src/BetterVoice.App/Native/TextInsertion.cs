using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using BetterVoice.Core;

namespace BetterVoice.App.Native;

public static class TextInsertion
{
    public readonly record struct AppContext(IntPtr HWnd, string ProcessName, DeveloperAppProfile Profile);

    public static AppContext GetCurrentContext()
    {
        IntPtr hwnd = Win32Api.GetForegroundWindow();
        if (hwnd == IntPtr.Zero)
        {
            return new AppContext(IntPtr.Zero, "general", DeveloperAppProfile.General);
        }

        Win32Api.GetWindowThreadProcessId(hwnd, out uint pid);
        string processName = "unknown";
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            processName = proc.ProcessName;
        }
        catch
        {
            // ignored
        }

        var profile = DeveloperAppProfileExtensions.Infer(processName, processName);
        return new AppContext(hwnd, processName, profile);
    }

    public static async Task InsertTextAsync(string text, AppContext context)
    {
        if (string.IsNullOrEmpty(text)) return;

        // Transcription can take long enough for another window to briefly receive
        // focus. Restore the window that was active when recording stopped.
        if (context.HWnd != IntPtr.Zero && Win32Api.IsWindow(context.HWnd))
        {
            Win32Api.SetForegroundWindow(context.HWnd);
            await Task.Delay(50);
        }

        // Small inserts are safe as direct Unicode input. Larger batches can overrun
        // some editors' input queues, so use the reliable clipboard path below.
        if (text.Length <= 32 && !text.Contains('\n') && !text.Contains('\r'))
        {
            SendUnicodeString(text);
            return;
        }

        // For longer text or multi-line text, use the clipboard + paste method
        string? previousText = null;
        bool hadText = false;

        try
        {
            if (System.Windows.Clipboard.ContainsText())
            {
                previousText = System.Windows.Clipboard.GetText();
                hadText = true;
            }
        }
        catch
        {
            // Clipboard access can intermittently fail if locked by another process
        }

        try
        {
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            // Fallback to direct unicode if clipboard is locked
            SendUnicodeString(text);
            return;
        }

        // Send Ctrl+V
        SendCtrlV();

        // Allow target app to read from clipboard asynchronously before restoring
        await Task.Delay(300);

        if (hadText && previousText != null)
        {
            try
            {
                System.Windows.Clipboard.SetText(previousText);
            }
            catch
            {
                // ignored
            }
        }
    }

    private static void SendUnicodeString(string text)
    {
        var inputs = new Win32Api.INPUT[text.Length * 2];
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            inputs[i * 2] = new Win32Api.INPUT
            {
                type = Win32Api.INPUT_KEYBOARD,
                u = new Win32Api.InputUnion
                {
                    ki = new Win32Api.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = Win32Api.KEYEVENTF_UNICODE
                    }
                }
            };

            inputs[i * 2 + 1] = new Win32Api.INPUT
            {
                type = Win32Api.INPUT_KEYBOARD,
                u = new Win32Api.InputUnion
                {
                    ki = new Win32Api.KEYBDINPUT
                    {
                        wVk = 0,
                        wScan = c,
                        dwFlags = Win32Api.KEYEVENTF_UNICODE | Win32Api.KEYEVENTF_KEYUP
                    }
                }
            };
        }

        Win32Api.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Api.INPUT>());
    }

    private static void SendCtrlV()
    {
        const ushort VK_CONTROL = 0x11;
        const ushort VK_V = 0x56;

        var inputs = new Win32Api.INPUT[4];

        // Ctrl down
        inputs[0] = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion { ki = new Win32Api.KEYBDINPUT { wVk = VK_CONTROL, dwFlags = 0 } }
        };

        // V down
        inputs[1] = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion { ki = new Win32Api.KEYBDINPUT { wVk = VK_V, dwFlags = 0 } }
        };

        // V up
        inputs[2] = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion { ki = new Win32Api.KEYBDINPUT { wVk = VK_V, dwFlags = Win32Api.KEYEVENTF_KEYUP } }
        };

        // Ctrl up
        inputs[3] = new Win32Api.INPUT
        {
            type = Win32Api.INPUT_KEYBOARD,
            u = new Win32Api.InputUnion { ki = new Win32Api.KEYBDINPUT { wVk = VK_CONTROL, dwFlags = Win32Api.KEYEVENTF_KEYUP } }
        };

        Win32Api.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Win32Api.INPUT>());
    }
}
