import AppKit
import ApplicationServices
import AudioToolbox
import AVFoundation
import CoreAudio
import CoreGraphics
import Darwin
import ScreenCaptureKit
import BetterVoiceCore
import FluidAudio

private enum BetterVoiceError: LocalizedError {
    case microphoneUnavailable
    case microphoneRoutingFailed(String, OSStatus)
    case localModelUnavailable
    case sessionUnavailable
    case sessionStorageFull
    case screenPermissionRequired
    case screenshotUnavailable

    var errorDescription: String? {
        switch self {
        case .microphoneUnavailable: return "No microphone input is available."
        case .microphoneRoutingFailed(let name, let status): return "Could not route audio from \(name) (AudioUnit error \(status))."
        case .localModelUnavailable: return "Download the Local Parakeet model from the BetterVoice menu first."
        case .sessionUnavailable: return "The recording session is no longer available."
        case .sessionStorageFull: return "The 500 MB saved-session limit has been reached."
        case .screenPermissionRequired: return "Enable Screen Recording for BetterVoice."
        case .screenshotUnavailable: return "The selected screen area could not be captured."
        }
    }
}

private struct MicrophoneDevice: Equatable {
    let id: AudioDeviceID
    let uid: String
    let name: String
    let isExternal: Bool
}

@MainActor
private final class MicrophoneManager {
    private let selectedUIDKey = "selectedMicrophoneUID"
    private(set) var devices: [MicrophoneDevice] = []

    var selectedUID: String? {
        UserDefaults.standard.string(forKey: selectedUIDKey)
    }

    var selectedDevice: MicrophoneDevice? {
        guard let selectedUID else { return nil }
        return devices.first { $0.uid == selectedUID }
    }

    var recordingDevice: MicrophoneDevice? {
        selectedDevice ?? automaticDevice
    }

    var selectedLabel: String {
        if let selectedDevice { return selectedDevice.name }
        return "Automatic — \(automaticDevice?.name ?? "Unavailable")"
    }

    private var automaticDevice: MicrophoneDevice? {
        let defaultID = Self.defaultInputDeviceID()
        return devices.first { $0.id == defaultID && $0.isExternal }
            ?? devices.first { $0.isExternal }
            ?? devices.first { $0.id == defaultID }
            ?? devices.first
    }

    func refresh() {
        devices = Self.enumerateInputDevices()
        if let selectedUID, !devices.contains(where: { $0.uid == selectedUID }) {
            UserDefaults.standard.removeObject(forKey: selectedUIDKey)
        }
    }

    func select(uid: String?) {
        if let uid {
            UserDefaults.standard.set(uid, forKey: selectedUIDKey)
        } else {
            UserDefaults.standard.removeObject(forKey: selectedUIDKey)
        }
    }

    private static func enumerateInputDevices() -> [MicrophoneDevice] {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDevices,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var dataSize: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(
            AudioObjectID(kAudioObjectSystemObject),
            &address,
            0,
            nil,
            &dataSize
        ) == noErr else { return [] }

        let count = Int(dataSize) / MemoryLayout<AudioDeviceID>.stride
        guard count > 0 else { return [] }
        var deviceIDs = [AudioDeviceID](repeating: 0, count: count)
        let status = deviceIDs.withUnsafeMutableBufferPointer { buffer -> OSStatus in
            guard let baseAddress = buffer.baseAddress else { return -1 }
            return AudioObjectGetPropertyData(
                AudioObjectID(kAudioObjectSystemObject),
                &address,
                0,
                nil,
                &dataSize,
                baseAddress
            )
        }
        guard status == noErr else { return [] }

        return deviceIDs.compactMap { deviceID in
            guard hasInputChannels(deviceID),
                  let uid = stringProperty(deviceID, selector: kAudioDevicePropertyDeviceUID),
                  let name = deviceName(for: deviceID) else { return nil }
            return MicrophoneDevice(
                id: deviceID,
                uid: uid,
                name: name,
                isExternal: isExternal(deviceID)
            )
        }.sorted { $0.name.localizedCaseInsensitiveCompare($1.name) == .orderedAscending }
    }

    private static func isExternal(_ deviceID: AudioDeviceID) -> Bool {
        guard let transport = uint32Property(deviceID, selector: kAudioDevicePropertyTransportType) else {
            return false
        }
        return [
            kAudioDeviceTransportTypeUSB,
            kAudioDeviceTransportTypeBluetooth,
            kAudioDeviceTransportTypeBluetoothLE,
            kAudioDeviceTransportTypeThunderbolt,
            kAudioDeviceTransportTypeFireWire,
            kAudioDeviceTransportTypePCI,
        ].contains(transport)
    }

    private static func hasInputChannels(_ deviceID: AudioDeviceID) -> Bool {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioDevicePropertyStreamConfiguration,
            mScope: kAudioObjectPropertyScopeInput,
            mElement: kAudioObjectPropertyElementMain
        )
        var dataSize: UInt32 = 0
        guard AudioObjectGetPropertyDataSize(deviceID, &address, 0, nil, &dataSize) == noErr,
              dataSize > 0 else { return false }

        let rawBuffer = UnsafeMutableRawPointer.allocate(
            byteCount: Int(dataSize),
            alignment: MemoryLayout<AudioBufferList>.alignment
        )
        defer { rawBuffer.deallocate() }
        guard AudioObjectGetPropertyData(deviceID, &address, 0, nil, &dataSize, rawBuffer) == noErr else {
            return false
        }
        let bufferList = UnsafeMutableAudioBufferListPointer(
            rawBuffer.assumingMemoryBound(to: AudioBufferList.self)
        )
        return bufferList.contains { $0.mNumberChannels > 0 }
    }

    private static func stringProperty(_ deviceID: AudioDeviceID, selector: AudioObjectPropertySelector) -> String? {
        var address = AudioObjectPropertyAddress(
            mSelector: selector,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var value: Unmanaged<CFString>?
        var dataSize = UInt32(MemoryLayout<Unmanaged<CFString>?>.size)
        let status = withUnsafeMutablePointer(to: &value) { pointer in
            AudioObjectGetPropertyData(deviceID, &address, 0, nil, &dataSize, pointer)
        }
        guard status == noErr, let value else { return nil }
        return value.takeUnretainedValue() as String
    }

    private static func uint32Property(
        _ deviceID: AudioDeviceID,
        selector: AudioObjectPropertySelector
    ) -> UInt32? {
        var address = AudioObjectPropertyAddress(
            mSelector: selector,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var value: UInt32 = 0
        var dataSize = UInt32(MemoryLayout<UInt32>.size)
        guard AudioObjectGetPropertyData(
            deviceID,
            &address,
            0,
            nil,
            &dataSize,
            &value
        ) == noErr else { return nil }
        return value
    }

    private static func deviceName(for deviceID: AudioDeviceID) -> String? {
        stringProperty(deviceID, selector: kAudioObjectPropertyName)
    }

    private static func defaultInputDeviceID() -> AudioDeviceID? {
        var address = AudioObjectPropertyAddress(
            mSelector: kAudioHardwarePropertyDefaultInputDevice,
            mScope: kAudioObjectPropertyScopeGlobal,
            mElement: kAudioObjectPropertyElementMain
        )
        var deviceID: AudioDeviceID = kAudioObjectUnknown
        var dataSize = UInt32(MemoryLayout<AudioDeviceID>.size)
        let status = withUnsafeMutablePointer(to: &deviceID) { pointer in
            AudioObjectGetPropertyData(AudioObjectID(kAudioObjectSystemObject), &address, 0, nil, &dataSize, pointer)
        }
        guard status == noErr, deviceID != kAudioObjectUnknown else { return nil }
        return deviceID
    }
}

private enum LocalModelState: Equatable {
    case missing
    case downloading(Int)
    case loading
    case ready
    case failed(String)
}

private enum GrammarModelState: Equatable {
    case missing
    case downloading
    case ready
    case failed(String)
}

@MainActor
private final class LocalTranscriber {
    private static let grammarCorrectionKey = "grammarCorrectionEnabled"
    private static let developerCleanupKey = "developerCleanupEnabled"
    private static let transcriptionLanguageKey = "transcriptionLanguage"
    private(set) var state: LocalModelState = .missing
    private(set) var grammarModelState: GrammarModelState = .missing
    private var manager: AsrManager?
    private let grammarCorrector = GrammarCorrector()
    private let vocabularyURL = VocabularyFile.defaultURL()
    private var cachedVocabulary: [(String, String)] = []
    private var cachedVocabularyStamp: Date?
    var onStateChange: (() -> Void)?
    var onGrammarStatus: ((String) -> Void)?

    var grammarCorrectionEnabled: Bool {
        UserDefaults.standard.object(forKey: Self.grammarCorrectionKey) as? Bool ?? false
    }

    var developerCleanupEnabled: Bool {
        UserDefaults.standard.object(forKey: Self.developerCleanupKey) as? Bool ?? true
    }

    var transcriptionLanguage: TranscriptionLanguage {
        TranscriptionLanguage(storedCode: UserDefaults.standard.string(forKey: Self.transcriptionLanguageKey))
    }

    /// English keeps the model it has always used. Only another language pulls the
    /// multilingual one, so an existing English install downloads nothing new and
    /// its transcription is unchanged.
    private var modelVersion: AsrModelVersion {
        transcriptionLanguage.usesEnglishOnlyModel ? .v2 : .v3
    }

    /// The hint filters decoder tokens by writing script. It earns its place
    /// against Cyrillic or Greek leaking into a Latin transcript; between two
    /// Latin languages it is a no-op, which is why English terms still come
    /// through while dictating another Latin-script language.
    private var languageHint: Language? {
        transcriptionLanguage.scriptHintCode.flatMap(Language.init(rawValue:))
    }

    var isDownloaded: Bool {
        let version = modelVersion
        return AsrModels.modelsExist(
            at: AsrModels.defaultCacheDirectory(for: version),
            version: version
        )
    }

    var canChangeLanguage: Bool {
        state != .loading && !isDownloading
    }

    func loadCachedModel() async {
        guard isDownloaded else {
            setState(.missing)
            return
        }
        await prepare(download: false)
    }

    func downloadModel() async {
        await prepare(download: true)
    }

    func setGrammarCorrectionEnabled(_ enabled: Bool) {
        UserDefaults.standard.set(enabled, forKey: Self.grammarCorrectionKey)
    }

    func setDeveloperCleanupEnabled(_ enabled: Bool) {
        UserDefaults.standard.set(enabled, forKey: Self.developerCleanupKey)
    }

    /// Switching language can switch models, so the loaded one is dropped and the
    /// cached replacement loaded when it is already on disk. Nothing downloads
    /// behind the user's back; the setup row asks first.
    func setTranscriptionLanguage(_ language: TranscriptionLanguage) async {
        guard language != transcriptionLanguage, canChangeLanguage else { return }
        UserDefaults.standard.set(language.code, forKey: Self.transcriptionLanguageKey)
        manager = nil
        await loadCachedModel()
    }

    func loadCachedGrammarModel() async {
        grammarModelState = await grammarCorrector.isCached() ? .ready : .missing
        onStateChange?()
    }

    func prewarmGrammarModel() async {
        guard grammarCorrectionEnabled, transcriptionLanguage.allowsGrammarCorrection,
              grammarModelState != .ready else { return }
        await downloadGrammarModel()
    }

    func downloadGrammarModel() async {
        guard grammarModelState != .downloading else { return }
        grammarModelState = .downloading
        onStateChange?()
        grammarModelState = await grammarCorrector.preload() ? .ready : .failed("retry from Settings")
        onStateChange?()
    }

    /// Re-reads the user's map when the file changed, so an edit lands on the next
    /// recording without restarting BetterVoice.
    private func currentVocabulary() -> [(String, String)] {
        let attributes = try? FileManager.default.attributesOfItem(atPath: vocabularyURL.path)
        let stamp = attributes?[.modificationDate] as? Date
        if stamp != cachedVocabularyStamp {
            cachedVocabulary = VocabularyFile.terms(at: vocabularyURL)
            cachedVocabularyStamp = stamp
        }
        return cachedVocabulary
    }

    func transcribe(_ url: URL, profile: DeveloperAppProfile = .general) async throws -> String {
        guard let manager else { throw BetterVoiceError.localModelUnavailable }
        var decoderState = TdtDecoderState.make(decoderLayers: await manager.decoderLayerCount)
        let transcript = try await manager.transcribe(
            url, decoderState: &decoderState, language: languageHint
        ).text
        let vocabulary = developerCleanupEnabled ? currentVocabulary() : []
        let developerCleaned = developerCleanupEnabled
            ? DeveloperTextCleanup.apply(transcript, profile: profile, overrides: vocabulary)
            : transcript
        guard grammarCorrectionEnabled, transcriptionLanguage.allowsGrammarCorrection else {
            return developerCleaned
        }
        guard developerCleaned.split(whereSeparator: \.isWhitespace).count > 1 else { return developerCleaned }
        onGrammarStatus?("Polishing transcript locally…")
        let corrected = await grammarCorrector.correct(developerCleaned)
        onGrammarStatus?("Finishing…")
        return developerCleanupEnabled
            ? DeveloperTextCleanup.apply(corrected, profile: profile, overrides: vocabulary)
            : corrected
    }

    private func prepare(download: Bool) async {
        guard state != .loading, !isDownloading else { return }
        setState(download ? .downloading(0) : .loading)
        do {
            let progress: ProgressHandler?
            if download {
                progress = { [weak self] update in
                    Task { @MainActor in
                        self?.setState(.downloading(Int(update.fractionCompleted * 100)))
                    }
                }
            } else {
                progress = nil
            }
            let version = modelVersion
            let models = download
                ? try await AsrModels.downloadAndLoad(version: version, progressHandler: progress)
                : try await AsrModels.loadFromCache(version: version)
            manager = AsrManager(config: .default, models: models)
            setState(.ready)
        } catch {
            manager = nil
            setState(.failed(error.localizedDescription))
        }
    }

    private var isDownloading: Bool {
        if case .downloading = state { return true }
        return false
    }

    private func setState(_ value: LocalModelState) {
        state = value
        onStateChange?()
    }
}

private final class AudioRecorder {
    private var engine: AVAudioEngine?
    private var audioFile: AVAudioFile?
    private var recordingURL: URL?
    var onLevel: (@MainActor @Sendable (Float) -> Void)?

    static func removeAbandonedRecordings() {
        let manager = FileManager.default
        let temporary = manager.temporaryDirectory
        guard let files = try? manager.contentsOfDirectory(
            at: temporary,
            includingPropertiesForKeys: nil,
            options: [.skipsHiddenFiles]
        ) else { return }
        for file in files where file.lastPathComponent.hasPrefix("BetterVoice-") && file.pathExtension == "caf" {
            let parts = file.deletingPathExtension().lastPathComponent.split(separator: "-", maxSplits: 2)
            if parts.count == 3, let processID = Int32(parts[1]), kill(processID, 0) == 0 {
                continue
            }
            try? manager.removeItem(at: file)
        }
    }

    func start(device: MicrophoneDevice) throws {
        guard engine == nil else { throw BetterVoiceError.sessionUnavailable }
        let engine = AVAudioEngine()
        let inputNode = engine.inputNode
        guard let audioUnit = inputNode.audioUnit else {
            throw BetterVoiceError.microphoneRoutingFailed(device.name, -1)
        }

        var deviceID = device.id
        let status = AudioUnitSetProperty(
            audioUnit,
            kAudioOutputUnitProperty_CurrentDevice,
            kAudioUnitScope_Global,
            0,
            &deviceID,
            UInt32(MemoryLayout<AudioDeviceID>.size)
        )
        guard status == noErr else {
            throw BetterVoiceError.microphoneRoutingFailed(device.name, status)
        }

        let format = inputNode.inputFormat(forBus: 0)
        guard format.channelCount > 0 else { throw BetterVoiceError.microphoneUnavailable }
        let url = FileManager.default.temporaryDirectory
            .appendingPathComponent("BetterVoice-\(getpid())-\(UUID().uuidString).caf")
        let file = try AVAudioFile(forWriting: url, settings: format.settings)
        let levelHandler = onLevel
        let recordingReadyAt = ProcessInfo.processInfo.systemUptime + 0.2

        inputNode.installTap(onBus: 0, bufferSize: 1_024, format: format) { buffer, _ in
            guard ProcessInfo.processInfo.systemUptime >= recordingReadyAt else { return }
            try? file.write(from: buffer)
            guard buffer.format.channelCount > 0,
                  let samples = buffer.floatChannelData?[0],
                  buffer.frameLength > 0 else { return }
            var sum: Float = 0
            for index in 0..<Int(buffer.frameLength) {
                sum += samples[index] * samples[index]
            }
            let level = min(1, sqrt(sum / Float(buffer.frameLength)) * 12)
            Task { @MainActor in levelHandler?(level) }
        }

        do {
            engine.prepare()
            try engine.start()
            self.engine = engine
            audioFile = file
            recordingURL = url
        } catch {
            inputNode.removeTap(onBus: 0)
            try? FileManager.default.removeItem(at: url)
            throw error
        }
    }

    func stop() throws -> URL {
        guard let engine, let recordingURL else { throw BetterVoiceError.sessionUnavailable }
        engine.inputNode.removeTap(onBus: 0)
        engine.stop()
        self.engine = nil
        audioFile = nil
        self.recordingURL = nil
        return recordingURL
    }
}

@MainActor
private final class ScreenshotCapture {
    static func capture(gesture: CircleGesture, to url: URL) async throws {
        guard CGPreflightScreenCaptureAccess() else {
            throw BetterVoiceError.screenPermissionRequired
        }
        guard let displayRegion = displayBounds(containing: gesture.center) else {
            throw BetterVoiceError.screenshotUnavailable
        }

        let image: CGImage
        let region: CGRect
        if #available(macOS 15.2, *) {
            region = displayRegion
            do {
                image = try await SCScreenshotManager.captureImage(in: region)
            } catch {
                throw captureError(error)
            }
        } else {
            let content: SCShareableContent
            do {
                content = try await SCShareableContent.excludingDesktopWindows(
                    false,
                    onScreenWindowsOnly: true
                )
            } catch {
                throw captureError(error)
            }
            guard let display = content.displays.first(where: { $0.frame.contains(gesture.center) }) else {
                throw BetterVoiceError.screenshotUnavailable
            }
            region = display.frame

            let ownApplication = content.applications.filter {
                $0.bundleIdentifier == Bundle.main.bundleIdentifier
            }
            let filter = SCContentFilter(
                display: display,
                excludingApplications: ownApplication,
                exceptingWindows: []
            )
            let configuration = SCStreamConfiguration()
            configuration.sourceRect = region.offsetBy(dx: -display.frame.minX, dy: -display.frame.minY)
            configuration.width = Int(region.width * CGFloat(display.width) / display.frame.width)
            configuration.height = Int(region.height * CGFloat(display.height) / display.frame.height)
            configuration.showsCursor = false
            do {
                image = try await SCScreenshotManager.captureImage(
                contentFilter: filter,
                configuration: configuration
                )
            } catch {
                throw captureError(error)
            }
        }

        let marked = try highlight(image, target: gesture.center, region: region, radius: gesture.radius)
        let representation = NSBitmapImageRep(cgImage: marked)
        guard let data = representation.representation(using: .png, properties: [:]) else {
            throw BetterVoiceError.screenshotUnavailable
        }
        try data.write(to: url, options: .atomic)
    }

    private static func displayBounds(containing point: CGPoint) -> CGRect? {
        var display = CGDirectDisplayID()
        var count: UInt32 = 0
        guard CGGetDisplaysWithPoint(point, 1, &display, &count) == .success, count == 1 else { return nil }
        return CGDisplayBounds(display)
    }

    private static func captureError(_ error: Error) -> BetterVoiceError {
        let error = error as NSError
        if error.domain == SCStreamErrorDomain,
           error.code == SCStreamError.Code.userDeclined.rawValue {
            return .screenPermissionRequired
        }
        return .screenshotUnavailable
    }

    private static func highlight(_ image: CGImage, target: CGPoint, region: CGRect, radius: CGFloat) throws -> CGImage {
        let width = image.width
        let height = image.height
        let colorSpace = CGColorSpaceCreateDeviceRGB()
        guard let context = CGContext(
            data: nil,
            width: width,
            height: height,
            bitsPerComponent: 8,
            bytesPerRow: width * 4,
            space: colorSpace,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue
        ) else {
            throw BetterVoiceError.screenshotUnavailable
        }
        context.draw(image, in: CGRect(x: 0, y: 0, width: width, height: height))

        let scaleX = CGFloat(width) / region.width
        let scaleY = CGFloat(height) / region.height
        let x = (target.x - region.minX) * scaleX
        let y = CGFloat(height) - (target.y - region.minY) * scaleY
        let markedRadius = max(24, radius * min(scaleX, scaleY))
        let colors = [
            NSColor.systemCyan.withAlphaComponent(0.18).cgColor,
            NSColor.systemBlue.withAlphaComponent(0.12).cgColor,
            NSColor.systemBlue.withAlphaComponent(0).cgColor
        ] as CFArray
        if let gradient = CGGradient(
            colorsSpace: colorSpace,
            colors: colors,
            locations: [0, 0.68, 1]
        ) {
            context.drawRadialGradient(
                gradient,
                startCenter: CGPoint(x: x, y: y),
                startRadius: 0,
                endCenter: CGPoint(x: x, y: y),
                endRadius: markedRadius * 1.35,
                options: [.drawsAfterEndLocation]
            )
        }

        let marker = CGRect(x: x - markedRadius, y: y - markedRadius, width: markedRadius * 2, height: markedRadius * 2)
        context.setStrokeColor(NSColor.systemBlue.withAlphaComponent(0.9).cgColor)
        context.setLineWidth(max(4, markedRadius * 0.055))
        context.strokeEllipse(in: marker)
        return context.makeImage() ?? image
    }
}

private struct TrailPoint {
    let point: CGPoint
    let time: TimeInterval
}

private struct TrailConfirmation {
    let center: CGPoint
    let radius: CGFloat
    let startedAt: TimeInterval
}

@MainActor
private final class TrailOverlayView: NSView {
    private let trailLifetime: TimeInterval = 0.9
    private let confirmationLifetime: TimeInterval = 1.1
    private var trail: [TrailPoint] = []
    private var confirmations: [TrailConfirmation] = []
    private var globalOrigin = CGPoint.zero
    var reduceMotion = false
    override var isOpaque: Bool { false }

    func setGlobalOrigin(_ origin: CGPoint) {
        globalOrigin = origin
    }

    func add(point: CGPoint, at time: TimeInterval) {
        trail.append(TrailPoint(point: point, time: time))
        prune(now: time)
        needsDisplay = true
    }

    func confirm(center: CGPoint, radius: CGFloat, at time: TimeInterval) {
        confirmations.append(TrailConfirmation(center: center, radius: radius, startedAt: time))
        needsDisplay = true
    }

    func tick(now: TimeInterval) {
        prune(now: now)
        needsDisplay = true
    }

    func reset() {
        trail.removeAll(keepingCapacity: true)
        confirmations.removeAll(keepingCapacity: true)
        needsDisplay = false
    }

    override func draw(_ dirtyRect: NSRect) {
        guard let context = NSGraphicsContext.current?.cgContext else { return }
        context.clear(dirtyRect)
        let now = ProcessInfo.processInfo.systemUptime

        context.setLineCap(.round)
        let segments = trailSegments(
            points: trail.map(\.point),
            times: trail.map(\.time)
        )
        for segment in segments {
            guard trail.indices.contains(segment.from), trail.indices.contains(segment.to) else { continue }
            let previous = trail[segment.from]
            let current = trail[segment.to]
            let age = max(0, now - current.time)
            let fade = max(0, 1 - age / trailLifetime)
            guard fade > 0 else { continue }
            context.move(to: localPoint(previous.point))
            context.addLine(to: localPoint(current.point))
            context.setStrokeColor(NSColor.systemCyan.withAlphaComponent(0.28 * fade).cgColor)
            context.setLineWidth(12)
            context.strokePath()

            context.move(to: localPoint(previous.point))
            context.addLine(to: localPoint(current.point))
            context.setStrokeColor(NSColor.systemBlue.withAlphaComponent(0.92 * fade).cgColor)
            context.setLineWidth(4.5)
            context.strokePath()
        }

        if let head = trail.last {
            let age = max(0, now - head.time)
            let fade = max(0, 1 - age / trailLifetime)
            if fade > 0 {
                let center = localPoint(head.point)
                context.setFillColor(NSColor.systemBlue.withAlphaComponent(0.24 * fade).cgColor)
                context.fillEllipse(in: CGRect(x: center.x - 9, y: center.y - 9, width: 18, height: 18))
                context.setFillColor(NSColor.systemCyan.withAlphaComponent(0.98 * fade).cgColor)
                context.fillEllipse(in: CGRect(x: center.x - 4, y: center.y - 4, width: 8, height: 8))
            }
        }

        for confirmation in confirmations {
            let age = max(0, now - confirmation.startedAt)
            let progress = min(1, age / confirmationLifetime)
            let alpha = max(0, 0.82 * (1 - progress))
            guard alpha > 0 else { continue }
            let scale = reduceMotion ? 1 : 0.82 + 0.18 * progress
            let radius = confirmation.radius * scale
            let center = localPoint(confirmation.center)
            let rect = CGRect(x: center.x - radius, y: center.y - radius, width: radius * 2, height: radius * 2)
            context.setFillColor(NSColor.systemBlue.withAlphaComponent(alpha * 0.2).cgColor)
            context.fillEllipse(in: rect)
            context.setStrokeColor(NSColor.systemBlue.withAlphaComponent(alpha).cgColor)
            context.setLineWidth(6)
            context.strokeEllipse(in: rect)
        }
    }

    private func localPoint(_ point: CGPoint) -> CGPoint {
        CGPoint(x: point.x - globalOrigin.x, y: point.y - globalOrigin.y)
    }

    private func prune(now: TimeInterval) {
        trail.removeAll { now - $0.time > trailLifetime }
        confirmations.removeAll { now - $0.startedAt > confirmationLifetime }
    }
}

@MainActor
private final class TrailOverlayWindow: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }
}

@MainActor
private final class TrailOverlayController {
    private var overlays: [(window: TrailOverlayWindow, view: TrailOverlayView)] = []
    private var timer: Timer?

    func start() {
        stop()
        let reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion

        for screen in NSScreen.screens {
            let view = TrailOverlayView(frame: CGRect(origin: .zero, size: screen.frame.size))
            view.reduceMotion = reduceMotion
            view.setGlobalOrigin(screen.frame.origin)
            view.wantsLayer = true
            view.layerContentsRedrawPolicy = .onSetNeedsDisplay
            view.layer?.backgroundColor = NSColor.clear.cgColor

            let window = TrailOverlayWindow(
                contentRect: screen.frame,
                styleMask: [.borderless, .nonactivatingPanel],
                backing: .buffered,
                defer: false,
                screen: screen
            )
            window.isOpaque = false
            window.backgroundColor = .clear
            window.hasShadow = false
            window.ignoresMouseEvents = true
            window.level = .screenSaver
            window.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .ignoresCycle]
            window.sharingType = .none
            window.animationBehavior = .none
            window.isFloatingPanel = true
            window.hidesOnDeactivate = false
            window.contentView = view
            overlays.append((window, view))
            window.orderFrontRegardless()
            window.display()
        }

        let timer = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            DispatchQueue.main.async { [weak self] in self?.tick() }
        }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    func stop() {
        timer?.invalidate()
        timer = nil
        for overlay in overlays {
            overlay.view.reset()
            overlay.window.orderOut(nil)
        }
        overlays.removeAll(keepingCapacity: true)
    }

    func add(point: CGPoint, at time: TimeInterval) {
        for overlay in overlays {
            overlay.view.add(point: point, at: time)
        }
    }

    func confirm(center: CGPoint, radius: CGFloat, at time: TimeInterval) {
        for overlay in overlays {
            overlay.view.confirm(center: center, radius: radius, at: time)
        }
    }

    private func tick() {
        let now = ProcessInfo.processInfo.systemUptime
        for overlay in overlays {
            overlay.view.tick(now: now)
            overlay.view.display()
        }
    }
}

@MainActor
private final class RecordingHUDView: NSView {
    var microphone = ""
    var level: Float = 0
    var contextCount = 0
    var isFinishing = false
    var finishingMessage = "Transcribing…"
    var captureMessage: String?
    var reduceMotion = false

    override var isFlipped: Bool { true }

    override func draw(_ dirtyRect: NSRect) {
        NSColor(calibratedWhite: 0.07, alpha: 0.96).setFill()
        NSBezierPath(roundedRect: bounds, xRadius: 18, yRadius: 18).fill()

        let phase = reduceMotion ? 0 : Int(ProcessInfo.processInfo.systemUptime * 10)
        let shape: [CGFloat] = [0.45, 0.75, 1, 0.7, 0.4]
        for index in shape.indices {
            let pulse = CGFloat((phase + index) % 5) * 0.35
            let height = 7 + shape[index] * CGFloat(level) * 20 + pulse
            let rect = NSRect(x: 18 + CGFloat(index) * 7, y: 28 - height / 2, width: 3.5, height: height)
            (captureMessage == nil ? NSColor.systemBlue : NSColor.systemCyan).setFill()
            NSBezierPath(roundedRect: rect, xRadius: 2, yRadius: 2).fill()
        }

        let title = captureMessage ?? (isFinishing ? finishingMessage : "Listening")
        let detail = contextCount > 0 ? "\(microphone)  •  \(contextCount) captured" : microphone
        (title as NSString).draw(
            at: NSPoint(x: 62, y: 10),
            withAttributes: [
                .font: NSFont.systemFont(ofSize: 13, weight: .semibold),
                .foregroundColor: NSColor.white
            ]
        )
        (detail as NSString).draw(
            at: NSPoint(x: 62, y: 30),
            withAttributes: [
                .font: NSFont.systemFont(ofSize: 11),
                .foregroundColor: NSColor(calibratedWhite: 0.72, alpha: 1)
            ]
        )
    }
}

@MainActor
private final class RecordingHUDController {
    private let view = RecordingHUDView(frame: NSRect(x: 0, y: 0, width: 290, height: 56))
    private var panel: NSPanel?
    private var timer: Timer?
    private var captureTimer: Timer?

    func show(microphone: String) {
        hide()
        view.microphone = microphone
        view.level = 0
        view.contextCount = 0
        view.isFinishing = false
        view.captureMessage = nil
        view.reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        view.setAccessibilityElement(true)
        view.setAccessibilityLabel("BetterVoice listening on \(microphone)")

        let pointer = NSEvent.mouseLocation
        let screen = NSScreen.screens.first { NSMouseInRect(pointer, $0.frame, false) } ?? NSScreen.main
        guard let frame = screen?.visibleFrame else { return }
        let panelFrame = NSRect(
            x: frame.midX - view.frame.width / 2,
            y: frame.minY + 24,
            width: view.frame.width,
            height: view.frame.height
        )
        let panel = NSPanel(
            contentRect: panelFrame,
            styleMask: [.borderless, .nonactivatingPanel],
            backing: .buffered,
            defer: false
        )
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = true
        panel.ignoresMouseEvents = true
        panel.sharingType = .none
        panel.level = .statusBar
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary, .stationary, .ignoresCycle]
        panel.contentView = view
        self.panel = panel
        panel.orderFrontRegardless()

        guard !view.reduceMotion else { return }
        let timer = Timer(timeInterval: 1.0 / 20, repeats: true) { [weak self] _ in
            DispatchQueue.main.async { self?.view.needsDisplay = true }
        }
        self.timer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    func update(level: Float) {
        view.level = max(level, view.level * 0.78)
        view.needsDisplay = true
    }

    func confirmCapture(count: Int) {
        view.contextCount = count
        view.captureMessage = "Screenshot captured"
        view.setAccessibilityLabel("Screenshot \(count) captured")
        view.needsDisplay = true
        captureTimer?.invalidate()
        let timer = Timer(timeInterval: 1.4, repeats: false) { [weak self] _ in
            DispatchQueue.main.async {
                self?.view.captureMessage = nil
                self?.view.needsDisplay = true
            }
        }
        captureTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    func showCaptureError(_ message: String) {
        view.captureMessage = message
        view.setAccessibilityLabel(message)
        view.needsDisplay = true
    }

    func showFinishing() {
        captureTimer?.invalidate()
        captureTimer = nil
        view.captureMessage = nil
        view.isFinishing = true
        view.finishingMessage = "Transcribing…"
        view.level = 0.2
        view.setAccessibilityLabel("BetterVoice transcribing")
        view.needsDisplay = true
    }

    func showFinishingStatus(_ message: String) {
        guard view.isFinishing else { return }
        view.finishingMessage = message
        view.setAccessibilityLabel("BetterVoice \(message)")
        view.needsDisplay = true
    }

    func hide() {
        timer?.invalidate()
        timer = nil
        captureTimer?.invalidate()
        captureTimer = nil
        panel?.orderOut(nil)
        panel = nil
    }
}

@MainActor
private final class RecordingSoundController {
    private var sound: NSSound?

    func play(_ cue: RecordingSoundCue) {
        sound?.stop()
        sound = NSSound(named: cue.systemSoundName)
        sound?.volume = 0.35
        sound?.play()
    }
}

@MainActor
private enum SessionStorage {
    struct RecentSession {
        let folder: URL
        let transcript: String
        let images: [URL]
    }

    static let maxAge: TimeInterval = 7 * 24 * 60 * 60
    static let maxBytes: Int64 = 500 * 1_024 * 1_024

    static var root: URL {
        let desktop = FileManager.default.urls(for: .desktopDirectory, in: .userDomainMask).first
            ?? FileManager.default.temporaryDirectory
        return desktop.appendingPathComponent("BetterVoice", isDirectory: true)
    }

    static func prune(reservingBytes: Int64 = 0) throws {
        let manager = FileManager.default
        guard manager.fileExists(atPath: root.path) else { return }
        let keys: Set<URLResourceKey> = [.contentModificationDateKey, .isDirectoryKey]
        let folders = try manager.contentsOfDirectory(
            at: root,
            includingPropertiesForKeys: Array(keys),
            options: [.skipsHiddenFiles]
        )
        let sessions = folders.compactMap { folder -> StoredSession? in
            guard isBetterVoiceSessionName(folder.lastPathComponent) else { return nil }
            guard let values = try? folder.resourceValues(forKeys: keys), values.isDirectory == true else {
                return nil
            }
            return StoredSession(
                name: folder.lastPathComponent,
                modifiedAt: values.contentModificationDate ?? .distantPast,
                bytes: allocatedBytes(in: folder)
            )
        }
        let policy = SessionRetentionPolicy(maxAge: maxAge, maxBytes: max(0, maxBytes - reservingBytes))
        for name in policy.sessionsToRemove(from: sessions, now: Date()) {
            try manager.removeItem(at: root.appendingPathComponent(name, isDirectory: true))
        }
    }

    static func clear() throws {
        guard FileManager.default.fileExists(atPath: root.path) else { return }
        try FileManager.default.removeItem(at: root)
    }

    static func latest() -> RecentSession? {
        let manager = FileManager.default
        guard let folders = try? manager.contentsOfDirectory(
            at: root,
            includingPropertiesForKeys: [.contentModificationDateKey, .isDirectoryKey],
            options: [.skipsHiddenFiles]
        ) else { return nil }

        let candidates = folders.compactMap { folder -> (URL, Date)? in
            guard isBetterVoiceSessionName(folder.lastPathComponent),
                  let values = try? folder.resourceValues(forKeys: [
                      .contentModificationDateKey,
                      .isDirectoryKey,
                      .isSymbolicLinkKey
                  ]),
                  values.isDirectory == true,
                  values.isSymbolicLink != true
            else { return nil }
            return (folder, values.contentModificationDate ?? .distantPast)
        }.sorted { $0.1 > $1.1 }

        for (folder, _) in candidates {
            let markdownURL = folder.appendingPathComponent("context.md")
            guard let markdownValues = try? markdownURL.resourceValues(forKeys: [
                .fileSizeKey,
                .isRegularFileKey,
                .isSymbolicLinkKey
            ]),
                  markdownValues.isRegularFile == true,
                  markdownValues.isSymbolicLink != true,
                  let fileSize = markdownValues.fileSize,
                  fileSize <= 256 * 1_024
            else { continue }
            let markdown = (try? String(contentsOf: markdownURL, encoding: .utf8)) ?? ""
            let marker = "\n\n## Screen context"
            let transcript = markdown
                .replacingOccurrences(of: "# BetterVoice session\n\n", with: "")
                .components(separatedBy: marker)
                .first?
                .trimmingCharacters(in: .whitespacesAndNewlines)
                .replacingOccurrences(of: "_No transcript captured._", with: "") ?? ""
            let images = (try? manager.contentsOfDirectory(
                at: folder,
                includingPropertiesForKeys: [.isRegularFileKey],
                options: [.skipsHiddenFiles]
            ))?
                .filter { url in
                    guard url.lastPathComponent.hasPrefix("context-"),
                          url.pathExtension.lowercased() == "png"
                    else { return false }
                    guard let values = try? url.resourceValues(forKeys: [
                        .isRegularFileKey,
                        .isSymbolicLinkKey
                    ]) else { return false }
                    return values.isRegularFile == true && values.isSymbolicLink != true
                }
                .sorted { $0.lastPathComponent.localizedStandardCompare($1.lastPathComponent) == .orderedAscending }
                ?? []

            if !transcript.isEmpty || !images.isEmpty {
                return RecentSession(folder: folder, transcript: transcript, images: images)
            }
        }
        return nil
    }

    static func allocatedBytes(in folder: URL) -> Int64 {
        let keys: Set<URLResourceKey> = [.totalFileAllocatedSizeKey, .fileAllocatedSizeKey]
        guard let files = FileManager.default.enumerator(
            at: folder,
            includingPropertiesForKeys: Array(keys),
            options: [.skipsHiddenFiles]
        ) else { return 0 }
        return files.reduce(into: Int64(0)) { total, item in
            guard let url = item as? URL, let values = try? url.resourceValues(forKeys: keys) else { return }
            total += Int64(values.totalFileAllocatedSize ?? values.fileAllocatedSize ?? 0)
        }
    }
}

@MainActor
private final class SessionOutput {
    let folder: URL
    private(set) var images: [URL] = []
    private(set) var clipboardToRestore: [Clipboard.SavedItem]?
    private var usedBytes: Int64

    init() throws {
        let transcriptReserve: Int64 = 1_024 * 1_024
        try SessionStorage.prune(reservingBytes: transcriptReserve)
        usedBytes = SessionStorage.allocatedBytes(in: SessionStorage.root) + transcriptReserve
        let stamp = ISO8601DateFormatter().string(from: Date()).replacingOccurrences(of: ":", with: "-")
        folder = SessionStorage.root.appendingPathComponent("\(stamp)-\(UUID().uuidString)", isDirectory: true)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
    }

    func addImage(for gesture: CircleGesture) async throws -> URL {
        let url = folder.appendingPathComponent("context-\(images.count + 1).png")
        try await ScreenshotCapture.capture(gesture: gesture, to: url)
        let values = try url.resourceValues(forKeys: [.totalFileAllocatedSizeKey, .fileSizeKey])
        let bytes = Int64(values.totalFileAllocatedSize ?? values.fileSize ?? 0)
        let policy = SessionRetentionPolicy(maxAge: SessionStorage.maxAge, maxBytes: SessionStorage.maxBytes)
        guard policy.canStore(additionalBytes: bytes, usedBytes: usedBytes) else {
            try? FileManager.default.removeItem(at: url)
            throw BetterVoiceError.sessionStorageFull
        }
        usedBytes += bytes
        images.append(url)
        return url
    }

    func discard() {
        try? FileManager.default.removeItem(at: folder)
    }

    func finish(
        transcript: String,
        insertionContext: TextInsertion.Context?,
        shouldCopyToClipboard: Bool
    ) throws -> (markdownURL: URL, clipboardCopied: Bool, transcriptInserted: Bool) {
        let trimmed = transcript.trimmingCharacters(in: .whitespacesAndNewlines)
        var markdown = "# BetterVoice session\n\n"
        markdown += trimmed.isEmpty ? "_No transcript captured._\n" : "\(trimmed)\n"
        if !images.isEmpty {
            markdown += "\n## Screen context\n\n"
            for (index, image) in images.enumerated() {
                markdown += "![Context \(index + 1)](\(image.lastPathComponent))\n\n"
            }
        }
        let markdownURL = folder.appendingPathComponent("context.md")
        try markdown.write(to: markdownURL, atomically: true, encoding: .utf8)
        clipboardToRestore = shouldCopyToClipboard ? nil : Clipboard.snapshot()
        let previousClipboard = clipboardToRestore
        guard !trimmed.isEmpty, let insertionContext else {
            if let previousClipboard, !shouldCopyToClipboard {
                Clipboard.restore(previousClipboard, ifChangeCount: NSPasteboard.general.changeCount)
            }
            return (
                markdownURL,
                shouldCopyToClipboard && Clipboard.copy(transcript: trimmed, images: images),
                false
            )
        }

        guard Clipboard.copyTextOnly(trimmed) else {
            if let previousClipboard {
                Clipboard.restore(previousClipboard, ifChangeCount: NSPasteboard.general.changeCount)
            }
            return (markdownURL, false, false)
        }

        let pasteboardChangeCount = NSPasteboard.general.changeCount
        guard TextInsertion.paste(into: insertionContext) else {
            if let previousClipboard {
                Clipboard.restore(previousClipboard, ifChangeCount: pasteboardChangeCount)
            }
            return (
                markdownURL,
                shouldCopyToClipboard && Clipboard.copy(transcript: trimmed, images: images),
                false
            )
        }

        if images.isEmpty, let previousClipboard {
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) {
                Clipboard.restore(previousClipboard, ifChangeCount: pasteboardChangeCount)
            }
        }
        return (markdownURL, shouldCopyToClipboard, true)
    }
}

@MainActor
private enum Clipboard {
    struct SavedItem {
        let representations: [(NSPasteboard.PasteboardType, Data)]
    }

    static func copy(transcript: String, images: [URL]) -> Bool {
        let pasteboard = NSPasteboard.general

        let textItem = NSPasteboardItem()
        let rich = NSMutableAttributedString(string: transcript + (images.isEmpty ? "" : "\n\n"))
        var imageItems: [NSPasteboardItem] = []
        for (index, imageURL) in images.enumerated() {
            guard let data = try? Data(contentsOf: imageURL), let image = NSImage(data: data) else {
                return copyTextOnly(transcript)
            }
            let attachment = NSTextAttachment()
            attachment.image = image
            rich.append(NSAttributedString(attachment: attachment))
            rich.append(NSAttributedString(string: "\nContext \(index + 1)\n\n"))

            let imageItem = NSPasteboardItem()
            guard imageItem.setData(data, forType: .png) else { return copyTextOnly(transcript) }
            if let tiff = image.tiffRepresentation { imageItem.setData(tiff, forType: .tiff) }
            imageItem.setString(imageURL.absoluteString, forType: .fileURL)
            imageItems.append(imageItem)
        }

        // Codex attaches separate pasteboard images last-in-first-out.
        var objects: [NSPasteboardWriting] = imageItems.reversed()
        if !transcript.isEmpty {
            guard textItem.setString(transcript, forType: .string) else { return copyTextOnly(transcript) }
            if let rtf = try? rich.data(
                from: NSRange(location: 0, length: rich.length),
                documentAttributes: [.documentType: NSAttributedString.DocumentType.rtf]
            ) {
                textItem.setData(rtf, forType: .rtf)
            }
            objects.append(textItem)
        }
        guard !objects.isEmpty else { return false }
        pasteboard.clearContents()
        guard pasteboard.writeObjects(objects) else {
            return transcript.isEmpty ? false : copyTextOnly(transcript)
        }
        return true
    }

    static func copyTextOnly(_ transcript: String) -> Bool {
        guard !transcript.isEmpty else { return false }
        let pasteboard = NSPasteboard.general
        pasteboard.clearContents()
        return pasteboard.setString(transcript, forType: .string)
    }

    static func snapshot() -> [SavedItem] {
        (NSPasteboard.general.pasteboardItems ?? []).compactMap { item in
            let representations = item.types.compactMap { type -> (NSPasteboard.PasteboardType, Data)? in
                guard let data = item.data(forType: type) else { return nil }
                return (type, data)
            }
            return representations.isEmpty ? nil : SavedItem(representations: representations)
        }
    }

    static func restore(_ items: [SavedItem], ifChangeCount expected: Int) {
        let pasteboard = NSPasteboard.general
        guard pasteboard.changeCount == expected else { return }
        pasteboard.clearContents()
        let freshItems = items.map { savedItem in
            let item = NSPasteboardItem()
            for (type, data) in savedItem.representations {
                _ = item.setData(data, forType: type)
            }
            return item
        }
        guard !freshItems.isEmpty else { return }
        _ = pasteboard.writeObjects(freshItems)
    }
}

@MainActor
private enum TextInsertion {
    struct Context {
        let focusedElement: AXUIElement?
        let processIdentifier: pid_t
        let applicationName: String?
        let bundleIdentifier: String?
    }

    static func captureContext() -> Context? {
        guard let application = NSWorkspace.shared.frontmostApplication,
              application.bundleIdentifier != Bundle.main.bundleIdentifier
        else { return nil }
        let processIdentifier = application.processIdentifier
        let systemWideElement = AXUIElementCreateSystemWide()
        var focused: CFTypeRef?
        var focusedElement: AXUIElement?
        if AXUIElementCopyAttributeValue(
            systemWideElement,
            kAXFocusedUIElementAttribute as CFString,
            &focused
        ) == .success, let focused {
            guard CFGetTypeID(focused) == AXUIElementGetTypeID() else { return nil }
            let candidate = unsafeDowncast(focused, to: AXUIElement.self)
            var focusedProcessIdentifier: pid_t = 0
            if AXUIElementGetPid(candidate, &focusedProcessIdentifier) == .success,
               focusedProcessIdentifier == processIdentifier {
                focusedElement = candidate
            }
        }
        return Context(
            focusedElement: focusedElement,
            processIdentifier: processIdentifier,
            applicationName: application.localizedName,
            bundleIdentifier: application.bundleIdentifier
        )
    }

    static func paste(into context: Context) -> Bool {
        guard CGPreflightPostEventAccess(),
              let application = NSRunningApplication(processIdentifier: context.processIdentifier),
              application.bundleIdentifier != Bundle.main.bundleIdentifier
        else { return false }

        if let focusedElement = context.focusedElement {
            _ = AXUIElementSetAttributeValue(
               focusedElement,
               kAXFocusedAttribute as CFString,
               kCFBooleanTrue
            )
        }

        let source = CGEventSource(stateID: .hidSystemState)
        guard let keyDown = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: true),
              let keyUp = CGEvent(keyboardEventSource: source, virtualKey: 9, keyDown: false)
        else { return false }
        keyDown.flags = .maskCommand
        keyUp.flags = .maskCommand
        keyDown.postToPid(context.processIdentifier)
        keyUp.postToPid(context.processIdentifier)
        return true
    }
}

@MainActor
private final class InputMonitor {
    private var globalFlagsMonitor: Any?
    private var localFlagsMonitor: Any?
    private var globalKeyDownMonitor: Any?
    private var localKeyDownMonitor: Any?
    private var globalKeyUpMonitor: Any?
    private var localKeyUpMonitor: Any?
    private var mouseTimer: Timer?
    private var pushToTalkTimer: Timer?
    private var lastMouseLocation: CGPoint?
    private var hotkeyConfiguration = HotkeyConfiguration.standard
    private var modifierQuickActive = false
    private var modifierLongActive = false
    private var quickKeyActive = false
    private var quickHoldActive = false
    private var quickNoteRecording = false
    private var quickDoubleTapDetector = ModifierDoubleTapDetector()
    private let startPushToTalk: () -> Void
    private let stopPushToTalk: () -> Void
    private let toggleLongForm: () -> Void
    private let promoteToLongForm: () -> Void
    private let mouseMoved: (CGPoint) -> Void

    init(
        startPushToTalk: @escaping () -> Void,
        stopPushToTalk: @escaping () -> Void,
        toggleLongForm: @escaping () -> Void,
        promoteToLongForm: @escaping () -> Void,
        mouseMoved: @escaping (CGPoint) -> Void
    ) {
        self.startPushToTalk = startPushToTalk
        self.stopPushToTalk = stopPushToTalk
        self.toggleLongForm = toggleLongForm
        self.promoteToLongForm = promoteToLongForm
        self.mouseMoved = mouseMoved
    }

    func start() {
        resetShortcutState()
        refreshKeyboardMonitoring()
    }

    func update(configuration: HotkeyConfiguration) {
        hotkeyConfiguration = configuration
        resetShortcutState()
    }

    func recordingDidEnd() {
        quickNoteRecording = false
        quickDoubleTapDetector.reset()
    }

    func refreshKeyboardMonitoring() {
        if let monitor = globalFlagsMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = globalKeyDownMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = globalKeyUpMonitor { NSEvent.removeMonitor(monitor) }
        globalFlagsMonitor = NSEvent.addGlobalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
            DispatchQueue.main.async { self?.handleFlags(event.modifierFlags) }
        }
        globalKeyDownMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyDown) { [weak self] event in
            DispatchQueue.main.async { self?.handleKeyDown(event) }
        }
        globalKeyUpMonitor = NSEvent.addGlobalMonitorForEvents(matching: .keyUp) { [weak self] event in
            DispatchQueue.main.async { self?.handleKeyUp(event) }
        }
        if localFlagsMonitor == nil {
            localFlagsMonitor = NSEvent.addLocalMonitorForEvents(matching: .flagsChanged) { [weak self] event in
                DispatchQueue.main.async { self?.handleFlags(event.modifierFlags) }
                return event
            }
        }
        if localKeyDownMonitor == nil {
            localKeyDownMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
                DispatchQueue.main.async { self?.handleKeyDown(event) }
                return event
            }
        }
        if localKeyUpMonitor == nil {
            localKeyUpMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyUp) { [weak self] event in
                DispatchQueue.main.async { self?.handleKeyUp(event) }
                return event
            }
        }
    }

    func startMouseTracking() {
        guard mouseTimer == nil else { return }
        lastMouseLocation = nil
        let timer = Timer(timeInterval: 1.0 / 60, repeats: true) { [weak self] _ in
            DispatchQueue.main.async { self?.sampleMouse() }
        }
        mouseTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    func stopMouseTracking() {
        mouseTimer?.invalidate()
        mouseTimer = nil
        lastMouseLocation = nil
    }

    func stop() {
        if let monitor = globalFlagsMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = localFlagsMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = globalKeyDownMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = localKeyDownMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = globalKeyUpMonitor { NSEvent.removeMonitor(monitor) }
        if let monitor = localKeyUpMonitor { NSEvent.removeMonitor(monitor) }
        mouseTimer?.invalidate()
        globalFlagsMonitor = nil
        localFlagsMonitor = nil
        globalKeyDownMonitor = nil
        localKeyDownMonitor = nil
        globalKeyUpMonitor = nil
        localKeyUpMonitor = nil
        mouseTimer = nil
        lastMouseLocation = nil
        resetShortcutState()
    }

    private func handleFlags(_ flags: NSEvent.ModifierFlags) {
        let normalized = flags.intersection(.deviceIndependentFlagsMask)
        let now = Date().timeIntervalSinceReferenceDate
        let quick = hotkeyConfiguration.quick
        if quick.keyCode == nil {
            let active = quick.matches(
                command: normalized.contains(.command),
                option: normalized.contains(.option),
                control: normalized.contains(.control),
                shift: normalized.contains(.shift)
            )
            switch hotkeyConfiguration.quickTriggerMode {
            case .hold:
                if active != modifierQuickActive {
                    modifierQuickActive = active
                    active ? beginQuickShortcut() : endQuickShortcut()
                }
            case .doubleTap:
                if quickDoubleTapDetector.modifierChanged(active: active, now: now) {
                    toggleQuickNoteRecording()
                }
                modifierQuickActive = active
            case .toggle:
                break
            }
        }

        guard hotkeyConfiguration.longNoteEnabled else {
            if modifierLongActive {
                modifierLongActive = false
            }
            return
        }

        let long = hotkeyConfiguration.long
        if long.keyCode == nil {
            let active = long.matches(
                command: normalized.contains(.command),
                option: normalized.contains(.option),
                control: normalized.contains(.control),
                shift: normalized.contains(.shift)
            )
            if active, !modifierLongActive {
                triggerLongShortcut()
            }
            modifierLongActive = active
        }
    }

    private func handleKeyDown(_ event: NSEvent) {
        guard !event.isARepeat else { return }
        if hotkeyConfiguration.quick.isModifierOnly,
           hotkeyConfiguration.quickTriggerMode == .doubleTap {
            quickDoubleTapDetector.nonModifierKeyPressed()
        }
        if hotkeyConfiguration.longNoteEnabled, matches(hotkeyConfiguration.long, event: event) {
            triggerLongShortcut()
            return
        }
        guard matches(hotkeyConfiguration.quick, event: event) else { return }
        switch hotkeyConfiguration.quickTriggerMode {
        case .hold:
            guard !quickKeyActive else { return }
            quickKeyActive = true
            beginQuickShortcut()
        case .doubleTap:
            break
        case .toggle:
            toggleQuickNoteRecording()
        }
    }

    private func handleKeyUp(_ event: NSEvent) {
        guard hotkeyConfiguration.quickTriggerMode == .hold,
              quickKeyActive,
              matches(hotkeyConfiguration.quick, event: event) else { return }
        quickKeyActive = false
        endQuickShortcut()
    }

    private func matches(_ binding: HotkeyBinding, event: NSEvent) -> Bool {
        guard let keyCode = binding.keyCode, event.keyCode == keyCode else { return false }
        let flags = event.modifierFlags.intersection(.deviceIndependentFlagsMask)
        return binding.matches(
            command: flags.contains(.command),
            option: flags.contains(.option),
            control: flags.contains(.control),
            shift: flags.contains(.shift)
        )
    }

    private func beginQuickShortcut() {
        guard !quickHoldActive else { return }
        quickHoldActive = true
        pushToTalkTimer?.invalidate()
        let timer = Timer(
            timeInterval: TimeInterval(hotkeyConfiguration.quickHoldDelayMilliseconds) / 1000,
            repeats: false
        ) { [weak self] _ in
            DispatchQueue.main.async { [weak self] in
                guard let self else { return }
                self.pushToTalkTimer = nil
                self.startPushToTalk()
            }
        }
        pushToTalkTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func endQuickShortcut() {
        quickHoldActive = false
        pushToTalkTimer?.invalidate()
        pushToTalkTimer = nil
        stopPushToTalk()
    }

    private func toggleQuickNoteRecording() {
        pushToTalkTimer?.invalidate()
        pushToTalkTimer = nil
        quickHoldActive = false
        if quickNoteRecording {
            quickNoteRecording = false
            stopPushToTalk()
        } else {
            quickNoteRecording = true
            startPushToTalk()
        }
    }

    private func triggerLongShortcut() {
        pushToTalkTimer?.invalidate()
        pushToTalkTimer = nil
        if quickHoldActive {
            promoteToLongForm()
        } else if quickNoteRecording {
            quickNoteRecording = false
            quickDoubleTapDetector.reset()
            promoteToLongForm()
        } else {
            toggleLongForm()
        }
    }

    private func resetShortcutState() {
        pushToTalkTimer?.invalidate()
        pushToTalkTimer = nil
        modifierQuickActive = false
        modifierLongActive = false
        quickKeyActive = false
        quickHoldActive = false
        quickNoteRecording = false
        quickDoubleTapDetector.reset()
    }

    private func sampleMouse() {
        guard let location = CGEvent(source: nil)?.location,
              location != lastMouseLocation else { return }
        lastMouseLocation = location
        mouseMoved(location)
    }
}

private enum SessionState {
    case idle
    case recording
    case finishing
}

private enum RecordingMode {
    case pushToTalk
    case longForm
}

private enum StatusIconState {
    case idle
    case recording
    case finishing
}

@MainActor
private final class AppController: NSObject, NSApplicationDelegate, NSMenuDelegate {
    private static let circleMinimumAngleKey = "circleMinimumAngleDegrees"
    private static let quickHotkeyKey = "quickRecordingHotkey"
    private static let longHotkeyKey = "longRecordingHotkey"
    private static let quickNoteTriggerModeKey = "quickNoteTriggerMode"
    private static let legacyLongNoteTriggerModeKey = "longNoteTriggerMode"
    private static let longNoteEnabledKey = "longNoteEnabled"
    private static let quickNoteHoldDelayKey = "quickNoteHoldDelayMilliseconds"
    private let statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.variableLength)
    private let microphones = MicrophoneManager()
    private let recorder = AudioRecorder()
    private let transcriber = LocalTranscriber()
    private let trailOverlay = TrailOverlayController()
    private let recordingHUD = RecordingHUDController()
    private let recordingSounds = RecordingSoundController()
    private let setupWindow = SetupWindowController()
    private let recoveryNotice = RecoveryNoticeController()
    private var inputMonitor: InputMonitor?
    private var detector = CircleGestureDetector()
    private var output: SessionOutput?
    private var transcript = ""
    private var recordingStartedAt: TimeInterval?
    private var state = SessionState.idle
    private var recordingMode: RecordingMode?
    private var statusMenuItem: NSMenuItem?
    private var recordingMenuItem: NSMenuItem?
    private var modelMenuItem: NSMenuItem?
    private var microphoneMenu: NSMenu?
    private var recentMenu: NSMenu?
    private var languageMenu: NSMenu?
    private var statusAnimationTimer: Timer?
    private var statusFeedbackTimer: Timer?
    private var recordingLimitTimer: Timer?
    private var captureTasks: [Task<Void, Never>] = []
    private var textInsertionContext: TextInsertion.Context?
    private var statusPulse = false
    private var reduceMotion = false
    private var hotkeyConfiguration: HotkeyConfiguration {
        HotkeyConfiguration(
            quick: loadHotkey(forKey: Self.quickHotkeyKey, fallback: .option),
            long: loadHotkey(forKey: Self.longHotkeyKey, fallback: .commandOption),
            quickTriggerMode: loadQuickTriggerMode(),
            longNoteEnabled: loadLongNoteEnabled(),
            quickHoldDelayMilliseconds: loadQuickNoteHoldDelay()
        )
    }

    private var circleMinimumAngleDegrees: Double {
        let stored = UserDefaults.standard.double(forKey: Self.circleMinimumAngleKey)
        return min(max(stored == 0 ? 340 : stored, 300), 359)
    }

    private var quickShortcutLabel: String { hotkeyConfiguration.quick.label }
    private var longShortcutLabel: String { hotkeyConfiguration.long.label }
    private var quickShortcutActionHint: String {
        actionHint(
            for: hotkeyConfiguration.quickTriggerMode,
            bindingLabel: quickShortcutLabel
        )
    }
    private var longShortcutActionHint: String {
        guard hotkeyConfiguration.longNoteEnabled else { return "" }
        return actionHint(for: .toggle, bindingLabel: longShortcutLabel)
    }
    private var quickShortcutStopHint: String {
        stopHint(
            for: hotkeyConfiguration.quickTriggerMode,
            bindingLabel: quickShortcutLabel
        )
    }
    private var readyStatusHint: String {
        let hints = [quickShortcutActionHint, longShortcutActionHint].filter { !$0.isEmpty }
        return hints.joined(separator: " or ")
    }

    private func actionHint(for mode: RecordingTriggerMode, bindingLabel: String) -> String {
        switch mode {
        case .hold: return "hold \(bindingLabel)"
        case .toggle: return "press \(bindingLabel)"
        case .doubleTap: return "double-tap \(bindingLabel)"
        }
    }

    private func stopHint(for mode: RecordingTriggerMode, bindingLabel: String) -> String {
        switch mode {
        case .hold: return "Release \(bindingLabel) to stop"
        case .toggle: return "Press \(bindingLabel) again to stop"
        case .doubleTap: return "Double-tap \(bindingLabel) to stop"
        }
    }

    private lazy var setupModel: SetupModel = {
        let model = SetupModel()
        model.requestMicrophone = { [weak self] in self?.requestMicrophoneAuthorization() }
        model.chooseMicrophone = { [weak self] id in
            guard id == "automatic" || !id.isEmpty else { return }
            self?.selectMicrophone(uid: id == "automatic" ? nil : id)
        }
        model.requestScreen = { [weak self] in self?.requestScreenCaptureAuthorization() }
        model.requestAccessibility = { [weak self] in self?.requestTextInsertionAuthorization() }
        model.downloadModel = { [weak self] in self?.downloadModel() }
        model.downloadGrammarModel = { [weak self] in self?.downloadGrammarModel() }
        model.setGrammarCorrection = { [weak self] enabled in
            self?.transcriber.setGrammarCorrectionEnabled(enabled)
        }
        model.setTranscriptionLanguage = { [weak self] language in
            guard let self, self.state == .idle, self.transcriber.canChangeLanguage else { return }
            Task { @MainActor in
                guard self.state == .idle, self.transcriber.canChangeLanguage else { return }
                await self.transcriber.setTranscriptionLanguage(language)
                self.refreshLanguageMenu()
                self.refreshModelMenu()
                self.refreshSetupModel()
            }
        }
        model.setDeveloperCleanup = { [weak self] enabled in
            self?.transcriber.setDeveloperCleanupEnabled(enabled)
        }
        model.setCircleMinimumAngle = { [weak self] degrees in
            self?.setCircleMinimumAngle(degrees)
        }
        model.setHotkeyConfiguration = { [weak self] configuration in
            self?.setHotkeyConfiguration(configuration)
        }
        model.refresh = { [weak self] in self?.refreshSetupModel() }
        model.complete = { [weak self] in
            UserDefaults.standard.set(true, forKey: "completedOnboarding")
            self?.setupWindow.close()
        }
        return model
    }()

    func applicationDidFinishLaunching(_ notification: Notification) {
        AudioRecorder.removeAbandonedRecordings()
        try? VocabularyFile.createTemplateIfMissing(at: VocabularyFile.defaultURL())
        microphones.refresh()
        reduceMotion = NSWorkspace.shared.accessibilityDisplayShouldReduceMotion
        setStatusIcon(.idle)
        statusItem.button?.imagePosition = .imageOnly
        statusItem.button?.imageScaling = .scaleProportionallyDown
        statusItem.button?.toolTip = "BetterVoice — \(readyStatusHint) • Microphone: \(microphones.selectedLabel)"

        let menu = NSMenu()
        let statusMenuItem = NSMenuItem(title: "Ready • \(readyStatusHint)", action: nil, keyEquivalent: "")
        statusMenuItem.isEnabled = false
        self.statusMenuItem = statusMenuItem
        menu.addItem(statusMenuItem)
        menu.addItem(NSMenuItem.separator())

        let recordingItem = NSMenuItem(title: "Start long recording (\(longShortcutLabel))", action: #selector(toggleRecording), keyEquivalent: "")
        recordingItem.target = self
        recordingMenuItem = recordingItem
        menu.addItem(recordingItem)

        let modelItem = NSMenuItem(title: "Download Local Model (~500 MB)", action: #selector(downloadModel), keyEquivalent: "")
        modelItem.target = self
        modelMenuItem = modelItem
        menu.addItem(modelItem)

        let microphoneItem = NSMenuItem(title: "Microphone", action: nil, keyEquivalent: "")
        let microphoneMenu = NSMenu()
        microphoneItem.submenu = microphoneMenu
        self.microphoneMenu = microphoneMenu
        menu.addItem(microphoneItem)

        let languageItem = NSMenuItem(title: "Dictation Language", action: nil, keyEquivalent: "")
        let languageMenu = NSMenu()
        languageItem.submenu = languageMenu
        self.languageMenu = languageMenu
        menu.addItem(languageItem)
        menu.addItem(NSMenuItem.separator())

        let recentItem = NSMenuItem(title: "Recent", action: nil, keyEquivalent: "")
        let recentMenu = NSMenu()
        recentItem.submenu = recentMenu
        self.recentMenu = recentMenu
        menu.addItem(recentItem)
        menu.addItem(NSMenuItem.separator())

        let setupItem = NSMenuItem(title: "Settings…", action: #selector(showSetup), keyEquivalent: ",")
        setupItem.target = self
        menu.addItem(setupItem)
        let vocabularyItem = NSMenuItem(title: "Edit Vocabulary…", action: #selector(editVocabulary), keyEquivalent: "")
        vocabularyItem.target = self
        menu.addItem(vocabularyItem)
        let sessionsItem = NSMenuItem(title: "Open Saved Sessions", action: #selector(openSavedSessions), keyEquivalent: "")
        sessionsItem.target = self
        menu.addItem(sessionsItem)
        let clearItem = NSMenuItem(title: "Clear Saved Sessions…", action: #selector(clearSavedSessions), keyEquivalent: "")
        clearItem.target = self
        menu.addItem(clearItem)
        menu.addItem(NSMenuItem.separator())

        let quitItem = NSMenuItem(title: "Quit BetterVoice", action: #selector(quit), keyEquivalent: "q")
        quitItem.target = self
        menu.addItem(quitItem)
        menu.delegate = self
        statusItem.menu = menu
        refreshMicrophoneMenu()
        refreshModelMenu()

        transcriber.onStateChange = { [weak self] in
            self?.refreshModelMenu()
            self?.refreshSetupModel()
        }
        setupModel.grammarCorrectionEnabled = transcriber.grammarCorrectionEnabled
        setupModel.developerCleanupEnabled = transcriber.developerCleanupEnabled
        transcriber.onGrammarStatus = { [weak self] status in
            self?.recordingHUD.showFinishingStatus(status)
            self?.showStatus(status)
        }
        recorder.onLevel = { [weak self] level in self?.recordingHUD.update(level: level) }
        Task {
            await transcriber.loadCachedModel()
            await transcriber.loadCachedGrammarModel()
            await transcriber.prewarmGrammarModel()
        }

        inputMonitor = InputMonitor(
            startPushToTalk: { [weak self] in self?.startPushToTalk() },
            stopPushToTalk: { [weak self] in self?.stopPushToTalk() },
            toggleLongForm: { [weak self] in self?.toggleRecording() },
            promoteToLongForm: { [weak self] in self?.promoteToLongForm() },
            mouseMoved: { [weak self] quartzPoint in
                self?.handleMouse(quartzPoint: quartzPoint)
            }
        )
        inputMonitor?.update(configuration: hotkeyConfiguration)
        inputMonitor?.start()
        if !CGPreflightPostEventAccess() {
            _ = CGRequestPostEventAccess()
        }
        setupModel.hotkeyConfiguration = hotkeyConfiguration
        setupModel.quickNoteTriggerMode = hotkeyConfiguration.quickTriggerMode
        setupModel.longNoteEnabled = hotkeyConfiguration.longNoteEnabled
        setupModel.quickNoteHoldDelayMilliseconds = hotkeyConfiguration.quickHoldDelayMilliseconds
        setupModel.circleMinimumAngleDegrees = circleMinimumAngleDegrees
        updateShortcutStatus()
        do {
            try SessionStorage.prune()
        } catch {
            showError("Saved-session cleanup failed", detail: error.localizedDescription)
        }
        if !UserDefaults.standard.bool(forKey: "completedOnboarding") {
            DispatchQueue.main.async { [weak self] in self?.showSetupWindow(onboarding: true) }
        }
    }

    func applicationDidBecomeActive(_ notification: Notification) {
        if AXIsProcessTrusted() {
            inputMonitor?.refreshKeyboardMonitoring()
        }
        refreshSetupModel()
    }

    func applicationShouldHandleReopen(_ sender: NSApplication, hasVisibleWindows flag: Bool) -> Bool {
        if !flag {
            showSetup()
        }
        return true
    }

    func applicationWillTerminate(_ notification: Notification) {
        shutdown()
    }

    func menuWillOpen(_ menu: NSMenu) {
        refreshLanguageMenu()
        guard let statusMenu = statusItem.menu, menu === statusMenu else { return }
        microphones.refresh()
        refreshMicrophoneMenu()
        refreshRecentMenu()
    }

    private func refreshRecentMenu() {
        guard let recentMenu else { return }
        recentMenu.removeAllItems()
        guard let recent = SessionStorage.latest() else {
            let empty = NSMenuItem(title: "No recent recording", action: nil, keyEquivalent: "")
            empty.isEnabled = false
            recentMenu.addItem(empty)
            return
        }

        let header = NSMenuItem(title: "Latest recording", action: nil, keyEquivalent: "")
        header.isEnabled = false
        recentMenu.addItem(header)

        let copyAll = NSMenuItem(title: "Copy All", action: #selector(copyRecentContent(_:)), keyEquivalent: "")
        copyAll.target = self
        copyAll.representedObject = recent
        recentMenu.addItem(copyAll)

        if !recent.transcript.isEmpty {
            let preview = NSMenuItem(title: "Text: \(menuPreview(recent.transcript))", action: nil, keyEquivalent: "")
            preview.isEnabled = false
            recentMenu.addItem(preview)

            let copyText = NSMenuItem(title: "Copy Text", action: #selector(copyRecentTranscript(_:)), keyEquivalent: "")
            copyText.target = self
            copyText.representedObject = recent.transcript
            recentMenu.addItem(copyText)
        }

        if !recent.images.isEmpty {
            if !recent.transcript.isEmpty { recentMenu.addItem(.separator()) }
            let copyImages = NSMenuItem(title: "Copy Images", action: #selector(copyRecentImages(_:)), keyEquivalent: "")
            copyImages.target = self
            copyImages.representedObject = recent.images
            recentMenu.addItem(copyImages)

            let openImages = NSMenuItem(title: "Open Images", action: #selector(openRecentImages(_:)), keyEquivalent: "")
            openImages.target = self
            openImages.representedObject = recent.images
            recentMenu.addItem(openImages)
        }

        recentMenu.addItem(.separator())
        let openFolder = NSMenuItem(title: "Open Session Folder", action: #selector(openRecentSession(_:)), keyEquivalent: "")
        openFolder.target = self
        openFolder.representedObject = recent.folder
        recentMenu.addItem(openFolder)
    }

    private func menuPreview(_ text: String) -> String {
        let preview = text.replacingOccurrences(of: "\\s+", with: " ", options: .regularExpression)
        guard preview.count > 72 else { return preview }
        return String(preview.prefix(69)) + "…"
    }

    private func refreshLanguageMenu() {
        let enabled = state == .idle && transcriber.canChangeLanguage
        setupModel.languageSelectionEnabled = enabled
        setupModel.transcriptionLanguage = transcriber.transcriptionLanguage
        guard let languageMenu else { return }
        languageMenu.removeAllItems()
        let selected = transcriber.transcriptionLanguage
        for language in TranscriptionLanguage.all {
            let item = NSMenuItem(
                title: language.name,
                action: #selector(selectTranscriptionLanguage(_:)),
                keyEquivalent: ""
            )
            item.target = self
            item.representedObject = language.code
            item.state = language == selected ? .on : .off
            item.isEnabled = enabled
            languageMenu.addItem(item)
        }
    }

    @objc private func selectTranscriptionLanguage(_ sender: NSMenuItem) {
        guard state == .idle, transcriber.canChangeLanguage else { return }
        let language = TranscriptionLanguage(storedCode: sender.representedObject as? String)
        Task { @MainActor in
            guard self.state == .idle else { return }
            await transcriber.setTranscriptionLanguage(language)
            refreshLanguageMenu()
            refreshModelMenu()
            refreshSetupModel()
        }
    }

    private func refreshMicrophoneMenu() {
        setupModel.microphoneSelectionEnabled = state == .idle
        guard let microphoneMenu else { return }
        microphoneMenu.removeAllItems()
        let enabled = state == .idle

        let automatic = NSMenuItem(
            title: microphones.selectedUID == nil ? microphones.selectedLabel : "Automatic",
            action: #selector(selectMicrophone(_:)),
            keyEquivalent: ""
        )
        automatic.target = self
        automatic.state = microphones.selectedUID == nil ? .on : .off
        automatic.isEnabled = enabled
        microphoneMenu.addItem(automatic)

        if !microphones.devices.isEmpty {
            microphoneMenu.addItem(.separator())
            for device in microphones.devices {
                let item = NSMenuItem(title: device.name, action: #selector(selectMicrophone(_:)), keyEquivalent: "")
                item.target = self
                item.representedObject = device.uid
                item.state = microphones.selectedUID == device.uid ? .on : .off
                item.isEnabled = enabled
                microphoneMenu.addItem(item)
            }
        } else {
            let unavailable = NSMenuItem(title: "No input microphones found", action: nil, keyEquivalent: "")
            unavailable.isEnabled = false
            microphoneMenu.addItem(unavailable)
        }
    }

    @objc private func selectMicrophone(_ sender: NSMenuItem) {
        selectMicrophone(uid: sender.representedObject as? String)
    }

    private func selectMicrophone(uid: String?) {
        guard state == .idle else { return }
        microphones.select(uid: uid)
        microphones.refresh()
        refreshMicrophoneMenu()
        refreshSetupModel()
        showStatus("Microphone: \(microphones.selectedLabel)", resetAfter: 3)
    }

    private func refreshModelMenu() {
        guard let modelMenuItem else { return }
        switch transcriber.state {
        case .missing:
            modelMenuItem.title = "Download Local Model (~500 MB)"
            modelMenuItem.isEnabled = state == .idle
        case .downloading(let percent):
            modelMenuItem.title = "Downloading Local Model… \(percent)%"
            modelMenuItem.isEnabled = false
        case .loading:
            modelMenuItem.title = "Loading Local Model…"
            modelMenuItem.isEnabled = false
        case .ready:
            modelMenuItem.title = "Local Parakeet Model Ready"
            modelMenuItem.isEnabled = false
        case .failed:
            modelMenuItem.title = "Retry Local Model Download"
            modelMenuItem.isEnabled = state == .idle
        }
        if state == .idle {
            recordingMenuItem?.isEnabled = transcriber.state == .ready
        }
    }

    private func refreshSetupModel() {
        if #available(macOS 14.0, *) {
            setupModel.microphoneGranted = AVAudioApplication.shared.recordPermission == .granted
        } else {
            setupModel.microphoneGranted = AVCaptureDevice.authorizationStatus(for: .audio) == .authorized
        }
        setupModel.screenGranted = CGPreflightScreenCaptureAccess()
        setupModel.accessibilityGranted = AXIsProcessTrusted() && CGPreflightPostEventAccess()
        microphones.refresh()
        setupModel.microphoneName = microphones.selectedLabel
        setupModel.microphoneSelectionEnabled = state == .idle
        setupModel.transcriptionLanguage = transcriber.transcriptionLanguage
        setupModel.languageSelectionEnabled = state == .idle && transcriber.canChangeLanguage
        setupModel.selectedMicrophoneID = microphones.selectedUID ?? "automatic"
        setupModel.microphoneOptions = [
            SetupMicrophoneOption(
                id: "automatic",
                name: "Automatic"
            )
        ] + microphones.devices.map { device in
            SetupMicrophoneOption(
                id: device.uid,
                name: device.name
            )
        }
        setupModel.circleMinimumAngleDegrees = circleMinimumAngleDegrees
        setupModel.hotkeyConfiguration = hotkeyConfiguration
        setupModel.quickNoteTriggerMode = hotkeyConfiguration.quickTriggerMode
        setupModel.longNoteEnabled = hotkeyConfiguration.longNoteEnabled
        setupModel.quickNoteHoldDelayMilliseconds = hotkeyConfiguration.quickHoldDelayMilliseconds
        setupModel.grammarSelectionEnabled = state == .idle
        switch transcriber.state {
        case .missing:
            setupModel.modelStatus = "Download once (~500 MB); transcription stays on this Mac"
            setupModel.modelReady = false
            setupModel.modelBusy = false
        case .downloading(let percent):
            setupModel.modelStatus = "Downloading… \(percent)%"
            setupModel.modelReady = false
            setupModel.modelBusy = true
        case .loading:
            setupModel.modelStatus = "Loading…"
            setupModel.modelReady = false
            setupModel.modelBusy = true
        case .ready:
            setupModel.modelStatus = "Parakeet model ready"
            setupModel.modelReady = true
            setupModel.modelBusy = false
        case .failed(let message):
            setupModel.modelStatus = "Download failed: \(message)"
            setupModel.modelReady = false
            setupModel.modelBusy = false
        }
        switch transcriber.grammarModelState {
        case .missing:
            setupModel.grammarStatus = "Download once (~36 MB) before recording"
            setupModel.grammarReady = false
            setupModel.grammarBusy = false
        case .downloading:
            setupModel.grammarStatus = "Downloading…"
            setupModel.grammarReady = false
            setupModel.grammarBusy = true
        case .ready:
            setupModel.grammarStatus = "Ready • runs locally on this Mac"
            setupModel.grammarReady = true
            setupModel.grammarBusy = false
        case .failed(let message):
            setupModel.grammarStatus = "Download failed: " + message
            setupModel.grammarReady = false
            setupModel.grammarBusy = false
        }
    }

    @objc private func showSetup() {
        showSetupWindow(onboarding: !UserDefaults.standard.bool(forKey: "completedOnboarding"))
    }

    private func showSetupWindow(onboarding: Bool) {
        refreshSetupModel()
        setupWindow.show(model: setupModel, onboarding: onboarding)
    }

    private func setCircleMinimumAngle(_ degrees: Double) {
        guard state == .idle else { return }
        let value = min(max(degrees, 300), 359)
        UserDefaults.standard.set(value, forKey: Self.circleMinimumAngleKey)
        setupModel.circleMinimumAngleDegrees = value
        showStatus("Circle detection: \(Int(value.rounded()))°", resetAfter: 3)
    }

    private func loadHotkey(forKey key: String, fallback: HotkeyBinding) -> HotkeyBinding {
        guard let values = UserDefaults.standard.dictionary(forKey: key) else { return fallback }
        return HotkeyBinding(
            keyCode: (values["keyCode"] as? NSNumber).map { UInt16(truncating: $0) },
            command: values["command"] as? Bool ?? false,
            option: values["option"] as? Bool ?? false,
            control: values["control"] as? Bool ?? false,
            shift: values["shift"] as? Bool ?? false,
            keyName: values["keyName"] as? String ?? ""
        )
    }

    private func saveHotkey(_ binding: HotkeyBinding, forKey key: String) {
        var values: [String: Any] = [
            "command": binding.command,
            "option": binding.option,
            "control": binding.control,
            "shift": binding.shift,
            "keyName": binding.keyName
        ]
        if let keyCode = binding.keyCode {
            values["keyCode"] = Int(keyCode)
        }
        UserDefaults.standard.set(values, forKey: key)
    }

    private func loadQuickTriggerMode() -> RecordingTriggerMode {
        if let raw = UserDefaults.standard.string(forKey: Self.quickNoteTriggerModeKey),
           let mode = RecordingTriggerMode(rawValue: raw),
           mode == .hold || mode == .doubleTap {
            return mode
        }
        if let raw = UserDefaults.standard.string(forKey: Self.legacyLongNoteTriggerModeKey),
           raw == RecordingTriggerMode.doubleTap.rawValue {
            return .doubleTap
        }
        return .hold
    }

    private func loadLongNoteEnabled() -> Bool {
        guard UserDefaults.standard.object(forKey: Self.longNoteEnabledKey) != nil else {
            return true
        }
        return UserDefaults.standard.bool(forKey: Self.longNoteEnabledKey)
    }

    private func loadQuickNoteHoldDelay() -> Int {
        let stored = UserDefaults.standard.integer(forKey: Self.quickNoteHoldDelayKey)
        return QuickNoteHoldDelay.clamp(stored == 0 ? QuickNoteHoldDelay.defaultMilliseconds : stored)
    }

    private func setHotkeyConfiguration(_ configuration: HotkeyConfiguration) {
        guard state == .idle else { return }
        saveHotkey(configuration.quick, forKey: Self.quickHotkeyKey)
        saveHotkey(configuration.long, forKey: Self.longHotkeyKey)
        UserDefaults.standard.set(configuration.quickTriggerMode.rawValue, forKey: Self.quickNoteTriggerModeKey)
        UserDefaults.standard.set(configuration.longNoteEnabled, forKey: Self.longNoteEnabledKey)
        UserDefaults.standard.set(
            QuickNoteHoldDelay.clamp(configuration.quickHoldDelayMilliseconds),
            forKey: Self.quickNoteHoldDelayKey
        )
        inputMonitor?.update(configuration: configuration)
        setupModel.hotkeyConfiguration = configuration
        setupModel.quickNoteTriggerMode = configuration.quickTriggerMode
        setupModel.longNoteEnabled = configuration.longNoteEnabled
        setupModel.quickNoteHoldDelayMilliseconds = configuration.quickHoldDelayMilliseconds
        updateShortcutStatus()
    }

    private func updateShortcutStatus() {
        statusItem.button?.toolTip = "BetterVoice — \(readyStatusHint) • Microphone: \(microphones.selectedLabel)"
        statusMenuItem?.title = "Ready • \(readyStatusHint)"
        recordingMenuItem?.title = "Start long recording (\(longShortcutLabel))"
    }

    /// Creates the template on demand too: a user who cleared the file still gets a
    /// documented starting point instead of an editor opening on nothing.
    @objc private func editVocabulary() {
        let url = VocabularyFile.defaultURL()
        try? VocabularyFile.createTemplateIfMissing(at: url)
        NSWorkspace.shared.open(url)
    }

    @objc private func openSavedSessions() {
        do {
            try FileManager.default.createDirectory(at: SessionStorage.root, withIntermediateDirectories: true)
            NSWorkspace.shared.open(SessionStorage.root)
        } catch {
            showError("Could not open saved sessions", detail: error.localizedDescription)
        }
    }

    @objc private func copyRecentTranscript(_ item: NSMenuItem) {
        guard let transcript = item.representedObject as? String,
              Clipboard.copyTextOnly(transcript)
        else {
            showError("Could not copy recent transcript")
            return
        }
        showStatus("Recent transcript copied", resetAfter: 3)
    }

    @objc private func copyRecentContent(_ item: NSMenuItem) {
        guard let recent = item.representedObject as? SessionStorage.RecentSession,
              Clipboard.copy(transcript: recent.transcript, images: recent.images)
        else {
            showError("Could not copy recent recording")
            return
        }
        showStatus("Recent recording copied", resetAfter: 3)
    }

    @objc private func copyRecentImages(_ item: NSMenuItem) {
        guard let images = item.representedObject as? [URL],
              Clipboard.copy(transcript: "", images: images)
        else {
            showError("Could not copy recent images")
            return
        }
        showStatus("Recent images copied", resetAfter: 3)
    }

    @objc private func openRecentImages(_ item: NSMenuItem) {
        guard let images = item.representedObject as? [URL],
              images.allSatisfy({ NSWorkspace.shared.open($0) })
        else {
            showError("Could not open recent images")
            return
        }
    }

    @objc private func openRecentSession(_ item: NSMenuItem) {
        guard let folder = item.representedObject as? URL,
              NSWorkspace.shared.open(folder)
        else {
            showError("Could not open recent session")
            return
        }
    }

    @objc private func clearSavedSessions() {
        let alert = NSAlert()
        alert.messageText = "Clear all saved BetterVoice sessions?"
        alert.informativeText = "This permanently removes transcripts and screenshots from Desktop/BetterVoice."
        alert.addButton(withTitle: "Clear Sessions")
        alert.addButton(withTitle: "Cancel")
        alert.alertStyle = .warning
        guard alert.runModal() == .alertFirstButtonReturn else { return }
        do {
            try SessionStorage.clear()
            showStatus("Saved sessions cleared", resetAfter: 4)
        } catch {
            showError("Could not clear saved sessions", detail: error.localizedDescription)
        }
    }

    @objc private func downloadModel() {
        guard state == .idle else { return }
        showStatus("Downloading local model…")
        Task {
            await transcriber.downloadModel()
            switch transcriber.state {
            case .ready:
                showStatus("Local model ready • \(readyStatusHint)", resetAfter: 4)
            case .failed(let message):
                showError("Model download failed: \(message)")
            default:
                break
            }
        }
    }

    private func downloadGrammarModel() {
        guard state == .idle else { return }
        showStatus("Downloading grammar model…")
        Task {
            await transcriber.downloadGrammarModel()
            switch transcriber.grammarModelState {
            case .ready:
                showStatus("Grammar cleanup ready", resetAfter: 4)
            case .failed(let message):
                showError("Grammar model download failed", detail: message)
            default:
                break
            }
        }
    }

    @objc private func toggleRecording() {
        switch state {
        case .idle:
            startRecording(mode: .longForm)
        case .recording:
            stopRecording()
        case .finishing:
            break
        }
    }

    private func startPushToTalk() {
        guard state == .idle else { return }
        startRecording(mode: .pushToTalk)
    }

    private func stopPushToTalk() {
        guard state == .recording, recordingMode == .pushToTalk else { return }
        stopRecording()
    }

    private func promoteToLongForm() {
        if state == .idle {
            startRecording(mode: .longForm)
        } else if state == .recording, recordingMode == .pushToTalk {
            recordingMode = .longForm
            showStatus("Recording • long-form mode")
            updateMenuTitle("Stop long recording (\(longShortcutLabel))", enabled: true)
        }
    }

    private func startRecording(mode: RecordingMode) {
        do {
            guard transcriber.state == .ready else { throw BetterVoiceError.localModelUnavailable }
            microphones.refresh()
            guard let selectedMicrophone = microphones.recordingDevice else {
                throw BetterVoiceError.microphoneUnavailable
            }
            output = try SessionOutput()
            captureTasks.removeAll(keepingCapacity: true)
            textInsertionContext = nil
            detector = CircleGestureDetector(minimumAngleDegrees: CGFloat(circleMinimumAngleDegrees))
            detector.reset()
            transcript = ""
            recordingSounds.play(.started)
            try recorder.start(device: selectedMicrophone)
            state = .recording
            recordingMode = mode
            recordingStartedAt = ProcessInfo.processInfo.systemUptime
            inputMonitor?.startMouseTracking()
            let timer = Timer(timeInterval: 20 * 60, repeats: false) { [weak self] _ in
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.state == .recording else { return }
                    self.stopRecording()
                    self.showError(
                        "20-minute recording limit reached",
                        detail: "The recording stopped safely and is being transcribed now."
                    )
                }
            }
            recordingLimitTimer = timer
            RunLoop.main.add(timer, forMode: .common)
            refreshMicrophoneMenu()
            refreshModelMenu()
            refreshLanguageMenu()
            trailOverlay.start()
            recordingHUD.show(microphone: selectedMicrophone.name)
            setStatusIcon(.recording)
            showStatus("Recording • Microphone: \(selectedMicrophone.name)")
            updateMenuTitle(
                mode == .pushToTalk ? quickShortcutStopHint : "Stop long recording (\(longShortcutLabel))",
                enabled: true
            )
        } catch {
            output?.discard()
            output = nil
            recordingStartedAt = nil
            recordingMode = nil
            switch error {
            case BetterVoiceError.localModelUnavailable:
                showStatus("Download the local model in Settings…", resetAfter: 5)
            case BetterVoiceError.microphoneUnavailable:
                showStatus("Select a microphone in Settings…", resetAfter: 5)
            default:
                showError(error.localizedDescription)
            }
        }
    }

    private func stopRecording() {
        guard state == .recording else { return }
        recordingLimitTimer?.invalidate()
        recordingLimitTimer = nil
        textInsertionContext = TextInsertion.captureContext()
        let developerProfile = textInsertionContext.map {
            DeveloperAppProfile.infer(
                bundleIdentifier: $0.bundleIdentifier,
                applicationName: $0.applicationName
            )
        } ?? .general
        state = .finishing
        inputMonitor?.stopMouseTracking()
        refreshMicrophoneMenu()
        trailOverlay.stop()
        recordingHUD.showFinishing()
        if transcriber.grammarCorrectionEnabled, transcriber.transcriptionLanguage.allowsGrammarCorrection {
            recordingHUD.showFinishingStatus("Polishing transcript locally…")
        }
        setStatusIcon(.finishing)
        showStatus("Finishing…")
        updateMenuTitle("Finishing… (\(longShortcutLabel))", enabled: false)
        do {
            let audioURL = try recorder.stop()
            recordingSounds.play(.finished)
            Task {
                defer { try? FileManager.default.removeItem(at: audioURL) }
                do {
                    transcript = try await transcriber.transcribe(audioURL, profile: developerProfile)
                    await waitForCaptures()
                    finishSession()
                } catch {
                    await waitForCaptures()
                    finishSession(transcriptionError: error)
                }
            }
        } catch {
            Task {
                await waitForCaptures()
                finishSession(transcriptionError: error)
            }
        }
    }

    private func finishSession(transcriptionError: Error? = nil) {
        let session = output
        let shouldCopyToClipboard = recordingMode == .longForm
        let hadTranscript = !transcript.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let hasContext = !(session?.images.isEmpty ?? true)
        let elapsed = recordingStartedAt.map { ProcessInfo.processInfo.systemUptime - $0 } ?? 0
        let disposition = sessionCompletionDisposition(
            hasTranscript: hadTranscript,
            hasContext: hasContext,
            duration: elapsed
        )
        if disposition != .deliver, let session {
            switch disposition {
            case .discardAccidental:
                session.discard()
            case .saveEmpty:
                do {
                    _ = try session.finish(
                        transcript: transcript,
                        insertionContext: textInsertionContext,
                        shouldCopyToClipboard: shouldCopyToClipboard
                    )
                } catch {
                    session.discard()
                }
            case .deliver:
                break
            }
            finishRecordingUI()
            showStatus(
                disposition == .discardAccidental
                    ? "Recording discarded"
                    : "No speech or screen context captured",
                resetAfter: 4
            )
            pruneSavedSessions()
            return
        }
        do {
            guard let session else { throw BetterVoiceError.sessionUnavailable }
            let pasteImagesAfterText = transcriptionError == nil
                && hadTranscript
                && hasContext
                && textInsertionContext != nil
            if pasteImagesAfterText {
                recordingHUD.showFinishingStatus("Pasting text…")
                showStatus("Pasting text…")
            }
            let result = try session.finish(
                transcript: transcript,
                insertionContext: textInsertionContext,
                shouldCopyToClipboard: shouldCopyToClipboard
            )
            if pasteImagesAfterText,
               result.transcriptInserted,
               !session.images.isEmpty,
               let insertionContext = textInsertionContext {
                pasteImagesAfterTranscript(
                    session.images,
                    transcript: transcript,
                    into: insertionContext,
                    restoreClipboard: session.clipboardToRestore
                )
                return
            }
            finishRecordingUI()

            if let transcriptionError {
                let delivery = !shouldCopyToClipboard
                    ? "Session saved."
                    : result.clipboardCopied
                    ? (hasContext ? "Screen context copied." : "Session saved.")
                    : "Session saved; clipboard text fallback used."
                showError("Transcription failed: \(transcriptionError.localizedDescription) \(delivery)")
            } else if result.transcriptInserted {
                showStatus(
                    shouldCopyToClipboard && hasContext
                        ? "Inserted transcript • context copied"
                        : "Inserted transcript",
                    resetAfter: 4
                )
            } else if result.clipboardCopied {
                if hadTranscript && hasContext {
                    showStatus("Copied transcript + context", resetAfter: 4)
                } else if hadTranscript {
                    showStatus("Copied transcript", resetAfter: 4)
                } else if hasContext {
                    showStatus("No speech detected • context copied", resetAfter: 4)
                } else {
                    showStatus("No speech detected • session saved", resetAfter: 4)
                }
            } else if !shouldCopyToClipboard && !hadTranscript {
                showStatus(
                    hasContext ? "Screen context saved" : "Session saved",
                    resetAfter: 4
                )
            } else if !shouldCopyToClipboard {
                showError("Saved session; transcript was not inserted.")
            } else {
                showError("Saved session; plain-text clipboard fallback used.")
            }
            pruneSavedSessions()
        } catch {
            finishRecordingUI()
            if let transcriptionError {
                showError("Transcription failed: \(transcriptionError.localizedDescription); session save failed: \(error.localizedDescription)")
            } else {
                showError(error.localizedDescription)
            }
        }
    }

    private func pasteImagesAfterTranscript(
        _ images: [URL],
        transcript: String,
        into insertionContext: TextInsertion.Context,
        restoreClipboard: [Clipboard.SavedItem]?
    ) {
        let expectedClipboardChangeCount = NSPasteboard.general.changeCount
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.3) { [weak self] in
            guard let self else { return }
            guard NSPasteboard.general.changeCount == expectedClipboardChangeCount else {
                self.finishRecordingUI()
                self.showStatus("Inserted transcript • images skipped", resetAfter: 4)
                self.pruneSavedSessions()
                return
            }

            self.recordingHUD.showFinishingStatus("Pasting images…")
            self.showStatus("Pasting images…")
            let copied = Clipboard.copy(transcript: "", images: images)
            let imageClipboardChangeCount = NSPasteboard.general.changeCount
            let pasted = copied && TextInsertion.paste(into: insertionContext)
            if let restoreClipboard {
                Clipboard.restore(restoreClipboard, ifChangeCount: NSPasteboard.general.changeCount)
            } else if pasted, NSPasteboard.general.changeCount == imageClipboardChangeCount {
                _ = Clipboard.copy(transcript: transcript, images: images)
            }
            self.finishRecordingUI()
            let message: String
            if pasted {
                message = "Inserted transcript + context"
            } else if copied && restoreClipboard == nil {
                message = "Inserted transcript • images copied"
            } else {
                message = "Inserted transcript • image paste failed"
            }
            self.showStatus(
                message,
                resetAfter: 4
            )
            self.pruneSavedSessions()
        }
    }

    private func finishRecordingUI() {
        inputMonitor?.recordingDidEnd()
        output = nil
        textInsertionContext = nil
        recordingStartedAt = nil
        state = .idle
        recordingMode = nil
        recordingHUD.hide()
        refreshMicrophoneMenu()
        refreshModelMenu()
        refreshLanguageMenu()
        setStatusIcon(.idle)
        updateMenuTitle("Start long recording (\(longShortcutLabel))", enabled: true)
    }

    private func handleMouse(quartzPoint: CGPoint) {
        guard state == .recording else { return }
        let now = ProcessInfo.processInfo.systemUptime
        let overlayPoint = appKitPoint(from: quartzPoint)
        trailOverlay.add(point: overlayPoint, at: now)
        guard let gesture = detector.add(point: quartzPoint, at: now) else { return }
        guard let output else { return }
        let previousCapture = captureTasks.last
        let task = Task { [weak self] in
            await previousCapture?.value
            do {
                _ = try await output.addImage(for: gesture)
                guard let self else { return }
                let appKitCenter = self.appKitPoint(from: gesture.center)
                self.trailOverlay.confirm(center: appKitCenter, radius: gesture.radius, at: now)
                self.recordingHUD.confirmCapture(count: output.images.count)
                self.showStatus("Recording • \(output.images.count) screen context")
            } catch {
                guard let self else { return }
                let message: String
                if case BetterVoiceError.screenPermissionRequired = error {
                    message = "Screen permission required"
                } else if case BetterVoiceError.sessionStorageFull = error {
                    message = "Saved-session limit reached"
                } else {
                    message = "Screenshot failed"
                }
                self.recordingHUD.showCaptureError(message)
                if case BetterVoiceError.screenPermissionRequired = error {
                    self.showError(
                        "Screen Recording access is off",
                        detail: "Enable BetterVoice in Privacy & Security → Screen Recording, then reopen the app.",
                        actionTitle: "Open Settings",
                        action: { [weak self] in self?.openPrivacySettings("Privacy_ScreenCapture") }
                    )
                } else if case BetterVoiceError.sessionStorageFull = error {
                    self.showError(
                        error.localizedDescription,
                        detail: "Finish this recording, or clear older sessions from the BetterVoice menu.",
                        actionTitle: "Manage Sessions",
                        action: { [weak self] in self?.openSavedSessions() }
                    )
                } else {
                    self.showError("Screenshot capture failed", detail: error.localizedDescription)
                }
            }
        }
        captureTasks.append(task)
    }

    private func waitForCaptures() async {
        for task in captureTasks {
            await task.value
        }
        captureTasks.removeAll(keepingCapacity: true)
    }

    private func appKitPoint(from quartzPoint: CGPoint) -> CGPoint {
        guard let primaryFrame = NSScreen.screens.first?.frame else { return quartzPoint }
        return CGPoint(
            x: quartzPoint.x + primaryFrame.minX,
            y: primaryFrame.maxY - quartzPoint.y
        )
    }

    private func requestMicrophoneAuthorization() {
        if #available(macOS 14.0, *) {
            if AVAudioApplication.shared.recordPermission == .denied {
                openPrivacySettings("Privacy_Microphone")
                return
            }
            let controller = self
            AVAudioApplication.requestRecordPermission { granted in
                DispatchQueue.main.async { [weak controller] in
                    controller?.refreshSetupModel()
                    if !granted {
                        controller?.showError(
                            "Microphone access is off",
                            detail: "Enable BetterVoice in Privacy & Security → Microphone."
                        )
                    }
                }
            }
        } else {
            if AVCaptureDevice.authorizationStatus(for: .audio) == .denied {
                openPrivacySettings("Privacy_Microphone")
                return
            }
            let controller = self
            AVCaptureDevice.requestAccess(for: .audio) { granted in
                DispatchQueue.main.async { [weak controller] in
                    controller?.refreshSetupModel()
                    if !granted {
                        controller?.showError(
                            "Microphone access is off",
                            detail: "Enable BetterVoice in Privacy & Security → Microphone."
                        )
                    }
                }
            }
        }
    }

    private func requestScreenCaptureAuthorization() {
        guard !CGPreflightScreenCaptureAccess() else {
            refreshSetupModel()
            return
        }
        if !CGRequestScreenCaptureAccess() {
            openPrivacySettings("Privacy_ScreenCapture")
        }
        refreshSetupModel()
    }

    private func requestTextInsertionAuthorization() {
        guard !AXIsProcessTrusted() || !CGPreflightPostEventAccess() else {
            refreshSetupModel()
            return
        }
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
        _ = CGRequestPostEventAccess()
        DispatchQueue.main.asyncAfter(deadline: .now() + 1) { [weak self] in
            self?.refreshSetupModel()
        }
    }

    private func openPrivacySettings(_ pane: String) {
        let modern = "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?\(pane)"
        let legacy = "x-apple.systempreferences:com.apple.preference.security?\(pane)"
        if let url = URL(string: modern), NSWorkspace.shared.open(url) {
            return
        }
        if let url = URL(string: legacy) {
            NSWorkspace.shared.open(url)
        }
    }

    private func pruneSavedSessions() {
        do {
            try SessionStorage.prune()
        } catch {
            showError("Saved-session cleanup failed", detail: error.localizedDescription)
        }
    }

    private func updateMenuTitle(_ title: String, enabled: Bool) {
        recordingMenuItem?.title = title
        recordingMenuItem?.isEnabled = enabled && (state != .idle || transcriber.state == .ready)
    }

    private func showStatus(_ message: String, resetAfter: TimeInterval? = nil) {
        statusFeedbackTimer?.invalidate()
        statusFeedbackTimer = nil
        statusMenuItem?.title = message
        statusItem.button?.toolTip = "BetterVoice — \(message)"

        guard let resetAfter else { return }
        let timer = Timer(timeInterval: resetAfter, repeats: false) { [weak self] _ in
            DispatchQueue.main.async { [weak self] in
                guard let self, self.state == .idle else { return }
                self.showStatus("Ready • \(self.readyStatusHint)")
            }
        }
        statusFeedbackTimer = timer
        RunLoop.main.add(timer, forMode: .common)
    }

    private func setStatusIcon(_ state: StatusIconState) {
        statusAnimationTimer?.invalidate()
        statusAnimationTimer = nil

        switch state {
        case .idle, .finishing:
            setIcon(named: "waveform")
        case .recording:
            statusPulse = false
            setIcon(named: "waveform.circle.fill")
            guard !reduceMotion else { return }
            let timer = Timer(timeInterval: 0.45, repeats: true) { [weak self] _ in
                DispatchQueue.main.async { [weak self] in
                    guard let self, self.state == .recording else { return }
                    self.statusPulse.toggle()
                    self.setIcon(named: self.statusPulse ? "waveform.circle" : "waveform.circle.fill")
                }
            }
            statusAnimationTimer = timer
            RunLoop.main.add(timer, forMode: .common)
        }
    }

    private func setIcon(named name: String) {
        let image = NSImage(systemSymbolName: name, accessibilityDescription: "BetterVoice")
        image?.isTemplate = true
        statusItem.button?.image = image
    }

    private func showError(
        _ message: String,
        detail: String? = nil,
        actionTitle: String = "Open Setup",
        action: (() -> Void)? = nil
    ) {
        showStatus("Needs attention: \(message)", resetAfter: 8)
        recoveryNotice.show(
            title: message,
            detail: detail ?? "Open setup to check permissions, microphone, and the local model.",
            actionTitle: actionTitle,
            action: action ?? { [weak self] in self?.showSetup() }
        )
    }

    @objc private func quit() {
        shutdown()
        NSApplication.shared.terminate(nil)
    }

    private func shutdown() {
        inputMonitor?.stop()
        trailOverlay.stop()
        recordingHUD.hide()
        statusAnimationTimer?.invalidate()
        statusFeedbackTimer?.invalidate()
        recordingLimitTimer?.invalidate()
        if state == .recording {
            if let audioURL = try? recorder.stop() {
                try? FileManager.default.removeItem(at: audioURL)
            }
            output?.discard()
            output = nil
            recordingStartedAt = nil
            state = .idle
        }
    }
}

let application = NSApplication.shared
private let delegate = MainActor.assumeIsolated { AppController() }
application.delegate = delegate
application.setActivationPolicy(.regular)
application.run()
