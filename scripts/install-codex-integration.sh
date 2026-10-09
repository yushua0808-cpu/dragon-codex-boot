#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source_app="$repo_root/build/DragonCodexBoot.app"
install_root="$HOME/Applications"
install_app="$install_root/DragonCodexBoot.app"
expected_bundle_id="community.dragoncodexboot.macos"
agent_label="community.dragoncodexboot.codex-watcher"
agent_plist="$HOME/Library/LaunchAgents/$agent_label.plist"
launch_domain="gui/$(id -u)"

"$repo_root/scripts/build-macos.sh"

if [[ -e "$install_app" ]]; then
  existing_bundle_id="$(/usr/libexec/PlistBuddy -c 'Print :CFBundleIdentifier' "$install_app/Contents/Info.plist" 2>/dev/null || true)"
  if [[ "$existing_bundle_id" != "$expected_bundle_id" ]]; then
    printf 'Refusing to replace an unrelated app at: %s\n' "$install_app" >&2
    exit 1
  fi
fi

mkdir -p "$install_root"
launchctl bootout "$launch_domain/$agent_label" >/dev/null 2>&1 || true
ditto "$source_app" "$install_app"
"$install_app/Contents/MacOS/DragonCodexBootMac" --install-integration
launchctl bootstrap "$launch_domain" "$agent_plist"
printf 'Installed app: %s\n' "$install_app"
printf 'Quit and reopen Codex to test the launch monitor.\n'
