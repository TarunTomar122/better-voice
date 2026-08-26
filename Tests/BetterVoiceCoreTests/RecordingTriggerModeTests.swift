import XCTest
@testable import BetterVoiceCore

final class ModifierDoubleTapDetectorTests: XCTestCase {
    func testDoubleTapWithinIntervalToggles() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        XCTAssertTrue(detector.modifierChanged(active: true, now: 0.2))
    }

    func testSingleTapDoesNotToggle() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 2))
    }

    func testHoldDoesNotToggle() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.5))
    }

    func testModifierComboCancelsPendingTap() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        detector.nonModifierKeyPressed()
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.2))
    }

    func testSlowSecondTapStartsFresh() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 1))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 1.1))
    }

    func testResetClearsArmedTap() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        detector.reset()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.2))
    }

    func testRepeatedModifierEventDoesNotShortenHold() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.2))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.3))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.4))
    }

    func testSecondTapReleaseDoesNotArmAnotherTap() {
        var detector = ModifierDoubleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
        XCTAssertTrue(detector.modifierChanged(active: true, now: 0.2))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.3))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.4))
    }
}

final class ModifierToggleTapDetectorTests: XCTestCase {
    func testShortTapToggles() {
        var detector = ModifierToggleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertTrue(detector.modifierChanged(active: false, now: 0.1))
    }

    func testHoldDoesNotToggle() {
        var detector = ModifierToggleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.5))
    }

    func testModifierComboCancelsTap() {
        var detector = ModifierToggleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        detector.nonModifierKeyPressed()
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.1))
    }

    func testRepeatedModifierEventDoesNotShortenHold() {
        var detector = ModifierToggleTapDetector()
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0))
        XCTAssertFalse(detector.modifierChanged(active: true, now: 0.2))
        XCTAssertFalse(detector.modifierChanged(active: false, now: 0.3))
    }
}

final class RecordingTriggerModeTests: XCTestCase {
    func testCommandOptionUsesPartialStateOnlyForItsOwnModifiers() {
        XCTAssertEqual(
            ModifierBindingState(
                bindingCommand: true,
                bindingOption: true,
                bindingControl: false,
                bindingShift: false,
                command: true,
                option: false,
                control: false,
                shift: false
            ),
            ModifierBindingState(active: false, partial: true)
        )
    }

    func testOptionBindingDoesNotBecomePartialWhenCommandIsHeld() {
        XCTAssertEqual(
            ModifierBindingState(
                bindingCommand: false,
                bindingOption: true,
                bindingControl: false,
                bindingShift: false,
                command: true,
                option: true,
                control: false,
                shift: false
            ),
            ModifierBindingState(active: false, partial: false)
        )
    }

    func testModifierChordWithExtraModifierIsNeitherActiveNorPartial() {
        let state = ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: false,
            control: false,
            shift: true
        )
        XCTAssertFalse(state.active)
        XCTAssertFalse(state.partial)
    }

    func testFullModifierChordIsActiveAndNotPartial() {
        let state = ModifierBindingState(
            bindingCommand: true,
            bindingOption: true,
            bindingControl: false,
            bindingShift: false,
            command: true,
            option: true,
            control: false,
            shift: false
        )
        XCTAssertTrue(state.active)
        XCTAssertFalse(state.partial)
    }

    func testQuickModesForModifierOnlyBinding() {
        XCTAssertEqual(
            RecordingTriggerMode.quickAvailableModes(modifierOnly: true),
            [.hold, .doubleTap]
        )
    }

    func testQuickModesForKeyComboBinding() {
        XCTAssertEqual(
            RecordingTriggerMode.quickAvailableModes(modifierOnly: false),
            [.hold]
        )
    }

    func testQuickHoldDetailIncludesMilliseconds() {
        XCTAssertTrue(
            RecordingTriggerMode.quickHoldDetail(bindingLabel: "⌥", holdDelayMilliseconds: 200).contains("200 ms")
        )
    }

    func testLongDisabledDetailMentionsBinding() {
        XCTAssertTrue(
            RecordingTriggerMode.longDisabledDetail(bindingLabel: "⌘⌥").contains("⌘⌥")
        )
    }

    func testHoldDelayMaximumIsOneSecond() {
        XCTAssertEqual(QuickNoteHoldDelay.maximumMilliseconds, 1000)
        XCTAssertEqual(QuickNoteHoldDelay.clamp(2000), 1000)
    }

    func testHoldDetailIncludesMilliseconds() {
        XCTAssertTrue(
            RecordingTriggerMode.hold.detail(bindingLabel: "⌥", holdDelayMilliseconds: 200).contains("200 ms")
        )
    }
}
