import Foundation

/// Which Parakeet model BetterVoice transcribes with.
///
/// `automatic` keeps the historical behavior: the English-only Parakeet v2 for
/// English diction and the multilingual Parakeet v3 for every other language,
/// so existing installs neither re-download nor change transcription quality.
/// The other options let the user trade size, accuracy, and language reach:
///
/// - `englishV2` pins English to the 0.6B Parakeet v2 model it has always used.
/// - `englishFast` pins English to the 110M hybrid TDT-CTC model, several
///   times smaller and faster at the cost of some accuracy on long prompts.
/// - `multilingualV3` pins every language to Parakeet v3, the newer larger
///   multilingual 0.6B model; many see better accuracy on English too.
public enum DictationModelChoice: String, CaseIterable, Hashable, Sendable, Identifiable {
    case automatic
    case englishV2
    case englishFast
    case multilingualV3

    public var id: String { rawValue }

    static let storageKey = "dictationModelChoice"

    public var name: String {
        switch self {
        case .automatic: return "Automatic"
        case .englishV2: return "English — Parakeet v2"
        case .englishFast: return "English — Parakeet 110M (fast)"
        case .multilingualV3: return "Multilingual — Parakeet v3"
        }
    }

    public var detail: String {
        switch self {
        case .automatic: return "Picks the right model for the language. English uses Parakeet v2; other languages use Parakeet v3."
        case .englishV2: return "English only. The model BetterVoice has always used, ~500 MB, already downloaded on most installs."
        case .englishFast: return "English only. A compact 110M model, a fraction of the download, with quicker transcription and lower memory use."
        case .multilingualV3: return "Every listed language, including English. The newer 0.6B multilingual model — a separate download unless you already dictate in another language."
        }
    }

    /// Whether this choice can transcribe the full language list. The English-
    /// only models decode English vocabulary only; running them with another
    /// language hint would sound like it works and then mis-transcribe.
    public var supportsAllLanguages: Bool {
        switch self {
        case .automatic, .multilingualV3: return true
        case .englishV2, .englishFast: return false
        }
    }

    public init(storedRaw: String?) {
        self = Self(rawValue: storedRaw ?? "") ?? .automatic
    }
}
