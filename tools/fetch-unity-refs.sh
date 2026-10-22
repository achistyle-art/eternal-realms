#!/usr/bin/env bash
# Lädt die Referenzassemblys eines Unity-6-Editors (Linux-Archiv der Unity-CDN) – ohne Lizenz, ohne den Editor zu installieren.
# Entpackt wird im Datenstrom nur Editor/Data/Managed/UnityEngine (~70 MB); das 4-GB-Archiv wird nicht gespeichert.
#   tools/fetch-unity-refs.sh [Ziel]        (Standard: ~/.cache/eternal-realms/unity-<version>)
#   danach: dotnet build tools/UnityCheck -p:UnityManaged=<Ziel>/Editor/Data/Managed
set -euo pipefail
version="${ER_UNITY_VERSION:-6000.0.64f1}"
changeset="${ER_UNITY_CHANGESET:-5360b7cd7953}"
target="${1:-$HOME/.cache/eternal-realms/unity-$version}"
managed="$target/Editor/Data/Managed/UnityEngine"
if [ -f "$managed/UnityEngine.CoreModule.dll" ] && [ -f "$managed/UnityEditor.CoreModule.dll" ]; then
  echo "Bereits vorhanden: $managed"; exit 0
fi
mkdir -p "$target"
url="https://download.unity3d.com/download_unity/$changeset/LinuxEditorInstaller/Unity.tar.xz"
echo "Lade Unity $version (nur Referenzassemblys) von $url …"
curl -fsSL --retry 3 "$url" | tar -xJ -C "$target" --wildcards 'Editor/Data/Managed/UnityEngine/*.dll'
count="$(ls "$managed"/*.dll | wc -l)"
[ -f "$managed/UnityEditor.CoreModule.dll" ] || { echo "UnityEditor.CoreModule.dll fehlt im Archiv" >&2; exit 1; }
echo "Fertig: $count Assemblys in $managed"
