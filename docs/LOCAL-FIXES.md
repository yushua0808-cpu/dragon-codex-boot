# Windows startup repairs: 0.2.10-local

Windows patch series based on upstream v0.2.0 (d3b7bb84b75134f8250491c86d72996a70d58165). Earlier local handoff policies are superseded by the following behavior.

## Current window policy

- Animation is centered in the selected monitor working area, at configured PlayerWidth/PlayerHeight, reduced proportionally only if it cannot fit. An existing client selects its monitor; a cold start uses the pointer monitor.
- Client position AND dimensions match the physical animation rectangle. No previous-position cache is read or written. Legacy MatchClientToPlayer remains accepted but does not override this policy.
- Neither window is made topmost. Alignment uses NOACTIVATE; switching to another app stops automatic operations. Foreground client identity is validated by process name and image path, independently of HWND discovery.

## Forward-only handoff

The installed client 26.1029.7803.0 was inspected read-only. Its main process explicitly checks CODEX_ELECTRON_START_IN_BACKGROUND=1 and uses showInactive instead of show/focus on initial reveal. This is internal client behavior, not a documented stable public contract.

The launcher resolves the installed package using GetPackagesByPackageFamily and GetPackagePathByFullName, then starts the packaged executable with that environment variable only in the child process. It does not modify global environment, client settings, client code, profile, or executable. Cold preparation begins during animation, once, while the launcher owns foreground. It avoids starting a second instance when a matching client process already exists, because second-instance handling can activate an existing window.

Probe can see hidden main-window candidates, allowing position/size and placement behind the active animation before normal background reveal. The largest eligible hidden window is preferred when no established main HWND is known. Ready still requires visible/responsive/stable geometry. Native helper windows that are invisible or DWM-cloaked are ignored when validating visible Z order.

0.2.7's CoverClientArrival path is removed: an already foregrounded client is never covered again by raising the animation. The movie's opacity is monotonic through the fade, including re-preparation after geometry/coverage invalidation. An unexpected interactive client reveal can only progress toward final handoff, never back to the movie. Automatic shell activation is suppressed while background startup is pending; the existing recovery button enables an explicit interactive retry. Minimized-client restoration is nonactivating. Failures stay bounded by MaxWaitSeconds and offer recovery.

Client bounds must remain unchanged for 600 ms; foreground confirmation then requires 300 ms and a launcher-side DwmFlush. DwmFlush is not a client content-ready event. showInactive does not itself guarantee an atomic compositor transition. Automated tests do not prove visual continuity. On 2026-11-04, the user confirmed the installed Windows build passed their live validation.

## Evidence and validation

0.2.6 log: client activation was mistaken for user switch, causing background completion at wrong geometry. 0.2.7 log at 17:48:01: no reveal-prepared event occurred, repeated composition confirmations occurred, and user observed client then animation last frame. These findings invalidated the prior workaround.

Before 0.2.8, scripts/test.ps1 failed: A reveal must never return to the animation's last frame after the real client has been revealed. After the fix: PASS 22 configuration/geometry + 60 handoff/visual + 37 isolated shortcuts = 119 assertions. Self-test includes 605 geometry and 12 placement cases; nativeUiTested=false. Headless --inspect-client resolves the current installed ChatGPT.exe without launching a client.

Automated checks cover the real unshown WPF handoff boundary, fake-desktop controller, child-process environment and early preparation orchestration. They do not prove actual visual continuity. No UI automation restriction was bypassed.

Primary API references:
- https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagesbypackagefamily
- https://learn.microsoft.com/en-us/windows/win32/api/appmodel/nf-appmodel-getpackagepathbyfullname
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- https://www.electronjs.org/docs/latest/api/browser-window


## 0.2.10-local — 冷启动旧位置闪现

2026-11-04 18:53 日志显示：主窗口以 (2215,0,1225,1440) 到达，启动器随后移到动画的 (784,180,1920,1080)。此前只能每250ms发现一个窗口，而且先等待响应，无法覆盖主窗口第一次显示前的定位。

- 在播放器 SourceInitialized 阶段、Prewarm 之前安装仅本次启动有效的 WinEvent CREATE/SHOW/LOCATIONCHANGE 观察器。
- 根据进程名称和完整程序路径、原生窗口类、顶层/owner/toolwindow/尺寸筛选，处理所有符合条件的到达窗口；不更改交接主窗口选择器，避免隐藏辅助窗口再次使交接稳定性饥饿。
- 不等待 SendMessageTimeout 响应，在隐藏窗口创建或坐标恢复事件上，以一次非激活、异步 SetWindowPos 同时请求动画位置/尺寸和播放器后方的层级；前台已切走或已开始淡出时停止事件定位。
- 对发现时仍隐藏的客户端窗口，临时禁止其 DWM 窗口过渡，播放器关闭时恢复默认开启状态。只影响已验证属于客户端的 HWND，不改系统动画设置。
- 轮询保底缩短为50ms；异步请求不作为交接就绪证明，仍须实际尺寸、稳定期、覆盖和前台检查。
- Closed 时解绑所有事件观察器。保持独立 Programs/ChatGPT/ChatGPT.lnk 入口及原图标，不写客户端配置。

验证：22项几何/配置、71项交接/窗口事件、37项隔离快捷方式断言通过；自检605项几何与12项固定定位。新增独立进程原生隐藏 Chrome_WidgetWin_1 测试，验证 CREATE 与再次恢复旧坐标后的 LOCATIONCHANGE 能在不显示窗口的情况下完成定位。自动检查未播放实际 Codex 动画。2026-11-04，用户另行确认 Windows 端实测通过；该结论来自用户验收，而非自动测试。

边界：OUTOFCONTEXT WinEvent 是异步通知，不能在客户端 show 调用之前做同步拦截；CREATE 事件若尚不满足窗口筛选条件，会由后续 SHOW/LOCATIONCHANGE 和轮询保底处理。用户已确认本机 Windows 实测通过；该异步边界仍适用于其他客户端版本和桌面环境。

官方API说明：
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwineventhook
- https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowpos
- https://learn.microsoft.com/en-us/windows/win32/api/dwmapi/ne-dwmapi-dwmwindowattribute


## 0.2.9 — 主窗口选择与交接稳定性

可见的主窗口优先于隐藏辅助窗口；隐藏 preferred HWND 不再提前终止枚举。Probe 与 HandoffController 共享的稳定性检查不会再因为两个不同 HWND 来回切换而饥饿。保留600ms几何稳定和300ms前台确认，并对超时提供可操作的恢复入口。

## 开始菜单安装范围

本机固定入口为用户 Programs 目录下的 `ChatGPT/ChatGPT.lnk`，目标为启动器，图标引用安装目录内的原客户端图标副本。独立子目录用于避开商店客户端改写根目录同名快捷方式的行为。根目录的客户端快捷方式保持原生入口；其他固定项保持原状。

此入口由本地安装操作创建，不由当前通用入口替换脚本自动生成。仓库不包含官方图标副本、用户快捷方式、安装状态或客户端包；公开构建需要自行提供合法图标。安装器 Verify 现在在没有有效记录、快捷方式丢失或被修改时返回 VerificationFailed，而非误报 Verified。

## Windows 验收状态

2026-11-04：用户确认 Windows 端验证通过并授权提交、推送。自动验证与用户实测分开记录；本次修复不构成对其他客户端版本、显示器环境或未来商店更新的兼容保证。
