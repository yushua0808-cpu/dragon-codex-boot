# 配置说明

运行目录中的 `launcher.json` 是实际配置；`config/launcher.example.json` 是公开模板。

| 字段 | 含义 |
| --- | --- |
| `DisplayName` | 播放窗口的显示名 |
| `Video` | 本地 MP4，相对路径以可执行文件目录为基准，也可写绝对路径 |
| `AppLaunch` | 已安装客户端的 `shell:AppsFolder\包族名!应用ID` |
| `UseImageWallpaper` | 使用本地图片背景；需要先运行 `scripts/Enable-ImageWallpaper.ps1` 安装固定版本的社区换肤引擎，默认 `false` |
| `ProcessNames` | 客户端进程名列表，不带 `.exe` |
| `ProcessPathContains` | 可执行文件路径中必须出现的字符串，用来区分同名进程 |
| `HoldAt` | 客户端未就绪时暂停视频的位置，单位秒 |
| `TransitionStart` / `TransitionEnd` | 真实界面淡入与填满窗口的起止秒数 |
| `MaxWaitSeconds` | 等待预算；当前实现达到 `MaxWaitSeconds + 20` 秒总墙钟时间后结束 |
| `Volume` | 0–1 音量 |
| `PlayerWidth` / `PlayerHeight` | 固定播放窗口的物理像素尺寸 |
| `MatchClientToPlayer` | 是否将真实客户端客户区对齐到播放窗口 |
| `AutoReplaceEntrypoints` | 首次运行自动替换可识别的启动快捷方式，默认 `true`；启动前设为 `false` 可关闭 |
| `ScanAllLocalDrives` | 除桌面、开始菜单和快速启动目录外，也扫描本地固定磁盘，默认 `true`；设为 `false` 仅扫描这些常用目录 |
| `ScreenFrames` | 视频中屏幕区域的矩形关键帧 |

## 找到本机客户端标识

```powershell
Get-StartApps | Where-Object { $_.Name -match 'ChatGPT|Codex' }
```

示例配置使用 `OpenAI.Codex_2p2nqsd0c76g0!App`。这来自项目开发机，不保证每个发行渠道都相同；用本机实际结果更新 `AppLaunch`。进程名和路径过滤也要与实际安装匹配。

## 图片背景与开机动画衔接

先准备一张本地 PNG/JPEG/WebP 图片，再在启动器解压目录执行：

```powershell
powershell -NoProfile -ExecutionPolicy RemoteSigned -File .\scripts\Enable-ImageWallpaper.ps1 -Image "C:\Path\To\wallpaper.png"
```

脚本会从 [CCDawn/Codex-Dynamic-Skin](https://github.com/CCDawn/Codex-Dynamic-Skin) 获取固定提交 `5a0e74043b63ed9518ab37cdcf6865340a57006c`，核对提交，复制 Windows 运行源码到 `skin-runtime/`，将图片复制到 `skin-runtime/dragon-wallpaper/` 和用户本机主题仓库，并启用 `UseImageWallpaper`。启动器始终从前一个固定目录加载和校验图片，避免 Store 应用的数据目录重定向把启动主题换成社区示例。可传 `-RevealPercent 0..100` 控制画面透出度。运行时只使用本机回环 CDP，不改官方程序文件；依赖已安装的 Node.js。

配置脚本下载源码还需要 Git 和网络。安装时会用 Windows PowerShell 5.1 解析启动脚本，避免文件编码导致运行时语法错误。社区项目说明已适配到 Codex `26.915`；较新的 Store 客户端存在[无法建立 CDP 的报告](https://github.com/Fei-Away/Codex-Dream-Skin/issues/442)，因此具体版本需要实际启动验证。启用图片背景后，启动器会重启已经运行的普通 Codex 客户端，以接入换肤引擎；启动失败时会明确显示错误和重试按钮，不会悄悄打开无图片背景的普通界面。

首次使用先正常关闭当前 Codex，再从 `DragonCodexBoot.exe` 启动。社区引擎会用独立的本机调试配置，可能需要重新登录。动画在引擎完成画面验证后才切到聊天页；若启动失败则打开原版客户端并在 `logs/launcher.log` 记录结果。此方案使用本机调试端口，其他本机程序也可能访问该会话；结束换肤会话需退出对应 Codex 进程。官方更新后如果界面结构变化，可能需要更新社区引擎的适配版本。

恢复原版启动：将 `launcher.json` 的 `UseImageWallpaper` 改回 `false`，关闭并重新打开 Codex。背景图片仍保存在本机 `CodexDreamSkin` 目录，可按需删除。

## 校准视频中的屏幕

`ScreenFrames` 使用画面归一化坐标，不是显示器坐标：

```json
{"Time":12.65,"X":0.123,"Y":0.094,"Width":0.736,"Height":0.725}
```

对于 1920×1080 的帧，左上角约为 `(236, 102)`，屏幕大小约为 `(1413, 783)`。

1. 在过渡开始附近截取视频帧，测量应替换为真实界面的内屏幕边界。
2. 用 `X = 左边像素/视频宽度`、`Y = 顶边像素/视频高度`，宽高同样归一化。
3. 随着屏幕放大，增加时间递增的关键帧。
4. 最后一个关键帧应覆盖画面：`X=0, Y=0, Width=1, Height=1`。

坐标不能为负，矩形不能超出画面；时间必须严格递增。仅支持轴对齐矩形，不能透视贴合倾斜的四边形。中间矩形线性插值，整体淡入和最终填满采用平滑曲线。

## 调整时间

必须满足 `0 < HoldAt < TransitionStart < TransitionEnd`。视频应长于 `TransitionEnd`，并在等待节点留一个适合停住的动作。默认节点为闭眼阶段。

用自己的视频必须同时重新测量时间与矩形。短视频沿用 14 秒模板，可能在真正过渡开始前就播放结束。

## 小屏幕

例如 1366×768 显示器可改为：

```json
"PlayerWidth": 1280,
"PlayerHeight": 720
```

物理像素大小不随 Windows 缩放比例变化。首版有意保留固定尺寸行为，不自动缩小超出显示器的播放器。
