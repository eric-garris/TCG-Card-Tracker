#!/usr/bin/env bash
# Builds the plugin and assembles a Thunderstore / r2modman compatible zip in mod/dist/.
# Usage: mod/scripts/package.sh [extra dotnet build args, e.g. -p:GameDir=...]
set -euo pipefail
here="$(cd "$(dirname "$0")/.." && pwd)"
version="$(sed -n 's/.*"version_number": *"\([^"]*\)".*/\1/p' "$here/package/manifest.json")"
out="$here/dist/stage"
rm -rf "$here/dist" && mkdir -p "$out/BepInEx/plugins/TCGCardTracker"
dotnet build "$here/src/TCGCardTracker/TCGCardTracker.csproj" -c Release -o "$here/dist/bin" "$@"
cp "$here/dist/bin/TCGCardTracker.dll" "$out/BepInEx/plugins/TCGCardTracker/"
cp "$here/package/manifest.json" "$here/package/icon.png" "$out/"
cp "$here/README.md" "$out/README.md"
(cd "$out" && zip -qr "$here/dist/TCG_Card_Tracker-$version.zip" .)
echo "Built $here/dist/TCG_Card_Tracker-$version.zip"
