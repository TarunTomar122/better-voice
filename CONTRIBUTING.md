# Contributing to BetterVoice

Thanks for helping improve BetterVoice. It is a small, native macOS app, so focused contributions are especially useful.

## Start here

1. Read the [README](README.md) to understand the recording flow and permission model.
2. Install macOS 14 or later on Apple Silicon with Swift 6 or the Xcode command-line tools.
3. Build and launch the app with `./scripts/build-app.sh`.
4. Run the core test suite with `swift test -Xswiftc -strict-concurrency=complete`.

The release currently targets Apple Silicon macOS. The app uses local transcription, Screen Recording, Accessibility, and microphone permissions; changes involving those areas should explain their privacy and failure behavior.

## Where to contribute

- Onboarding and permission recovery
- Dictation quality and developer vocabulary
- Screen-context capture and session browsing
- Clipboard safety and local storage retention
- Accessibility, keyboard shortcuts, and menu-bar usability
- Build, signing, documentation, and test coverage

## Pull requests

- Keep each pull request focused on one user-facing improvement or one maintenance problem.
- Add or update `BetterVoiceCore` tests for platform-independent behavior.
- Test permission and UI changes on a real macOS installation when possible.
- Do not commit downloaded model weights, recordings, screenshots containing private data, or signing credentials.
- Explain what changed, why it helps users, and how it was tested.

If you are unsure where to start, open an issue describing the problem or choose a small documentation, onboarding, or test improvement first.
