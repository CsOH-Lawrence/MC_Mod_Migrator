#!/bin/bash
set -euo pipefail

# Run this script on macOS. It produces a drag-to-install DMG; players do not
# need Node.js. Apple Silicon uses arm64, Intel uses x64.
ARCH="${1:-}"
if [[ -z "$ARCH" ]]; then ARCH="arm64"; fi
case "$ARCH" in arm64|x64) ;; *) echo "Usage: $0 [arm64|x64]"; exit 64;; esac

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/dist"
APP_NAME="MC Mod Migrator"
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

release_json="$TMP/electron-release.json"
curl --fail --location --silent --show-error \
  -H 'User-Agent: MC-Mod-Migrator-Packager' \
  -o "$release_json" https://api.github.com/repos/electron/electron/releases/latest
archive_url="$(grep -oE 'https://[^" ]+electron-v[^" ]+-darwin-'"$ARCH"'\.zip' "$release_json" | head -n 1)"
if [[ -z "$archive_url" ]]; then
  echo "Could not find Electron runtime for darwin/$ARCH."
  exit 1
fi

archive="$TMP/electron.zip"
curl --fail --location --silent --show-error -o "$archive" "$archive_url"
unzip -q "$archive" -d "$TMP/runtime"
SOURCE_APP="$(find "$TMP/runtime" -maxdepth 2 -type d -name Electron.app -print -quit)"
if [[ -z "$SOURCE_APP" ]]; then
  echo "Electron.app was not present in the archive."
  exit 1
fi

APP="$TMP/$APP_NAME.app"
cp -R "$SOURCE_APP" "$APP"
mv "$APP/Contents/MacOS/Electron" "$APP/Contents/MacOS/$APP_NAME"
PLIST="$APP/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleDisplayName $APP_NAME" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleName $APP_NAME" "$PLIST"
/usr/libexec/PlistBuddy -c "Set :CFBundleExecutable $APP_NAME" "$PLIST"

APP_ROOT="$APP/Contents/Resources/app"
mkdir -p "$APP_ROOT"
cp "$ROOT/server.js" "$ROOT/electron-main.js" "$ROOT/package.json" "$ROOT/Background.jpg" "$APP_ROOT/"
cp -R "$ROOT/web" "$APP_ROOT/web"

STAGE="$TMP/dmg"
mkdir -p "$STAGE"
cp -R "$APP" "$STAGE/"
ln -s /Applications "$STAGE/Applications"
mkdir -p "$OUT"
DMG="$OUT/$APP_NAME-macos-$ARCH.dmg"
rm -f "$DMG"
hdiutil create -volname "$APP_NAME" -srcfolder "$STAGE" -ov -format UDZO "$DMG"
echo "Created: $DMG"
echo "Sign and notarize the app before public distribution to avoid Gatekeeper warnings."
