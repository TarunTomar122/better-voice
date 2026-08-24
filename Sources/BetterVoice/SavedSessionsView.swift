import AppKit
import SwiftUI
import BetterVoiceCore

@MainActor
final class SavedSessionsModel: ObservableObject {
    @Published private(set) var sessions: [SavedSessionSummary] = []
    @Published var selectedSessionName: String?

    private let rootURL: URL

    init(rootURL: URL) {
        self.rootURL = rootURL
        refresh()
    }

    var selectedSession: SavedSessionSummary? {
        guard let selectedSessionName else { return nil }
        return sessions.first { $0.name == selectedSessionName }
    }

    func refresh() {
        sessions = SessionStorage.savedSessions()
        if let selectedSessionName, sessions.contains(where: { $0.name == selectedSessionName }) {
            return
        }
        selectedSessionName = sessions.first?.name
    }

    func image(for imageName: String, in session: SavedSessionSummary) -> NSImage? {
        guard session.imageNames.contains(imageName), SavedSessionBrowser.isSafeImageName(imageName) else {
            return nil
        }
        let url = rootURL
            .appendingPathComponent(session.name, isDirectory: true)
            .appendingPathComponent(imageName, isDirectory: false)
        guard let values = try? url.resourceValues(forKeys: [.isRegularFileKey, .isSymbolicLinkKey, .fileSizeKey]),
              values.isRegularFile == true,
              values.isSymbolicLink != true,
              (values.fileSize ?? .max) <= 32 * 1_024 * 1_024 else {
            return nil
        }
        return NSImage(contentsOf: url)
    }

    func revealSelected() {
        guard let selectedSession else { return }
        let folder = rootURL.appendingPathComponent(selectedSession.name, isDirectory: true)
        guard FileManager.default.fileExists(atPath: folder.path) else {
            refresh()
            return
        }
        NSWorkspace.shared.activateFileViewerSelecting([folder])
    }
}

struct SavedSessionsView: View {
    @ObservedObject var model: SavedSessionsModel

    var body: some View {
        NavigationSplitView {
            List(selection: $model.selectedSessionName) {
                if model.sessions.isEmpty {
                    Text("No saved sessions yet")
                        .foregroundStyle(.secondary)
                } else {
                    ForEach(model.sessions, id: \.name) { session in
                        VStack(alignment: .leading, spacing: 4) {
                            Text(Self.displayName(for: session))
                                .fontWeight(.medium)
                                .lineLimit(1)
                            HStack(spacing: 6) {
                                Text(session.modifiedAt, format: .dateTime.month(.abbreviated).day().year().hour().minute())
                                if !session.imageNames.isEmpty {
                                    Text("•")
                                    Text("\(session.imageNames.count) image\(session.imageNames.count == 1 ? "" : "s")")
                                }
                            }
                            .font(.caption)
                            .foregroundStyle(.secondary)
                        }
                        .padding(.vertical, 3)
                        .tag(session.name)
                    }
                }
            }
            .listStyle(.sidebar)
            .navigationTitle("Saved Sessions")
            .frame(minWidth: 250)
        } detail: {
            if let session = model.selectedSession {
                SessionPreview(session: session, model: model)
            } else {
                ContentUnavailableView(
                    "No Saved Sessions",
                    systemImage: "waveform",
                    description: Text("Sessions will appear here after a recording is saved.")
                )
            }
        }
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Button("Reveal in Finder", systemImage: "folder") {
                    model.revealSelected()
                }
                .disabled(model.selectedSession == nil)
            }
            ToolbarItem {
                Button("Refresh", systemImage: "arrow.clockwise") {
                    model.refresh()
                }
                .help("Refresh saved sessions")
            }
        }
    }

    private static func displayName(for session: SavedSessionSummary) -> String {
        session.name
            .replacingOccurrences(of: "T", with: " ")
            .replacingOccurrences(of: "Z-", with: "  ")
    }
}

private struct SessionPreview: View {
    let session: SavedSessionSummary
    @ObservedObject var model: SavedSessionsModel

    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                HStack(alignment: .firstTextBaseline) {
                    VStack(alignment: .leading, spacing: 4) {
                        Text("Session preview")
                            .font(.title2.weight(.semibold))
                        Text(session.modifiedAt, format: .dateTime.month(.wide).day().year().hour().minute())
                            .font(.callout)
                            .foregroundStyle(.secondary)
                    }
                    Spacer()
                    Text(session.name)
                        .font(.caption.monospaced())
                        .foregroundStyle(.secondary)
                        .textSelection(.enabled)
                }

                GroupBox("Transcript") {
                    if session.transcript.isEmpty {
                        Text("No transcript captured in this session.")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    } else {
                        Text(session.transcript)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .textSelection(.enabled)
                    }
                }

                GroupBox("Screen context") {
                    if session.imageNames.isEmpty {
                        Text("No screen captures in this session.")
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                    } else {
                        LazyVGrid(columns: [GridItem(.adaptive(minimum: 240), spacing: 14)], spacing: 14) {
                            ForEach(session.imageNames, id: \.self) { imageName in
                                if let image = model.image(for: imageName, in: session) {
                                    Image(nsImage: image)
                                        .resizable()
                                        .scaledToFit()
                                        .frame(maxHeight: 240)
                                        .clipShape(RoundedRectangle(cornerRadius: 8))
                                        .overlay(alignment: .bottomLeading) {
                                            Text(imageName)
                                                .font(.caption2.monospaced())
                                                .padding(5)
                                                .background(.ultraThinMaterial, in: RoundedRectangle(cornerRadius: 5))
                                                .padding(6)
                                        }
                                } else {
                                    Label("Could not preview \(imageName)", systemImage: "photo.badge.exclamationmark")
                                        .foregroundStyle(.secondary)
                                        .frame(maxWidth: .infinity, minHeight: 100)
                                }
                            }
                        }
                    }
                }
            }
            .padding(24)
        }
        .navigationTitle("Preview")
    }
}

@MainActor
final class SavedSessionsWindowController: NSObject, NSWindowDelegate {
    private var window: NSWindow?
    private var model: SavedSessionsModel?

    func show(rootURL: URL) {
        if let window, let model {
            model.refresh()
            NSApplication.shared.activate(ignoringOtherApps: true)
            window.makeKeyAndOrderFront(nil)
            return
        }

        let model = SavedSessionsModel(rootURL: rootURL)
        let window = NSWindow(contentViewController: NSHostingController(rootView: SavedSessionsView(model: model)))
        window.title = "Saved Sessions — BetterVoice"
        window.styleMask = [.titled, .closable, .miniaturizable, .resizable]
        window.isReleasedWhenClosed = false
        window.minSize = NSSize(width: 760, height: 480)
        window.setContentSize(NSSize(width: 980, height: 640))
        window.center()
        window.delegate = self
        self.model = model
        self.window = window
        NSApplication.shared.activate(ignoringOtherApps: true)
        window.makeKeyAndOrderFront(nil)
    }

    func windowWillClose(_ notification: Notification) {
        window = nil
        model = nil
    }
}
