#!/bin/zsh
set -euo pipefail

print "BetterVoice needs an Apple Development certificate so macOS keeps Microphone,"
print "Screen Recording, and Accessibility permissions across rebuilds."
print ""
print "1. Open Xcode → Settings (⌘,) → Accounts"
print "2. Click + → Add Apple ID (free)"
print "3. Select your account → Manage Certificates… → + → Apple Development"
print "4. Re-run: ./scripts/install-local.sh"
print ""

if security find-identity -v -p codesigning 2>/dev/null | rg -q "Apple Development:"; then
  print "Apple Development identity is already installed:"
  security find-identity -v -p codesigning | rg "Apple Development:"
  exit 0
fi

open -a Xcode
sleep 1
osascript <<'APPLESCRIPT'
tell application "Xcode" to activate
delay 0.4
tell application "System Events" to keystroke "," using command down
APPLESCRIPT

print "Opened Xcode Settings — add your Apple ID and create an Apple Development certificate."
