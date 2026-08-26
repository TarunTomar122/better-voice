import CoreGraphics
import XCTest
@testable import BetterVoiceCore

final class CircleGestureDetectorTests: XCTestCase {
    func testConfiguredShortcutsExposeQuickAndLongModifierStates() {
        let configuration = RecordingShortcutConfiguration(
            quickModifier: .command,
            longShortcut: .commandShift
        )

        XCTAssertEqual(
            configuration.activeStates(command: true, option: false, control: false, shift: false).quick,
            true
        )
        XCTAssertEqual(
            configuration.activeStates(command: true, option: false, control: false, shift: true).long,
            true
        )
        XCTAssertEqual(
            configuration.activeStates(command: true, option: true, control: false, shift: false).other,
            true
        )
    }

    func testCircleThresholdDefaultsTo340DegreesAndClampsUserValues() {
        XCTAssertEqual(CircleGestureDetector().minimumAngleDegrees, 340)
        XCTAssertEqual(CircleGestureDetector(minimumAngleDegrees: 290).minimumAngleDegrees, 300)
        XCTAssertEqual(CircleGestureDetector(minimumAngleDegrees: 370).minimumAngleDegrees, 359)
    }

    func testOptionHoldStartsAfterDelayAndStopsOnRelease() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: false, option: true), [.schedulePushToTalk])
        XCTAssertEqual(shortcut.pushToTalkDelayElapsed(), [.startPushToTalk])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [.stopPushToTalk])
    }

    func testOtherModifierDoesNotStopActivePushToTalk() {
        var shortcut = RecordingShortcutState()

        _ = shortcut.flagsChanged(command: false, option: true)
        _ = shortcut.pushToTalkDelayElapsed()
        XCTAssertEqual(
            shortcut.flagsChanged(command: false, option: true, otherModifier: true),
            []
        )
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [.stopPushToTalk])
    }

    func testOptionTapCancelsBeforeRecordingStarts() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: false, option: true), [.schedulePushToTalk])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [.cancelPendingPushToTalk])
        XCTAssertEqual(shortcut.pushToTalkDelayElapsed(), [])
    }

    func testCommandOptionBeforeDelayStartsLongFormOnly() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: false, option: true), [.schedulePushToTalk])
        XCTAssertEqual(
            shortcut.flagsChanged(command: true, option: true),
            [.cancelPendingPushToTalk, .toggleLongForm]
        )
        XCTAssertEqual(shortcut.pushToTalkDelayElapsed(), [])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [])
    }

    func testAddingCommandPromotesPushToTalkWithoutStopping() {
        var shortcut = RecordingShortcutState()

        _ = shortcut.flagsChanged(command: false, option: true)
        _ = shortcut.pushToTalkDelayElapsed()
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [.promoteToLongForm])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [])
    }

    func testCommandOptionTogglesOncePerChord() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [.toggleLongForm])
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [])
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [.toggleLongForm])
    }

    func testCommandAloneDoesNotActivateLongShortcut() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: true, option: false), [])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [])
    }

    func testCompleteCommandOptionChordCanToggleOnAndOff() {
        var shortcut = RecordingShortcutState()

        XCTAssertEqual(shortcut.flagsChanged(command: true, option: false), [])
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [.toggleLongForm])
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [])
        XCTAssertEqual(shortcut.flagsChanged(command: false, option: false), [])
        XCTAssertEqual(shortcut.flagsChanged(command: true, option: true), [.toggleLongForm])
    }

    func testTrailSegmentsSkipPausesAndPointerJumps() {
        XCTAssertEqual(trailSegments(points: [], times: []), [])
        XCTAssertEqual(
            trailSegments(points: [CGPoint(x: 0, y: 0)], times: [0]),
            []
        )
        XCTAssertEqual(
            trailSegments(
                points: [CGPoint(x: 0, y: 0), CGPoint(x: 30, y: 0)],
                times: [0, 0.15]
            ),
            [TrailSegment(from: 0, to: 1)]
        )
        XCTAssertEqual(
            trailSegments(
                points: [CGPoint(x: 0, y: 0), CGPoint(x: 4, y: 3)],
                times: [0, 0.25]
            ),
            []
        )
        XCTAssertEqual(
            trailSegments(
                points: [CGPoint(x: 0, y: 0), CGPoint(x: 240, y: 0)],
                times: [0, 0.016]
            ),
            []
        )
    }

    func testRecognizesClosedCircle() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?
        let center = CGPoint(x: 300, y: 200)

        for index in 0..<48 {
            let angle = CGFloat(index) / 47 * 2 * .pi
            result = detector.add(
                point: CGPoint(x: center.x + 52 * cos(angle), y: center.y + 52 * sin(angle)),
                at: Double(index) / 60
            ) ?? result
        }

        XCTAssertEqual(result?.center.x ?? 0, center.x, accuracy: 3)
        XCTAssertEqual(result?.center.y ?? 0, center.y, accuracy: 3)
        XCTAssertEqual(result?.radius ?? 0, 52, accuracy: 3)
    }

    func testRecognizesSlowLooseLoop() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?
        let center = CGPoint(x: 900, y: 500)

        for index in 0..<150 {
            let angle = CGFloat(index) / 149 * 2 * .pi
            let wobble = 1 + 0.1 * sin(angle * 3)
            result = detector.add(
                point: CGPoint(
                    x: center.x + 110 * wobble * cos(angle),
                    y: center.y + 82 * wobble * sin(angle)
                ),
                at: Double(index) * 0.02
            ) ?? result
        }

        XCTAssertNotNil(result)
    }

    func testRecognizesSlowLooseLoopAfterPointerMovement() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?

        for index in 0..<60 {
            _ = detector.add(
                point: CGPoint(x: 300 + CGFloat(index) * 5, y: 240 + CGFloat(index % 7)),
                at: Double(index) * 0.02
            )
        }

        let center = CGPoint(x: 900, y: 500)
        for index in 0..<150 {
            let angle = CGFloat(index) / 149 * 2 * .pi
            let wobble = 1 + 0.1 * sin(angle * 3)
            result = detector.add(
                point: CGPoint(
                    x: center.x + 110 * wobble * cos(angle),
                    y: center.y + 82 * wobble * sin(angle)
                ),
                at: 1.2 + Double(index) * 0.02
            ) ?? result
        }

        XCTAssertNotNil(result)
    }

    func testRejectsStraightLine() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?

        for index in 0..<48 {
            result = detector.add(
                point: CGPoint(x: CGFloat(index) * 4, y: 200),
                at: Double(index) / 60
            ) ?? result
        }

        XCTAssertNil(result)
    }

    func testRejectsIrregularClosedLoop() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?

        for index in 0..<96 {
            let angle = CGFloat(index) / 95 * 2 * .pi
            let radius = 70 * (1 + 0.55 * sin(angle * 2))
            result = detector.add(
                point: CGPoint(x: 400 + radius * cos(angle), y: 300 + radius * sin(angle)),
                at: Double(index) / 60
            ) ?? result
        }

        XCTAssertNil(result)
    }

    func testRejectsPartialArc() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?

        for index in 0..<48 {
            let angle = CGFloat(index) / 47 * (4 * .pi / 3)
            result = detector.add(
                point: CGPoint(x: 600 + 60 * cos(angle), y: 400 + 60 * sin(angle)),
                at: Double(index) / 60
            ) ?? result
        }

        XCTAssertNil(result)
    }

    func testRejectsHook() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?
        let points = (0..<48).map { index in
            let progress = CGFloat(index) / 47
            return CGPoint(
                x: 600 + 100 * progress,
                y: 400 + 60 * sin(progress * .pi) + 18 * progress
            )
        }

        for (index, point) in points.enumerated() {
            result = detector.add(point: point, at: Double(index) / 60) ?? result
        }

        XCTAssertNil(result)
    }

    func testRejectsZigzag() {
        var detector = CircleGestureDetector()
        var result: CircleGesture?

        for index in 0..<60 {
            result = detector.add(
                point: CGPoint(
                    x: 500 + CGFloat(index) * 2,
                    y: 400 + (index.isMultiple(of: 2) ? 35 : -35)
                ),
                at: Double(index) / 60
            ) ?? result
        }

        XCTAssertNil(result)
    }

    func testLongContinuousLoopCapturesOnceUntilPointerLeaves() {
        var detector = CircleGestureDetector()
        let center = CGPoint(x: 400, y: 300)
        var captures = 0

        for index in 0..<240 {
            let angle = CGFloat(index) / 47 * 2 * .pi
            if detector.add(
                point: CGPoint(x: center.x + 70 * cos(angle), y: center.y + 70 * sin(angle)),
                at: Double(index) / 60
            ) != nil {
                captures += 1
            }
        }

        XCTAssertEqual(captures, 1)
    }
}
