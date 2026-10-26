# 发布到 GitHub

建议仓库名：`dragon-codex-boot`，可见性：Public，默认分支：`main`。

## 上传源码

本仓库源码已经独立整理。只上传 Git 跟踪文件，避免把原制作项目、下载目录和本机配置一起上传。

有 GitHub CLI 并登录后，在仓库根目录执行：

```powershell
gh repo create dragon-codex-boot --public --source . --remote origin --push --description "Custom animation startup and live-window handoff for Codex on Windows"
```

如果仓库已经创建，不再次执行创建命令。用 `git remote -v` 检查远端，并推送到对应仓库。

## 首次 Release

1. 推送源码后，检查 GitHub Actions 的 Windows 构建结果。
2. 执行 `scripts/test.ps1` 和 `scripts/package.ps1`，确认程序包包含随附视频与媒体说明。
3. 上传版本包和对应 `.sha256` 到 Release。
4. Release 说明写明视频已经包含，解压即可运行，并说明如何换成自己的动画。

`build.yml` 会构建、运行离线检查、打包仓库中的示例视频，并把 ZIP 与哈希作为 Actions artifact 保留；它不自动建立 Release，也不会读取开发者的个人视频目录。

## 发布内容边界

- 源码、配置模板和本项目文档使用 MIT 许可证。
- 默认 ZIP 包含程序、示例配置、完整1080p Codex 主题启动视频、导入及入口替换脚本、一键恢复/重新扫描、许可证和文档。首次运行默认替换可识别的快捷方式，必须在 Release 说明中明确。
- 原角色图、其他制作中间文件、提取的官方图标、本机路径与日志没有纳入源码仓库。
- 视频放在 `media/startup.mp4`，可编辑源码在 `animation/`，素材说明放在 `media/MEDIA_NOTICE.md`；代码 MIT 许可不自动扩展到第三方名称和标志。
