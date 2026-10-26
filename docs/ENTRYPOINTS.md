# 启动入口替换与恢复

下载版首次运行默认启动一次后台扫描。播放动画和打开客户端无需等待磁盘扫描结束。它针对 `launcher.json` 中配置的客户端；开发机的应用名显示为 ChatGPT，应用包标识是 OpenAI.Codex。

## 操作

1. 解压到长期保留的目录，双击 `DragonCodexBoot.exe`。程序播放动画，后台扫描和替换入口。
2. 打开 `.integration/report.json` 查看替换、补建、跳过、权限错误和扫描超时。扫描最多 300 秒，下次播放不会自动重复扫描。
3. 新增了入口时运行 `Rescan-Entrypoints.cmd`。公共目录写入失败时可由自己选择以管理员身份运行这个文件。
4. 删除、移动程序或不再使用之前，运行 `Restore-Original-Entrypoints.cmd`。它恢复备份，并阻止下次播放自动重装。已被用户改动或删除的快捷方式会保留，原因记录在报告中。

恢复与重新扫描也可使用 `DragonCodexBoot.exe --restore-entrypoints` / `--integrate-entrypoints`，不播放视频。命令行执行会等待入口脚本完成；平常播放的扫描在后台进行。

如果要完全关闭替换，在首次运行前设置：

```json
"AutoReplaceEntrypoints": false
```

如果只想扫描常用入口，设置 `ScanAllLocalDrives: false`。已有替换不会因为关闭配置自动恢复，应先运行恢复文件。

## 匹配与备份

扫描 `.lnk` 的实际目标与参数。对包装应用，读取已安装包的应用清单和 AppsFolder ID；部分目标为空的包装应用快捷方式用 Shell 的 AppUserModel.ID 确认。只凭 ChatGPT / Codex 文件名不能触发替换。ChatGPT 网页 `.url` 和浏览器链接不改。

替换时建立新的快捷方式，避免编辑原 MSIX 链接时遗留激活属性。保留文件名、已有图标、快捷键和窗口样式，清空旧参数，工作目录设为启动器目录。原文件保存在 `.integration/backups/`，原始哈希和安装后哈希记录在 `entrypoints-state.json`。每次替换先持久化记录，再写入文件，并检查写入结果。

恢复只覆盖内容仍与安装后的哈希相同的文件。备份校验不符则拒绝恢复；用户改变过的文件不会覆盖。程序补建且仍由它持有的快捷方式在恢复时移除。所有日志与备份仅留在本机，不上传到 GitHub。

## 实际覆盖范围

| 入口 | 行为 |
| --- | --- |
| 用户桌面、开始菜单程序目录中的 `.lnk` | 精确匹配后替换；缺少对应入口时补建 |
| 公共桌面、公共开始菜单、其他用户目录 | 可访问且有写权限时替换；否则记录失败 |
| 快速启动/任务栏目录中的 `.lnk` | 修改能找到的快捷方式；MSIX 应用固定项通常不是这里的 `.lnk`，需要取消原固定项后固定启动器 |
| 其他本地固定磁盘上的 `.lnk` | 默认继续扫描可访问目录，受时间预算和排除目录限制 |
| MSIX 注册的所有应用、搜索结果、原固定的开始菜单项 | 仍可能直接激活官方客户端；应取消原固定项，再固定启动器 |
| 直接运行官方 exe、协议、第三方程序内部调用 | 不拦截 |
| 网页 ChatGPT、浏览器书签 | 不替换为本地 Codex 客户端 |
| Windows、回收站、链接目录、运行时/缓存目录、启动器目录 | 跳过；不修改应用包 |

因此这项功能是可恢复的快捷方式替换，不能声称所有启动路径都已接管。默认设置尽量覆盖本机可识别的入口，报告同时保留 `RegisteredApplicationChanged: false`、`StartPinsAutomaticallyChanged: false` 和 `TaskbarCacheRefreshVerified: false`。

本机已固定的官方 Codex/ChatGPT 图标会继续直接启动官方应用。要从任务栏播放动画：右键取消固定原图标，然后在“开始”菜单搜索 `Dragon Codex Boot`，右键搜索结果固定到任务栏。固定后先退出 Codex，再点击新图标验证。这个独立开始菜单入口的目标是 `DragonCodexBoot.exe`。启动器会在找不到官方窗口时自动重试，并在超时后保留错误提示和手动重试按钮；日志在 `logs/launcher.log`。

## 迁移与卸载

先恢复，再移动文件夹，并从新位置运行或重新扫描。不要只移动 exe：程序需要同目录的视频、配置、脚本和恢复记录。没有系统服务或全局执行钩子；完成恢复、取消自己固定的启动器入口后，可以删除解压目录。恢复遇到用户改动时保留文件，需自己决定这些入口后续指向哪里。

## 高级扫描

脚本支持 `-SearchRoots` 显式指定扫描目录；使用它不会补建实际桌面/开始菜单入口，也不会扫描其他磁盘。`-OfficialExecutablePaths` 可额外指定已确认的客户端可执行文件；默认从配置的已安装应用包清单获得目标。`-ScanBudgetSeconds` 可设置 1–1800 秒预算。

回归测试只扫描项目内的隔离目录，不自动修改开发者真实入口。

Windows 对快捷方式身份和重新启动信息的处理参考 [Microsoft AppUserModelID 文档](https://learn.microsoft.com/zh-cn/windows/win32/shell/appids)；客户端也可能使用独立的[应用激活方式](https://learn.microsoft.com/en-us/windows/apps/develop/launch/)，因此快捷方式改写与应用注册接管是不同的覆盖范围。
