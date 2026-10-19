#!/bin/zsh
set -euo pipefail
cd "${0:A:h}/.."
if [[ -z "${DEVELOPER_DIR:-}" && -d /Applications/Xcode.app/Contents/Developer ]]; then
  export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
fi
DOTNET_BIN="${DOTNET_BIN:-dotnet}"
if [[ "$DOTNET_BIN" == dotnet && -x .tools/dotnet/dotnet ]]; then DOTNET_BIN="$PWD/.tools/dotnet/dotnet"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
SWIFT_ARGS=(${=SWIFT_BUILD_ARGS:-})
swift build $SWIFT_ARGS
BIN_DIR="$(swift build --show-bin-path $SWIFT_ARGS)"
mkdir -p artifacts
"$BIN_DIR/ListFXFlow" --transport-self-test > artifacts/transport-self-test.log 2>&1 &
SERVER_PID=$!
trap 'kill "$SERVER_PID" 2>/dev/null || true; wait "$SERVER_PID" 2>/dev/null || true' EXIT
for ATTEMPT in {1..50}; do
  if rg -q '等待 Windows' artifacts/transport-self-test.log; then break; fi
  if ! kill -0 "$SERVER_PID" 2>/dev/null; then cat artifacts/transport-self-test.log; exit 1; fi
  sleep 0.1
done
"$DOTNET_BIN" run --project windows/FlowCore.Tests/FlowCore.Tests.csproj -c Release -- --interop
