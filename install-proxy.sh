#!/usr/bin/env bash
# Build the proxy mod loader and install it into a game directory.
#
#   ./install-proxy.sh [/path/to/Atomcraft]
#
# Installs only files the game does not ship, so nothing here is reverted by a
# Steam integrity check or a game update. Atomcraft.dll is never touched; if a
# previous release patched it, pass --restore to put the backup back.
set -euo pipefail
cd "$(dirname "$0")"
ROOT=$(pwd)

say()  { printf '\033[1m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33mwarning:\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31merror:\033[0m %s\n' "$*" >&2; exit 1; }

RESTORE=0
GAME=""
while [ $# -gt 0 ]; do
    case "$1" in
        --restore) RESTORE=1; shift ;;
        -h|--help) sed -n '2,8p' "$0"; exit 0 ;;
        *) GAME="$1"; shift ;;
    esac
done

[ -n "$GAME" ] || GAME="$HOME/Games/Steam/steamapps/common/Atomcraft"
[ -f "$GAME/AtomCraft.exe" ] || die "no AtomCraft.exe in $GAME"

DATA=$(find "$GAME" -maxdepth 1 -type d -name 'data_*' | head -1)
[ -n "$DATA" ] || die "no data_* directory in $GAME"

# The old release injected a type into Atomcraft.dll. The proxy makes that
# unnecessary, and leaving it in place would load the mod loader twice.
# A backup existing does not mean the patch is still applied, so compare before
# nagging: identical files mean it has already been restored.
if [ -f "$DATA/Atomcraft.dll.backup" ] && ! cmp -s "$DATA/Atomcraft.dll" "$DATA/Atomcraft.dll.backup"; then
    if [ "$RESTORE" = 1 ]; then
        say "restoring the unpatched Atomcraft.dll"
        # --remove-destination: game files are often hardlinked between installs,
        # so writing in place would modify every one of them.
        cp --remove-destination "$DATA/Atomcraft.dll.backup" "$DATA/Atomcraft.dll"
    else
        warn "$DATA/Atomcraft.dll may still carry the old injected patch."
        warn "Re-run with --restore to put the backup back."
    fi
fi

say "building the native proxy (dinput8.dll)"
./tools/win-aot-host/build.sh ModLoaderHook/ModLoaderHook.csproj >/dev/null

say "building the managed bootstrap"
dotnet build -c Release ModLoaderBootstrap/ModLoaderBootstrap.csproj >/dev/null

say "building the mod loader"
# CreateZip runs as part of this build and collects the binaries published above,
# so Release/GodotMonoModLoader.zip ends up holding what this script installs.
dotnet build -c Release -p:GameInstallDir="$GAME" \
    GodotMonoModLoader/GodotMonoModLoader.csproj >/dev/null

say "installing into $GAME"
install -Dm644 ModLoaderHook/bin/Release/net10.0/win-x64/publish/dinput8.dll "$GAME/dinput8.dll"
install -Dm644 ModLoaderBootstrap/bin/Release/net8.0/ModLoaderBootstrap.dll "$GAME/GodotMonoModLoader/ModLoaderBootstrap.dll"
install -Dm644 GodotMonoModLoader/bin/Release/net8.0/GodotMonoModLoader.dll "$GAME/GodotMonoModLoader/GodotMonoModLoader.dll"
install -Dm644 GodotMonoModLoader/bin/Release/net8.0/0Harmony.dll "$GAME/GodotMonoModLoader/0Harmony.dll"
for f in GDScripts/*; do
    [ -f "$f" ] && install -Dm644 "$f" "$GAME/GodotMonoModLoader/$(basename "$f")"
done

VERSION=$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' GodotMonoModLoader/GodotMonoModLoader.csproj | head -1)
HARMONY=$(sed -n 's:.*<HarmonyVersion>\(.*\)</HarmonyVersion>.*:\1:p' GodotMonoModLoader/GodotMonoModLoader.csproj | head -1)
sed -e "s/@VERSION@/${VERSION:-0.0.0}/" -e "s/@HARMONY_VERSION@/${HARMONY:-unknown}/" \
    GodotMonoModLoader.gd > "$GAME/GodotMonoModLoader.gd"

# ModLoaderPatch.dll was only ever loaded by the injected type.
rm -f "$GAME/GodotMonoModLoader/ModLoaderPatch.dll"

mkdir -p "$GAME/Mods"

say "done. Launch options:"
echo
echo "    Proton/Linux:  WINEDLLOVERRIDES=\"dinput8=n,b\" %command% -s GodotMonoModLoader.gd"
echo "    Windows:       -s GodotMonoModLoader.gd"
echo
echo "  The override is required under Proton: Wine prefers its own builtin dinput8"
echo "  and ignores the file beside the executable without it."
echo
echo "  Startup diagnostics: $GAME/GodotMonoModLoader.proxy.log"
