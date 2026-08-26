# Architecture

Better Voice is a small native Swift app with one executable target and one testable core target.

## Recording flow

1. `InputMonitor` watches the configured shortcuts and applies their trigger modes. Quick notes default to hold `⌥`; long-form recording defaults to press-to-toggle `⌘⌥`. Modifier-only bindings can use hold, tap-to-toggle, or double-tap-to-toggle where the UI allows it, with a configurable 50–500 ms quick-note hold delay. Modifier events are filtered to the bound keys so unrelated chords do not start a recording. Settings → Shortcuts applies captured combinations in place and one reset action restores the defaults.
2. `AVAudioEngine` records the selected microphone while the HUD displays its live level.
3. Native `Purr` and `Pop` cues mark the listening start and finish.
4. Mouse events feed `CircleGestureDetector`; the overlay renders the trail without appearing in screenshots.
5. A recognized gesture asks ScreenCaptureKit for the full display under the pointer, then draws the blue highlight into the saved PNG.
6. FluidAudio transcribes the temporary audio file locally. `TranscriptionLanguage` decides which model is loaded, English staying on the English-only one, and supplies the decoder's script hint.
7. `VocabularyFile` supplies the user's own corrections, re-read whenever the file's modification date changes. The developer vocabulary beta applies them first, then a small deterministic casing/acronym pass, using the captured app to recognize terminals, editors, and AI chats. It preserves the raw wording and needs no model download.
8. The optional, off-by-default `t5-tiny-gec-hone` ONNX beta cleans up punctuation and sentence structure locally. When enabled, it preloads in the background after launch; incomplete or unavailable results fall back to the raw transcript. Developer casing runs again afterward so technical terms survive the generic pass.
9. At stop, BetterVoice captures the focused app and field. `SessionOutput` writes `context.md`, pastes the transcript, then pastes captured images into the same field when both are present. Quick notes restore the previous clipboard afterward; long explanations leave the captured images available on the clipboard. The menu bar's Recent submenu reads the latest valid local session for recovery.
10. `SessionStorage` deletes sessions older than 7 days and keeps the remaining folder below 500 MB.

## First-run onboarding

`AppController` opens `OnboardingView` until `completedOnboarding` is set. The guided flow keeps setup sequential: download the local speech model, request microphone and Accessibility permissions, offer Screen Recording as an optional visual-context permission, choose the microphone, then explain the two recording shortcuts and circle gesture. Later visits use `SetupView` for the full settings navigator. The app remains visible in the Dock and menu bar so users can quit it through standard macOS controls; `applicationShouldHandleReopen` restores Settings when the Dock icon is reopened.

## Repository map

```text
Sources/BetterVoice/main.swift                 macOS app and system integrations
Sources/BetterVoice/SetupView.swift            onboarding and actionable recovery UI
Resources/BetterVoice.icns                      branded macOS app icon
Sources/BetterVoice/GrammarCorrector.swift     optional tiny local grammar pass
Sources/BetterVoiceCore/DeveloperTextCleanup.swift
                                                zero-download developer vocabulary pass
Sources/BetterVoiceCore/VocabularyFile.swift   user-editable term corrections
Sources/BetterVoiceCore/TranscriptionLanguage.swift
                                                dictation language and the model it needs
Sources/BetterVoiceCore/CircleGestureDetector.swift
Sources/BetterVoiceCore/RecordingSoundCue.swift
Sources/BetterVoiceCore/RecordingTriggerMode.swift
Sources/BetterVoiceCore/RecordingShortcutState.swift
Sources/BetterVoiceCore/SessionCompletionPolicy.swift
Sources/BetterVoiceCore/SessionRetentionPolicy.swift
Sources/BetterVoiceCore/TrailSegments.swift   gesture logic shared with tests
Tests/BetterVoiceCoreTests/                    focused gesture tests
scripts/build-app.sh                           release build, signing, and launch
Info.plist                                     app identity and permission descriptions
```

The app keeps a stable bundle identifier and code signature because macOS TCC permissions are tied to app identity. Rebuilding with ad-hoc or different signing identities can make Screen Recording and Accessibility appear granted while the new executable is rejected.
