# Shared configuration for the host-native Windows AOT toolchain.
# Sourced by setup.sh, build.sh, and selftest.sh; not meant to be run directly.

# shellcheck shell=bash

WIN_AOT_HOST_DIR=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)

# Where the MSVC CRT and Windows SDK get splatted. Override with WINSDK_ROOT.
: "${WINSDK_ROOT:=${XDG_DATA_HOME:-$HOME/.local/share}/win-aot-sdk}"

: "${XWIN_VERSION:=0.10.0}"
: "${WINSDK_ARCH:=x86_64}"

# lld-link parses these with Windows ';' separators, not ':'. A colon-separated
# LIB is silently ignored and every import library then fails to resolve.
win_aot_export_env() {
    export LIB="$WINSDK_ROOT/crt/lib/x86_64;$WINSDK_ROOT/sdk/lib/um/x86_64;$WINSDK_ROOT/sdk/lib/ucrt/x86_64"
    export INCLUDE="$WINSDK_ROOT/crt/include;$WINSDK_ROOT/sdk/include/ucrt;$WINSDK_ROOT/sdk/include/um;$WINSDK_ROOT/sdk/include/shared"
    export WINSDK_ROOT
}

win_aot_require_sdk() {
    if [ ! -f "$WINSDK_ROOT/crt/lib/x86_64/libcmt.lib" ]; then
        cat >&2 <<EOF
The Windows SDK is not installed at:
  $WINSDK_ROOT

Run ./setup.sh first (see README.md for the license terms this involves).
EOF
        return 1
    fi
}
