import Foundation
import XCTest
@testable import DragonCodexBootCore

final class LauncherConfigurationTests: XCTestCase {
    func testExampleConfigurationDecodesAndValidates() throws {
        let json = #"{"DisplayName":"Dragon Codex Boot","Video":"media/startup.mp4","AppBundleIdentifier":"com.openai.codex","AppPath":"","HoldAt":11.65,"TransitionStart":12.45,"TransitionEnd":13.95,"MaxWaitSeconds":60,"Volume":1,"Fullscreen":true,"PlayerWidth":1920,"PlayerHeight":1080}"#
        let configuration = try LauncherConfiguration.decode(from: Data(json.utf8))
        XCTAssertEqual(configuration.appBundleIdentifier, "com.openai.codex")
        XCTAssertTrue(configuration.fullscreen)
    }

    func testPlayerFadesSmoothlyAcrossConfiguredTransition() {
        let configuration = LauncherConfiguration()
        XCTAssertEqual(configuration.playerOpacity(at: 12), 1, accuracy: 0.0001)
        XCTAssertEqual(configuration.playerOpacity(at: 12.45), 1, accuracy: 0.0001)
        XCTAssertEqual(configuration.playerOpacity(at: 13.2), 0.5, accuracy: 0.0001)
        XCTAssertEqual(configuration.playerOpacity(at: 13.95), 0, accuracy: 0.0001)
        XCTAssertEqual(configuration.playerOpacity(at: 20), 0, accuracy: 0.0001)
    }

    func testRelativeVideoPathIsResolvedAgainstApplicationSupportDirectory() {
        let root = URL(fileURLWithPath: "/tmp/DragonCodexBoot", isDirectory: true)
        XCTAssertEqual(LauncherConfiguration().videoURL(relativeTo: root).path, "/tmp/DragonCodexBoot/media/startup.mp4")
    }

    func testInvalidTimingAndMissingAppAreRejected() {
        var invalidTiming = LauncherConfiguration()
        invalidTiming.transitionEnd = invalidTiming.transitionStart
        XCTAssertThrowsError(try invalidTiming.validate())

        var missingApp = LauncherConfiguration()
        missingApp.appBundleIdentifier = ""
        XCTAssertThrowsError(try missingApp.validate())
    }
}
