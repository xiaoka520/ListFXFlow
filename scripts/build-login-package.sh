#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
APP="$1"
STAGE="$PWD/.build/login-package-root"
rm -rf "$STAGE"
SUPPORT="$STAGE/Library/Application Support/ListFXFlow/LoginSupport"
mkdir -p "$SUPPORT" "$STAGE/Library/LaunchAgents"
ditto "$APP" "$SUPPORT/ListFXFlow.app"
cp scripts/mac-login-installer/uninstall.sh "$SUPPORT/uninstall.sh"
chmod 755 "$SUPPORT/uninstall.sh"
cp scripts/mac-login-installer/com.listfxflow.login-window.plist "$STAGE/Library/LaunchAgents/"
cp scripts/mac-login-installer/com.listfxflow.autostart.plist "$STAGE/Library/LaunchAgents/"
mkdir -p .build/login-package-scripts
cp scripts/mac-login-installer/postinstall .build/login-package-scripts/postinstall
chmod 755 .build/login-package-scripts/postinstall
pkgbuild --root "$STAGE" --scripts .build/login-package-scripts --identifier com.listfxflow.login-support \
  --version "$(cat VERSION).$(cat BUILD)" --ownership recommended --install-location / \
  "$APP/Contents/Resources/ListFXFlow-LoginSupport.pkg" >/dev/null
mkdir -p .build/login-remove-scripts
cp scripts/mac-login-installer/remove-postinstall .build/login-remove-scripts/postinstall
chmod 755 .build/login-remove-scripts/postinstall
pkgbuild --nopayload --scripts .build/login-remove-scripts --identifier com.listfxflow.remove-login-support \
  --version "$(cat VERSION).$(cat BUILD)" "$APP/Contents/Resources/ListFXFlow-RemoveLoginSupport.pkg" >/dev/null
rm -rf "$STAGE"
