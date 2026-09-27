#!/bin/sh
#
# Installs Jinxy AutoClicker.
#
#     curl -fsSL https://raw.githubusercontent.com/JinxyJoshua/JinxyMac-Beta/main/install.sh | sh
#
# ---------------------------------------------------------------------------
# Why this exists rather than "download it from the releases page"
# ---------------------------------------------------------------------------
#
# Downloading the app in a browser makes macOS refuse to open it, with:
#
#     "JinxyMac" is damaged and can't be opened. You should move it to the Trash.
#
# Nothing is damaged. Safari attaches a com.apple.quarantine flag to whatever it
# downloads, and Gatekeeper then checks the app against Apple's records. The app
# is ad-hoc signed — signed enough for macOS to run it, not signed by a $99
# Apple Developer account — and Gatekeeper reports a signature it cannot trace
# to a certificate as corruption rather than as what it is. Right-click and Open
# does not clear that particular message.
#
# The flag is set by the program doing the downloading. curl does not set it.
# So an app fetched this way is simply never quarantined, and opens normally.
#
# This also picks the right build. There are two, they are not interchangeable,
# and choosing wrongly is the other thing people get stuck on.

set -eu

if [ "$(uname -s)" != "Darwin" ]; then
    echo "This installs the macOS build, and this is not a Mac."
    exit 1
fi

# Two things decide the download: the chip, and whether macOS is new enough for
# the ordinary build. The ordinary builds are .NET 10, which cannot start below
# macOS 12; the "older" ones are .NET 8 and go back to 10.15. Picking wrongly
# here gives someone an app macOS simply refuses to open.
major=$(sw_vers -productVersion 2>/dev/null | cut -d. -f1)
[ -n "$major" ] || major=12

if [ "$major" -lt 12 ]; then
    older="_older"
    note=" (build for older macOS)"
else
    older=""
    note=""
fi

case "$(uname -m)" in
    arm64) asset="JinxyMac-mac${older}.tar.gz";        flavour="Apple silicon" ;;
    x86_64) asset="JinxyMac-mac${older}_intel.tar.gz"; flavour="Intel" ;;
    *) echo "Unknown architecture: $(uname -m)"; exit 1 ;;
esac

# Below 10.15 nothing here runs, and saying so beats downloading 50 MB to find
# out.
if [ "$major" -lt 11 ] && [ "$(sw_vers -productVersion | cut -d. -f2)" -lt 15 ]; then
    echo "This needs macOS 10.15 or later. You have $(sw_vers -productVersion)."
    exit 1
fi

url="https://github.com/JinxyJoshua/JinxyMac-Beta/releases/latest/download/$asset"
work=$(mktemp -d)
# Leaves nothing behind, including when the download fails half way.
trap 'rm -rf "$work"' EXIT INT TERM

echo "Jinxy AutoClicker — $flavour build$note"
echo "Downloading (about 50 MB)…"
curl -fL --progress-bar -o "$work/jinxy.tar.gz" "$url"

echo "Unpacking…"
tar xzf "$work/jinxy.tar.gz" -C "$work"

# Checked before anything installed is removed. A truncated download fails
# here, while the copy already on the machine is still whole.
[ -x "$work/JinxyMac.app/Contents/MacOS/JinxyMac" ] || {
    echo "The download did not contain a usable app. Nothing has been changed."
    exit 1
}

# Quit a running copy first. Replacing the files under a running app does not
# stop it, and `open` at the end would then just bring that OLD copy to the
# front - the update would look like it had done nothing.
if pgrep -x JinxyMac >/dev/null 2>&1; then
    echo "Closing Jinxy so it can be replaced…"
    osascript -e 'tell application id "com.jinxyjoshua.jinxymac" to quit' >/dev/null 2>&1 || true
    for _ in 1 2 3 4 5 6 7 8 9 10; do
        pgrep -x JinxyMac >/dev/null 2>&1 || break
        sleep 0.5
    done
    pkill -x JinxyMac >/dev/null 2>&1 || true
    sleep 0.5
fi

if [ -d /Applications/JinxyMac.app ]; then
    echo "Replacing the copy already in Applications…"
    rm -rf /Applications/JinxyMac.app
fi

# ditto rather than cp or mv: it is the tool that preserves a bundle's
# permissions and extended attributes, and one copied with anything else can
# arrive without its executable bit.
/usr/bin/ditto "$work/JinxyMac.app" /Applications/JinxyMac.app

# An Accessibility grant from an older copy points at an app that is gone, and
# macOS keeps showing it ticked while refusing the new one - the "still
# blocked" everyone ran into. Clearing Jinxy's own entry means the next grant
# is made against this copy. It touches nothing but Jinxy, and failing is fine:
# the instructions below still cover it.
tccutil reset Accessibility com.jinxyjoshua.jinxymac >/dev/null 2>&1 || true

echo
echo "Installed to /Applications/JinxyMac.app"
echo
echo "One thing left, and clicking does not work without it:"
echo
echo "  System Settings › Privacy & Security › Accessibility"
echo
echo "  Turn on JinxyMac, or add it with the plus button if it is not listed."
echo "  If an old JinxyMac entry is still there, remove it with the minus button"
echo "  and add this one. Then quit Jinxy and open it again."
echo
echo "Opening it now."
open /Applications/JinxyMac.app
