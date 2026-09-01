#!/usr/bin/env bash
# Builds the native lvglnet library on Linux/macOS and stages it into
# runtimes/<rid>/native so `dotnet run` picks it up.
#
#   ./native/build.sh                                   # host build
#   ./native/build.sh --pi4                             # tune for Raspberry Pi 4 (Cortex-A72)
#   ./native/build.sh --no-demos                        # skip the bundled LVGL demos
#   ./native/build.sh --cross=aarch64-linux-gnu         # cross-compile with a GNU triplet
#   ./native/build.sh --osx-arch=x86_64 --rid=osx-x64   # the other Mac architecture
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DEMOS=ON
PI4=OFF
CROSS=""
OSX_ARCH=""
RID=""

for arg in "$@"; do
    case "$arg" in
        --pi4)        PI4=ON ;;
        --no-demos)   DEMOS=OFF ;;
        --cross=*)    CROSS="${arg#*=}" ;;
        --osx-arch=*) OSX_ARCH="${arg#*=}" ;;
        --rid=*)      RID="${arg#*=}" ;;
        *) echo "unknown option: $arg" >&2; exit 2 ;;
    esac
done

command -v cmake >/dev/null || { echo "cmake is required (apt install cmake build-essential)" >&2; exit 1; }

# Every target gets its own build tree. A CMake cache remembers the compiler it was configured
# with, so reusing one directory across architectures fails with "the compiler has changed".
BUILD_DIR="$ROOT/native/build"
# Expanded as ${EXTRA[@]+...} further down: under `set -u`, bash 3.2 - which is what macOS
# ships - treats an empty array's plain expansion as an unbound variable.
EXTRA=()

if [ -n "$CROSS" ]; then
    command -v "$CROSS-gcc" >/dev/null || {
        echo "$CROSS-gcc not found - install the matching cross toolchain package" >&2
        exit 1
    }
    # The triplet's first component is the processor name CMakeLists.txt maps onto a RID:
    # aarch64 -> linux-arm64, arm -> linux-arm.
    BUILD_DIR="$ROOT/native/build-$CROSS"
    EXTRA+=(-DCMAKE_SYSTEM_NAME=Linux
            -DCMAKE_SYSTEM_PROCESSOR="${CROSS%%-*}"
            -DCMAKE_C_COMPILER="$CROSS-gcc")
fi

if [ -n "$OSX_ARCH" ]; then
    # One Apple SDK builds both Mac architectures, so the second one needs no second machine.
    # CMAKE_SYSTEM_PROCESSOR still reports the host though, hence the explicit --rid alongside.
    BUILD_DIR="$ROOT/native/build-$OSX_ARCH"
    EXTRA+=(-DCMAKE_OSX_ARCHITECTURES="$OSX_ARCH")
fi

if [ -n "$RID" ]; then
    BUILD_DIR="$ROOT/native/build-$RID"
    EXTRA+=(-DLVGLNET_RID="$RID")
fi

cmake -S "$ROOT/native" -B "$BUILD_DIR" \
      -DCMAKE_BUILD_TYPE=Release \
      -DLVGLNET_WITH_DEMOS="$DEMOS" \
      -DLVGLNET_TUNE_PI4="$PI4" \
      ${EXTRA[@]+"${EXTRA[@]}"}

cmake --build "$BUILD_DIR" --config Release -j "$(getconf _NPROCESSORS_ONLN 2>/dev/null || echo 4)"

echo
echo "Staged native library:"
find "$ROOT/runtimes" -name 'liblvglnet.*' -newermt '-5 minutes' 2>/dev/null || true
