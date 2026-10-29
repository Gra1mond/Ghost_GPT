#!/bin/bash
set -euo pipefail
arch="${1:?Pass arm64 or x86_64}"
case "$arch" in arm64|x86_64) ;; *) exit 2 ;; esac
root="$(cd "$(dirname "$0")/.." && pwd)"
cd "$root/macos"
swift build -c release --arch "$arch"
bin="$(swift build -c release --arch "$arch" --show-bin-path)"
app="$root/artifacts/macos-$arch/Ghost GPT.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"
cp "$bin/GhostGPT" "$app/Contents/MacOS/GhostGPT"
cp Info.plist "$app/Contents/Info.plist"
cp "$root/INTERVIEW.md" "$root/LICENSE" "$app/Contents/Resources/"
# Reuse the Windows artwork; no private user settings or keys are packaged.
python3 - "$root/src/GhostGPT/Assets/ghost.ico" "$root/artifacts/macos-$arch/icon.png" <<'PY'
import struct,sys
b=open(sys.argv[1],'rb').read(); n=struct.unpack_from('<H',b,4)[0]
entries=[struct.unpack_from('<BBBBHHII',b,6+16*i) for i in range(n)]
e=max(entries,key=lambda x:x[0] or 256)
open(sys.argv[2],'wb').write(b[e[7]:e[7]+e[6]])
PY
icons="$root/artifacts/macos-$arch/GhostGPT.iconset"
mkdir -p "$icons"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$root/artifacts/macos-$arch/icon.png" --out "$icons/icon_${size}x${size}.png" >/dev/null
  double=$((size * 2))
  sips -z "$double" "$double" "$root/artifacts/macos-$arch/icon.png" --out "$icons/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$icons" -o "$app/Contents/Resources/GhostGPT.icns"
codesign --force --deep --sign - "$app"
codesign --verify --deep --strict "$app"
file "$app/Contents/MacOS/GhostGPT"
# Verify the deployment target of the actual Mach-O binary, not just Info.plist.
python3 - "$app" <<'PY'
import plistlib,re,subprocess,sys
from pathlib import Path
app=Path(sys.argv[1])
info=plistlib.loads((app/'Contents/Info.plist').read_bytes())
assert info['LSMinimumSystemVersion']=='13.0', info
build=subprocess.check_output(['xcrun','vtool','-show-build',str(app/'Contents/MacOS/GhostGPT')],text=True)
versions=re.findall(r'minos\s+(\d+(?:\.\d+)*)',build)
assert versions and all(v in ('13.0','13.0.0') for v in versions), build
print('Verified minimum macOS 13.0 in Info.plist and Mach-O')
PY
# Smoke-check AppKit construction; no API calls or microphone capture.
"$app/Contents/MacOS/GhostGPT" --smoke-test --snapshot "$root/artifacts/macos-$arch/Chat.png"
GHOSTGPT_FORCE_MACOS13=1 "$app/Contents/MacOS/GhostGPT" --smoke-test --snapshot "$root/artifacts/macos-$arch/Chat-macOS13.png"
cp "$root/docs/MACOS.md" "$root/artifacts/macos-$arch/README-macOS.md"
mkdir -p "$root/artifacts/downloads"
ditto -c -k --sequesterRsrc --keepParent "$app" "$root/artifacts/downloads/GhostGPT-macos13-$arch.zip"
