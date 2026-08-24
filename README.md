# BetterVoice

Voice dictation with the screen context you point at.

BetterVoice is an experimental, open-source macOS menu-bar app. It transcribes speech locally and captures the full screen whenever you circle something with your pointer, leaving a restrained blue highlight around the referenced area.

<p>
  <a href="https://buymeacoffee.com/taratdev">
    <img src="docs/assets/buy-me-a-coffee.svg" alt="Buy me a coffee" height="52">
  </a>
</p>

If you enjoy BetterVoice or any of my other experiments, a coffee helps support future projects. ❤️

![BetterVoice onboarding and visual capture preview](docs/assets/bettervoice-onboarding.png)

## Use it

- Hold `⌥` for a quick note. Recording starts after a short hold and finishes when you release.
- Press `⌘⌥` for a long explanation. Press it again to finish.
- A soft native sound confirms when listening starts and when it stops.
- While recording, circle any important UI with the pointer. A blue trail follows your movement and a pulse confirms each capture.
- BetterVoice inserts the transcript into the selected text field when macOS allows it. Long explanations also copy the transcript and captured images to the clipboard; quick notes leave your existing clipboard unchanged.

Each circle captures the complete display beneath the pointer. Multiple circles produce screenshots in the same order you referenced them.

## Grammar cleanup (Beta)

Grammar cleanup is off by default. To try the beta, open **Getting Started…**, turn on **Grammar cleanup (Beta)**, and press **Download**. BetterVoice then runs each transcript through the tiny, English-focused [`t5-tiny-gec-hone`](https://huggingface.co/rabden/t5-tiny-gec-hone) model. Its quantized ONNX weights and tokenizer are about 36 MB, stored in `~/Library/Application Support/BetterVoice`, and run locally. When enabled, BetterVoice preloads the cached model in the background after launch so the first recording does not pay the initialization cost. The status bar shows “Polishing transcript locally…” while this step runs. If the model cannot download, exceeds its context limit, or returns an incomplete result, BetterVoice keeps the raw transcript so recording still completes.

This is intentionally experimental: the model fixes capitalization, punctuation, and sentence structure, and it can occasionally change wording. Delete the `t5-tiny-gec-hone` folder to force a fresh download.

## Developer vocabulary (Beta)

The branch experiment also includes a fast, zero-download developer pass inspired by [WhisperDictation](https://github.com/sam-pop/WhisperDictation) and [Dictate](https://github.com/0xbrando/dictate). It fixes common casing such as `github` → `GitHub`, `javascript` → `JavaScript`, and `json` → `JSON`, and recognizes spoken acronyms such as “n p m” in terminals and editors. It preserves the transcript’s wording and runs locally in milliseconds. The pass is enabled by default in this branch and can be turned off from **Getting Started…**. The generic grammar model remains an independent, opt-in beta.

## Download

Download the latest Apple Silicon build from the [BetterVoice releases page](https://github.com/TarunTomar122/better-voice/releases/latest). Choose `BetterVoice-macos-arm64.zip`, unzip it, and open `BetterVoice.app`:

```sh
unzip BetterVoice-macos-arm64.zip
open BetterVoice.app
```

The release targets macOS 14+ on Apple Silicon. This experimental build is signed with an Apple Development certificate and is not notarized with a Developer ID certificate yet. On the first launch, macOS may require you to Control-click the app, choose **Open**, and confirm. If macOS still blocks it, use **System Settings → Privacy & Security → Open Anyway**. Then approve Microphone, Screen Recording, and Accessibility when BetterVoice asks. The local speech model downloads once (about 500 MB).

## Install from source

Requirements: macOS 14+, Swift 6/Xcode command-line tools, and a local Apple code-signing identity.

```sh
git clone https://github.com/TarunTomar122/better-voice.git
cd better-voice
./scripts/build-app.sh
```

The script builds, signs, and opens `.build/BetterVoice.app`. BetterVoice then walks through:

1. Microphone permission
2. Screen Recording permission
3. Accessibility permission for returning text to the selected field
4. The one-time local Parakeet model download (~500 MB)

Automatic microphone selection prefers a connected external input and falls back to the system input. You can choose a specific device during setup or from the menu bar.

To keep macOS permissions attached to the same identity across rebuilds, explicitly select your certificate when needed:

```sh
BETTERVOICE_SIGNING_IDENTITY="Apple Development: Your Name (TEAMID)" ./scripts/build-app.sh
```

## If something is not working

Open the menu-bar icon and choose **Getting Started…**. It shows the live state of the microphone, Screen Recording, Accessibility, selected input, local transcription model, and the grammar cleanup toggle. Errors stay visible in a small recovery window with a path back to setup instead of disappearing as a system beep.

- **Shortcut does nothing:** enable **Accessibility**, confirm the local model says Ready, and make sure only one BetterVoice process is running. The build script closes the previous process before launching a rebuild.
- **No screenshot:** enable BetterVoice in **System Settings → Privacy & Security → Screen Recording**, then quit and reopen the app. The setup screen reads the current macOS permission every time; it does not cache an old answer.
- **Transcript is not inserted:** enable **Accessibility**. The transcript remains on the clipboard and in the saved session when the target app blocks paste events.
- **Wrong microphone:** choose the device under **Microphone** in the menu-bar menu.
- **Model download failed:** reopen **Getting Started…** and retry from the model row.
- **Grammar model download failed:** reopen **Getting Started…** and retry **Download** on the grammar cleanup row.
- **Try grammar cleanup:** turn on **Grammar cleanup (Beta)** in **Getting Started…**, then press **Download**.
- **Accidental empty recording:** a session shorter than 2.5 seconds with no speech or circles is discarded quietly. Longer empty sessions are saved without opening an error dialog.

## Clipboard behavior

macOS lets one clipboard contain text, rich text, and image representations, but each destination decides which representation to accept. For a long explanation, BetterVoice captures the focused app and field when recording stops, puts only the transcript on the clipboard, and sends one `⌘V` directly to that captured app. It then restores the full text-plus-image clipboard. A quick `⌥` note uses a temporary clipboard only for insertion and restores the clipboard you had before recording.

## Privacy and storage

- Transcription runs locally through [FluidAudio](https://github.com/FluidInference/FluidAudio).
- Temporary audio is deleted after transcription.
- Sessions live in `~/Desktop/BetterVoice` for at most 7 days.
- Saved sessions are capped at 500 MB, including during an active capture; the oldest are removed first.
- The local speech model is a separate one-time cache of roughly 500 MB.
- Recordings stop safely at 20 minutes, and abandoned temporary audio is removed on launch.
- Use **Open Saved Sessions** to browse retained transcripts and screen captures, reveal a selected session in Finder, or use **Clear Saved Sessions…** to remove them.

A session contains:

```text
<timestamp>-<id>/
├── context.md
├── context-1.png
└── context-2.png
```

## Development

```sh
swift test -Xswiftc -strict-concurrency=complete
```

See [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) for the implementation map.

Current scope: English transcription, Apple Silicon macOS 14+, and an experimental downloadable release. The release is not notarized with a Developer ID certificate yet. Circle recognition is intentionally forgiving; you do not need to draw a perfect circle.

Inspired by the fluidity of Wispr Flow. BetterVoice is not affiliated with Wispr Flow.
