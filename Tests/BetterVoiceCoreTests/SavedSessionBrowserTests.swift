import Foundation
import XCTest
@testable import BetterVoiceCore

final class SavedSessionBrowserTests: XCTestCase {
    func testNewestFirstSortsByModifiedDateAndUsesNameAsTieBreaker() {
        let date = Date(timeIntervalSince1970: 100)
        let sessions = [
            SavedSessionSummary(name: "older", modifiedAt: date.addingTimeInterval(-1), transcript: "", imageNames: []),
            SavedSessionSummary(name: "newer-a", modifiedAt: date, transcript: "", imageNames: []),
            SavedSessionSummary(name: "newer-b", modifiedAt: date, transcript: "", imageNames: [])
        ]

        XCTAssertEqual(
            SavedSessionBrowser.newestFirst(sessions).map(\.name),
            ["newer-b", "newer-a", "older"]
        )
    }

    func testTranscriptPreviewOmitsStorageHeadingsAndImageLinks() {
        let markdown = """
        # BetterVoice session

        First line.
        Second line.

        ## Screen context

        ![Context 1](context-1.png)
        """

        XCTAssertEqual(
            SavedSessionBrowser.transcriptPreview(from: markdown),
            "First line.\nSecond line."
        )
    }

    func testImageNamesOnlyIncludeNumberedPngAssetsInCaptureOrder() {
        XCTAssertEqual(
            SavedSessionBrowser.imageNames(from: [
                "context-10.png", "context-2.png", "context.md", "context-0.png",
                "context-1.png", "other.png", "context-2.jpg", "context-01.png"
            ]),
            ["context-1.png", "context-2.png", "context-10.png"]
        )
    }

    func testMissingTranscriptMarkerProducesAnEmptyPreview() {
        XCTAssertEqual(
            SavedSessionBrowser.transcriptPreview(from: "# BetterVoice session\n\n_No transcript captured._\n"),
            ""
        )
    }

    func testTranscriptHeadingIsPreservedWhenThereAreNoScreenContextLinks() {
        let markdown = """
        # BetterVoice session

        The transcript mentions:
        ## Screen context
        but no image was captured.
        """

        XCTAssertEqual(
            SavedSessionBrowser.transcriptPreview(from: markdown),
            "The transcript mentions:\n## Screen context\nbut no image was captured."
        )
    }
}
