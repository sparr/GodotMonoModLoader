#!/usr/bin/env bash
# Cross-compile a C# project to a native AOT Windows binary, on this host.
#
# Supplies only the cross-compilation plumbing. Whether the output is a DLL or an
# EXE stays a property of the project itself (`NativeLib`, `OutputType`).
set -euo pipefail

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=config.sh
. "$here/config.sh"

CONFIG=${CONFIG:-Release}
RID=${RID:-win-x64}

usage() {
    cat <<'EOF'
Usage:
  ./build.sh [<project-or-solution>] [extra dotnet publish args...]
  ./build.sh exec <command> [args...]

Cross-compiles a C# project to a native win-x64 binary using the Linux
ILCompiler plus lld-link against the Windows SDK installed by ./setup.sh.

Environment:
  CONFIG       build configuration (default: Release)
  RID          runtime identifier (default: win-x64)
  WINSDK_ROOT  Windows SDK location (default: ~/.local/share/win-aot-sdk)

The project must opt into native AOT itself, e.g.:

  <PublishAot>true</PublishAot>
  <NativeLib>Shared</NativeLib>   <!-- for a .dll; omit for an .exe -->

Forms:
  ./build.sh src/MyProxy/MyProxy.csproj
  ./build.sh src/MyProxy/MyProxy.csproj -p:OptimizationPreference=Size
  ./build.sh exec clang --target=x86_64-pc-windows-msvc ...
EOF
}

case "${1-}" in
    -h|--help|help)
        usage
        exit 0
        ;;
esac

win_aot_require_sdk
win_aot_export_env

if [ "${1-}" = "exec" ]; then
    # Run an arbitrary command with the Windows SDK env in place.
    shift
    exec "$@"
fi

project=
if [ $# -gt 0 ] && [ "${1#-}" = "$1" ]; then
    project="$1"
    shift
fi

set -- publish ${project:+"$project"} \
    -c "$CONFIG" \
    -r "$RID" \
    -p:DisableUnsupportedError=true \
    -p:CppLinker="$here/win-link" \
    -p:CppLibCreator="$here/win-lib" \
    -p:EnableSourceLink=false \
    "$@"

echo "+ dotnet $*" >&2
exec dotnet "$@"
