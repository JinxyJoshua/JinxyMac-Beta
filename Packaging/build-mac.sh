#!/usr/bin/env bash
#
# Builds JinxyMac.app for both Mac architectures and packs it for download.
#
# Runs on Windows. .NET cross-publishes a real Mach-O apphost for either macOS
# target, and an .app bundle is only a folder with a plist in it, so nothing
# here needs a Mac — with two exceptions that cannot be worked around from a PC:
#
#   Signing.     The bundle is unsigned. Gatekeeper will refuse the first
#                launch until the user clears the quarantine flag, which the
#                README explains. Removing that step needs an Apple Developer
#                account and a Mac to notarise from.
#
#   Universal.   A single fat binary needs Apple's lipo. Instead both builds go
#                in the bundle side by side and a launcher script picks one, so
#                the download still runs anywhere.
#
# The output is a .tar.gz rather than a .zip on purpose: a zip written on
# Windows carries no Unix permission bits, so every executable inside would
# arrive without +x and the app would not start.

set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
project=$(dirname "$here")
out="$project/dist"
app="$out/JinxyMac.app"

echo "==> Cleaning"
rm -rf "$out"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

for arch in arm64 x64; do
    echo "==> Publishing osx-$arch"

    dotnet publish "$project/JinxyMac.csproj" \
        --configuration Release \
        --runtime "osx-$arch" \
        --self-contained true \
        --output "$app/Contents/MacOS/$arch" \
        --nologo --verbosity quiet

    # The build drops a Windows app.manifest and .pdb files into the output.
    # Neither means anything on macOS and together they are a few megabytes of
    # a download that is already large.
    rm -f "$app/Contents/MacOS/$arch"/*.pdb
done

echo "==> Assembling the bundle"
cp "$here/Info.plist" "$app/Contents/Info.plist"
cp "$here/JinxyMac.icns" "$app/Contents/Resources/JinxyMac.icns"

# Written with unix line endings whatever this file was saved as. A CRLF in the
# shebang line makes the kernel look for an interpreter called "/bin/sh\r",
# which fails with a message naming a file that plainly exists.
tr -d '\r' < "$here/launcher.sh" > "$app/Contents/MacOS/JinxyMac"
chmod +x "$app/Contents/MacOS/JinxyMac"

cp "$here/README-mac.txt" "$out/README.txt"

echo "==> Packing"
cd "$out"

# Every file 0755. NTFS carries no execute bit, so without this the apphosts
# and dylibs would extract unrunnable — the failure being a bundle that opens
# and immediately closes with nothing in Console to explain it.
tar --mode='0755' --owner=0 --group=0 -czf JinxyMac-mac.tar.gz JinxyMac.app README.txt

echo
echo "Built $(du -sh "$app" | cut -f1) bundle -> $out/JinxyMac-mac.tar.gz ($(du -h JinxyMac-mac.tar.gz | cut -f1))"
