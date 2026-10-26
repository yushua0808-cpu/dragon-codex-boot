# macOS 原生启动器

macOS 版使用 AppKit 与 AVFoundation 播放仓库中已有的 Codex 主题动画和原音轨，同时启动或复用 Codex。视频结尾淡出后将焦点交给真实客户端。

## 环境与构建

- Apple Silicon Mac，macOS 13 或更新版本。
- 构建需要 Xcode 或 Xcode Command Line Tools；运行不需要额外运行时。
- 使用启动器需要已安装应用标识为 `com.openai.codex` 的客户端。按标识查找，不依赖应用显示名或固定安装路径。

在仓库根目录运行：

```sh
./scripts/build-macos-standalone.sh
```

输出为 `build/macos/Dragon Codex Boot.app`。将它复制到个人 `~/Applications` 或“应用程序”目录，双击即可使用，也可拖入 Dock 作为独立启动入口。

打包并生成 SHA-256：

```sh
./scripts/package-macos-standalone.sh
```

输出为 `dist/DragonCodexBoot-版本-macos-arm64-with-video.zip` 及对应 `.sha256`。包中保留原仓库许可和媒体说明。构建使用本地 ad-hoc 签名，未作 Developer ID 签名或 Apple 公证；在其他电脑上分发仍可能需要系统的正常首次打开确认。本脚本不修改 Gatekeeper 设置。

## 使用与行为

- 窗口在鼠标所在屏幕居中显示，保持 16:9，最大 1280 × 720 点，并适应屏幕可用空间。
- 后台打开 Codex；若已经运行，复用现有实例。
- 按 `Esc`、`Command+.` 或点击右上角“跳过”可立即请求交接。`Command+Q` 退出启动器。
- 默认保留原音轨。客户端启动未完成时，在约 11.65 秒处暂停等待；启动完成后继续播放，在 12.45 秒处开始淡出。
- 媒体失败会尝试跳过；媒体卡住和客户端启动都有有限等待，避免永久遮挡。
- 最近一次诊断日志位于系统临时目录下的 `local.dragoncodexboot.macos/last-run.log`，只记录启动、播放及交接状态，不记录聊天内容或屏幕画面。
- 删除启动器即可卸载；若自行固定到 Dock，再移除该入口。

## 与 Windows 版的差异

macOS 版使用淡出交接，没有移植 Windows DWM 实时窗口嵌入、客户端窗口重排、全盘快捷方式替换或 `launcher.json` 配置读取。当前时间点和音量匹配仓库自带视频；若更换视频，需调整 `src/macos/main.swift` 中的时间点并重新构建。

`NSWorkspace` 的启动回调只能说明应用已启动，不保证客户端窗口或所有后台内容加载完成。启动器不请求辅助功能或屏幕录制权限，不修改官方应用包、Dock 设置或登录项。

## 验证

```sh
./scripts/test-macos-standalone.sh
```

该脚本检查编译、arm64 架构、应用元数据、素材一致性、许可文件、签名，以及 ZIP 解包后的二进制和视频一致性。不需要安装 Codex，不把静态检查当作真实播放验证。

桌面手动检查：

1. 在 Codex 已运行时双击启动器，确认动画和音轨播放、窗口适应屏幕，约 14 秒后启动器退出，客户端可操作。
2. 再次启动，在视频开始时按 Esc，确认立即淡出并切换到客户端；单独测试右上角按钮和 Command+.。
3. 在可关闭 Codex 的测试环境中退出客户端后启动，确认打开客户端并交接，等待有上限。
4. 在测试副本中移除视频，确认错误提示；在未安装 Codex 的测试环境中确认缺少客户端的提示。

本次适配已在 Apple Silicon / macOS 27.0.1 实测完整播放和 Esc 跳过，日志均记录 `HANDOFF activation=true` 和 `FINISHED`。未关闭用户客户端进行冷启动测试，未实测缺少客户端或损坏媒体的分支。Windows 测试应继续在 Windows 环境运行原有 `scripts/test.ps1`。
