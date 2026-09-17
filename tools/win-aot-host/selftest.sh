#!/usr/bin/env bash
# Prove the host toolchain really produces a working native AOT Windows DLL.
#
#   1. cross-compile ProbeLib to ProbeLib.dll (native AOT, win-x64)
#   2. confirm it is a PE32+ DLL exporting ProbeExport
#   3. cross-compile a small C harness that LoadLibrary's it
#   4. if wine is installed, run the harness and check the result
#
# Steps 3 and 4 are skipped when clang or wine is unavailable; steps 1-2 still
# fail loudly.
set -euo pipefail

here=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
# shellcheck source=config.sh
. "$here/config.sh"

win_aot_require_sdk

out=$here/selftest/out
rm -rf "$out"
mkdir -p "$out"

echo "== 1. cross-compiling ProbeLib to a native Windows DLL =="
"$here/build.sh" "$here/selftest/ProbeLib/ProbeLib.csproj" -p:PublishDir="$out/"

dll=$out/ProbeLib.dll
[ -f "$dll" ] || { echo "FAIL: $dll was not produced" >&2; exit 1; }

echo
echo "== 2. checking the binary =="
file "$dll"
file "$dll" | grep -q 'PE32+' || { echo "FAIL: not a PE32+ image" >&2; exit 1; }

if command -v llvm-readobj >/dev/null 2>&1; then
    llvm-readobj --coff-exports "$dll" > "$out/readobj.txt"
    grep -q 'Name: ProbeExport' "$out/readobj.txt" \
        || { echo "FAIL: ProbeExport not exported" >&2; exit 1; }
    echo "OK: PE32+ DLL exporting ProbeExport"
else
    echo "OK: PE32+ DLL (llvm-readobj absent, export table not inspected)"
fi

echo
echo "== 3. cross-compiling the C probe harness =="
if ! command -v clang >/dev/null 2>&1; then
    echo "SKIP: clang is not installed, cannot build the probe harness"
    echo
    echo "Self-test passed (build and inspection steps)."
    exit 0
fi

"$here/build.sh" exec clang --target=x86_64-pc-windows-msvc -fuse-ld=lld-link -nostdinc \
    -isystem "$WINSDK_ROOT/crt/include" \
    -isystem "$WINSDK_ROOT/sdk/include/ucrt" \
    -isystem "$WINSDK_ROOT/sdk/include/um" \
    -isystem "$WINSDK_ROOT/sdk/include/shared" \
    -L"$WINSDK_ROOT/crt/lib/x86_64" \
    -L"$WINSDK_ROOT/sdk/lib/um/x86_64" \
    -L"$WINSDK_ROOT/sdk/lib/ucrt/x86_64" \
    "$here/selftest/probe.c" -o "$out/probe.exe"
echo "OK: probe.exe built"

echo
echo "== 4. running under wine =="
if ! command -v wine >/dev/null 2>&1; then
    echo "SKIP: wine is not installed, cannot execute the Windows binaries here"
    echo
    echo "Self-test passed (build and inspection steps)."
    exit 0
fi

# Deliberately outside the repository, and enforced below.
#
# A wine prefix contains `dosdevices/z: -> /` . Any tool that follows symlinks
# and reaches a prefix therefore walks the entire filesystem.
#
# Keeping it in the cache also means it survives the `rm -rf` of $out above, so
# repeat runs reuse the prefix rather than rebuilding it.
export WINEPREFIX=${WINEPREFIX:-${XDG_CACHE_HOME:-$HOME/.cache}/win-aot-selftest/prefix}

# Refuse an in-tree prefix however it was chosen, including via the environment. Both
# roots are checked: this repository is sometimes worked on from a worktree under the
# main repository's .claude/worktrees, and --show-toplevel alone would then name only
# the worktree and miss a prefix placed elsewhere in the main tree.
prefix_path=$(realpath -m "$WINEPREFIX")
for root in \
    "$(git -C "$here" rev-parse --show-toplevel 2>/dev/null || dirname "$here")" \
    "$(dirname "$(git -C "$here" rev-parse --path-format=absolute --git-common-dir 2>/dev/null || echo /nonexistent/x)")"
do
    [ -d "$root" ] || continue
    case "$prefix_path/" in
        "$(realpath -m "$root")"/*)
            echo "FAIL: WINEPREFIX is inside the repository ($WINEPREFIX)." >&2
            echo "Choose a path outside $root." >&2
            exit 1
            ;;
    esac
done

# wine creates the prefix itself but will not create its parent.
mkdir -p "$WINEPREFIX"
export WINEDEBUG=${WINEDEBUG:--all}
export DISPLAY=
if ! result=$(cd "$out" && nice -n 19 wine probe.exe 2>&1); then
    echo "$result"
    echo "FAIL: probe.exe did not run successfully" >&2
    exit 1
fi
echo "$result"
grep -q 'ProbeExport(41) = 42' <<<"$result" || { echo "FAIL: unexpected probe output" >&2; exit 1; }

echo
echo "Self-test passed: the cross-compiled DLL loads and executes."
