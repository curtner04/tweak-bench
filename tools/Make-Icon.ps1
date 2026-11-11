# Generates assets\TB.ico (PNG entries 16, 20, 24, 32, 40, 48, 64, 128, 256) and assets\TB.png (256):
# a rounded square with a Windows-blue gradient and three white tuning sliders.
# Run once (or when the artwork changes); the build and the installer use the committed files. Needs only System.Drawing.
param([string]$AssetsFolder = (Join-Path (Split-Path -Parent $PSScriptRoot) 'assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Path $AssetsFolder -Force | Out-Null

function New-RoundedRect([single]$X, [single]$Y, [single]$W, [single]$H, [single]$R) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = [single]($R * 2)
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc([single]($X + $W - $d), $Y, $d, $d, 270, 90)
    $path.AddArc([single]($X + $W - $d), [single]($Y + $H - $d), $d, $d, 0, 90)
    $path.AddArc($X, [single]($Y + $H - $d), $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

# Draws one icon at its own pixel size, so small sizes get thicker strokes instead of a blurry downscale.
function New-Artwork([int]$Size) {
    $bmp = New-Object System.Drawing.Bitmap $Size, $Size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.CompositingQuality = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $Size / 256.0
    $small = $Size -le 32

    # Tile: Windows-blue gradient, almost edge to edge.
    $inset = [single]([Math]::Max(0.5, 6 * $s))
    $tileSize = [single]($Size - 2 * $inset)
    $radius = [single]($tileSize * 0.22)
    $tile = New-RoundedRect $inset $inset $tileSize $tileSize $radius
    $from = [System.Drawing.Color]::FromArgb(255, 10, 100, 200)
    $to = [System.Drawing.Color]::FromArgb(255, 76, 168, 255)
    $rect = New-Object System.Drawing.RectangleF 0, 0, $Size, $Size
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, $from, $to, 45.0
    $g.FillPath($gradient, $tile)
    # Three sliders: faint track, bright filled part, round knob (with a blue ring at sizes where it fits).
    $white = [System.Drawing.Color]::White
    if ($small) {
        # 16-32 px: no rings, thin rows packed evenly, knobs sized to the row gap.
        $rows = @(@(0.27, 0.64), @(0.50, 0.30), @(0.73, 0.72))
        $trackWidth = [single]([Math]::Max(1.3, 0.075 * $Size))
        $knobRadius = [single]([Math]::Max(1.7, 0.105 * $Size))
        $ringWidth = [single]0
        $x0 = [single](0.22 * $Size); $x1 = [single](0.78 * $Size)
    } else {
        $rows = @(@(0.29, 0.66), @(0.50, 0.30), @(0.71, 0.74))
        $trackWidth = [single](17 * $s)
        $knobRadius = [single](19 * $s)
        $ringWidth = [single](6 * $s)
        $x0 = [single](58 * $s); $x1 = [single](198 * $s)
    }
    $ringColor = [System.Drawing.Color]::FromArgb(255, 14, 108, 214)
    foreach ($row in $rows) {
        $y = [single]($row[0] * $Size)
        $kx = [single]($x0 + ($x1 - $x0) * $row[1])
        $track = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(115, 255, 255, 255)), $trackWidth
        $track.StartCap = 'Round'; $track.EndCap = 'Round'
        $g.DrawLine($track, $x0, $y, $x1, $y)
        $fill = New-Object System.Drawing.Pen $white, $trackWidth
        $fill.StartCap = 'Round'; $fill.EndCap = 'Round'
        $g.DrawLine($fill, $x0, $y, $kx, $y)
        if ($ringWidth -gt 0) {
            $ring = New-Object System.Drawing.SolidBrush $ringColor
            $outer = [single]($knobRadius + $ringWidth)
            $g.FillEllipse($ring, [single]($kx - $outer), [single]($y - $outer), [single](2 * $outer), [single](2 * $outer))
            $ring.Dispose()
        }
        $g.FillEllipse([System.Drawing.Brushes]::White, [single]($kx - $knobRadius), [single]($y - $knobRadius), [single](2 * $knobRadius), [single](2 * $knobRadius))
        $track.Dispose(); $fill.Dispose()
    }    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = foreach ($size in $sizes) { $b = New-Artwork $size; $ms = New-Object IO.MemoryStream; $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $b.Dispose(); , $ms.ToArray() }
$out = New-Object IO.MemoryStream
$w = New-Object IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Flush()
[IO.File]::WriteAllBytes((Join-Path $AssetsFolder 'TB.ico'), $out.ToArray())
[IO.File]::WriteAllBytes((Join-Path $AssetsFolder 'TB.png'), $pngs[-1])
Write-Output "Wrote $(Join-Path $AssetsFolder 'TB.ico') and TB.png"
