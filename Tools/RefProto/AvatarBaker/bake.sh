#!/bin/sh
# Private prototype only: renders the reference avatar model (from the git-ignored asset export) into character
# frames for the RefProto skin. Runs in its own throwaway Unity project, so the main Editor can stay open.
# Usage (project root):  sh Tools/RefProto/AvatarBaker/bake.sh   then   powershell -File Tools/RefProto/import.ps1
set -e
ROOT="$(cd "$(dirname "$0")/../../.." && pwd)"
WORK="$ROOT/Builds/AvatarBaker"            # git-ignored
UNITY="${UNITY:-/c/Program Files/Unity/Hub/Editor/6000.6.4f1/Editor/Unity.exe}"
mkdir -p "$WORK/Assets/Model"
cp -r "$ROOT/Tools/RefProto/AvatarBaker/Assets/." "$WORK/Assets/"
cp "$ROOT/Builds/Exports/GameAssets-0.9.143a/configuration/model.xml" "$WORK/Assets/Model/avatar.dae"
"$UNITY" -batchmode -projectPath "$WORK" -executeMethod Bake.Setup -quit -logFile "$WORK/setup.log"
"$UNITY" -batchmode -projectPath "$WORK" -executeMethod BakeAll.Run -quit -logFile "$WORK/bake.log"
echo "Frames in Builds/Exports/Characters, map in Tools/RefProto/art_map_characters.json"
