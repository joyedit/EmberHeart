#!/bin/bash
set -e

MOD_NAME="EmberHeart"
MODS_DIR="$HOME/.config/VintagestoryData/Mods"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"

# Deploying over a running game swaps the zip out from under a loaded assembly: the
# server half keeps the old version while the client half loads the new one, and the
# client dies with "Assembly with same name is already loaded", which surfaces as some
# unrelated "don't know how to instantiate" error. Close the game first, or set FORCE=1.
#
# Detected by whether the game still holds its own log file open. Do NOT use
# `pgrep -f Vintagestory`: the pattern appears in the checking command's own command
# line, so it matches this script and reports "running" every time.
VS_LOG="$HOME/.config/VintagestoryData/Logs/client-main.log"

if [ -f "$VS_LOG" ] && fuser "$VS_LOG" > /dev/null 2>&1; then
	if [ "$FORCE" != "1" ]; then
		echo "!!! Vintage Story is running. Close it before deploying, or use FORCE=1." >&2
		exit 1
	fi
	echo "!!! Vintage Story is running and FORCE=1 was set - expect a client crash."
fi

echo "=== Cleaning ==="
rm -rf "$SCRIPT_DIR/obj" "$SCRIPT_DIR/bin"

echo "=== Building ==="
dotnet build -c Debug "$SCRIPT_DIR/$MOD_NAME.csproj"

echo "=== Packaging ==="
STAGING="/tmp/${MOD_NAME}_stage"
rm -rf "$STAGING"
mkdir -p "$STAGING/assets"

cp "$SCRIPT_DIR/bin/Debug/$MOD_NAME.dll" "$STAGING/"
cp "$SCRIPT_DIR/modinfo.json" "$STAGING/"

# `if`, not `[ -f ] && cp`: under `set -e` a bare failing test aborts the whole script,
# so the one-liner form would make a missing modicon fatal. There is no modicon yet.
if [ -f "$SCRIPT_DIR/modicon.png" ]; then
	cp "$SCRIPT_DIR/modicon.png" "$STAGING/"
fi

# Only the mod's own asset domain, so working material under assets/ never inflates a
# release zip.
cp -r "$SCRIPT_DIR/assets/emberheart" "$STAGING/assets/"

# Zip is named with the mod version (EmberHeart_x.y.z.zip); older versions are removed
# so the game never loads two copies side by side.
VERSION=$(grep -oP '"version":\s*"\K[^"]+' "$SCRIPT_DIR/modinfo.json")
ZIP_NAME="${MOD_NAME}_${VERSION}.zip"

cd "$STAGING"
rm -f "$MODS_DIR/$MOD_NAME"*.zip
zip -r -q "$MODS_DIR/$ZIP_NAME" *

echo "=== Cleaning up ==="
rm -rf "$STAGING"

echo "=== Deployed to $MODS_DIR/$ZIP_NAME ==="
