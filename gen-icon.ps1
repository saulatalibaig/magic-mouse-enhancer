# Generates assets\app.ico (multi-size, PNG-compressed ICO) with System.Drawing.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$assetsDir = Join-Path $root "assets"
New-Item -ItemType Directory -Force -Path $assetsDir | Out-Null
$outPath = Join-Path $assetsDir "app.ico"

$bgColor = [System.Drawing.Color]::FromArgb(255, 30, 41, 59)     # slate-800
$fgColor = [System.Drawing.Color]::White
$accent  = [System.Drawing.Color]::FromArgb(255, 45, 212, 191)   # teal-400

function Draw-Icon([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap -ArgumentList @($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 64.0

    # rounded-square background
    $rect = New-Object System.Drawing.RectangleF -ArgumentList @((3 * $s), (3 * $s), (58 * $s), (58 * $s))
    $d = 15 * $s
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
    $path.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
    $path.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bgBrush = New-Object System.Drawing.SolidBrush -ArgumentList @($bgColor)
    $g.FillPath($bgBrush, $path)

    # mouse body
    $body = New-Object System.Drawing.RectangleF -ArgumentList @((15 * $s), (13 * $s), (34 * $s), (38 * $s))
    $fgBrush = New-Object System.Drawing.SolidBrush -ArgumentList @($fgColor)
    $g.FillEllipse($fgBrush, $body)

    # scroll line
    $pen = New-Object System.Drawing.Pen -ArgumentList @($accent, [Math]::Max(1, (2.5 * $s)))
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $g.DrawLine($pen, (32 * $s), (21 * $s), (32 * $s), (43 * $s))

    # up + down arrows
    $arrowBrush = New-Object System.Drawing.SolidBrush -ArgumentList @($accent)
    $up = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF -ArgumentList @((32 * $s), (16 * $s))),
        (New-Object System.Drawing.PointF -ArgumentList @((28 * $s), (22 * $s))),
        (New-Object System.Drawing.PointF -ArgumentList @((36 * $s), (22 * $s))))
    $down = [System.Drawing.PointF[]]@(
        (New-Object System.Drawing.PointF -ArgumentList @((32 * $s), (48 * $s))),
        (New-Object System.Drawing.PointF -ArgumentList @((28 * $s), (42 * $s))),
        (New-Object System.Drawing.PointF -ArgumentList @((36 * $s), (42 * $s))))
    $g.FillPolygon($arrowBrush, $up)
    $g.FillPolygon($arrowBrush, $down)

    $g.Dispose()
    return $bmp
}

$sizes = @(16, 24, 32, 48, 64)
$pngs = @()
foreach ($sz in $sizes) {
    $bmp = Draw-Icon $sz
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , ($ms.ToArray())
    $bmp.Dispose(); $ms.Dispose()
}

$msOut = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter -ArgumentList @($msOut)
$bw.Write([uint16]0)                 # reserved
$bw.Write([uint16]1)                 # type: icon
$bw.Write([uint16]$pngs.Count)
$offset = 6 + 16 * $pngs.Count
for ($i = 0; $i -lt $pngs.Count; $i++) {
    $w = $sizes[$i]
    $bw.Write([byte]$w)
    $bw.Write([byte]$w)
    $bw.Write([byte]0)               # palette
    $bw.Write([byte]0)               # reserved
    $bw.Write([uint16]1)             # planes
    $bw.Write([uint16]32)            # bpp
    $bw.Write([uint32]$pngs[$i].Length)
    $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $msOut.ToArray())
$bw.Dispose(); $msOut.Dispose()

Write-Output "Icon written to $outPath"
