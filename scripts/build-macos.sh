#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
build_root="$repo_root/build/macos"
app_bundle="$repo_root/build/DragonCodexBoot.app"

mkdir -p "$build_root"
swiftc \
  -O \
  -parse-as-library \
  -module-name DragonCodexBootMac \
  -framework AppKit \
  -framework AVKit \
  "$repo_root/macos/Sources/DragonCodexBootCore/LauncherConfiguration.swift" \
  "$repo_root/macos/Sources/DragonCodexBootMac/main.swift" \
  -o "$build_root/DragonCodexBootMac"

rm -rf "$app_bundle"
mkdir -p "$app_bundle/Contents/MacOS" "$app_bundle/Contents/Resources"
cp "$build_root/DragonCodexBootMac" "$app_bundle/Contents/MacOS/DragonCodexBootMac"
cp "$repo_root/config/launcher.macos.example.json" "$app_bundle/Contents/Resources/launcher.macos.example.json"
cp "$repo_root/media/MEDIA_NOTICE.md" "$app_bundle/Contents/Resources/MEDIA_NOTICE.md"

if [[ -f "$repo_root/media/startup.mp4" ]]; then
  mkdir -p "$app_bundle/Contents/Resources/media"
  cp "$repo_root/media/startup.mp4" "$app_bundle/Contents/Resources/media/startup.mp4"
fi

cat > "$app_bundle/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key><string>zh_CN</string>
  <key>CFBundleExecutable</key><string>DragonCodexBootMac</string>
  <key>CFBundleIdentifier</key><string>community.dragoncodexboot.macos</string>
  <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
  <key>CFBundleName</key><string>Dragon Codex Boot</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>0.2.0</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

codesign --force --deep --sign - "$app_bundle"
printf 'Built: %s\n' "$app_bundle"
