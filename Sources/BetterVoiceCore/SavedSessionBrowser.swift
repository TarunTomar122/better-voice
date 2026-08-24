import Foundation

public struct SavedSessionSummary: Equatable {
    public let name: String
    public let modifiedAt: Date
    public let transcript: String
    public let imageNames: [String]

    public init(name: String, modifiedAt: Date, transcript: String, imageNames: [String]) {
        self.name = name
        self.modifiedAt = modifiedAt
        self.transcript = transcript
        self.imageNames = imageNames
    }
}

public enum SavedSessionBrowser {
    public static func newestFirst(_ sessions: [SavedSessionSummary]) -> [SavedSessionSummary] {
        sessions.sorted {
            if $0.modifiedAt != $1.modifiedAt {
                return $0.modifiedAt > $1.modifiedAt
            }
            return $0.name > $1.name
        }
    }

    public static func transcriptPreview(from markdown: String) -> String {
        let lines = markdown.components(separatedBy: .newlines)
        let contextStart = lines.firstIndex { line in
            line.trimmingCharacters(in: .whitespaces) == "## Screen context"
        }.flatMap { index in
            lines[(index + 1)...].contains(where: { $0.trimmingCharacters(in: .whitespaces).hasPrefix("![Context ") })
                ? index
                : nil
        }

        var transcriptLines: [String] = []
        for (index, line) in lines.enumerated() {
            let trimmed = line.trimmingCharacters(in: .whitespaces)
            if trimmed == "# BetterVoice session" { continue }
            if index == contextStart { break }
            if trimmed == "_No transcript captured._" { return "" }
            transcriptLines.append(line)
        }
        return transcriptLines.joined(separator: "\n")
            .trimmingCharacters(in: .whitespacesAndNewlines)
    }

    public static func imageNames(from fileNames: [String]) -> [String] {
        fileNames
            .filter(isSafeImageName)
            .sorted { imageNumber(in: $0) < imageNumber(in: $1) }
    }

    public static func isSafeImageName(_ name: String) -> Bool {
        name.range(of: #"^context-[1-9][0-9]*\.png$"#, options: .regularExpression) != nil
    }

    private static func imageNumber(in name: String) -> Int {
        Int(name.dropFirst("context-".count).dropLast(".png".count)) ?? .max
    }
}
