#!/bin/zsh
set -euo pipefail

swift build -c release
app_dir=".build/BetterVoice.app"
if pgrep -x BetterVoice >/dev/null; then
  osascript -e 'tell application id "com.tarun.bettervoice" to quit'
  for _ in {1..50}; do
    pgrep -x BetterVoice >/dev/null || break
    sleep 0.1
  done
  if pgrep -x BetterVoice >/dev/null; then
    print -u2 "BetterVoice did not quit cleanly; close it and run the build again."
    exit 1
  fi
fi
rm -rf ".build/BetterVoice.app"
mkdir -p "$app_dir/Contents/MacOS"
cp ".build/release/BetterVoice" "$app_dir/Contents/MacOS/BetterVoice"
cp "Info.plist" "$app_dir/Contents/Info.plist"
mkdir -p "$app_dir/Contents/Resources"
cp "Resources/BetterVoice.icns" "$app_dir/Contents/Resources/BetterVoice.icns"
signing_identity=${BETTERVOICE_SIGNING_IDENTITY:-$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '
  /Apple Development:|Developer ID Application:/ { print $2; exit }
  /BetterVoice Developer/ { dev=$2 }
  END { if (dev != "") print dev }
')}
if [[ -z "$signing_identity" ]]; then
  signing_identity=$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '/BetterVoice Local Code Sign/ { print $2; exit }')
fi
if [[ -z "$signing_identity" ]]; then
  print -u2 "BetterVoice needs a code-signing identity."
  print -u2 "Run: ./scripts/setup-signing.sh  (adds a free Apple Development certificate)"
  print -u2 "Falling back to ad-hoc signature; paste and screenshots will not work until properly signed."
  signing_identity="-"
elif [[ "$signing_identity" != *"Apple Development"* && "$signing_identity" != *"Developer ID"* ]]; then
  print -u2 "Using local signing identity: $signing_identity"
  print -u2 "For reliable permissions, run ./scripts/setup-signing.sh and add an Apple Development certificate."
fi
entitlements_file="BetterVoice.entitlements"
sign_args=(--force --deep --sign "$signing_identity" --options runtime --timestamp=none)
if [[ -f "$entitlements_file" ]]; then
  sign_args+=(--entitlements "$entitlements_file")
fi
codesign "${sign_args[@]}" "$app_dir"
if [[ "${BETTERVOICE_SKIP_OPEN:-0}" != "1" ]]; then
  open -n "$app_dir"
fi
