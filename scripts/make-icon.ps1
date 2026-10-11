# Draws the Pagjoro icon (a page with a folded corner on a deep purple tile; no letters) and writes src\Pagjoro.App\Assets\Pagjoro.ico.
# Original artwork: made entirely from shapes drawn here. Run: powershell -ExecutionPolicy Bypass -File scripts\make-icon.ps1
Add-Type -AssemblyName System.Drawing
$out = Join-Path $PSScriptRoot '..\src\Pagjoro.App\Assets\Pagjoro.ico'
New-Item -ItemType Directory -Force (Split-Path $out) | Out-Null

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'; $g.Clear([System.Drawing.Color]::Transparent)
    $s = [single]$size
    # tile
    $r = $s * 0.22
    $tile = New-Object System.Drawing.Drawing2D.GraphicsPath
    $tile.AddArc(0, 0, 2 * $r, 2 * $r, 180, 90); $tile.AddArc($s - 2 * $r, 0, 2 * $r, 2 * $r, 270, 90)
    $tile.AddArc($s - 2 * $r, $s - 2 * $r, 2 * $r, 2 * $r, 0, 90); $tile.AddArc(0, $s - 2 * $r, 2 * $r, 2 * $r, 90, 90); $tile.CloseFigure()
    $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush ([System.Drawing.PointF]::new(0, 0)), ([System.Drawing.PointF]::new($s, $s)), ([System.Drawing.Color]::FromArgb(255, 59, 15, 118)), ([System.Drawing.Color]::FromArgb(255, 124, 58, 237))
    $g.FillPath($grad, $tile)
    # page with a folded top-right corner
    $x0 = $s * 0.27; $x1 = $s * 0.73; $y0 = $s * 0.17; $y1 = $s * 0.83; $fold = $s * 0.17
    $page = New-Object System.Drawing.Drawing2D.GraphicsPath
    $page.AddPolygon(@(
        [System.Drawing.PointF]::new($x0, $y0), [System.Drawing.PointF]::new($x1 - $fold, $y0), [System.Drawing.PointF]::new($x1, $y0 + $fold),
        [System.Drawing.PointF]::new($x1, $y1), [System.Drawing.PointF]::new($x0, $y1)))
    $g.FillPath([System.Drawing.Brushes]::White, $page)
    $foldBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 221, 214, 254))
    $g.FillPolygon($foldBrush, @([System.Drawing.PointF]::new($x1 - $fold, $y0), [System.Drawing.PointF]::new($x1 - $fold, $y0 + $fold), [System.Drawing.PointF]::new($x1, $y0 + $fold)))
    # lines of text
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 76, 29, 149)), ([single]($s * 0.045))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($row in 0.44, 0.56, 0.68) { $g.DrawLine($pen, $s * 0.36, $s * $row, $s * 0.64, $s * $row) }
    $amber = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255, 245, 158, 11)), ([single]($s * 0.045))
    $amber.StartCap = 'Round'; $amber.EndCap = 'Round'
    $g.DrawLine($amber, $s * 0.36, $s * 0.32, $s * 0.5, $s * 0.32)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$frames = foreach ($n in $sizes) {
    $bmp = Draw $n; $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , @($n, $ms.ToArray())
}
$w = New-Object System.IO.BinaryWriter ([System.IO.File]::Create($out))
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$frames.Count)
$offset = 6 + 16 * $frames.Count
foreach ($f in $frames) {
    $n = $f[0]; $bytes = $f[1]
    $w.Write([byte]$(if ($n -ge 256) { 0 } else { $n })); $w.Write([byte]$(if ($n -ge 256) { 0 } else { $n }))
    $w.Write([byte]0); $w.Write([byte]0); $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$bytes.Length); $w.Write([uint32]$offset); $offset += $bytes.Length
}
foreach ($f in $frames) { $w.Write($f[1]) }
$w.Close()
# a crisp 256-pixel picture for the window itself (an .ico lets WPF pick its smallest frame)
$big = Draw 256; $big.Save((Join-Path (Split-Path $out) 'Pagjoro.png'), [System.Drawing.Imaging.ImageFormat]::Png); $big.Dispose()
"wrote $out"
