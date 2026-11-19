#!/usr/bin/env bash
# Syntax and type check for the project's hand-written shaders, without Unity.
# Usage: Tools/shadercheck/run.sh        (needs glslangValidator: apt-get install glslang-tools)
#
# For each shader it joins the shared CGINCLUDE block with each pass's CGPROGRAM block, swaps Unity's
# includes for UnityStubs.hlsl, and compiles the vertex and fragment functions as HLSL.
# It proves the code parses and its types agree. It does not prove Unity compiles it: Unity's own
# macros, keywords and platform differences are not exercised. See Tools/README.md.
set -uo pipefail
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
OUT="$(mktemp -d)"
FAILED=0
command -v glslangValidator >/dev/null || { echo "glslangValidator not found (apt-get install glslang-tools)"; exit 2; }

for shader in "$REPO"/Assets/Resources/OuroborosShaders/*.shader "$REPO"/Assets/Ouroboros/Shaders/*.shader; do
  [ -f "$shader" ] || continue
  name="$(basename "$shader" .shader)"
  python3 - "$shader" "$OUT" "$HERE/UnityStubs.hlsl" <<'PY'
import re, sys, os
src, out, stubs = sys.argv[1], sys.argv[2], sys.argv[3]
text = open(src).read()
name = os.path.splitext(os.path.basename(src))[0]
shared = ''.join(re.findall(r'CGINCLUDE(.*?)ENDCG', text, re.S))
passes = re.findall(r'CGPROGRAM(.*?)ENDCG', text, re.S)
clean = lambda code: re.sub(r'^\s*#(include|pragma).*$', '', code, flags=re.M)
for i, body in enumerate(passes):
    vert = re.search(r'#pragma\s+vertex\s+(\w+)', body).group(1)
    frag = re.search(r'#pragma\s+fragment\s+(\w+)', body).group(1)
    path = os.path.join(out, f'{name}_{i}.hlsl')
    open(path, 'w').write(open(stubs).read() + '\n' + clean(shared) + '\n' + clean(body))
    print(path, vert, frag)
PY
done > "$OUT/list.txt"

while read -r file vert frag; do
  [ -n "$file" ] || continue
  label="$(basename "$file" .hlsl)"
  for stage in "vert $vert" "frag $frag"; do
    set -- $stage
    if log="$(glslangValidator -D -V --hlsl-enable-16bit-types -S "$1" -e "$2" -o "$OUT/out.spv" "$file" 2>&1)"; then
      echo "PASS  $label  $1 ($2)"
    else
      echo "FAIL  $label  $1 ($2)"
      echo "$log" | grep -v "^$file$" | sed 's/^/        /' | head -12
      FAILED=1
    fi
  done
done < "$OUT/list.txt"
rm -rf "$OUT"
[ "$FAILED" = 0 ] && echo "Shader check passed." || { echo "Shader check failed."; exit 1; }
