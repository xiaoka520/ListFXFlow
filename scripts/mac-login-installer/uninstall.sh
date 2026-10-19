#!/bin/zsh
set -euo pipefail
[[ "$EUID" == 0 ]] || { echo 'Run this uninstaller with sudo.' >&2;exit 1; }
UID_NUMBER="$(stat -f '%u' /dev/console)"
launchctl bootout "gui/$UID_NUMBER/com.listfxflow.autostart" 2>/dev/null || true
rm -f /Library/LaunchAgents/com.listfxflow.login-window.plist /Library/LaunchAgents/com.listfxflow.autostart.plist
rm -rf '/Library/Application Support/ListFXFlow/LoginSupport'
pkgutil --forget com.listfxflow.login-support >/dev/null 2>&1 || true
