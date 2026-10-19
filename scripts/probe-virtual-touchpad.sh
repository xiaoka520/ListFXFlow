#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if [[ -z "${DEVELOPER_DIR:-}" && -d /Applications/Xcode.app/Contents/Developer ]]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi
mkdir -p .build/virtual-touchpad-probe
xcrun swiftc -parse-as-library -target arm64-apple-macos15.0 Tools/VirtualTouchpadProbe.swift \
  -o .build/virtual-touchpad-probe/VirtualTouchpadProbe
.build/virtual-touchpad-probe/VirtualTouchpadProbe "$@"
