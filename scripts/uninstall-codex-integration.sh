#!/usr/bin/env bash
set -euo pipefail

install_app="$HOME/Applications/DragonCodexBoot.app"
agent_label="community.dragoncodexboot.codex-watcher"
agent_plist="$HOME/Library/LaunchAgents/$agent_label.plist"
launch_domain="gui/$(id -u)"
app_executable="$install_app/Contents/MacOS/DragonCodexBootMac"

launchctl bootout "$launch_domain/$agent_label" >/dev/null 2>&1 || true

if [[ -x "$app_executable" ]]; then
  "$app_executable" --uninstall-integration
  rm -rf "$install_app"
  printf 'Removed Codex launch monitor and app: %s\n' "$install_app"
else
  rm -f "$agent_plist"
  printf 'Installed app was not found: %s\n' "$install_app"
fi

printf 'Configuration and copied video in ~/Library/Application Support/DragonCodexBoot were kept.\n'
