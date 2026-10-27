# Windows 双屏修复与验收（2026-10-27）

基线：`c63a203e5857e85fb3c8a9891eddb570e7a27b54`。仅修改社区 WPF 启动器，不修改 Codex 官方安装。补丁没有推送到 GitHub：当前连接只有读取权限。

## 根因与证据

原 `BootWindow` 在已有窗口时使用 `Screen.FromHandle`，冷启动使用 `Cursor.Position`。这使鼠标、资源管理器和已有实例的位置影响选择。`Geometry.Centered` 没有尺寸约束，固定播放器确实可能大于较小屏幕。

但本机的 `(320,1260)` 不能直接证明物理窗口越界：DPI 不感知 PowerShell 得到副屏 `(0,1080,1280,720)`，PerMonitorV2 进程与原生 `GetMonitorInfo` 得到 `(0,1080,1920,1080)`；副屏实际有效 DPI 为 144（150%），主屏为 96。1280×720 在后一矩形中的中心正是 `(320,1260)`。这里混用了两种坐标体系。补丁统一使用 PerMonitorV2 物理像素与原生 WorkingArea，并记录实际客户端位置和 DPI。不能把修复后日志中的物理坐标再直接与 DPI 虚拟化坐标比较。

原 `MediaEnded` 直接调用 `HandOff`；`HandOff` 又重新找任意合格窗口，绕过稳定性与响应检查，并且无论焦点激活是否成功都写成功日志。探测丢失后还保留旧 target，缩略图没有随目标身份变化清理。没有窗口时直接退出。7 秒视频也可能早于默认 HoldAt=11.3；本机当前配置实际为 HoldAt=4.5、TransitionStart=5.6、TransitionEnd=6.8，不能把默认时序问题当成本机全部故障的唯一解释。

真实桌面回归还捕捉到 MediaOpened 后约 0.06～0.3 秒就报告 Position=7 / MediaEnded 的情况。一次 Stop/Play 重试仍曾失败，因此最终改用软件组合、关闭 Scrubbing、提前结束时 Close 并重新打开一次。最终测试通过；尚未单独隔离 GPU、媒体管线和 Scrubbing 各自的因果贡献。重复提前结束会记录异常并进入安全交接，不无限重播。

## 补丁行为

- 新增 `TargetDisplay`：空值固定主屏；`\\.\DISPLAY2` 固定副屏。设备缺失时记录原因并退回主屏。不会跟随鼠标、Explorer 或已有 Codex 窗口。
- 使用原生显示器枚举；以 WorkingArea 等比缩小、居中，禁止播放器超出可见工作区；DPI 和显示配置变化后重新定位。PlayerWidth/Height 单位为物理像素。
- 保留 ProcessNames 筛选，额外精确验证 AppLaunch 对应的包家族和同一 Windows 会话。ProcessPathContains 字段保留兼容读取，但包家族身份是主要安全边界。
- 只接受有效、可见、无 owner、非工具/非 NOACTIVATE、未 cloaked、尺寸合格的顶层窗口，并要求响应与稳定至少 0.6 秒。身份包含 HWND、PID、进程开始时间与包路径。保持同一目标，变化时清理旧缩略图和对齐状态。
- 运行中至多一次 AppLaunch；已有实例不发起 AppLaunch。视频结束/失败/停滞后隐藏视频并继续有限等待，避免永久最后一帧；MaxWaitSeconds 是总等待上限。
- 交接再次验证目标身份、响应、有效性，检查实际前台窗口。激活被拒、超时或启动失败时显示提示，8 秒自动退出，Esc 可立即退出错误页；失败退出码为 2。不会杀掉 Codex 进程。
- 对齐失败最多重试三次，避免持续移动窗口。默认配置依然决定是否 MatchClientToPlayer；交付测试包保留用户的 false。
- 普通启动不自动执行入口集成，即使旧配置 AutoReplaceEntrypoints=true。原字段和显式 `--integrate-entrypoints` / `--restore-entrypoints` 命令保留，但本次没有运行这些命令。

窗口可见/响应/稳定仍是启发式判断，不代表 Codex 官方内容加载完成事件。

## 已执行测试及范围

`scripts/test.ps1`：构建成功；605 个关键帧几何案例、12 个放置/边框案例、22 条几何/配置断言、50 条生命周期/多屏断言、33 条隔离入口测试全部通过。包含负坐标、上下排列、竖屏、小屏缩放、窗口消失/复现/替换/句柄复用/失去响应、启动去重和截止时间。真正物理 1280×720 的副屏以及 672 高工作区也有合成几何测试。

`scripts/test-desktop.ps1 -InstallRoot D:\Apps\DragonCodexBoot`：使用本机真实自定义 H.264、24fps、7.000 秒 MP4，编译同源码桌面测试宿主；检查真实顶层窗口包身份、无关窗口和销毁 HWND 的拒绝；主屏和 150% 副屏完整播放后交接，连续采样客户端均在物理 WorkingArea 内；已有实例没有 AppLaunch；短视频配默认较晚 HoldAt 也完成交接。缺失窗口测试注入“永远无窗口”和启动计数器，验证一次请求、超时、错误提示、8 秒退出及退出码 2，没有真正执行冷启动。

最终交付的两个 EXE 也从 PowerShell 直接启动，工作目录设为 Windows 目录；各自完整播放、退出码 0、前台窗口确认成功，没有 AppLaunch。原安装 EXE / launcher.json / MP4 未替换；测试包中的 MP4 与原文件 SHA256 相同。QA 日志和哈希清单见交付包 qa/。

尚未完成：真实冷启动；Explorer 在两屏时分别双击启动；重启后的多轮可靠性；测试中途拔屏/更改 DPI；真实窗口崩溃重建和操作系统拒绝前台激活；MatchClientToPlayer=true 的真实对齐。冷启动需要先正常退出当前 Codex，本次会话仍依赖它，因此没有强制关闭。不能宣称所有验收项已完成或问题已彻底解决。

## 独立试用

交付 `test-version/Primary` 和 `test-version/Secondary` 分别包含 EXE、配置副本与原自定义 MP4 的逐字节副本。双击相应 EXE 即可，或 PowerShell：

```powershell
& '解压目录\test-version\Primary\DragonCodexBoot.exe'
& '解压目录\test-version\Secondary\DragonCodexBoot.exe'
```

每次只启动一个。两份配置只新增对应 TargetDisplay；原安装配置不变。日志位于各自 logs/launcher.log。它们没有用户快捷方式修改脚本，也不修改官方 Codex。

本机 1280×720 物理播放器的预期位置：主屏 WorkingArea=(0,0,1920,1032)，播放器=(320,156,1280,720)；副屏物理 WorkingArea=(0,1080,1920,1008)，播放器=(320,1224,1280,720)，DPI=144。真正的 1280×720 物理屏且工作区高 672 时，将缩为 1194×672，位置=(43,1080)。

## 部署与回滚

先完成独立试用和下列人工验收。部署只替换社区 EXE，不运行 install-start-menu、integrate-entrypoints、Rescan 或 Restore 脚本。

1. 退出社区启动器，保留 Codex。备份 `D:\Apps\DragonCodexBoot\DragonCodexBoot.exe`、`launcher.json` 与 `media` 到带时间戳的独立目录，记录 SHA256。
2. 将已测试的 Primary 或 Secondary 目录中的 `DragonCodexBoot.exe` 复制到安装目录，**不要复制 launcher.json 或 media 覆盖原文件**。普通启动默认主屏。
3. 如需固定副屏，在已备份原配置后，仅向安装配置添加 `"TargetDisplay": "\\\\.\\DISPLAY2"`；其余 Video、时序、音量、尺寸和 AppLaunch 保持原样。在实际 JSON 文件中该项应为下面代码块的写法。

```json
"TargetDisplay": "\\\\.\\DISPLAY2"
```

4. 再次记录安装文件哈希，运行一轮验收并保存日志。若文件被占用，不杀 Codex；只关闭社区启动器后重试。
5. 回滚：退出社区启动器，把备份 EXE 恢复到原路径；若修改了 TargetDisplay，恢复备份 launcher.json；media 未部署覆盖，无需恢复。检查 SHA256 与备份一致。未改过快捷方式，无需执行入口恢复脚本。

源码回滚：对基线应用交付 patch；撤销时 `git apply -R dragon-codex-boot-windows.patch`。实际安装的原 EXE 比 GitHub 当前源码构建早，回滚安装应使用备份 EXE，不能用重新构建的基线替代。

## 人工验收清单

对主屏/副屏两个 profile 分别执行：Explorer 在主屏双击、Explorer 在副屏双击、PowerShell 从不同工作目录启动。每一种再覆盖已有实例、最小化实例、正常退出 Codex 后的真实冷启动。冷启动必须确认没有剩余 Codex 应用进程，但不杀本次工作会话的进程。

每轮检查：目标屏幕一致；动画约 7 秒且可见；播放器在目标屏幕工作区内；已有实例无 app-launch-requested，冷启动只有一条；候选 HWND/PID/包身份符合预期；最终 foreground-confirmed=True 且交接到实际 Codex；无永久最后一帧。异常时保留完整日志，不只截最后一行。

另做：等待期间正常关闭/重新打开 Codex，验证旧目标清除、候选重新稳定；暂时选不存在的 DISPLAY 编号，验证主屏回退日志；改变副屏缩放或断开显示器，验证重新枚举；手动导致 Codex 未能出现，验证有提示且最终退出；按 Esc/跳过验证状态。记录结果后再决定是否替换安装 EXE。
