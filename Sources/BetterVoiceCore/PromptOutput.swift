import Foundation

public enum BetterVoiceOutputMode: String, CaseIterable, Hashable, Sendable {
    case transcript
    case prompt

    public var title: String {
        switch self {
        case .transcript:
            return "Transcript"
        case .prompt:
            return "Prompt"
        }
    }

    public var detail: String {
        switch self {
        case .transcript:
            return "Insert the words as spoken, with the enabled local cleanup applied."
        case .prompt:
            return "Format the request for an AI assistant and include screen-context guidance when available."
        }
    }
}

public enum PromptFormatter {
    public static func makePrompt(from transcript: String, hasScreenContext: Bool) -> String {
        let text = transcript.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !text.isEmpty else { return "" }

        var sections = [
            "Task:",
            text,
            "",
            "Response:",
            "Give a direct, practical answer. State important assumptions and include complete examples when they are useful."
        ]
        if hasScreenContext {
            sections.insert(
                "Use the attached screen capture(s) as context when they are relevant.",
                at: sections.count - 1
            )
        }
        return sections.joined(separator: "\n").trimmingCharacters(in: .whitespacesAndNewlines)
    }
}
