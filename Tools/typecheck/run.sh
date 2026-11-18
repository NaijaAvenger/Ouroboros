#!/usr/bin/env bash
# Offline C# type-check for the Unity project. No Unity install or licence needed.
# Usage: Tools/typecheck/run.sh            (needs: dotnet SDK 8+, git, curl, unzip)
#   INPUTSYSTEM_SRC=/path/to/InputSystem   use an existing checkout instead of cloning
# See README.md in this folder for what this does and does not prove.
set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
CACHE="$HERE/.cache"
mkdir -p "$CACHE"

# --- Input System source, at the version pinned in Packages/manifest.json ---------------------------------
IS_VERSION="$(sed -n 's/.*"com.unity.inputsystem"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' "$REPO/Packages/manifest.json")"
IS_SRC="${INPUTSYSTEM_SRC:-$CACHE/inputsystem-$IS_VERSION}"
if [ ! -d "$IS_SRC/Packages/com.unity.inputsystem/InputSystem" ]; then
  echo "== Fetching Input System $IS_VERSION"
  GIT_LFS_SKIP_SMUDGE=1 git -c advice.detachedHead=false clone -q --depth 1 --branch "$IS_VERSION" \
    https://github.com/Unity-Technologies/InputSystem "$IS_SRC" || { echo "FAILED to fetch Input System $IS_VERSION"; exit 2; }
fi

# --- Editor-side reference assemblies (only individual DLLs are used) ------------------------------------
fetch_nupkg() { # id version
  local dir="$CACHE/$1"
  [ -d "$dir/lib" ] && return 0
  echo "== Fetching $1 $2"
  mkdir -p "$dir"
  curl -fsSL -o "$dir/pkg.zip" "https://api.nuget.org/v3-flatcontainer/$1/$2/$1.$2.nupkg" \
    && unzip -oq "$dir/pkg.zip" 'lib/*' -d "$dir" && rm -f "$dir/pkg.zip" \
    || { echo "FAILED to fetch $1 $2"; exit 2; }
}
fetch_nupkg unity3d.sdk 2021.1.14.1            # UnityEditor.dll
fetch_nupkg unity3d.unityengine.ui 2020.3.21   # UnityEngine.UI.dll (EventSystem)

# --- Build ----------------------------------------------------------------------------------------------
FAILED=0
build() { # label project [msbuild args...]
  local label="$1" proj="$2"; shift 2
  local log; log="$(mktemp)"
  if dotnet build "$HERE/$proj" -nologo -v q "$@" >"$log" 2>&1; then
    local warnings; warnings="$(grep -E 'warning CS' "$log" | sed "s#$REPO/##; s# \[.*##" | sort -u)"
    echo "PASS  $label"
    [ -n "$warnings" ] && echo "$warnings" | sed 's/^/        /'
  else
    echo "FAIL  $label"
    grep -E 'error' "$log" | sed "s#$REPO/##; s# \[.*##" | sort -u | sed 's/^/        /'
    FAILED=1
  fi
  rm -f "$log"
}

# Third-party assemblies: built once, quietly. A failure here is a tooling problem, not a game-code problem.
build "third-party: Unity.InputSystem"  Unity.InputSystem.csproj -p:InputSystemSrc="$IS_SRC"
build "third-party: Photon.Realtime"    Photon.Realtime.csproj
build "third-party: Fusion.Unity"       Fusion.Unity.csproj
[ "$FAILED" = 0 ] || { echo "Third-party reference build failed; cannot check game code."; exit 2; }

build "game runtime  (player, Input System + legacy)" Assembly-CSharp.csproj -p:Variant=both
build "game runtime  (editor)"                        Assembly-CSharp.csproj -p:Variant=editor
build "game runtime  (Input System only)"             Assembly-CSharp.csproj -p:Variant=inputsys
build "game runtime  (legacy input only)"             Assembly-CSharp.csproj -p:Variant=legacy
build "game editor scripts"                           Assembly-CSharp-Editor.csproj

if [ "$FAILED" = 0 ]; then echo "Type-check passed."; else echo "Type-check FAILED."; fi
exit "$FAILED"
