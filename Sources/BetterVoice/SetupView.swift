import AppKit
import SwiftUI

struct SetupMicrophoneOption: Identifiable, Equatable {
    let id: String
    let name: String
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

    var requestMicrophone: () -> Void = {}
    var chooseMicrophone: (String) -> Void = { _ in }
    var requestScreen: () -> Void = {}
    var requestAccessibility: () -> Void = {}
    var downloadModel: () -> Void = {}
    var downloadGrammarModel: () -> Void = {}
    var setGrammarCorrection: (Bool) -> Void = { _ in }
    var setDeveloperCleanup: (Bool) -> Void = { _ in }
    var refresh: () -> Void = {}
    var complete: () -> Void = {}

    var readyCount: Int {
        [microphoneGranted, screenGranted, accessibilityGranted, modelReady].filter { $0 }.count
    }

    var setupComplete: Bool {
        readyCount == 4
    }
}

struct SetupView: View {
    @ObservedObject var model: SetupModel

    private enum Links {
        static let guide = URL(string: "https://github.com/TarunTomar122/better-voice#use-it")!
        static let contributing = URL(string: "https://github.com/TarunTomar122/better-voice/blob/main/CONTRIBUTING.md")!
        static let issues = URL(string: "https://github.com/TarunTomar122/better-voice/issues")!
    }

    var body: some View {
        ScrollView {
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
                        .frame(width: 250, height: 150)
                }

                HStack(spacing: 12) {
                    Image(systemName: model.setupComplete ? "checkmark.seal.fill" : "sparkles")
                        .font(.title2)
                        .foregroundStyle(model.setupComplete ? Color.green : Color.accentColor)
                    VStack(alignment: .leading, spacing: 3) {
                        Text(model.setupComplete ? "Ready to record" : "Finish setup to get started")
                            .fontWeight(.semibold)
                        Text(model.setupComplete
                             ? "Use ⌥ for a quick note or ⌘⌥ for a longer explanation."
                             : "\(model.readyCount) of 4 essentials are ready. You can return here any time from the menu bar.")
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    ProgressView(value: Double(model.readyCount), total: 4)
                        .frame(width: 110)
                        .accessibilityLabel("Setup progress")
                }
                .padding(14)
                .background(.quaternary.opacity(0.45), in: RoundedRectangle(cornerRadius: 12))

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
                    ShortcutGuide(keys: "⌥", title: "Quick note", detail: "Hold to record. Release to finish.")
                    ShortcutGuide(keys: "⌘⌥", title: "Long explanation", detail: "Press once to start, again to finish.")
                }

                Divider()

                VStack(alignment: .leading, spacing: 12) {
                    Text("Setup")
                        .font(.headline)
                    MicrophoneSetupRow(model: model)
                    SetupRow(
                        title: "Screen Recording",
                        detail: model.screenGranted ? "Ready to capture circles" : "Needed only when you circle the screen",
                        ready: model.screenGranted,
                        action: model.requestScreen
                    )
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
                    GrammarSetupRow(
                        title: "Grammar cleanup (Beta)",
                        detail: "t5-tiny-gec-hone runs locally after transcription to fix punctuation and sentence structure. It falls back to the raw transcript if unavailable.",
                        status: model.grammarStatus,
                        ready: model.grammarReady,
                        busy: model.grammarBusy,
                        selectionEnabled: model.grammarSelectionEnabled,
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

                HStack {
                    Text("Sessions stay in Desktop/BetterVoice for up to 7 days, capped at 500 MB.")
                        .font(.footnote)
                        .foregroundStyle(.secondary)
                    Spacer()
                    Button("Refresh") { model.refresh() }
                    Button("Done") { model.complete() }
                        .keyboardShortcut(.defaultAction)
                }
            }
            .padding(28)
        }
        .frame(minWidth: 720, idealWidth: 720, minHeight: 620, idealHeight: 680)
        .onAppear { model.refresh() }
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

    func show(model: SetupModel) {
        if let window {
            NSApplication.shared.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
            return
        }
        let window = NSWindow(contentViewController: NSHostingController(rootView: SetupView(model: model)))
        window.title = "Getting Started with BetterVoice"
        window.styleMask = [.titled, .closable, .miniaturizable]
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
