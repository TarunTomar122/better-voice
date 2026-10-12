# BetterVoice for Windows

Native Windows port of **BetterVoice**, bringing offline speech recognition, developer vocabulary cleanup, circular pointer gestures with screenshot context, and seamless active-field text insertion to Windows 10 and 11.

The English Whisper model downloads on the first transcription. Selecting another language downloads the multilingual model on first use. Grammar correction is optional and downloads its pinned ONNX model and tokenizer the first time it is enabled.

For screen-recorded product demos, launch with `BETTERVOICE_CAPTURE_HUD=1` to
include the recording HUD. Normal launches exclude it from capture so it cannot
appear in saved context screenshots.

## Architecture & Subsystems

| Subsystem | Windows Implementation | Role |
| :--- | :--- | :--- |
| **Math & Gesture Core** | `BetterVoice.Core` | 1:1 port of `CircleGestureDetector`, `TrailSegments`, `DeveloperTextCleanup`, `VocabularyFile`, `RecordingShortcutState`, `TranscriptionLanguage`. |
| **Pointer Trail & Overlay** | `TrailOverlayWindow` | Virtual screen click-through layered window (`WS_EX_LAYERED \| WS_EX_TRANSPARENT \| WS_EX_NOACTIVATE`) with `SetWindowDisplayAffinity(..., WDA_EXCLUDEFROMCAPTURE)` ensuring the overlay is never captured in screenshots. |
| **Input & Shortcuts** | `InputMonitor` | Decoupled `WH_KEYBOARD_LL` hook with lock-free `Channel<KeyEvent>` (preventing `LowLevelHooksTimeout` unhooking) and 60Hz mouse sampling. |
| **Audio Capture** | `AudioRecorder` | WASAPI 16kHz 16-bit mono recording with real-time RMS/peak level metering for live HUD waveforms. |
| **Context Capture** | `ScreenshotCapture` | Multi-monitor display capture excluding overlay window, drawing target circle radial gradient and border. |
| **Text Insertion** | `TextInsertion` | Direct Unicode injection (`KEYEVENTF_UNICODE`) for short text (zero clipboard pollution) with clipboard backup + `Ctrl+V` fallback for long blocks. |
| **Grammar Correction** | `GrammarCorrector` | ONNX Runtime (`Microsoft.ML.OnnxRuntime`) running HuggingFace `t5-tiny-gec-hone`. |
| **UI & Shell** | `TrayIconManager` & `SetupWindow` | Modern system tray notification icon, menus, and Fluent dark settings window. |

## Building & Testing

### Prerequisites
- .NET 9 SDK (or later)

### Run Unit Tests
```powershell
dotnet test windows/BetterVoice.sln
```
*Executes the core, integration, hardware, and performance test suites.*

### Build Release Application
```powershell
dotnet build windows/BetterVoice.sln -c Release
```
Output binary is generated at:
`windows/src/BetterVoice.App/bin/Release/net9.0-windows/BetterVoice.App.exe`
