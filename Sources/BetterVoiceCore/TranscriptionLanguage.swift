import Foundation

/// The dictation language the user picked.
///
/// The core target stays free of FluidAudio, so this type carries intent only:
/// which model the app has to load, whether the English-only grammar beta may
/// run, and which script hint to forward. The app target does the mapping.
public struct TranscriptionLanguage: Sendable, Hashable {
    /// An ISO 639-1 code, or `automaticCode` to let the model decide.
    public let code: String
    public let name: String

    static let automaticCode = "auto"
    public static let englishCode = "en"

    public init(code: String, name: String) {
        self.code = code
        self.name = name
    }

    public init(storedCode: String?) {
        self = TranscriptionLanguage.all.first { $0.code == storedCode } ?? .english
    }

    /// English keeps the English-only model it has always used, so existing
    /// installs neither re-download nor change transcription behavior.
    public var usesEnglishOnlyModel: Bool { code == TranscriptionLanguage.englishCode }

    /// `t5-tiny-gec-hone` is trained on English. Run over another language it does
    /// not decline to help, it rewrites correct sentences into broken ones.
    public var allowsGrammarCorrection: Bool { usesEnglishOnlyModel }

    /// `nil` leaves the multilingual decoder unconstrained.
    public var scriptHintCode: String? {
        code == TranscriptionLanguage.automaticCode ? nil : code
    }

    public static let automatic = TranscriptionLanguage(code: automaticCode, name: "Automatic")
    public static let english = TranscriptionLanguage(code: englishCode, name: "English")

    /// Mirrors the languages the multilingual model and its script filter know.
    /// Offering a code beyond that list would silently degrade to no hint at all.
    public static let all: [TranscriptionLanguage] = [
        .automatic,
        .english,
        TranscriptionLanguage(code: "pt", name: "Português"),
        TranscriptionLanguage(code: "es", name: "Español"),
        TranscriptionLanguage(code: "fr", name: "Français"),
        TranscriptionLanguage(code: "de", name: "Deutsch"),
        TranscriptionLanguage(code: "it", name: "Italiano"),
        TranscriptionLanguage(code: "nl", name: "Nederlands"),
        TranscriptionLanguage(code: "da", name: "Dansk"),
        TranscriptionLanguage(code: "sv", name: "Svenska"),
        TranscriptionLanguage(code: "fi", name: "Suomi"),
        TranscriptionLanguage(code: "et", name: "Eesti"),
        TranscriptionLanguage(code: "lv", name: "Latviešu"),
        TranscriptionLanguage(code: "lt", name: "Lietuvių"),
        TranscriptionLanguage(code: "pl", name: "Polski"),
        TranscriptionLanguage(code: "cs", name: "Čeština"),
        TranscriptionLanguage(code: "sk", name: "Slovenčina"),
        TranscriptionLanguage(code: "sl", name: "Slovenščina"),
        TranscriptionLanguage(code: "hr", name: "Hrvatski"),
        TranscriptionLanguage(code: "bs", name: "Bosanski"),
        TranscriptionLanguage(code: "hu", name: "Magyar"),
        TranscriptionLanguage(code: "ro", name: "Română"),
        TranscriptionLanguage(code: "mt", name: "Malti"),
        TranscriptionLanguage(code: "el", name: "Ελληνικά"),
        TranscriptionLanguage(code: "bg", name: "Български"),
        TranscriptionLanguage(code: "ru", name: "Русский"),
        TranscriptionLanguage(code: "uk", name: "Українська"),
        TranscriptionLanguage(code: "be", name: "Беларуская"),
        TranscriptionLanguage(code: "sr", name: "Српски")
    ]
}
