#!/usr/bin/env bash
# Builds the native lvglnet library on Linux/macOS and stages it into
# runtimes/<rid>/native so `dotnet run` picks it up.
#
#   ./native/build.sh                 # host build
#   ./native/build.sh --pi4           # tune for Raspberry Pi 4 (Cortex-A72)
#   ./native/build.sh --no-demos      # skip the bundled LVGL demos
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
BUILD_DIR="$ROOT/native/build"
DEMOS=ON
PI4=OFF

for arg in "$@"; do
    case "$arg" in
        --pi4)      PI4=ON ;;
        --no-demos) DEMOS=OFF ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

command -v cmake >/dev/null || { echo "cmake is required (apt install cmake build-essential)" >&2; exit 1; }

cmake -S "$ROOT/native" -B "$BUILD_DIR" \
      -DCMAKE_BUILD_TYPE=Release \
      -DLVGLNET_WITH_DEMOS="$DEMOS" \
      -DLVGLNET_TUNE_PI4="$PI4"

cmake --build "$BUILD_DIR" --config Release -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"

echo
echo "Staged native library:"
find "$ROOT/runtimes" -name 'liblvglnet.*' -newermt '-5 minutes' 2>/dev/null || true
