import Foundation

public struct LauncherConfiguration: Codable, Equatable {
    public var displayName: String
    public var video: String
    public var appBundleIdentifier: String
    public var appPath: String
    public var holdAt: Double
    public var transitionStart: Double
    public var transitionEnd: Double
    public var maxWaitSeconds: Double
    public var volume: Double
    public var fullscreen: Bool
    public var playerWidth: Int
    public var playerHeight: Int

    enum CodingKeys: String, CodingKey {
        case displayName = "DisplayName"
        case video = "Video"
        case appBundleIdentifier = "AppBundleIdentifier"
        case appPath = "AppPath"
        case holdAt = "HoldAt"
        case transitionStart = "TransitionStart"
        case transitionEnd = "TransitionEnd"
        case maxWaitSeconds = "MaxWaitSeconds"
        case volume = "Volume"
        case fullscreen = "Fullscreen"
        case playerWidth = "PlayerWidth"
        case playerHeight = "PlayerHeight"
    }

    public init(
        displayName: String = "Dragon Codex Boot",
        video: String = "media/startup.mp4",
        appBundleIdentifier: String = "com.openai.codex",
        appPath: String = "",
        holdAt: Double = 11.3,
        transitionStart: Double = 12.7,
        transitionEnd: Double = 13.65,
        maxWaitSeconds: Double = 60,
        volume: Double = 1,
        fullscreen: Bool = true,
        playerWidth: Int = 1920,
        playerHeight: Int = 1080
    ) {
        self.displayName = displayName
        self.video = video
        self.appBundleIdentifier = appBundleIdentifier
        self.appPath = appPath
        self.holdAt = holdAt
        self.transitionStart = transitionStart
        self.transitionEnd = transitionEnd
        self.maxWaitSeconds = maxWaitSeconds
        self.volume = volume
        self.fullscreen = fullscreen
        self.playerWidth = playerWidth
        self.playerHeight = playerHeight
    }

    public static func decode(from data: Data) throws -> LauncherConfiguration {
        let configuration = try JSONDecoder().decode(LauncherConfiguration.self, from: data)
        try configuration.validate()
        return configuration
    }

    public func validate() throws {
        guard !displayName.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw LauncherConfigurationError("DisplayName 不能为空。")
        }
        guard !video.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty else {
            throw LauncherConfigurationError("Video 必须指向本地视频文件。")
        }
        let hasBundleIdentifier = !appBundleIdentifier.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        let hasAppPath = !appPath.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty
        guard hasBundleIdentifier || hasAppPath else {
            throw LauncherConfigurationError("请填写 AppBundleIdentifier 或 AppPath。")
        }
        if hasBundleIdentifier {
            let pattern = #"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)+$"#
            guard appBundleIdentifier.range(of: pattern, options: .regularExpression) != nil else {
                throw LauncherConfigurationError("AppBundleIdentifier 格式无效。")
            }
        }
        if hasAppPath {
            let expanded = (appPath as NSString).expandingTildeInPath
            guard expanded.hasPrefix("/") else {
                throw LauncherConfigurationError("AppPath 必须是绝对路径或留空。")
            }
        }
        let times = [holdAt, transitionStart, transitionEnd, maxWaitSeconds, volume]
        guard times.allSatisfy({ $0.isFinite }) else {
            throw LauncherConfigurationError("时间和音量必须是有限数值。")
        }
        guard holdAt > 0, transitionStart > holdAt, transitionEnd > transitionStart, maxWaitSeconds >= 10 else {
            throw LauncherConfigurationError("请确保 0 < HoldAt < TransitionStart < TransitionEnd，且 MaxWaitSeconds 至少为 10。")
        }
        guard (0...1).contains(volume) else {
            throw LauncherConfigurationError("Volume 范围必须是 0 到 1。")
        }
        guard (450...7680).contains(playerWidth), (280...4320).contains(playerHeight) else {
            throw LauncherConfigurationError("播放器尺寸超出支持范围。")
        }
    }

    public func videoURL(relativeTo root: URL) -> URL {
        let expanded = (video as NSString).expandingTildeInPath
        if expanded.hasPrefix("/") {
            return URL(fileURLWithPath: expanded).standardizedFileURL
        }
        return root.appendingPathComponent(expanded).standardizedFileURL
    }

    /// Window opacity for a whole-player crossfade into the Codex window.
    public func playerOpacity(at videoTime: Double) -> Double {
        guard videoTime >= transitionStart else { return 1 }
        let progress = min(1, max(0, (videoTime - transitionStart) / (transitionEnd - transitionStart)))
        let eased = progress * progress * (3 - 2 * progress)
        return 1 - eased
    }
}

public struct LauncherConfigurationError: LocalizedError, Equatable {
    public let message: String

    public init(_ message: String) {
        self.message = message
    }

    public var errorDescription: String? { message }
}
