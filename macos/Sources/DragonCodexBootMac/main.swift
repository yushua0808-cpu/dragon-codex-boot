import AppKit
import AVKit
import Darwin
import Foundation
#if canImport(DragonCodexBootCore)
import DragonCodexBootCore
#endif

private enum LauncherError: LocalizedError {
    case missingConfig
    case missingVideo(URL)
    case missingApp(String)

    var errorDescription: String? {
        switch self {
        case .missingConfig:
            return "找不到 macOS 配置文件。请重新运行构建脚本。"
        case .missingVideo(let url):
            return "找不到启动视频：\n\(url.path)\n\n请把 MP4 放到该位置，或修改 launcher.json 中的 Video。"
        case .missingApp(let detail):
            return "找不到 Codex 桌面 App。\n\(detail)\n\n请在 launcher.json 中填写正确的 AppBundleIdentifier 或 AppPath。"
        }
    }
}

private struct RuntimeConfiguration {
    let configuration: LauncherConfiguration
    let root: URL

    static func load() throws -> RuntimeConfiguration {
        let fileManager = FileManager.default
        let supportRoot = try fileManager.url(
            for: .applicationSupportDirectory,
            in: .userDomainMask,
            appropriateFor: nil,
            create: true
        ).appendingPathComponent("DragonCodexBoot", isDirectory: true)
        try fileManager.createDirectory(at: supportRoot, withIntermediateDirectories: true)

        let configURL = supportRoot.appendingPathComponent("launcher.json")
        if !fileManager.fileExists(atPath: configURL.path) {
            guard let template = Bundle.main.resourceURL?.appendingPathComponent("launcher.macos.example.json"),
                  fileManager.fileExists(atPath: template.path) else {
                throw LauncherError.missingConfig
            }
            try fileManager.copyItem(at: template, to: configURL)
        }

        let data = try Data(contentsOf: configURL)
        let configuration = try LauncherConfiguration.decode(from: data)
        let runtime = RuntimeConfiguration(configuration: configuration, root: supportRoot)
        runtime.copyBundledVideoIfNeeded()
        return runtime
    }

    private func copyBundledVideoIfNeeded() {
        let target = configuration.videoURL(relativeTo: root)
        guard !FileManager.default.fileExists(atPath: target.path),
              let resourceRoot = Bundle.main.resourceURL else { return }
        let relativeVideo = (configuration.video as NSString).expandingTildeInPath
        guard !relativeVideo.hasPrefix("/") else { return }
        let bundled = resourceRoot.appendingPathComponent(relativeVideo).standardizedFileURL
        guard FileManager.default.fileExists(atPath: bundled.path) else { return }
        do {
            try FileManager.default.createDirectory(
                at: target.deletingLastPathComponent(),
                withIntermediateDirectories: true
            )
            try FileManager.default.copyItem(at: bundled, to: target)
        } catch {
            NSLog("Could not copy bundled startup video: %@", error.localizedDescription)
        }
    }
}

@MainActor
private final class BootController: NSObject, NSWindowDelegate {
    private let configuration: LauncherConfiguration
    private let videoURL: URL
    private let appURL: URL
    private let targetBundleIdentifier: String
    private var targetApplication: NSRunningApplication?
    private var window: NSWindow?
    private var player: AVPlayer?
    private var timer: Timer?
    private var endObserver: NSObjectProtocol?
    private var statusObservation: NSKeyValueObservation?
    private var keyMonitor: Any?
    private var statusLabel: NSTextField?
    private var handedOff = false
    private var waitingAtHold = false
    private var elapsedBeforeWait: Double = 0
    private var startedAt = Date()

    init(runtime: RuntimeConfiguration) throws {
        configuration = runtime.configuration
        videoURL = runtime.configuration.videoURL(relativeTo: runtime.root)
        let resolved = try Self.resolveApplication(for: runtime.configuration)
        appURL = resolved.url
        targetBundleIdentifier = resolved.bundleIdentifier
        super.init()
        guard FileManager.default.fileExists(atPath: videoURL.path) else {
            throw LauncherError.missingVideo(videoURL)
        }
    }

    func start() {
        let app = NSApplication.shared
        app.setActivationPolicy(.regular)
        targetApplication = runningTargetApplication()

        let screen = NSScreen.main ?? NSScreen.screens.first
        let visibleFrame = screen?.visibleFrame ?? NSRect(x: 0, y: 0, width: 1280, height: 800)
        let frame: NSRect
        if configuration.fullscreen, let screenFrame = screen?.frame {
            frame = screenFrame
        } else {
            frame = NSRect(
                x: visibleFrame.midX - CGFloat(configuration.playerWidth) / 2,
                y: visibleFrame.midY - CGFloat(configuration.playerHeight) / 2,
                width: CGFloat(configuration.playerWidth),
                height: CGFloat(configuration.playerHeight)
            )
        }

        let playerWindow = NSWindow(
            contentRect: frame,
            styleMask: [.borderless],
            backing: .buffered,
            defer: false
        )
        playerWindow.title = configuration.displayName
        playerWindow.backgroundColor = .black
        playerWindow.isOpaque = true
        playerWindow.hasShadow = false
        playerWindow.level = .screenSaver
        playerWindow.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]
        playerWindow.isReleasedWhenClosed = false
        playerWindow.delegate = self
        window = playerWindow

        let rootView = NSView(frame: NSRect(origin: .zero, size: frame.size))
        rootView.wantsLayer = true
        rootView.layer?.backgroundColor = NSColor.black.cgColor

        let videoView = AVPlayerView(frame: rootView.bounds)
        videoView.translatesAutoresizingMaskIntoConstraints = false
        videoView.controlsStyle = .none
        videoView.videoGravity = .resizeAspect
        rootView.addSubview(videoView)
        NSLayoutConstraint.activate([
            videoView.leadingAnchor.constraint(equalTo: rootView.leadingAnchor),
            videoView.trailingAnchor.constraint(equalTo: rootView.trailingAnchor),
            videoView.topAnchor.constraint(equalTo: rootView.topAnchor),
            videoView.bottomAnchor.constraint(equalTo: rootView.bottomAnchor),
        ])

        let skipButton = NSButton(title: "跳过  Esc", target: self, action: #selector(skip))
        skipButton.bezelStyle = .rounded
        skipButton.font = .systemFont(ofSize: 14, weight: .medium)
        skipButton.alphaValue = 0.78
        skipButton.translatesAutoresizingMaskIntoConstraints = false
        rootView.addSubview(skipButton)
        NSLayoutConstraint.activate([
            skipButton.trailingAnchor.constraint(equalTo: rootView.trailingAnchor, constant: -20),
            skipButton.topAnchor.constraint(equalTo: rootView.topAnchor, constant: 20),
        ])

        let label = NSTextField(labelWithString: "Codex 正在启动…  Esc 跳过")
        label.textColor = .white
        label.font = .systemFont(ofSize: 16, weight: .medium)
        label.alignment = .center
        label.isHidden = true
        label.translatesAutoresizingMaskIntoConstraints = false
        rootView.addSubview(label)
        NSLayoutConstraint.activate([
            label.centerXAnchor.constraint(equalTo: rootView.centerXAnchor),
            label.bottomAnchor.constraint(equalTo: rootView.bottomAnchor, constant: -36),
        ])
        statusLabel = label

        playerWindow.contentView = rootView
        playerWindow.makeKeyAndOrderFront(nil)
        app.activate()
        installEscapeMonitor()
        beginOpeningTarget()
        beginPlayback(in: videoView)
        startedAt = Date()
        let playbackTimer = Timer.scheduledTimer(withTimeInterval: 1.0 / 30.0, repeats: true) { [weak self] _ in
            Task { @MainActor in self?.tick() }
        }
        timer = playbackTimer
        RunLoop.main.add(playbackTimer, forMode: .common)
        NSLog("Dragon Codex Boot started; video=%@", videoURL.path)
    }

    private func beginPlayback(in view: AVPlayerView) {
        let item = AVPlayerItem(url: videoURL)
        let mediaPlayer = AVPlayer(playerItem: item)
        mediaPlayer.volume = Float(configuration.volume)
        view.player = mediaPlayer
        player = mediaPlayer

        endObserver = NotificationCenter.default.addObserver(
            forName: AVPlayerItem.didPlayToEndTimeNotification,
            object: item,
            queue: .main
        ) { [weak self] _ in
            Task { @MainActor in self?.handOff() }
        }
        statusObservation = item.observe(\.status, options: [.initial, .new]) { [weak self] item, _ in
            guard item.status == .failed else { return }
            let error = item.error
            Task { @MainActor in self?.showPlaybackFailure(error) }
        }
        mediaPlayer.play()
    }

    private func beginOpeningTarget() {
        if let running = targetApplication, !running.isTerminated {
            revealTargetBehindOverlay(running)
            return
        }
        let openConfiguration = NSWorkspace.OpenConfiguration()
        openConfiguration.activates = true
        NSWorkspace.shared.openApplication(at: appURL, configuration: openConfiguration) { [weak self] app, error in
            Task { @MainActor in
                guard let self else { return }
                if let error {
                    self.showPlaybackFailure(error)
                    return
                }
                self.targetApplication = app
                if let app { self.revealTargetBehindOverlay(app) }
            }
        }
    }

    private func revealTargetBehindOverlay(_ app: NSRunningApplication) {
        targetApplication = app
        app.unhide()
        _ = app.activate(options: [.activateAllWindows])
        // Keep the animation in front and keep Esc available while Codex opens underneath it.
        window?.orderFrontRegardless()
        NSApplication.shared.activate()
    }

    private func runningTargetApplication() -> NSRunningApplication? {
        NSWorkspace.shared.runningApplications.first {
            $0.bundleIdentifier == targetBundleIdentifier && !$0.isTerminated
        }
    }

    private func targetIsReady() -> Bool {
        guard let app = targetApplication, !app.isTerminated, app.isFinishedLaunching else {
            targetApplication = runningTargetApplication()
            return targetApplication?.isFinishedLaunching == true
        }
        return true
    }

    private func tick() {
        guard !handedOff, let player, let window else { return }
        if Date().timeIntervalSince(startedAt) > configuration.maxWaitSeconds + 20 {
            handOff()
            return
        }

        let time = player.currentTime().seconds
        guard time.isFinite else { return }
        let ready = targetIsReady()
        if !ready, !waitingAtHold, time >= configuration.holdAt {
            elapsedBeforeWait = time
            waitingAtHold = true
            player.pause()
            statusLabel?.isHidden = false
        } else if ready, waitingAtHold {
            waitingAtHold = false
            statusLabel?.isHidden = true
            player.seek(to: CMTime(seconds: elapsedBeforeWait, preferredTimescale: 600))
            player.play()
        }

        if ready, time >= configuration.transitionStart {
            window.alphaValue = configuration.playerOpacity(at: time)
            if time >= configuration.transitionEnd {
                handOff()
            }
        }
    }

    @objc private func skip() {
        handOff()
    }

    private func installEscapeMonitor() {
        keyMonitor = NSEvent.addLocalMonitorForEvents(matching: .keyDown) { [weak self] event in
            guard event.keyCode == 53 else { return event }
            Task { @MainActor in self?.handOff() }
            return nil
        }
    }

    private func showPlaybackFailure(_ error: Error?) {
        guard !handedOff else { return }
        player?.pause()
        let alert = NSAlert()
        alert.alertStyle = .warning
        alert.messageText = "启动动画或 Codex 启动失败"
        alert.informativeText = error?.localizedDescription ?? "无法播放启动视频。按确定后将尝试切换到 Codex。"
        alert.addButton(withTitle: "切换到 Codex")
        alert.runModal()
        handOff()
    }

    private func handOff() {
        guard !handedOff else { return }
        handedOff = true
        timer?.invalidate()
        timer = nil
        player?.pause()
        if let endObserver { NotificationCenter.default.removeObserver(endObserver) }
        if let keyMonitor { NSEvent.removeMonitor(keyMonitor) }
        endObserver = nil
        statusObservation?.invalidate()
        statusObservation = nil
        keyMonitor = nil

        window?.orderOut(nil)
        window?.close()
        window = nil

        if let app = targetApplication, !app.isTerminated {
            app.unhide()
            _ = app.activate(options: [.activateAllWindows])
            NSApplication.shared.terminate(nil)
        } else {
            let openConfiguration = NSWorkspace.OpenConfiguration()
            openConfiguration.activates = true
            NSWorkspace.shared.openApplication(at: appURL, configuration: openConfiguration) { _, error in
                if let error { NSLog("Codex handoff launch failed: %@", error.localizedDescription) }
                NSApplication.shared.terminate(nil)
            }
        }
    }

    func windowWillClose(_ notification: Notification) {
        if !handedOff { handOff() }
    }

    private static func resolveApplication(for configuration: LauncherConfiguration) throws -> (url: URL, bundleIdentifier: String) {
        let fileManager = FileManager.default
        let bundleIdentifier = configuration.appBundleIdentifier.trimmingCharacters(in: .whitespacesAndNewlines)
        let rawPath = configuration.appPath.trimmingCharacters(in: .whitespacesAndNewlines)
        if !rawPath.isEmpty {
            let expanded = (rawPath as NSString).expandingTildeInPath
            let url = URL(fileURLWithPath: expanded, isDirectory: true).standardizedFileURL
            guard fileManager.fileExists(atPath: url.path) else {
                throw LauncherError.missingApp("AppPath 不存在：\(url.path)")
            }
            let discoveredBundleID = Bundle(url: url)?.bundleIdentifier ?? bundleIdentifier
            guard !discoveredBundleID.isEmpty else {
                throw LauncherError.missingApp("无法从 AppPath 读取 Bundle Identifier。")
            }
            return (url, discoveredBundleID)
        }

        if let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: bundleIdentifier),
           fileManager.fileExists(atPath: url.path) {
            return (url, bundleIdentifier)
        }
        throw LauncherError.missingApp("未通过 Bundle Identifier 找到应用：\(bundleIdentifier)")
    }
}

@MainActor
private final class AppDelegate: NSObject, NSApplicationDelegate {
    private var controller: BootController?

    func applicationDidFinishLaunching(_ notification: Notification) {
        do {
            let runtime = try RuntimeConfiguration.load()
            controller = try BootController(runtime: runtime)
            controller?.start()
        } catch {
            let alert = NSAlert(error: error)
            alert.runModal()
            NSApplication.shared.terminate(nil)
        }
    }

    func applicationShouldTerminateAfterLastWindowClosed(_ sender: NSApplication) -> Bool { true }
}

private func exampleConfigurationURL() -> URL? {
    if let resource = Bundle.main.resourceURL?.appendingPathComponent("launcher.macos.example.json"),
       FileManager.default.fileExists(atPath: resource.path) {
        return resource
    }
    let current = URL(fileURLWithPath: FileManager.default.currentDirectoryPath, isDirectory: true)
    let candidates = [
        current.appendingPathComponent("config/launcher.macos.example.json"),
        current.appendingPathComponent("../config/launcher.macos.example.json"),
    ]
    return candidates.first { FileManager.default.fileExists(atPath: $0.path) }
}

@main
@MainActor
private struct DragonCodexBootMacApp {
    static func main() {
        if CommandLine.arguments.contains("--self-test") {
            do {
                guard let url = exampleConfigurationURL() else { throw LauncherError.missingConfig }
                let configuration = try LauncherConfiguration.decode(from: Data(contentsOf: url))
                let opacityChecks: [(Double, Double)] = [
                    (configuration.transitionStart, 1),
                    ((configuration.transitionStart + configuration.transitionEnd) / 2, 0.5),
                    (configuration.transitionEnd, 0),
                ]
                for (time, expected) in opacityChecks {
                    guard abs(configuration.playerOpacity(at: time) - expected) < 0.0001 else {
                        throw LauncherConfigurationError("播放器淡出曲线检查失败。")
                    }
                }
                print("macOS launcher configuration and fade checks passed: \(url.path)")
                exit(0)
            } catch {
                fputs("macOS launcher self-test failed: \(error.localizedDescription)\n", stderr)
                exit(1)
            }
        }

        let application = NSApplication.shared
        let appDelegate = AppDelegate()
        application.delegate = appDelegate
        application.run()
    }
}
