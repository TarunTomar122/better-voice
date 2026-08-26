#!/bin/zsh
set -euo pipefail

root_dir=${0:a:h:h}
cd "$root_dir"

print -u2 "Stopping BetterVoice…"
pkill -x BetterVoice 2>/dev/null || true
sleep 1
if pgrep -x BetterVoice >/dev/null; then
  pkill -9 -x BetterVoice
  sleep 1
fi

# Prefer Apple Development; fall back to trusted local BetterVoice Developer cert
signing_identity=${BETTERVOICE_SIGNING_IDENTITY:-$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '
  /Apple Development:|Developer ID Application:/ { print $2; preferred=1; exit }
  /BetterVoice Developer/ { dev=$2 }
  END { if (!preferred && dev != "") print dev }
')}
if [[ -z "$signing_identity" ]]; then
  signing_identity=$(security find-identity -v -p codesigning 2>/dev/null | awk -F'"' '/BetterVoice Local Code Sign/ { print $2; exit }')
fi
if [[ -z "$signing_identity" ]]; then
  "$root_dir/scripts/setup-signing.sh"
  exit 1
fi

print -u2 "Using signing identity: $signing_identity"
print -u2 "Resetting privacy approvals for com.tarun.bettervoice…"
tccutil reset All com.tarun.bettervoice

BETTERVOICE_SIGNING_IDENTITY="$signing_identity" BETTERVOICE_SKIP_OPEN=1 "$root_dir/scripts/build-app.sh"

print -u2 "Installing to /Applications/BetterVoice.app…"
rm -rf /Applications/BetterVoice.app
ditto .build/BetterVoice.app /Applications/BetterVoice.app
xattr -cr /Applications/BetterVoice.app
codesign --verify --deep --strict /Applications/BetterVoice.app
codesign -dv /Applications/BetterVoice.app 2>&1 | grep -E 'Authority|TeamIdentifier|Signature'

print -u2 "Launching BetterVoice from /Applications…"
open -n /Applications/BetterVoice.app
sleep 2
open "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Microphone"
open "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_ScreenCapture"
open "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_Accessibility"
open "x-apple.systempreferences:com.apple.settings.PrivacySecurity.extension?Privacy_ListenEvent"

print ""
print "Installed: /Applications/BetterVoice.app"
print "IMPORTANT: Re-grant all four permissions for BetterVoice:"
print "  • Microphone"
print "  • Screen Recording"
print "  • Accessibility"
print "  • Input Monitoring (if shown)"
print "Then quit and reopen BetterVoice."
if [[ "$signing_identity" != *"Apple Development"* && "$signing_identity" != *"Developer ID"* ]]; then
  print ""
  print "For permanent fix: Xcode → Settings → Accounts → add Apple ID → create Apple Development cert,"
  print "then re-run ./scripts/install-local.sh"
fi
