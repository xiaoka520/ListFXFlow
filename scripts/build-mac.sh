#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if [[ -z "${DEVELOPER_DIR:-}" && -d /Applications/Xcode.app/Contents/Developer ]]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi
SWIFT_ARGS=(${=SWIFT_BUILD_ARGS:-})
swift build -c release --arch arm64 $SWIFT_ARGS
BIN_DIR="$(swift build -c release --arch arm64 --show-bin-path $SWIFT_ARGS)"
APP_VERSION="$(cat VERSION)"
APP_BUILD="$(cat BUILD)"
PACKAGE_TAG="${APP_VERSION}-b${APP_BUILD}"
APP="${APP_BUNDLE_PATH:-$PWD/artifacts/ListFXFlow.app}"
DMG="$PWD/artifacts/ListFXFlow-Mac-arm64-${PACKAGE_TAG}.dmg"
ZIP="$PWD/artifacts/ListFXFlow-Mac-arm64-${PACKAGE_TAG}.zip"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN_DIR/ListFXFlow" "$APP/Contents/MacOS/ListFXFlow"
for RESOURCE_BUNDLE in "$BIN_DIR"/*.bundle(N); do
  ditto "$RESOURCE_BUNDLE" "$APP/Contents/Resources/${RESOURCE_BUNDLE:t}"
done
swift scripts/create-icon.swift "$APP/Contents/Resources"
cp THIRD_PARTY_NOTICES.txt "$APP/Contents/Resources/THIRD_PARTY_NOTICES.txt"
cat > "$APP/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>CFBundleIdentifier</key><string>com.listfxflow.mac</string>
<key>CFBundleName</key><string>ListFXFlow</string>
<key>CFBundleDisplayName</key><string>ListFXFlow</string>
<key>CFBundleExecutable</key><string>ListFXFlow</string>
<key>CFBundlePackageType</key><string>APPL</string>
<key>CFBundleShortVersionString</key><string>${APP_VERSION}</string>
<key>CFBundleVersion</key><string>${APP_BUILD}</string>
<key>CFBundleIconFile</key><string>FlowIcon</string>
<key>LSMinimumSystemVersion</key><string>14.0</string>
<key>NSHighResolutionCapable</key><true/>
<key>NSHumanReadableCopyright</key><string>ListFXFlow</string>
<key>NSLocalNetworkUsageDescription</key><string>通过雷雳网桥自动发现 Windows，并共享键盘和鼠标。</string>
<key>NSInputMonitoringUsageDescription</key><string>读取键鼠输入，在光标跨屏时转发到另一台设备。</string>
<key>LSUIElement</key><true/>
<key>LSMultipleInstancesProhibited</key><true/>
<key>NSPrincipalClass</key><string>NSApplication</string>
</dict></plist>
PLIST
SIGNING_IDENTITY="${SIGN_IDENTITY:-}"
if [[ -z "$SIGNING_IDENTITY" ]]; then
  SIGNING_IDENTITY="$(security find-identity -v -p codesigning | /usr/bin/awk '/Apple Development:/ { print $2; exit }')"
fi
codesign --force --deep --sign "${SIGNING_IDENTITY:--}" --identifier com.listfxflow.mac "$APP"
codesign --verify --deep --strict "$APP"
zsh scripts/build-login-package.sh "$APP"
codesign --force --deep --sign "${SIGNING_IDENTITY:--}" --identifier com.listfxflow.mac "$APP"
codesign --verify --deep --strict "$APP"

# A drag-to-Applications disk image is the primary Mac installer. Keep the ZIP as a
# portable alternative for CI and for users who prefer it.
swift scripts/create-dmg-background.swift assets/DmgBackground.png
rm -f "$DMG" "$ZIP"
DMG_STAGE="$PWD/.build/dmg-${PACKAGE_TAG}"
DMG_RW="$PWD/.build/ListFXFlow-${PACKAGE_TAG}-rw.dmg"
rm -rf "$DMG_STAGE"
mkdir -p "$DMG_STAGE/.background"
ditto "$APP" "$DMG_STAGE/ListFXFlow.app"
ln -s /Applications "$DMG_STAGE/Applications"
cp assets/DmgBackground.png "$DMG_STAGE/.background/background.png"
cp "$APP/Contents/Resources/FlowIcon.icns" "$DMG_STAGE/.VolumeIcon.icns"
hdiutil create -volname "ListFXFlow ${APP_VERSION} build ${APP_BUILD}" -srcfolder "$DMG_STAGE" \
  -ov -fs HFS+ -format UDRW "$DMG_RW" >/dev/null
[[ -d "/Volumes/ListFXFlow ${APP_VERSION} build ${APP_BUILD}" ]] && hdiutil detach "/Volumes/ListFXFlow ${APP_VERSION} build ${APP_BUILD}" >/dev/null 2>&1 || true
ATTACH_OUTPUT="$(hdiutil attach -nobrowse -readwrite "$DMG_RW")"
MOUNT_POINT="$(printf '%s\n' "$ATTACH_OUTPUT" | awk -F '\t' '/\/Volumes\// { print $NF; exit }')"
if [[ -z "$MOUNT_POINT" || ! -d "$MOUNT_POINT" ]]; then
  echo "failed to mount the temporary DMG" >&2; exit 1
fi
VOLUME_NAME="$(diskutil info "$MOUNT_POINT" | awk -F': *' '/Volume Name:/ { print $2; exit }')"
if command -v SetFile >/dev/null; then SetFile -a C "$MOUNT_POINT" || true; fi
PACKAGE_PYTHON="$PWD/.tools/package-env/bin/python"
if [[ ! -x "$PACKAGE_PYTHON" ]] || ! "$PACKAGE_PYTHON" -c 'import ds_store, mac_alias' >/dev/null 2>&1; then
  python3 -m venv "$PWD/.tools/package-env"
  "$PACKAGE_PYTHON" -m pip install 'ds_store==1.3.1' 'mac_alias==2.2.2'
fi
"$PACKAGE_PYTHON" scripts/configure-dmg.py "$MOUNT_POINT"
sync
hdiutil detach "$MOUNT_POINT" >/dev/null || hdiutil detach "$MOUNT_POINT" -force >/dev/null
hdiutil convert "$DMG_RW" -format UDZO -imagekey zlib-level=9 -o "$DMG" >/dev/null
hdiutil verify "$DMG" >/dev/null
rm -f "$DMG_RW"; rm -rf "$DMG_STAGE"

ditto -c -k --keepParent "$APP" "$ZIP"
(
  cd artifacts
  shasum -a 256 "${DMG:t}" "${ZIP:t}" > SHA256SUMS-mac.txt
)
echo "Built $APP"
echo "Built $DMG"
echo "Built $ZIP"
