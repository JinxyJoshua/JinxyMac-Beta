#!/usr/bin/env bash
#
# Builds JinxyMac.app for both Mac architectures and packs each for download.
#
# Runs on Windows. .NET cross-publishes a real Mach-O apphost for either macOS
# target, and an .app bundle is only a folder with a plist in it, so nothing
# here needs a Mac — with one exception that cannot be worked around from a PC:
#
#   Signing.     The bundles are unsigned. Gatekeeper will refuse the first
#                launch until the user clears the quarantine flag, which the
#                README explains. Removing that step needs an Apple Developer
#                account and a Mac to notarise from.
#
# ---------------------------------------------------------------------------
# Why two bundles instead of one with a launcher script
# ---------------------------------------------------------------------------
#
# Until 1.2.2 this built ONE bundle whose CFBundleExecutable was a /bin/sh
# script that read `uname -m` and exec'd Contents/MacOS/<arch>/JinxyMac. It
# packaged both architectures in one download and it was completely broken.
#
# macOS decides which bundle a process belongs to by the executable being at
# Contents/MacOS/<CFBundleExecutable>. After the script exec'd, the running
# binary was a directory deeper, so the process had NO bundle: NSBundle found
# no Info.plist, the menu bar read "Avalonia Application" instead of
# CFBundleName, and — the part that mattered — Accessibility could never be
# granted. The permission is given to JinxyMac.app; the process asking for it
# was not recognised as JinxyMac.app, so AXIsProcessTrusted() returned false
# no matter how many times the box was ticked. Clicking had never worked on
# any Mac, for anyone, since 1.0.7.
#
# The obvious repair is one universal binary, and it is not available here:
# only 15 files in the publish output are Mach-O and could be combined, while
# 174 more are managed assemblies that Microsoft ships ReadyToRun-compiled per
# architecture. Those cannot be merged. So it is one bundle per architecture,
# each with a real apphost sitting exactly where macOS expects to find it.

set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
project=$(dirname "$here")
out="$project/dist"

echo "==> Cleaning"
rm -rf "$out"
mkdir -p "$out"

# The version is written once, in Updater.cs, and stamped into the plist here.
# Kept in both places by hand they drift, and the first symptom of that is an
# updater cheerfully offering the version already installed, for ever.
version=$(grep -oE 'Version = "[0-9.]+"' "$project/Core/Updater.cs" | grep -oE '[0-9.]+')
[ -n "$version" ] || { echo "Could not read the version out of Updater.cs"; exit 1; }
echo "    version $version"

# arch | folder | tarball
build() {
    rid=$1
    dir=$2
    tarball=$3
    label=$4

    app="$out/$dir/JinxyMac.app"

    echo "==> Publishing $rid ($label)"
    mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

    # Straight into Contents/MacOS, so the apphost lands at
    # Contents/MacOS/JinxyMac — which is what CFBundleExecutable names and
    # what macOS matches the process against. Nothing between them.
    dotnet publish "$project/JinxyMac.csproj" \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        --output "$app/Contents/MacOS" \
        --nologo --verbosity quiet

    # The build drops a Windows app.manifest and .pdb files into the output.
    # Neither means anything on macOS and together they are a few megabytes of
    # a download that is already large.
    rm -f "$app/Contents/MacOS"/*.pdb

    [ -f "$app/Contents/MacOS/JinxyMac" ] \
        || { echo "No apphost at Contents/MacOS/JinxyMac — the bundle would be inert"; exit 1; }

    sed "s|<string>1\.0\.0</string>|<string>$version</string>|g" \
        "$here/Info.plist" > "$app/Contents/Info.plist"
    cp "$here/JinxyMac.icns" "$app/Contents/Resources/JinxyMac.icns"

    cp "$here/README-mac.txt" "$out/$dir/README.txt"

    echo "==> Packing $tarball"
    (
        cd "$out/$dir"
        # Every file 0755. NTFS carries no execute bit, so without this the
        # apphost and dylibs would extract unrunnable — the failure being a
        # bundle that opens and immediately closes with nothing in Console to
        # explain it.
        #
        # .tar.gz and not .zip for the same reason: a zip written on Windows
        # carries no Unix permission bits at all.
        tar --mode='0755' --owner=0 --group=0 -czf "$out/$tarball" JinxyMac.app README.txt
    )

    echo "    $(du -sh "$app" | cut -f1) bundle -> $out/$tarball ($(du -h "$out/$tarball" | cut -f1))"
}

build osx-arm64 applesilicon JinxyMac-mac.tar.gz "Apple silicon"
build osx-x64   intel        JinxyMac-mac_intel.tar.gz "Intel"

echo
echo "Done. Upload JinxyMac-mac.tar.gz FIRST — see Core/Updater.cs on why order matters."
