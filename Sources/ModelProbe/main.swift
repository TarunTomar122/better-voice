// ModelProbe: downloads (if needed) and loads a Parakeet model version the
// same way BetterVoice does, then runs a transcription pass over a generated
// buffer. Used to verify a model choice works end to end without launching
// the menu-bar app.
//
// Usage: ModelProbe v2|v3|tdtCtc110m
import FluidAudio
import Foundation

enum ProbeError: Error { case badArguments }

func run() async throws {
    let raw = CommandLine.arguments.count > 1 ? CommandLine.arguments[1] : "tdtCtc110m"
    let version: AsrModelVersion = switch raw {
    case "v2": .v2
    case "v3": .v3
    case "tdtCtc110m": .tdtCtc110m
    default: throw ProbeError.badArguments
    }

    print("Downloading/loading model: \(version)")
    let models = try await AsrModels.downloadAndLoad(version: version) { update in
        let done = Int(update.fractionCompleted * 100)
        if update.fractionCompleted >= 1 { print("  download finished") }
    }
    print("Loaded. Encoder present: \(models.encoder != nil)")

    let manager = AsrManager(config: .default, models: models)
    // 2 s of quiet 16 kHz audio: enough to prove the pipeline runs without
    // crashing; silence decodes to an empty or near-empty transcript.
    let sampleCount = 32_000
    var samples = [Float](repeating: 0, count: sampleCount)
    for index in 0..<sampleCount {
        samples[index] = 0.001 * sin(Float(index) * 0.02)
    }
    var decoderState = TdtDecoderState.make(decoderLayers: await manager.decoderLayerCount)
    let result = try await manager.transcribe(samples, decoderState: &decoderState)
    print("Transcription pass OK. Text: \"\(result.text)\"")
}

let probeDone = DispatchSemaphore(value: 0)
Task {
    do {
        try await run()
        print("Probe done")
    } catch {
        print("Probe failed: \(error)")
    }
    probeDone.signal()
}
_ = probeDone.wait(timeout: .now() + 60 * 60)
exit(0)
