#!/usr/bin/env bash
# One-time setup for cross-compiling native AOT Windows binaries on this host.
#
# Checks the toolchain this machine must already provide, then downloads
# Microsoft's MSVC CRT and Windows SDK into WINSDK_ROOT via xwin.
set -euo pipefail

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=config.sh
. "$here/config.sh"

accept=${ACCEPT_MSVC_LICENSE:-}
force=

while [ $# -gt 0 ]; do
    case "$1" in
        --accept-license) accept=yes; shift ;;
        --force) force=yes; shift ;;
        -h|--help)
            cat <<'EOF'
Usage: ./setup.sh [--accept-license] [--force]

Installs the Windows SDK and MSVC CRT needed to link native AOT Windows
binaries on this host. Roughly 640 MB on disk.

Downloading them is governed by the Visual Studio license terms:
  https://visualstudio.microsoft.com/license-terms/
Pass --accept-license (or set ACCEPT_MSVC_LICENSE=yes) to confirm you accept.

  --force   re-download even if the SDK is already installed

Environment:
  WINSDK_ROOT   install location (default: ~/.local/share/win-aot-sdk)
  XWIN_VERSION  xwin release to use (default: 0.10.0)
EOF
            exit 0
            ;;
        *) echo "unknown argument: $1" >&2; exit 2 ;;
    esac
done

echo "== checking host toolchain =="

missing=
check() {
    if command -v "$1" >/dev/null 2>&1; then
        printf '  ok      %-10s %s\n' "$1" "$(command -v "$1")"
    else
        printf '  MISSING %-10s (%s)\n' "$1" "$2"
        missing=yes
    fi
}

check dotnet "the .NET SDK, 8.0 or newer"
check lld-link "MSVC-compatible linker, from the 'lld' package"
check clang "only needed to cross-compile native C/C++, from the 'clang' package"
check curl "to download the SDK"

if [ -n "$missing" ]; then
    cat >&2 <<'EOF'

Install the missing tools and re-run. On Arch / EndeavourOS:

  sudo pacman -S --needed dotnet-sdk lld clang curl

On Debian / Ubuntu:

  sudo apt install dotnet-sdk-8.0 lld clang curl
EOF
    exit 1
fi

sdk_version=$(dotnet --version 2>/dev/null || echo unknown)
echo "  .NET SDK version: $sdk_version"
echo

if [ -f "$WINSDK_ROOT/crt/lib/x86_64/libcmt.lib" ] && [ -z "$force" ]; then
    echo "Windows SDK already installed at $WINSDK_ROOT"
    echo "Re-run with --force to reinstall."
    exit 0
fi

if [ "$accept" != "yes" ]; then
    cat >&2 <<'EOF'
This installs Microsoft's MSVC CRT and Windows SDK. Downloading them is
governed by the Visual Studio license terms:

  https://visualstudio.microsoft.com/license-terms/

Review those terms, then re-run:

  ./setup.sh --accept-license
EOF
    exit 1
fi

echo "== downloading the Windows SDK to $WINSDK_ROOT =="

tmp=$(mktemp -d)

# xwin splats by renaming files out of its cache, so the cache has to sit on the
# same filesystem as the destination. A cache under /tmp fails with EXDEV
# ("Cross-device link") on any host where /tmp is tmpfs or a separate mount.
parent=$(dirname "$WINSDK_ROOT")
mkdir -p "$parent"
cache=$(mktemp -d "$parent/.win-aot-xwin-cache.XXXXXX")

trap 'rm -rf "$tmp" "$cache"' EXIT

curl -sSLf -o "$tmp/xwin.tar.gz" \
    "https://github.com/Jake-Shadle/xwin/releases/download/${XWIN_VERSION}/xwin-${XWIN_VERSION}-x86_64-unknown-linux-musl.tar.gz"
mkdir -p "$tmp/xwin"
tar -xzf "$tmp/xwin.tar.gz" -C "$tmp/xwin" --strip-components=1

rm -rf "$WINSDK_ROOT"

nice -n 19 "$tmp/xwin/xwin" --accept-license --arch "$WINSDK_ARCH" \
    --cache-dir "$cache" splat --output "$WINSDK_ROOT"

echo
echo "Installed $(du -sh "$WINSDK_ROOT" | cut -f1) to $WINSDK_ROOT"
echo
echo "Next: ./selftest.sh   to verify, or"
echo "      ./build.sh path/to/Project.csproj   to compile."
