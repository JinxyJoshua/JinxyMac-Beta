# Builds JinxyMac.icns from nothing but System.Drawing.
#
# An .icns is a trivially simple container: the magic 'icns', a big-endian
# total length, then one chunk per size — a four-character type, a big-endian
# length that counts its own header, and PNG bytes. Modern macOS reads PNG in
# these chunks, which is what makes writing one from Windows possible at all.
#
# The alternative was shipping no icon and getting the blank generic one, or
# needing a Mac to run iconutil. Neither was worth it for forty lines.

param([string]$OutputPath = "JinxyMac.icns")

Add-Type -AssemblyName System.Drawing

function New-IconPng {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias

    # A rounded square in the app's crimson, with the click dot punched out of
    # it. Recognisable at 32px, which is the size that actually matters.
    $inset = [Math]::Round($Size * 0.06)
    $side = $Size - ($inset * 2)
    $radius = [Math]::Round($Size * 0.22)

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($inset, $inset, $d, $d, 180, 90)
    $path.AddArc($inset + $side - $d, $inset, $d, $d, 270, 90)
    $path.AddArc($inset + $side - $d, $inset + $side - $d, $d, $d, 0, 90)
    $path.AddArc($inset, $inset + $side - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    $crimson = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 255, 75, 82))
    $graphics.FillPath($crimson, $path)

    $dot = [Math]::Round($Size * 0.30)
    $centre = [Math]::Round(($Size - $dot) / 2)
    $white = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $graphics.FillEllipse($white, $centre, $centre, $dot, $dot)

    $stream = New-Object System.IO.MemoryStream
    $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)

    $graphics.Dispose()
    $bitmap.Dispose()
    $path.Dispose()

    return $stream.ToArray()
}

function ConvertTo-BigEndian {
    param([int]$Value)

    $bytes = [BitConverter]::GetBytes([int]$Value)
    if ([BitConverter]::IsLittleEndian) { [Array]::Reverse($bytes) }

    return $bytes
}

# Type codes macOS looks for, paired with the pixel size each one holds.
$variants = @(
    @{ Type = 'icp4'; Size = 16 },
    @{ Type = 'icp5'; Size = 32 },
    @{ Type = 'ic07'; Size = 128 },
    @{ Type = 'ic08'; Size = 256 },
    @{ Type = 'ic09'; Size = 512 },
    @{ Type = 'ic13'; Size = 256 },
    @{ Type = 'ic14'; Size = 512 }
)

$chunks = New-Object System.Collections.Generic.List[byte]

foreach ($variant in $variants) {
    $png = [byte[]](New-IconPng -Size $variant.Size)

    $chunks.AddRange([byte[]][System.Text.Encoding]::ASCII.GetBytes($variant.Type))
    $chunks.AddRange([byte[]](ConvertTo-BigEndian ($png.Length + 8)))
    $chunks.AddRange([byte[]]$png)
}

$icns = New-Object System.Collections.Generic.List[byte]
$icns.AddRange([byte[]][System.Text.Encoding]::ASCII.GetBytes("icns"))
$icns.AddRange([byte[]](ConvertTo-BigEndian ($chunks.Count + 8)))
$icns.AddRange([byte[]]$chunks.ToArray())

[System.IO.File]::WriteAllBytes($OutputPath, $icns.ToArray())

Write-Output "Wrote $OutputPath ($($icns.Count) bytes, $($variants.Count) sizes)"
