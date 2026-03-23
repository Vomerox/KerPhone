Add-Type -AssemblyName System.Drawing

$src = New-Object System.Drawing.Bitmap('Z:\Downloads\ChatGPT Image 21 mars 2026, 13_53_19.png')
$w = $src.Width
$h = $src.Height
$wm = $w - 1
$hm = $h - 1
$wh = [int]($w / 2)
$hh = [int]($h / 2)

Write-Host "Image: ${w}x${h}"

$result = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

for ($y = 0; $y -lt $h; $y++) {
    for ($x = 0; $x -lt $w; $x++) {
        $result.SetPixel($x, $y, $src.GetPixel($x, $y))
    }
}

function IsBackground($color) {
    if ($color.A -lt 10) { return $true }
    # White/light gray background (corners outside rounded rect)
    if ($color.R -gt 230 -and $color.G -gt 230 -and $color.B -gt 230) {
        return $true
    }
    return $false
}

$queue = New-Object System.Collections.Generic.Queue[System.Drawing.Point]
$visited = New-Object 'bool[,]' $w, $h

$starts = @()
$starts += New-Object System.Drawing.Point(0, 0)
$starts += New-Object System.Drawing.Point($wm, 0)
$starts += New-Object System.Drawing.Point(0, $hm)
$starts += New-Object System.Drawing.Point($wm, $hm)

foreach ($sp in $starts) {
    if (-not $visited[$sp.X, $sp.Y]) {
        $queue.Enqueue($sp)
        $visited[$sp.X, $sp.Y] = $true
    }
}

$transparent = [System.Drawing.Color]::FromArgb(0, 0, 0, 0)
$count = 0

while ($queue.Count -gt 0) {
    $p = $queue.Dequeue()
    $c = $src.GetPixel($p.X, $p.Y)

    if (IsBackground $c) {
        $result.SetPixel($p.X, $p.Y, $transparent)
        $count++

        $px = $p.X
        $py = $p.Y
        $pxm = $px - 1
        $pxp = $px + 1
        $pym = $py - 1
        $pyp = $py + 1

        if ($pxm -ge 0 -and -not $visited[$pxm, $py]) {
            $visited[$pxm, $py] = $true
            $queue.Enqueue((New-Object System.Drawing.Point($pxm, $py)))
        }
        if ($pxp -lt $w -and -not $visited[$pxp, $py]) {
            $visited[$pxp, $py] = $true
            $queue.Enqueue((New-Object System.Drawing.Point($pxp, $py)))
        }
        if ($pym -ge 0 -and -not $visited[$px, $pym]) {
            $visited[$px, $pym] = $true
            $queue.Enqueue((New-Object System.Drawing.Point($px, $pym)))
        }
        if ($pyp -lt $h -and -not $visited[$px, $pyp]) {
            $visited[$px, $pyp] = $true
            $queue.Enqueue((New-Object System.Drawing.Point($px, $pyp)))
        }
    }
}

Write-Host "Pixels transparents: $count"

$outPath = 'C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets\edrphone_logo.png'
$result.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)

# Also regenerate ICO from the transparent version
$sizes = @(16, 32, 48, 256)
$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ms)
$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$sizes.Count)

$imageData = @()
foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($result, 0, 0, $sz, $sz)
    $g.Dispose()

    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData += ,$pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
}

$offset = 6 + ($sizes.Count * 16)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $imageData[$i]
    $ww = if ($sz -eq 256) { 0 } else { $sz }
    $hh2 = if ($sz -eq 256) { 0 } else { $sz }
    $writer.Write([byte]$ww)
    $writer.Write([byte]$hh2)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int32]$data.Length)
    $writer.Write([int32]$offset)
    $offset += $data.Length
}
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $writer.Write($imageData[$i])
}
$writer.Flush()
[System.IO.File]::WriteAllBytes('C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets\edrphone.ico', $ms.ToArray())
$ms.Dispose()

$result.Dispose()
$src.Dispose()
Write-Host "Logo et ICO transparents crees avec succes"
