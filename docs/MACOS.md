# macOS 版

macOS 版是 `macos/` 下的 Swift 原生启动器，要求 macOS 13 或更新版本以及 Swift 5.9 以上。它使用系统 `AVKit` 播放本地 MP4，启动 `com.openai.codex`，并在可配置的等待点暂停。Codex 完成启动后，播放器淡出并将键盘焦点交给 Codex；按 `Esc` 或点击“跳过”可提前交接。就绪判断使用系统进程状态，不能证明 Codex 的主界面已经完全加载。

## 功能边界

- macOS 版用整层淡出显示当前 Codex 窗口，不使用屏幕录制或辅助功能 API，也不需要额外系统权限。
- 它不把真实 Codex 窗口实时裁切到动画里的屏幕矩形。Windows 版对应效果依靠 DWM 缩略图，macOS 没有相同的无权限接口。
- 启动器不会改写 Dock、Launch Services、Codex App 或登录项。用户可自行把生成的 App 拖到 Dock。
- `AppBundleIdentifier` 默认是 `com.openai.codex`。如果本机使用不同的发行渠道，可填写准确的 `AppPath`，例如 `/Applications/Codex.app`。
- 配置位于 `~/Library/Application Support/DragonCodexBoot/`；首次运行会从 App 包复制配置模板。MP4 在该目录下的 `media/startup.mp4`，也可在配置里填绝对路径。启动信息写入 macOS Console。
- 构建脚本会把 `media/startup.mp4` 和 `media/MEDIA_NOTICE.md` 一并装进 App；首次运行时复制到用户配置目录。

## 构建

在仓库根目录运行：

```sh
./scripts/build-macos.sh
```

生成的 App 位于 `build/DragonCodexBoot.app`。脚本使用 `swiftc` 和本机 SDK，无需安装第三方依赖。构建结果使用本机 ad hoc 签名，仅用于本机检查；公开分发还需要 Developer ID 签名与 notarization。

首次运行后，把自己的 H.264/AAC MP4 放到：

```text
~/Library/Application Support/DragonCodexBoot/media/startup.mp4
```

编辑同目录的 `launcher.json`，按视频修改 `HoldAt`、`TransitionStart`、`TransitionEnd`、`Volume`。如 Codex 未被自动找到，可填写应用 Bundle Identifier 或 `AppPath`。

## 验证

```sh
./scripts/test-macos.sh
```

该脚本检查 Swift 编译、配置校验、淡出曲线、App 包清单和本机签名。它不启动 Codex，也不验证实际 MP4 解码、窗口层级和焦点交接；这些行为需要在安装了 Codex 的 macOS 桌面上手动确认。完整 Xcode 环境下也可运行 `swift test --package-path macos` 执行单元测试。
