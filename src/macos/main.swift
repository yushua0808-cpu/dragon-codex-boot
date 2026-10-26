import AppKit
import AVFoundation
import QuartzCore

// Native, independent launcher. No changes to the target app, Dock or login items.
private let targetID = "com.openai.codex"
private let diagnosticURL = FileManager.default.temporaryDirectory
    .appendingPathComponent("local.dragoncodexboot.macos", isDirectory: true)
    .appendingPathComponent("last-run.log")

func report(_ message: String) {
    print("[DragonCodexBoot] \(message)")
    fflush(stdout)
    if let handle = try? FileHandle(forWritingTo: diagnosticURL) {
        defer { try? handle.close() }
        _ = try? handle.seekToEnd()
        try? handle.write(contentsOf: Data("\(Date().timeIntervalSince1970) \(message)\n".utf8))
    }
}

final class MovieWindow: NSWindow {
    var skip: (() -> Void)?
    override var canBecomeKey: Bool { true }
    override var canBecomeMain: Bool { true }
    override func keyDown(with event: NSEvent) {
        if event.keyCode == 53 { skip?() } else { super.keyDown(with: event) }
    }
}

final class Launcher: NSObject, NSApplicationDelegate {
    var window: MovieWindow?
    var player: AVPlayer?
    var target: NSRunningApplication?
    var statusObservation: NSKeyValueObservation?
    var endObserver: NSObjectProtocol?
    var failedObserver: NSObjectProtocol?
    var timeObserver: Any?
    var watchdog: Timer?
    var playbackProgress = PlaybackProgressWatchdog()
    var beganAt = Date()
    var readyAt: Date?
    var heldAt: Date?
    var finishing = false
    var launchFinished = false
    var skipRequested = false
    var playbackStarted = false
    var targetURL: URL?
    let verification = CommandLine.arguments.contains("--verify")

    func applicationDidFinishLaunching(_ notification: Notification) {
        try? FileManager.default.createDirectory(at: diagnosticURL.deletingLastPathComponent(), withIntermediateDirectories: true)
        try? Data().write(to: diagnosticURL, options: .atomic)
        report("START")
        targetURL = NSWorkspace.shared.urlForApplication(withBundleIdentifier: targetID)
        guard let url = Bundle.main.url(forResource: "startup", withExtension: "mp4") else {
            fail("缺少启动视频 startup.mp4，请重新安装完整的启动器。")
            return
        }
        guard targetURL != nil else {
            fail("没有找到 Codex（应用标识 com.openai.codex）。请先安装 Codex 桌面客户端。")
            return
        }
        if verification {
            Task { @MainActor in
                do {
                    let asset = AVURLAsset(url: url)
                    let duration = try await asset.load(.duration)
                    let playable = try await asset.load(.isPlayable)
                    let videos = try await asset.loadTracks(withMediaType: .video)
                    let audios = try await asset.loadTracks(withMediaType: .audio)
                    guard playable, duration.seconds > 13.95, !videos.isEmpty, !audios.isEmpty else {
                        self.fail("视频验证失败。")
                        return
                    }
                    report("VERIFY OK: \(duration.seconds)s; video=\(videos.count); audio=\(audios.count); target=\(self.targetURL!.path)")
                    NSApp.terminate(nil)
                } catch { self.fail("视频读取失败：\(error.localizedDescription)") }
            }
            return
        }
        createMenu()
        showMovie(url)
        launchTarget()
        watchdog = Timer.scheduledTimer(withTimeInterval: 0.2, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    func createMenu() {
        let menu = NSMenu()
        let item = NSMenuItem()
        menu.addItem(item)
        let submenu = NSMenu()
        submenu.addItem(withTitle: "跳过动画", action: #selector(skip), keyEquivalent: ".").target = self
        submenu.addItem(.separator())
        submenu.addItem(withTitle: "退出启动器", action: #selector(NSApplication.terminate(_:)), keyEquivalent: "q")
        item.submenu = submenu
        NSApp.mainMenu = menu
    }

    func showMovie(_ url: URL) {
        let screen = NSScreen.screens.first(where: { NSMouseInRect(NSEvent.mouseLocation, $0.frame, false) }) ?? NSScreen.main!
        let visible = screen.visibleFrame
        let width = min(1280, visible.width * 0.9, visible.height * 0.9 * 16 / 9)
        let height = width * 9 / 16
        let rect = NSRect(x: visible.midX - width / 2, y: visible.midY - height / 2, width: width, height: height)
        let movie = MovieWindow(contentRect: rect, styleMask: .borderless, backing: .buffered, defer: false)
        movie.title = "Dragon Codex Boot"
        movie.backgroundColor = .black
        movie.isReleasedWhenClosed = false
        movie.level = .floating
        movie.collectionBehavior = [.moveToActiveSpace, .fullScreenAuxiliary]
        movie.hasShadow = true
        movie.skip = { [weak self] in self?.skip() }
        let view = NSView(frame: NSRect(origin: .zero, size: rect.size))
        view.wantsLayer = true
        let item = AVPlayerItem(url: url)
        let avPlayer = AVPlayer(playerItem: item)
        avPlayer.volume = 1
        let layer = AVPlayerLayer(player: avPlayer)
        layer.videoGravity = .resizeAspect
        layer.frame = view.bounds
        layer.autoresizingMask = [.layerWidthSizable, .layerHeightSizable]
        view.layer!.addSublayer(layer)
        let button = NSButton(title: "跳过 · Esc", target: self, action: #selector(skip))
        button.bezelStyle = .rounded
        button.frame = NSRect(x: width - 114, y: height - 42, width: 100, height: 28)
        button.autoresizingMask = [.minXMargin, .minYMargin]
        view.addSubview(button)
        movie.contentView = view
        window = movie
        player = avPlayer
        movie.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        report("WINDOW shown \(Int(width))x\(Int(height))")
        statusObservation = item.observe(\.status, options: [.initial, .new]) { [weak self] item, _ in
            DispatchQueue.main.async {
                guard let self = self, !self.finishing else { return }
                if item.status == .readyToPlay {
                    self.readyAt = Date()
                    self.player?.play()
                    report("PLAYBACK ready")
                } else if item.status == .failed {
                    report("PLAYBACK failed: \(item.error?.localizedDescription ?? "unknown")")
                    self.skip()
                }
            }
        }
        endObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemDidPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            self?.skip()
        }
        failedObserver = NotificationCenter.default.addObserver(forName: .AVPlayerItemFailedToPlayToEndTime, object: item, queue: .main) { [weak self] _ in
            self?.skip()
        }
        timeObserver = avPlayer.addPeriodicTimeObserver(forInterval: CMTime(seconds: 0.05, preferredTimescale: 600), queue: .main) { [weak self] time in
            guard let self = self, !self.finishing else { return }
            if time.seconds > 0.1 && !self.playbackStarted {
                self.playbackStarted = true
                report("PLAYBACK advancing")
            }
            if time.seconds >= 11.65 && !self.launchFinished && self.heldAt == nil {
                self.heldAt = Date()
                self.player?.pause()
                report("HOLD waiting for application")
            }
            if time.seconds >= 12.45 && self.launchFinished { self.handoff() }
        }
    }

    func launchTarget() {
        let configuration = NSWorkspace.OpenConfiguration()
        configuration.activates = false
        configuration.createsNewApplicationInstance = false
        NSWorkspace.shared.openApplication(at: targetURL!, configuration: configuration) { [weak self] application, error in
            DispatchQueue.main.async {
                guard let self = self, !self.finishing else { return }
                guard let application = application else {
                    self.fail("Codex 启动失败：\(error?.localizedDescription ?? "未知错误")")
                    return
                }
                self.target = application
                self.launchFinished = true
                report("TARGET launched: \(application.bundleIdentifier ?? "unknown")")
                if self.skipRequested { self.handoff() }
                else if self.heldAt != nil {
                    self.heldAt = nil
                    self.playbackProgress = PlaybackProgressWatchdog()
                    self.player?.play()
                }
            }
        }
    }

    func tick() {
        guard !finishing else { return }
        if Date().timeIntervalSince(beganAt) > 65 {
            fail("等待 Codex 超时。请直接打开 Codex 后重试。")
        } else if readyAt == nil && Date().timeIntervalSince(beganAt) > 10 {
            skip()
        } else if playbackProgress.isStalled(
            position: player?.currentTime().seconds ?? .nan,
            now: ProcessInfo.processInfo.systemUptime,
            pausedForLaunch: !launchFinished && heldAt != nil
        ) && launchFinished {
            // Bounded fallback for a stalled media pipeline.
            skip()
        }
    }

    @objc func skip() {
        guard !finishing else { return }
        skipRequested = true
        player?.pause()
        report("SKIP requested")
        if launchFinished { handoff() }
    }

    func handoff() {
        guard !finishing, let application = target, !application.isTerminated else {
            if target?.isTerminated == true { fail("Codex 已退出，请重新打开启动器。") }
            return
        }
        finishing = true
        watchdog?.invalidate()
        let activated = application.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
        report("HANDOFF activation=\(activated)")
        NSAnimationContext.runAnimationGroup { context in
            context.duration = skipRequested ? 0.18 : 0.95
            context.timingFunction = CAMediaTimingFunction(name: .easeInEaseOut)
            self.window?.animator().alphaValue = 0
        } completionHandler: {
            self.player?.pause()
            self.window?.orderOut(nil)
            // Repeat after the overlay closes to give the real app keyboard focus.
            _ = application.activate(options: [.activateAllWindows, .activateIgnoringOtherApps])
            report("FINISHED")
            NSApp.terminate(nil)
        }
    }

    func fail(_ message: String) {
        report("ERROR: \(message)")
        if verification { exit(1) }
        finishing = true
        watchdog?.invalidate()
        player?.pause()
        window?.orderOut(nil)
        let alert = NSAlert()
        alert.messageText = "Dragon Codex Boot"
        alert.informativeText = message
        alert.alertStyle = .warning
        alert.addButton(withTitle: "好")
        NSApp.activate(ignoringOtherApps: true)
        alert.runModal()
        NSApp.terminate(nil)
    }

    func applicationWillTerminate(_ notification: Notification) {
        watchdog?.invalidate()
        player?.pause()
        if let observer = timeObserver { player?.removeTimeObserver(observer) }
        if let observer = endObserver { NotificationCenter.default.removeObserver(observer) }
        if let observer = failedObserver { NotificationCenter.default.removeObserver(observer) }
    }
}

let application = NSApplication.shared
application.setActivationPolicy(.regular)
let launcher = Launcher()
application.delegate = launcher
application.run()
