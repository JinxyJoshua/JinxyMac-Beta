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

case "$(uname -m)" in
    arm64) asset=JinxyMac-mac.tar.gz;       flavour="Apple silicon" ;;
    x86_64) asset=JinxyMac-mac_intel.tar.gz; flavour="Intel" ;;
    *) echo "Unknown architecture: $(uname -m)"; exit 1 ;;
esac

url="https://github.com/JinxyJoshua/JinxyMac-Beta/releases/latest/download/$asset"
work=$(mktemp -d)
# Leaves nothing behind, including when the download fails half way.
trap 'rm -rf "$work"' EXIT INT TERM

echo "Jinxy AutoClicker — $flavour build"
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

if [ -d /Applications/JinxyMac.app ]; then
    echo "Replacing the copy already in Applications…"
    rm -rf /Applications/JinxyMac.app
fi

# ditto rather than cp or mv: it is the tool that preserves a bundle's
# permissions and extended attributes, and one copied with anything else can
# arrive without its executable bit.
/usr/bin/ditto "$work/JinxyMac.app" /Applications/JinxyMac.app

echo
echo "Installed to /Applications/JinxyMac.app"
echo
echo "One thing left, and clicking does not work without it:"
echo
echo "  System Settings › Privacy & Security › Accessibility"
echo
echo "  If a JinxyMac entry is already there from an older copy, remove it with"
echo "  the minus button first — it points at an app that is gone and macOS will"
echo "  not match it. Then add this one with the plus button."
echo
echo "Opening it now."
open /Applications/JinxyMac.app
