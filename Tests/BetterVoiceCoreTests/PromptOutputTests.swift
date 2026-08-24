import XCTest
@testable import BetterVoiceCore

final class PromptOutputTests: XCTestCase {
    func testTranscriptModeDescribesTheLiteralOutput() {
        XCTAssertEqual(
            BetterVoiceOutputMode.transcript.detail,
            "Insert the words as spoken, with the enabled local cleanup applied."
        )
    }

    func testPromptModeBuildsAnActionableLocalPrompt() {
        XCTAssertEqual(
            PromptFormatter.makePrompt(from: "Fix the failing login test", hasScreenContext: false),
            "Task:\nFix the failing login test\n\nResponse:\nGive a direct, practical answer. State important assumptions and include complete examples when they are useful."
        )
    }

    func testPromptModeAddsScreenContextGuidance() {
        let prompt = PromptFormatter.makePrompt(from: "Explain this error", hasScreenContext: true)

        XCTAssertTrue(prompt.contains("Task:\nExplain this error"))
        XCTAssertTrue(prompt.contains("Use the attached screen capture(s) as context when they are relevant."))
        XCTAssertTrue(prompt.hasSuffix("include complete examples when they are useful."))
    }

    func testPromptFormatterKeepsEmptyInputEmpty() {
        XCTAssertEqual(PromptFormatter.makePrompt(from: " \n ", hasScreenContext: true), "")
    }
}
