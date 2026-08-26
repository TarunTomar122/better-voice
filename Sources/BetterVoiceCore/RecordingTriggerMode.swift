import Foundation

/// How a recording shortcut starts and stops.
public enum RecordingTriggerMode: String, Sendable, CaseIterable {
    /// Press and hold the shortcut; release to finish.
    case hold
    /// Press the shortcut once to start and again to finish.
    case toggle
    /// Tap the shortcut twice quickly to start; tap twice again to finish.
    case doubleTap

    public var name: String {
        switch self {
        case .hold: return "Hold to record"
        case .toggle: return "Press to toggle"
        case .doubleTap: return "Double-tap to toggle"
        }
    }

    /// Short label for segmented controls in narrow settings panes.
    public var pickerLabel: String {
        switch self {
        case .hold: return "Hold"
        case .toggle: return "Shortcut"
        case .doubleTap: return "Double-tap"
        }
    }

    /// Short label for quick-note trigger picker.
    public var quickPickerLabel: String {
        switch self {
        case .hold: return "Hold"
        case .toggle: return "Shortcut"
        case .doubleTap: return "Double-tap"
        }
    }

    public static func quickAvailableModes(modifierOnly: Bool) -> [RecordingTriggerMode] {
        modifierOnly ? [.hold, .doubleTap] : [.hold]
    }

    public static func quickHoldDetail(bindingLabel: String, holdDelayMilliseconds: Int) -> String {
        let delay = QuickNoteHoldDelay.clamp(holdDelayMilliseconds)
        return "Hold \(bindingLabel) for \(delay) ms to record. Release it to finish."
    }

    public static func longDisabledDetail(bindingLabel: String) -> String {
        "Long explanation is off. Pressing \(bindingLabel) will not start a recording."
    }

    public func detail(bindingLabel: String, holdDelayMilliseconds: Int) -> String {
        switch self {
        case .hold:
            let delay = QuickNoteHoldDelay.clamp(holdDelayMilliseconds)
            return "Hold \(bindingLabel) for \(delay) ms to record. Release it to finish."
        case .toggle:
            return "Press \(bindingLabel) once to start and again to finish."
        case .doubleTap:
            return "Double-tap \(bindingLabel) to start. Double-tap again to finish."
        }
    }
}

public typealias QuickNoteTriggerMode = RecordingTriggerMode

/// Hold delay before hold-mode recording starts.
public enum QuickNoteHoldDelay: Sendable {
    public static let defaultMilliseconds = 140
    public static let minimumMilliseconds = 50
    public static let maximumMilliseconds = 1000

    public static func clamp(_ milliseconds: Int) -> Int {
        min(max(milliseconds, minimumMilliseconds), maximumMilliseconds)
    }
}

/// Detects a double-tap on a lone modifier key without treating holds or
/// modifier+key combos as taps.
public struct ModifierDoubleTapDetector: Sendable {
    public static let maxTapDuration: TimeInterval = 0.25
    public static let doubleTapInterval: TimeInterval = 0.40

    private var modifierPressed = false
    private var modifierDownAt: TimeInterval?
    private var firstTapReleasedAt: TimeInterval?
    private var comboInterrupted = false

    public init() {}

    public mutating func reset() {
        modifierPressed = false
        modifierDownAt = nil
        firstTapReleasedAt = nil
        comboInterrupted = false
    }

    public mutating func nonModifierKeyPressed() {
        comboInterrupted = true
        firstTapReleasedAt = nil
    }

    public mutating func modifierChanged(active: Bool, now: TimeInterval) -> Bool {
        if active {
            if let armedAt = firstTapReleasedAt {
                if now - armedAt <= Self.doubleTapInterval {
                    reset()
                    modifierPressed = true
                    modifierDownAt = now
                    return true
                }
                firstTapReleasedAt = nil
            }
            comboInterrupted = false
            modifierPressed = true
            modifierDownAt = now
            return false
        }

        guard modifierPressed else { return false }
        modifierPressed = false
        let downAt = modifierDownAt
        modifierDownAt = nil

        guard let downAt else { return false }
        let held = now - downAt
        guard !comboInterrupted, held <= Self.maxTapDuration else {
            firstTapReleasedAt = nil
            comboInterrupted = false
            return false
        }

        firstTapReleasedAt = now
        return false
    }
}

/// Detects a short modifier tap (press and release) for toggle mode.
public struct ModifierToggleTapDetector: Sendable {
    private var modifierPressed = false
    private var modifierDownAt: TimeInterval?
    private var comboInterrupted = false

    public init() {}

    public mutating func reset() {
        modifierPressed = false
        modifierDownAt = nil
        comboInterrupted = false
    }

    public mutating func nonModifierKeyPressed() {
        comboInterrupted = true
    }

    /// Returns `true` when a completed short tap should toggle recording.
    public mutating func modifierChanged(active: Bool, now: TimeInterval) -> Bool {
        if active {
            comboInterrupted = false
            modifierPressed = true
            modifierDownAt = now
            return false
        }

        guard modifierPressed else { return false }
        modifierPressed = false
        let downAt = modifierDownAt
        modifierDownAt = nil

        guard let downAt else { return false }
        let held = now - downAt
        guard !comboInterrupted, held <= ModifierDoubleTapDetector.maxTapDuration else {
            comboInterrupted = false
            return false
        }
        return true
    }
}
