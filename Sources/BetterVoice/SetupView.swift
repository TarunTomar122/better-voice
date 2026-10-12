import AppKit
import BetterVoiceCore
import SwiftUI

struct SetupMicrophoneOption: Identifiable, Equatable {
    let id: String
    let name: String
}

struct HotkeyBinding: Equatable, Sendable {
    let keyCode: UInt16?
    let command: Bool
    let option: Bool
    let control: Bool
    let shift: Bool
    let keyName: String

    static let option = HotkeyBinding(
        keyCode: nil,
        command: false,
        option: true,
        control: false,
        shift: false,
        keyName: ""
    )

    static let commandOption = HotkeyBinding(
        keyCode: nil,
        command: true,
        option: true,
        control: false,
        shift: false,
        keyName: ""
    )

    var label: String {
        let modifiers = [
            command ? "⌘" : "",
            option ? "⌥" : "",
            control ? "⌃" : "",
            shift ? "⇧" : ""
        ].joined()
        return modifiers + keyName
    }

    func matches(command: Bool, option: Bool, control: Bool, shift: Bool) -> Bool {
        self.command == command
            && self.option == option
            && self.control == control
            && self.shift == shift
    }

    var isModifierOnly: Bool { keyCode == nil }
}

struct HotkeyConfiguration: Equatable, Sendable {
    var quick: HotkeyBinding
    var long: HotkeyBinding
    var quickTriggerMode: RecordingTriggerMode
    var longTriggerMode: RecordingTriggerMode
    var quickHoldDelayMilliseconds: Int

    static let standard = HotkeyConfiguration(
        quick: .option,
        long: .commandOption,
        quickTriggerMode: .hold,
        longTriggerMode: .toggle,
        quickHoldDelayMilliseconds: QuickNoteHoldDelay.defaultMilliseconds
    )
}

@MainActor
final class SetupModel: ObservableObject {
    @Published var microphoneGranted = false
    @Published var screenGranted = false
    @Published var accessibilityGranted = false
    @Published var microphoneName = "Checking…"
    @Published var microphoneOptions: [SetupMicrophoneOption] = []
    @Published var selectedMicrophoneID = "automatic"
    @Published var microphoneSelectionEnabled = true
    @Published var modelStatus = "Checking…"
    @Published var modelReady = false
    @Published var modelBusy = false
    @Published var grammarCorrectionEnabled = false
    @Published var grammarStatus = "Download on first use (~36 MB)"
    @Published var grammarReady = false
    @Published var grammarBusy = false
    @Published var developerCleanupEnabled = true
    @Published var grammarSelectionEnabled = true
    @Published var transcriptionLanguage = TranscriptionLanguage.english
    @Published var languageSelectionEnabled = true
    @Published var dictationModel = DictationModelChoice.automatic
    @Published var modelChoiceEnabled = true
    @Published var circleMinimumAngleDegrees = 340.0
    @Published var hotkeyConfiguration = HotkeyConfiguration.standard
    @Published var hotkeyError: String?
    @Published var quickNoteTriggerMode: RecordingTriggerMode = .hold
    @Published var longNoteTriggerMode: RecordingTriggerMode = .toggle
    @Published var quickNoteHoldDelayMilliseconds = QuickNoteHoldDelay.defaultMilliseconds

    var requestMicrophone: () -> Void = {}
    var chooseMicrophone: (String) -> Void = { _ in }
    var requestScreen: () -> Void = {}
    var requestAccessibility: () -> Void = {}
    var downloadModel: () -> Void = {}
    var downloadGrammarModel: () -> Void = {}
    var setGrammarCorrection: (Bool) -> Void = { _ in }
    var setDeveloperCleanup: (Bool) -> Void = { _ in }
    var setTranscriptionLanguage: (TranscriptionLanguage) -> Void = { _ in }
    var setDictationModel: (DictationModelChoice) -> Void = { _ in }
    var setCircleMinimumAngle: (Double) -> Void = { _ in }
    var setHotkeyConfiguration: (HotkeyConfiguration) -> Void = { _ in }
    var refresh: () -> Void = {}
    var complete: () -> Void = {}

    var readyCount: Int {
        dictationReadyCount + (screenGranted ? 1 : 0)
    }

    var dictationReadyCount: Int {
        [microphoneGranted, accessibilityGranted, modelReady].filter { $0 }.count
    }

    var dictationReady: Bool {
        dictationReadyCount == 3
    }

    var contextReady: Bool {
        dictationReady && screenGranted
    }

    var microphoneAvailable: Bool {
        microphoneGranted && microphoneOptions.contains { $0.id != "automatic" }
    }

    var setupComplete: Bool {
        dictationReady
    }
}

private enum SetupSection: String, CaseIterable, Identifiable {
    case overview
    case dictation
    case visualContext
    case shortcuts
    case storage

    var id: String { rawValue }

    var title: String {
        switch self {
        case .overview: return "Overview"
        case .dictation: return "Dictation"
        case .visualContext: return "Visual context"
        case .shortcuts: return "Shortcuts"
        case .storage: return "Storage"
        }
    }

    var subtitle: String {
        switch self {
        case .overview: return "Your recording setup at a glance."
        case .dictation: return "Microphone, language, and local models."
        case .visualContext: return "Screen capture and circle detection."
        case .shortcuts: return "Choose how BetterVoice starts and stops."
        case .storage: return "Saved sessions and local data."
        }
    }

    var icon: String {
        switch self {
        case .overview: return "rectangle.grid.2x2"
        case .dictation: return "waveform"
        case .visualContext: return "scope"
        case .shortcuts: return "command"
        case .storage: return "internaldrive"
        }
    }
}

struct SetupView: View {
    @ObservedObject var model: SetupModel
    @State private var selection: SetupSection = .overview

    private enum Links {
        static let guide = URL(string: "https://github.com/TarunTomar122/better-voice#use-it")!
        static let contributing = URL(string: "https://github.com/TarunTomar122/better-voice/blob/main/CONTRIBUTING.md")!
        static let issues = URL(string: "https://github.com/TarunTomar122/better-voice/issues")!
    }

    var body: some View {
        HStack(spacing: 0) {
            SetupSidebar(selection: $selection)
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 24) {
                    HStack(alignment: .top) {
                        VStack(alignment: .leading, spacing: 5) {
                            Text(selection.title)
                                .font(.title2.weight(.semibold))
                            Text(selection.subtitle)
                                .foregroundStyle(.secondary)
                        }
                        Spacer()
                        Button("Done") { model.complete() }
                            .buttonStyle(.borderedProminent)
                            .keyboardShortcut(.defaultAction)
                    }
                    sectionContent
                }
                .padding(32)
                .frame(maxWidth: .infinity, alignment: .leading)
            }
            .frame(maxWidth: .infinity, maxHeight: .infinity)
        }
        .frame(minWidth: 900, idealWidth: 980, minHeight: 620, idealHeight: 700)
        .onAppear { model.refresh() }
    }

    @ViewBuilder
    private var sectionContent: some View {
        switch selection {
        case .overview:
            overview
        case .dictation:
            dictation
        case .visualContext:
            visualContext
        case .shortcuts:
            shortcuts
        case .storage:
            storage
        }
    }

    private var overview: some View {
        VStack(alignment: .leading, spacing: 22) {
            HStack(alignment: .top, spacing: 20) {
                VStack(alignment: .leading, spacing: 9) {
                    Label("BetterVoice", systemImage: "waveform.circle.fill")
                        .font(.system(size: 26, weight: .semibold))
                    Text("Talk. Point. Give your agent the whole thought.")
                        .font(.title3)
                    Text("Speak normally, circle anything important, and BetterVoice keeps the words and full-screen visual context together.")
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer(minLength: 4)
                CapturePreview()
                    .frame(width: 220, height: 132)
            }
            ReadinessCard(model: model)
            HStack(spacing: 10) {
                Link(destination: Links.guide) {
                    Label("Read the guide", systemImage: "book")
                }
                .buttonStyle(.bordered)
                Link(destination: Links.contributing) {
                    Label("Start contributing", systemImage: "hammer")
                }
                .buttonStyle(.borderedProminent)
                Link(destination: Links.issues) {
                    Label("Report a problem", systemImage: "exclamationmark.bubble")
                }
                .buttonStyle(.bordered)
            }
            HStack(spacing: 28) {
                ShortcutGuide(
                    keys: model.hotkeyConfiguration.quick.label,
                    title: "Quick note",
                    detail: model.quickNoteTriggerMode.detail(
                        bindingLabel: model.hotkeyConfiguration.quick.label,
                        holdDelayMilliseconds: model.quickNoteHoldDelayMilliseconds
                    )
                )
                ShortcutGuide(
                    keys: model.hotkeyConfiguration.long.label,
                    title: "Long explanation",
                    detail: model.longNoteTriggerMode.detail(
                        bindingLabel: model.hotkeyConfiguration.long.label,
                        holdDelayMilliseconds: model.quickNoteHoldDelayMilliseconds
                    )
                )
            }
        }
    }

    private var dictation: some View {
        VStack(alignment: .leading, spacing: 16) {
            MicrophoneSetupRow(model: model)
            SetupRow(
                title: "Accessibility",
                detail: model.accessibilityGranted ? "Shortcuts and transcript insertion are ready" : "Needed for global shortcuts and returning text",
                ready: model.accessibilityGranted,
                action: model.requestAccessibility
            )
            SetupRow(
                title: "Local transcription model",
                detail: model.modelStatus,
                ready: model.modelReady,
                busy: model.modelBusy,
                action: model.downloadModel
            )
            LanguageSetupRow(model: model)
            DictationModelSetupRow(model: model)
            GrammarSetupRow(
                title: "Grammar cleanup (Beta)",
                detail: "t5-tiny-gec-hone runs locally after transcription to fix punctuation and sentence structure. It falls back to the raw transcript if unavailable.",
                status: model.transcriptionLanguage.allowsGrammarCorrection
                    ? model.grammarStatus
                    : "English only. Skipped while dictating \(model.transcriptionLanguage.name).",
                ready: model.grammarReady && model.transcriptionLanguage.allowsGrammarCorrection,
                busy: model.grammarBusy,
                selectionEnabled: model.grammarSelectionEnabled
                    && model.transcriptionLanguage.allowsGrammarCorrection,
                download: model.downloadGrammarModel,
                isEnabled: Binding(
                    get: { model.grammarCorrectionEnabled },
                    set: {
                        model.grammarCorrectionEnabled = $0
                        model.setGrammarCorrection($0)
                    }
                )
            )
            GrammarSetupRow(
                title: "Developer vocabulary (Beta)",
                detail: "A fast local pass for technical terms such as npm, GitHub, SwiftUI, API, and JSON. No extra model download.",
                status: "Preserves your wording and adapts acronyms for developer apps.",
                ready: true,
                busy: false,
                selectionEnabled: model.grammarSelectionEnabled,
                download: {},
                toggleAccessibilityLabel: "Enable developer vocabulary beta",
                isEnabled: Binding(
                    get: { model.developerCleanupEnabled },
                    set: {
                        model.developerCleanupEnabled = $0
                        model.setDeveloperCleanup($0)
                    }
                )
            )
        }
    }

    private var visualContext: some View {
        VStack(alignment: .leading, spacing: 20) {
            SetupRow(
                title: "Screen Recording",
                detail: model.screenGranted ? "Ready to capture circles" : "Needed only when you circle the screen",
                ready: model.screenGranted,
                action: model.requestScreen
            )
            Divider()
            VStack(alignment: .leading, spacing: 10) {
                HStack {
                    Label("Circle detection", systemImage: "scope")
                        .font(.headline)
                    Spacer()
                    Text("\(Int(model.circleMinimumAngleDegrees.rounded()))°")
                        .font(.system(.body, design: .monospaced))
                        .foregroundStyle(.secondary)
                }
                Text("Set how much of a circle BetterVoice needs before it captures the screen. Higher values reduce accidental captures.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                HStack(spacing: 12) {
                    Text("300°").font(.caption).foregroundStyle(.secondary)
                    Slider(
                        value: Binding(
                            get: { model.circleMinimumAngleDegrees },
                            set: {
                                model.circleMinimumAngleDegrees = $0
                                model.setCircleMinimumAngle($0)
                            }
                        ),
                        in: 300...359,
                        step: 1
                    )
                    Text("359°").font(.caption).foregroundStyle(.secondary)
                }
                .accessibilityElement(children: .contain)
                .accessibilityLabel("Minimum circle angle")
                Button("Use 340° default") {
                    model.circleMinimumAngleDegrees = 340
                    model.setCircleMinimumAngle(340)
                }
                .buttonStyle(.link)
                .font(.callout)
            }
            .padding(16)
            .background(.quaternary.opacity(0.28), in: RoundedRectangle(cornerRadius: 12))
        }
    }

    private var shortcuts: some View {
        VStack(alignment: .leading, spacing: 20) {
            Text("Each recording mode has its own shortcut and trigger style. Changes apply while BetterVoice is idle.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)

            recordingShortcutCard(
                title: "Quick note",
                binding: Binding(
                    get: { model.hotkeyConfiguration.quick },
                    set: { value in updateHotkeys(quick: value) }
                ),
                triggerMode: Binding(
                    get: { model.quickNoteTriggerMode },
                    set: { value in updateHotkeys(quickTriggerMode: value) }
                ),
                holdDelayMilliseconds: Binding(
                    get: { model.quickNoteHoldDelayMilliseconds },
                    set: { value in updateHotkeys(quickHoldDelayMilliseconds: value) }
                ),
                forQuick: true
            )

            recordingShortcutCard(
                title: "Long explanation",
                binding: Binding(
                    get: { model.hotkeyConfiguration.long },
                    set: { value in updateHotkeys(long: value) }
                ),
                triggerMode: Binding(
                    get: { model.longNoteTriggerMode },
                    set: { value in updateHotkeys(longTriggerMode: value) }
                ),
                holdDelayMilliseconds: nil,
                forQuick: false
            )

            if let hotkeyError = model.hotkeyError {
                Label(hotkeyError, systemImage: "exclamationmark.triangle")
                    .font(.callout)
                    .foregroundStyle(.orange)
            }
            HStack(spacing: 10) {
                Image(systemName: "info.circle")
                Text("Click the shortcut button, press a combination, then click Done. The new shortcut is shown immediately and never types into your app while you set it.")
            }
            .font(.callout)
            .foregroundStyle(.secondary)
            Button("Reset shortcuts to defaults") {
                applyHotkeyConfiguration(.standard)
            }
            .buttonStyle(.link)
        }
    }

    private func recordingShortcutCard(
        title: String,
        binding: Binding<HotkeyBinding>,
        triggerMode: Binding<RecordingTriggerMode>,
        holdDelayMilliseconds: Binding<Int>?,
        forQuick: Bool
    ) -> some View {
        let modes = RecordingTriggerMode.availableModes(
            forQuick: forQuick,
            modifierOnly: binding.wrappedValue.isModifierOnly
        )
        let resolvedMode = modes.contains(triggerMode.wrappedValue)
            ? triggerMode.wrappedValue
            : (modes.first ?? .toggle)
        let detail = resolvedMode.detail(
            bindingLabel: binding.wrappedValue.label,
            holdDelayMilliseconds: holdDelayMilliseconds?.wrappedValue
                ?? QuickNoteHoldDelay.defaultMilliseconds
        )

        return VStack(alignment: .leading, spacing: 14) {
            HotkeyRecordingRow(title: title, detail: detail, binding: binding)

            Divider()

            Text("Trigger")
                .font(.headline)

            Picker("\(title) trigger", selection: Binding(
                get: { resolvedMode },
                set: { triggerMode.wrappedValue = $0 }
            )) {
                ForEach(modes, id: \.self) { mode in
                    Text(forQuick ? mode.quickPickerLabel : mode.pickerLabel).tag(mode)
                }
            }
            .pickerStyle(.segmented)
            .labelsHidden()
            .frame(maxWidth: .infinity)

            if forQuick, resolvedMode == .hold, let holdDelayMilliseconds {
                holdDelayControls(holdDelayMilliseconds)
            } else if resolvedMode == .doubleTap {
                Text("Double-tap avoids holding modifiers while you use Option-based shortcuts in other apps.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            } else if resolvedMode == .toggle {
                Text(forQuick
                    ? "Tap the shortcut once to start and again to finish."
                    : "Press the shortcut once to start and again to finish.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
        .padding(16)
        .frame(maxWidth: .infinity, alignment: .leading)
        .background(.quaternary.opacity(0.28), in: RoundedRectangle(cornerRadius: 12))
        .onChange(of: binding.wrappedValue) { _, _ in
            normalizeTriggerMode(forQuick: forQuick)
        }
        .onChange(of: triggerMode.wrappedValue) { _, newValue in
            if forQuick {
                updateHotkeys(quickTriggerMode: newValue)
            } else {
                updateHotkeys(longTriggerMode: newValue)
            }
        }
    }

    private func holdDelayControls(_ holdDelayMilliseconds: Binding<Int>) -> some View {
        VStack(alignment: .leading, spacing: 10) {
            HStack(alignment: .firstTextBaseline) {
                Label("Hold delay", systemImage: "timer")
                    .font(.headline)
                Spacer(minLength: 12)
                Text("\(holdDelayMilliseconds.wrappedValue) ms")
                    .font(.system(.body, design: .monospaced))
                    .foregroundStyle(.secondary)
                    .lineLimit(1)
            }
            Text("How long you must hold the shortcut before recording starts. Increase this if Option-based shortcuts trigger dictation too easily.")
                .font(.callout)
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            VStack(alignment: .leading, spacing: 6) {
                Slider(
                    value: Binding(
                        get: { Double(holdDelayMilliseconds.wrappedValue) },
                        set: {
                            let milliseconds = QuickNoteHoldDelay.clamp(Int($0.rounded()))
                            holdDelayMilliseconds.wrappedValue = milliseconds
                            updateHotkeys(quickHoldDelayMilliseconds: milliseconds)
                        }
                    ),
                    in: Double(QuickNoteHoldDelay.minimumMilliseconds)...Double(QuickNoteHoldDelay.maximumMilliseconds),
                    step: 10
                )
                HStack {
                    Text("\(QuickNoteHoldDelay.minimumMilliseconds) ms")
                    Spacer()
                    Text("\(QuickNoteHoldDelay.maximumMilliseconds) ms")
                }
                .font(.caption)
                .foregroundStyle(.secondary)
            }
            .accessibilityElement(children: .contain)
            .accessibilityLabel("Quick note hold delay")
            Button("Use \(QuickNoteHoldDelay.defaultMilliseconds) ms default") {
                holdDelayMilliseconds.wrappedValue = QuickNoteHoldDelay.defaultMilliseconds
                updateHotkeys(quickHoldDelayMilliseconds: QuickNoteHoldDelay.defaultMilliseconds)
            }
            .buttonStyle(.link)
            .font(.callout)
        }
    }

    private func normalizeTriggerMode(forQuick: Bool) {
        let binding = forQuick ? model.hotkeyConfiguration.quick : model.hotkeyConfiguration.long
        let modes = RecordingTriggerMode.availableModes(forQuick: forQuick, modifierOnly: binding.isModifierOnly)
        if forQuick {
            if !modes.contains(model.quickNoteTriggerMode) {
                updateHotkeys(quickTriggerMode: modes.first ?? .hold)
            }
        } else if !modes.contains(model.longNoteTriggerMode) {
            updateHotkeys(longTriggerMode: modes.first ?? .toggle)
        }
    }

    private func applyHotkeyConfiguration(_ configuration: HotkeyConfiguration) {
        model.hotkeyError = nil
        model.hotkeyConfiguration = configuration
        model.quickNoteTriggerMode = configuration.quickTriggerMode
        model.longNoteTriggerMode = configuration.longTriggerMode
        model.quickNoteHoldDelayMilliseconds = configuration.quickHoldDelayMilliseconds
        model.setHotkeyConfiguration(configuration)
    }

    private func updateHotkeys(
        quick: HotkeyBinding? = nil,
        long: HotkeyBinding? = nil,
        quickTriggerMode: RecordingTriggerMode? = nil,
        longTriggerMode: RecordingTriggerMode? = nil,
        quickHoldDelayMilliseconds: Int? = nil
    ) {
        var configuration = model.hotkeyConfiguration
        if let quick { configuration.quick = quick }
        if let long { configuration.long = long }
        if let quickTriggerMode { configuration.quickTriggerMode = quickTriggerMode }
        if let longTriggerMode { configuration.longTriggerMode = longTriggerMode }
        if let quickHoldDelayMilliseconds {
            configuration.quickHoldDelayMilliseconds = QuickNoteHoldDelay.clamp(quickHoldDelayMilliseconds)
        }
        let quickModes = RecordingTriggerMode.availableModes(
            forQuick: true,
            modifierOnly: configuration.quick.isModifierOnly
        )
        if !quickModes.contains(configuration.quickTriggerMode) {
            configuration.quickTriggerMode = quickModes.first ?? .hold
        }
        let longModes = RecordingTriggerMode.availableModes(
            forQuick: false,
            modifierOnly: configuration.long.isModifierOnly
        )
        if !longModes.contains(configuration.longTriggerMode) {
            configuration.longTriggerMode = longModes.first ?? .toggle
        }
        guard configuration.quick != configuration.long else {
            model.hotkeyError = "Quick note and long explanation need different shortcuts."
            return
        }
        applyHotkeyConfiguration(configuration)
    }

    private var storage: some View {
        VStack(alignment: .leading, spacing: 18) {
            Label("Local by default", systemImage: "lock.shield")
                .font(.headline)
            Text("Transcripts, audio cleanup, and captured images stay on this Mac. BetterVoice stores sessions in Desktop/BetterVoice for up to 7 days and caps the folder at 500 MB.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Divider()
            Label("Saved sessions", systemImage: "folder")
                .font(.headline)
            Text("Use Recent in the menu bar to copy the latest transcript or images, or open Saved Sessions to browse the local folders.")
                .foregroundStyle(.secondary)
                .fixedSize(horizontal: false, vertical: true)
            Link(destination: Links.guide) {
                Label("Read storage details in the guide", systemImage: "book")
            }
            .buttonStyle(.bordered)
        }
    }
}

private struct SetupSidebar: View {
    @Binding var selection: SetupSection

    var body: some View {
        VStack(alignment: .leading, spacing: 18) {
            Text("Settings")
                .font(.title2.weight(.semibold))
                .padding(.horizontal, 8)
            VStack(spacing: 4) {
                ForEach(SetupSection.allCases) { section in
                    Button {
                        selection = section
                    } label: {
                        Label(section.title, systemImage: section.icon)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal, 10)
                            .padding(.vertical, 7)
                    }
                    .buttonStyle(.plain)
                    .foregroundStyle(selection == section ? .primary : .secondary)
                    .background(
                        selection == section
                            ? Color.accentColor.opacity(0.16)
                            : Color.clear,
                        in: RoundedRectangle(cornerRadius: 7)
                    )
                }
            }
        }
        .padding(18)
        .padding(.top, 10)
        .frame(maxHeight: .infinity, alignment: .top)
        .frame(width: 198)
        .background(Color(nsColor: .controlBackgroundColor))
    }
}

private enum OnboardingStep: Int, CaseIterable {
    case model
    case permissions
    case microphone
    case guide

    var title: String {
        switch self {
        case .model: return "Download the local model"
        case .permissions: return "Allow BetterVoice to work anywhere"
        case .microphone: return "Choose your microphone"
        case .guide: return "You’re ready to use BetterVoice"
        }
    }

    var subtitle: String {
        switch self {
        case .model: return "Your voice stays on this Mac."
        case .permissions: return "A few macOS permissions unlock dictation and screen context."
        case .microphone: return "Use Automatic or choose a specific input."
        case .guide: return "Talk, point, and give your agent the whole thought."
        }
    }
}

private struct OnboardingView: View {
    @ObservedObject var model: SetupModel
    @State private var step: OnboardingStep = .model

    var body: some View {
        VStack(spacing: 0) {
            VStack(alignment: .leading, spacing: 12) {
                HStack(alignment: .top) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("BetterVoice")
                            .font(.title2.weight(.semibold))
                        Text(step.title)
                            .font(.title3.weight(.medium))
                        Text(step.subtitle)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Text("Step \(step.rawValue + 1) of \(OnboardingStep.allCases.count)")
                        .font(.callout.weight(.medium))
                        .foregroundStyle(.secondary)
                }
                ProgressView(value: Double(step.rawValue + 1), total: Double(OnboardingStep.allCases.count))
                    .tint(.accentColor)
            }
            .padding(.horizontal, 34)
            .padding(.top, 28)
            .padding(.bottom, 18)

            Divider()

            ScrollView {
                stepContent
                    .padding(34)
                    .frame(maxWidth: .infinity, alignment: .leading)
            }

            Divider()

            HStack {
                if step != .model {
                    Button("Back") { step = OnboardingStep(rawValue: step.rawValue - 1) ?? .model }
                }
                Spacer()
                Button(continueTitle) { advance() }
                    .buttonStyle(.borderedProminent)
                    .keyboardShortcut(.defaultAction)
                    .disabled(!canContinue)
            }
            .padding(.horizontal, 34)
            .padding(.vertical, 18)
        }
        .frame(minWidth: 860, idealWidth: 920, minHeight: 660, idealHeight: 700)
        .onAppear { model.refresh() }
    }

    @ViewBuilder
    private var stepContent: some View {
        switch step {
        case .model:
            modelStep
        case .permissions:
            permissionsStep
        case .microphone:
            microphoneStep
        case .guide:
            guideStep
        }
    }

    private var modelStep: some View {
        VStack(alignment: .leading, spacing: 24) {
            OnboardingHero(
                systemImage: "waveform.circle.fill",
                title: "Private, local dictation",
                detail: "BetterVoice uses the Parakeet speech model on your Mac. Download it once, then dictate without sending audio to a server."
            )
            OnboardingPanel {
                HStack(spacing: 14) {
                    Image(systemName: model.modelReady ? "checkmark.circle.fill" : "arrow.down.circle.fill")
                        .font(.system(size: 30))
                        .foregroundStyle(model.modelReady ? Color.green : Color.accentColor)
                        .accessibilityHidden(true)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(model.modelReady ? "Local model ready" : "Download the Parakeet model")
                            .font(.headline)
                        Text(model.modelStatus)
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    if model.modelBusy {
                        ProgressView()
                            .controlSize(.small)
                    } else if !model.modelReady {
                        Button(model.modelStatus.hasPrefix("Download failed") ? "Retry download" : "Download model") {
                            model.downloadModel()
                        }
                        .buttonStyle(.borderedProminent)
                    } else {
                        Label("Ready", systemImage: "checkmark")
                            .foregroundStyle(.green)
                    }
                }
            }
            Label("About 500 MB • stored locally • no account required", systemImage: "lock.shield")
                .font(.callout)
                .foregroundStyle(.secondary)
        }
    }

    private var permissionsStep: some View {
        VStack(alignment: .leading, spacing: 14) {
            OnboardingPermissionRow(
                title: "Microphone",
                detail: "Needed to hear your dictation.",
                ready: model.microphoneGranted,
                actionTitle: "Allow",
                action: model.requestMicrophone
            )
            OnboardingPermissionRow(
                title: "Accessibility",
                detail: "Needed for global shortcuts and inserting text into the selected app.",
                ready: model.accessibilityGranted,
                actionTitle: "Open Settings",
                action: model.requestAccessibility
            )
            OnboardingPermissionRow(
                title: "Screen Recording",
                detail: "Needed only when you circle something for visual context.",
                ready: model.screenGranted,
                actionTitle: "Open Settings",
                action: model.requestScreen
            )
            Label(
                "Screen Recording is optional for text-only dictation. You can continue without it and enable it later from Settings.",
                systemImage: "info.circle"
            )
            .font(.callout)
            .foregroundStyle(.secondary)
            .fixedSize(horizontal: false, vertical: true)
        }
    }

    private var microphoneStep: some View {
        VStack(alignment: .leading, spacing: 24) {
            OnboardingHero(
                systemImage: "mic.fill",
                title: "Use the microphone that feels right",
                detail: "Automatic prefers a connected external microphone, then falls back to your Mac’s default input. You can change this later from Settings."
            )
            OnboardingPanel {
                MicrophoneSetupRow(model: model)
            }
            if !model.microphoneGranted {
                Label("Microphone access is still needed before you can continue.", systemImage: "exclamationmark.triangle")
                    .font(.callout)
                    .foregroundStyle(.orange)
            } else if !model.microphoneAvailable {
                HStack(spacing: 10) {
                    Label("No input devices found. Connect a microphone and refresh.", systemImage: "exclamationmark.triangle")
                        .font(.callout)
                        .foregroundStyle(.orange)
                    Spacer()
                    Button("Refresh devices", action: model.refresh)
                        .controlSize(.small)
                }
            }
        }
    }

    private var guideStep: some View {
        VStack(alignment: .leading, spacing: 22) {
            HStack(alignment: .top, spacing: 24) {
                VStack(alignment: .leading, spacing: 8) {
                    Label("Talk. Point. Give your agent the whole thought.", systemImage: "waveform.circle.fill")
                        .font(.title3.weight(.semibold))
                    Text("BetterVoice inserts your words and any screen context into the app you were using.")
                        .foregroundStyle(.secondary)
                        .fixedSize(horizontal: false, vertical: true)
                }
                Spacer()
                CapturePreview()
                    .frame(width: 190, height: 114)
            }
            VStack(alignment: .leading, spacing: 12) {
                OnboardingHowStep(number: "1", title: "Speak", detail: quickNoteOnboardingDetail)
                OnboardingHowStep(number: "2", title: "Circle", detail: "While recording, draw a deliberate circle around anything important. A blue trail shows what BetterVoice sees.")
                OnboardingHowStep(number: "3", title: "Finish", detail: quickNoteFinishOnboardingDetail)
            }
            .padding(16)
            .background(.quaternary.opacity(0.28), in: RoundedRectangle(cornerRadius: 12))
            HStack(spacing: 28) {
                ShortcutGuide(
                    keys: model.hotkeyConfiguration.quick.label,
                    title: "Quick note",
                    detail: quickNoteShortcutGuideDetail
                )
                ShortcutGuide(
                    keys: model.hotkeyConfiguration.long.label,
                    title: "Long explanation",
                    detail: longNoteShortcutGuideDetail
                )
            }
            if !model.screenGranted {
                Label("Text dictation is ready. Enable Screen Recording later to add visual context.", systemImage: "eye.slash")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
    }

    private var quickNoteShortcutGuideDetail: String {
        switch model.quickNoteTriggerMode {
        case .hold: return "Hold to record"
        case .toggle: return "Tap to toggle"
        case .doubleTap: return "Double-tap to toggle"
        }
    }

    private var longNoteShortcutGuideDetail: String {
        switch model.longNoteTriggerMode {
        case .hold: return "Hold to record"
        case .toggle: return "Press to toggle"
        case .doubleTap: return "Double-tap to toggle"
        }
    }

    private var quickNoteOnboardingDetail: String {
        let quick = model.hotkeyConfiguration.quick.label
        let long = model.hotkeyConfiguration.long.label
        let quickAction = quickNoteActionPhrase
        let longAction = longNoteActionPhrase
        return "\(quickAction) \(quick) for a quick note, or \(longAction) \(long) for a longer explanation."
    }

    private var quickNoteFinishOnboardingDetail: String {
        let quick = quickNoteStopPhrase
        let long = longNoteStopPhrase
        return "\(quick), or \(long). BetterVoice inserts the transcript and captured images together."
    }

    private var quickNoteActionPhrase: String {
        switch model.quickNoteTriggerMode {
        case .hold: return "Hold"
        case .toggle: return "Tap"
        case .doubleTap: return "Double-tap"
        }
    }

    private var longNoteActionPhrase: String {
        switch model.longNoteTriggerMode {
        case .hold: return "Hold"
        case .toggle: return "Press"
        case .doubleTap: return "Double-tap"
        }
    }

    private var quickNoteStopPhrase: String {
        switch model.quickNoteTriggerMode {
        case .hold: return "Release the quick-note key to finish"
        case .toggle: return "Tap the quick-note key again to finish"
        case .doubleTap: return "Double-tap the quick-note key again to finish"
        }
    }

    private var longNoteStopPhrase: String {
        switch model.longNoteTriggerMode {
        case .hold: return "release the long shortcut to finish"
        case .toggle: return "press the long shortcut again to finish"
        case .doubleTap: return "double-tap the long shortcut again to finish"
        }
    }

    private var canContinue: Bool {
        switch step {
        case .model: return model.modelReady
        case .permissions: return model.microphoneGranted && model.accessibilityGranted
        case .microphone: return model.microphoneAvailable
        case .guide: return true
        }
    }

    private var continueTitle: String {
        switch step {
        case .model: return "Continue"
        case .permissions: return model.screenGranted ? "Continue" : "Continue without screen context"
        case .microphone: return "Show me how it works"
        case .guide: return "Start using BetterVoice"
        }
    }

    private func advance() {
        guard canContinue else { return }
        if step == .guide {
            model.complete()
        } else {
            step = OnboardingStep(rawValue: step.rawValue + 1) ?? .guide
        }
    }
}

private struct OnboardingHero: View {
    let systemImage: String
    let title: String
    let detail: String

    var body: some View {
        HStack(spacing: 16) {
            Image(systemName: systemImage)
                .font(.system(size: 42))
                .foregroundStyle(.blue)
                .frame(width: 64, height: 64)
                .background(.blue.opacity(0.12), in: RoundedRectangle(cornerRadius: 16))
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 5) {
                Text(title)
                    .font(.title3.weight(.semibold))
                Text(detail)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct OnboardingPanel<Content: View>: View {
    let content: Content

    init(@ViewBuilder content: () -> Content) {
        self.content = content()
    }

    var body: some View {
        content
            .padding(20)
            .background(.quaternary.opacity(0.28), in: RoundedRectangle(cornerRadius: 14))
    }
}

private struct OnboardingPermissionRow: View {
    let title: String
    let detail: String
    let ready: Bool
    let actionTitle: String
    let action: () -> Void

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: ready ? "checkmark.circle.fill" : "circle")
                .font(.title2)
                .foregroundStyle(ready ? Color.green : Color.accentColor)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                HStack(spacing: 8) {
                    Text(title).font(.headline)
                    Text(ready ? "Ready" : "Needed")
                        .font(.caption.weight(.semibold))
                        .foregroundStyle(ready ? Color.green : Color.accentColor)
                }
                Text(detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            if !ready {
                Button(actionTitle, action: action)
                    .controlSize(.small)
            }
        }
        .padding(16)
        .background(.quaternary.opacity(0.28), in: RoundedRectangle(cornerRadius: 12))
    }
}

private struct OnboardingHowStep: View {
    let number: String
    let title: String
    let detail: String

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Text(number)
                .font(.headline)
                .foregroundStyle(.white)
                .frame(width: 27, height: 27)
                .background(Color.accentColor, in: Circle())
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.semibold)
                Text(detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
        }
    }
}

private struct ReadinessCard: View {
    @ObservedObject var model: SetupModel

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: model.setupComplete ? "checkmark.seal.fill" : "sparkles")
                .font(.title2)
                .foregroundStyle(model.setupComplete ? Color.green : Color.accentColor)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 3) {
                Text(model.contextReady ? "Ready to record" : model.dictationReady ? "Ready to dictate" : "Finish setup to get started")
                    .fontWeight(.semibold)
                Text(model.dictationReady
                     ? (model.contextReady
                        ? "Use \(model.hotkeyConfiguration.quick.label) for a quick note or \(model.hotkeyConfiguration.long.label) for a longer explanation."
                        : "Text dictation is ready. Enable Screen Recording to add visual context.")
                     : "\(model.dictationReadyCount) of 3 dictation essentials are ready. You can return here any time from the menu bar.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            ProgressView(value: Double(model.readyCount), total: 4)
                .frame(width: 110)
                .accessibilityLabel("Setup progress")
        }
        .padding(16)
        .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 12))
    }
}

private struct HotkeyRecordingRow: View {
    let title: String
    let detail: String
    @Binding var binding: HotkeyBinding
    @State private var isRecording = false

    var body: some View {
        HStack(spacing: 14) {
            Text(binding.label.isEmpty ? "⌁" : binding.label)
                .font(.system(size: 21, weight: .medium, design: .rounded))
                .frame(width: 48, height: 42)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 9))
            VStack(alignment: .leading, spacing: 3) {
                Text(title).fontWeight(.medium)
                Text(detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            .frame(maxWidth: .infinity, alignment: .leading)
            Button(isRecording ? "Listening…" : binding.label) {
                isRecording = true
            }
            .controlSize(.small)
            .accessibilityLabel(isRecording ? "Listening for \(title) shortcut" : "Change \(title) shortcut, currently \(binding.label)")
            HotkeyCaptureView(isRecording: $isRecording) { captured in
                isRecording = false
                binding = captured
            }
            .frame(width: 1, height: 1)
        }
    }
}

private struct HotkeyCaptureView: NSViewRepresentable {
    @Binding var isRecording: Bool
    let onCapture: (HotkeyBinding) -> Void

    func makeNSView(context: Context) -> HotkeyCaptureNSView {
        let view = HotkeyCaptureNSView()
        view.onCapture = onCapture
        return view
    }

    func updateNSView(_ nsView: HotkeyCaptureNSView, context: Context) {
        nsView.isRecording = isRecording
        nsView.onCapture = onCapture
        if isRecording {
            DispatchQueue.main.async {
                nsView.window?.makeFirstResponder(nsView)
            }
        }
    }
}

private final class HotkeyCaptureNSView: NSView {
    var isRecording = false
    var onCapture: ((HotkeyBinding) -> Void)?
    private var pendingModifierBinding: HotkeyBinding?
    private var captureWorkItem: DispatchWorkItem?

    override var acceptsFirstResponder: Bool { true }

    override func keyDown(with event: NSEvent) {
        guard isRecording else { return }
        captureWorkItem?.cancel()
        pendingModifierBinding = nil
        onCapture?(HotkeyBinding(
            keyCode: event.keyCode,
            command: event.modifierFlags.contains(.command),
            option: event.modifierFlags.contains(.option),
            control: event.modifierFlags.contains(.control),
            shift: event.modifierFlags.contains(.shift),
            keyName: keyName(for: event)
        ))
    }

    override func flagsChanged(with event: NSEvent) {
        guard isRecording, Self.modifierKeyCodes.contains(event.keyCode) else { return }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        guard flags.contains(.command)
                || flags.contains(.option)
                || flags.contains(.control)
                || flags.contains(.shift) else { return }
        pendingModifierBinding = HotkeyBinding(
            keyCode: nil,
            command: flags.contains(.command),
            option: flags.contains(.option),
            control: flags.contains(.control),
            shift: flags.contains(.shift),
            keyName: ""
        )
        captureWorkItem?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self, self.isRecording, let binding = self.pendingModifierBinding else { return }
            self.onCapture?(binding)
        }
        captureWorkItem = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.15, execute: work)
    }

    private static let modifierKeyCodes: Set<UInt16> = [54, 55, 56, 58, 59, 60, 61, 62]

    private func keyName(for event: NSEvent) -> String {
        switch event.keyCode {
        case 36: return "Return"
        case 48: return "Tab"
        case 49: return "Space"
        case 51: return "Delete"
        case 53: return "Escape"
        case 123: return "←"
        case 124: return "→"
        case 125: return "↓"
        case 126: return "↑"
        default:
            return event.charactersIgnoringModifiers?.uppercased() ?? "Key \(event.keyCode)"
        }
    }
}

private struct MicrophoneSetupRow: View {
    @ObservedObject var model: SetupModel

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: model.microphoneGranted ? "checkmark.circle.fill" : "circle")
                .foregroundStyle(model.microphoneGranted ? Color.green : Color.secondary)
                .font(.title3)
                .accessibilityLabel(model.microphoneGranted ? "Ready" : "Needs setup")
            VStack(alignment: .leading, spacing: 2) {
                Text("Microphone").fontWeight(.medium)
                Text(model.microphoneGranted ? model.microphoneName : "Needed to record your voice")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer()
            if model.microphoneGranted && !model.microphoneOptions.isEmpty {
                Picker("Microphone", selection: Binding(
                    get: { model.selectedMicrophoneID },
                    set: { model.chooseMicrophone($0) }
                )) {
                    ForEach(model.microphoneOptions) { option in
                        Text(option.name).tag(option.id)
                    }
                }
                .labelsHidden()
                .frame(maxWidth: 260)
                .disabled(!model.microphoneSelectionEnabled)
                .accessibilityLabel("Microphone input")
            } else if !model.microphoneGranted {
                Button("Set Up", action: model.requestMicrophone)
            } else {
                Text("No inputs found")
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

private struct LanguageSetupRow: View {
    @ObservedObject var model: SetupModel

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "globe")
                .foregroundStyle(Color.secondary)
                .font(.title3)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Dictation language").fontWeight(.medium)
                Text(model.transcriptionLanguage.usesEnglishOnlyModel
                     ? "English uses the model you already downloaded."
                     : "Other languages use the multilingual model, a separate one-time download.")
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            Picker("Dictation language", selection: Binding(
                get: { model.transcriptionLanguage },
                set: {
                    model.transcriptionLanguage = $0
                    model.setTranscriptionLanguage($0)
                }
            )) {
                ForEach(TranscriptionLanguage.all, id: \.self) { language in
                    Text(language.name).tag(language)
                }
            }
            .labelsHidden()
            .frame(maxWidth: 260)
            .disabled(!model.languageSelectionEnabled)
            .accessibilityLabel("Dictation language")
        }
    }
}

private struct DictationModelSetupRow: View {
    @ObservedObject var model: SetupModel

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: "waveform.badge.magnifyingglass")
                .foregroundStyle(Color.secondary)
                .font(.title3)
                .accessibilityHidden(true)
            VStack(alignment: .leading, spacing: 2) {
                Text("Dictation model").fontWeight(.medium)
                Text(model.dictationModel.detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
            }
            Spacer()
            Picker("Dictation model", selection: Binding(
                get: { model.dictationModel },
                set: {
                    model.dictationModel = $0
                    model.setDictationModel($0)
                }
            )) {
                ForEach(DictationModelChoice.allCases) { choice in
                    Text(choice.name).tag(choice)
                }
            }
            .labelsHidden()
            .frame(maxWidth: 260)
            .disabled(!model.modelChoiceEnabled)
            .accessibilityLabel("Dictation model")
        }
    }
}

private struct ShortcutGuide: View {
    let keys: String
    let title: String
    let detail: String

    var body: some View {
        HStack(spacing: 12) {
            Text(keys)
                .font(.system(size: 22, weight: .medium, design: .rounded))
                .frame(minWidth: 52)
                .padding(.vertical, 8)
                .background(.quaternary, in: RoundedRectangle(cornerRadius: 9))
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.medium)
                Text(detail).foregroundStyle(.secondary)
            }
        }
        .frame(maxWidth: .infinity, alignment: .leading)
    }
}

private struct SetupRow: View {
    let title: String
    let detail: String
    let ready: Bool
    var busy = false
    let action: () -> Void

    var body: some View {
        HStack(spacing: 12) {
            Image(systemName: ready ? "checkmark.circle.fill" : "circle")
                .foregroundStyle(ready ? Color.green : Color.secondary)
                .font(.title3)
                .accessibilityLabel(ready ? "Ready" : "Needs setup")
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.medium)
                Text(detail).font(.callout).foregroundStyle(.secondary)
            }
            Spacer()
            if busy {
                ProgressView().controlSize(.small)
            } else if !ready {
                Button("Set Up", action: action)
            }
        }
    }
}

private struct GrammarSetupRow: View {
    let title: String
    let detail: String
    let status: String
    let ready: Bool
    let busy: Bool
    let selectionEnabled: Bool
    let download: () -> Void
    var toggleAccessibilityLabel = "Enable grammar cleanup beta"
    @Binding var isEnabled: Bool

    var body: some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: "text.badge.checkmark")
                .foregroundStyle(.blue)
                .font(.title3)
                .accessibilityLabel("Information")
            VStack(alignment: .leading, spacing: 2) {
                Text(title).fontWeight(.medium)
                Text(detail)
                    .font(.callout)
                    .foregroundStyle(.secondary)
                    .fixedSize(horizontal: false, vertical: true)
                Text(status)
                    .font(.callout)
                    .foregroundStyle(.secondary)
            }
            Spacer(minLength: 12)
            VStack(alignment: .trailing, spacing: 7) {
                Toggle("", isOn: $isEnabled)
                    .labelsHidden()
                    .accessibilityLabel(toggleAccessibilityLabel)
                    .disabled(!selectionEnabled)
                if busy {
                    ProgressView().controlSize(.small)
                } else if !ready {
                    Button("Download", action: download)
                        .controlSize(.small)
                        .disabled(!selectionEnabled)
                }
            }
        }
    }
}

private struct CapturePreview: View {
    var body: some View {
        ZStack {
            RoundedRectangle(cornerRadius: 13)
                .fill(Color(nsColor: .windowBackgroundColor))
                .shadow(color: .black.opacity(0.12), radius: 12, y: 5)
            VStack(spacing: 9) {
                HStack(spacing: 5) {
                    Circle().fill(.red.opacity(0.7)).frame(width: 7)
                    Circle().fill(.yellow.opacity(0.7)).frame(width: 7)
                    Circle().fill(.green.opacity(0.7)).frame(width: 7)
                    Spacer()
                }
                HStack {
                    VStack(alignment: .leading, spacing: 8) {
                        Capsule().fill(.secondary.opacity(0.22)).frame(width: 110, height: 8)
                        Capsule().fill(.secondary.opacity(0.15)).frame(width: 86, height: 8)
                        RoundedRectangle(cornerRadius: 6).fill(.blue.opacity(0.16)).frame(width: 72, height: 28)
                    }
                    Spacer()
                }
            }
            .padding(15)
            Circle()
                .stroke(.blue, style: StrokeStyle(lineWidth: 4, lineCap: .round, dash: [44, 7]))
                .frame(width: 92, height: 72)
                .rotationEffect(.degrees(-16))
                .offset(x: 39, y: 25)
            Circle()
                .fill(.blue.opacity(0.14))
                .frame(width: 78, height: 60)
                .offset(x: 39, y: 25)
        }
        .accessibilityElement(children: .ignore)
        .accessibilityLabel("A blue mouse trail circles a button and captures the screen")
    }
}

@MainActor
final class SetupWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?

    func show(model: SetupModel, onboarding: Bool = false) {
        let rootView = onboarding
            ? AnyView(OnboardingView(model: model))
            : AnyView(SetupView(model: model))
        if let window {
            window.contentViewController = NSHostingController(rootView: rootView)
            window.title = onboarding ? "Welcome to BetterVoice" : "BetterVoice Settings"
            NSApplication.shared.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
            return
        }
        let window = NSWindow(contentViewController: NSHostingController(rootView: rootView))
        window.title = onboarding ? "Welcome to BetterVoice" : "BetterVoice Settings"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.minSize = NSSize(width: 900, height: 620)
        window.isReleasedWhenClosed = false
        window.center()
        window.delegate = self
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    func close() {
        window?.close()
    }

    func windowWillClose(_ notification: Notification) {
        window = nil
    }
}

@MainActor
final class RecoveryNoticeModel: ObservableObject {
    @Published var title = ""
    @Published var detail = ""
    @Published var actionTitle = "Open Setup"
    var action: () -> Void = {}
}

private struct RecoveryNoticeView: View {
    @ObservedObject var model: RecoveryNoticeModel

    var body: some View {
        HStack(spacing: 14) {
            Image(systemName: "exclamationmark.triangle.fill")
                .font(.title2)
                .foregroundStyle(.orange)
            VStack(alignment: .leading, spacing: 3) {
                Text(model.title).fontWeight(.semibold)
                Text(model.detail).font(.callout).foregroundStyle(.secondary)
            }
            Spacer(minLength: 16)
            Button(model.actionTitle, action: model.action)
        }
        .padding(18)
        .frame(width: 540)
    }
}

@MainActor
final class RecoveryNoticeController: NSObject, NSWindowDelegate {
    let model = RecoveryNoticeModel()
    private var window: NSWindow?

    func show(title: String, detail: String, actionTitle: String, action: @escaping () -> Void) {
        model.title = title
        model.detail = detail
        model.actionTitle = actionTitle
        model.action = { [weak self] in
            self?.window?.close()
            action()
        }
        if window == nil {
            let panel = NSPanel(contentViewController: NSHostingController(rootView: RecoveryNoticeView(model: model)))
            panel.title = "BetterVoice"
            panel.styleMask = [.titled, .closable, .utilityWindow]
            panel.isReleasedWhenClosed = false
            panel.level = .floating
            panel.delegate = self
            panel.center()
            window = panel
        }
        NSApplication.shared.activate(ignoringOtherApps: true)
        window?.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        window = nil
    }
}
