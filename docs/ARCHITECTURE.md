# Architecture

Better Voice is a small native Swift app with one executable target and one testable core target.

## Recording flow

1. Global modifier monitoring starts push-to-talk after a short `⌥` hold, or toggles long-form recording with `⌘⌥`.
2. `AVAudioEngine` records the selected microphone while the HUD displays its live level.
3. Native `Purr` and `Pop` cues mark the listening start and finish.
4. Mouse events feed `CircleGestureDetector`; the overlay renders the trail without appearing in screenshots.
5. A recognized gesture asks ScreenCaptureKit for the full display under the pointer, then draws the blue highlight into the saved PNG.
6. FluidAudio transcribes the temporary audio file locally.
7. The developer vocabulary beta applies a small deterministic casing/acronym pass, using the captured app to recognize terminals, editors, and AI chats. It preserves the raw wording and needs no model download.
8. The optional, off-by-default `t5-tiny-gec-hone` ONNX beta cleans up punctuation and sentence structure locally. When enabled, it preloads in the background after launch; incomplete or unavailable results fall back to the raw transcript. Developer casing runs again afterward so technical terms survive the generic pass.
9. At stop, BetterVoice captures the focused app and field. `SessionOutput` writes `context.md`, sends one text-only paste to that app, then restores the full text-plus-image clipboard.
10. `SessionStorage` deletes sessions older than 7 days and keeps the remaining folder below 500 MB.
11. `SavedSessionsWindowController` presents a native, read-only browser for retained sessions. `BetterVoiceCore` supplies newest-first ordering and safe transcript/image-name parsing; the app layer loads bounded Markdown previews and local PNGs.

## Repository map

```text
Sources/BetterVoice/main.swift                 macOS app and system integrations
Sources/BetterVoice/SetupView.swift            onboarding and actionable recovery UI
Sources/BetterVoice/GrammarCorrector.swift     optional tiny local grammar pass
Sources/BetterVoiceCore/DeveloperTextCleanup.swift
                                                zero-download developer vocabulary pass
Sources/BetterVoice/SavedSessionsView.swift    saved-session browser and preview window
Sources/BetterVoiceCore/CircleGestureDetector.swift
Sources/BetterVoiceCore/RecordingSoundCue.swift
Sources/BetterVoiceCore/RecordingShortcutState.swift
Sources/BetterVoiceCore/SessionCompletionPolicy.swift
Sources/BetterVoiceCore/SessionRetentionPolicy.swift
Sources/BetterVoiceCore/SavedSessionBrowser.swift saved-session ordering and parsing
Sources/BetterVoiceCore/TrailSegments.swift   gesture logic shared with tests
Tests/BetterVoiceCoreTests/                    focused gesture tests
scripts/build-app.sh                           release build, signing, and launch
Info.plist                                     app identity and permission descriptions
```

The app keeps a stable bundle identifier and code signature because macOS TCC permissions are tied to app identity. Rebuilding with ad-hoc or different signing identities can make Screen Recording and Accessibility appear granted while the new executable is rejected.
