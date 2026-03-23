Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Image]::FromFile('Z:\Downloads\ChatGPT Image 21 mars 2026, 13_53_19.png')
$sizes = @(16, 32, 48, 256)
$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($ms)

# ICO header
$writer.Write([int16]0)
$writer.Write([int16]1)
$writer.Write([int16]$sizes.Count)

# Prepare each image as PNG
$imageData = @()
foreach ($sz in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap($sz, $sz)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.DrawImage($src, 0, 0, $sz, $sz)
    $g.Dispose()

    $pngMs = New-Object System.IO.MemoryStream
    $bmp.Save($pngMs, [System.Drawing.Imaging.ImageFormat]::Png)
    $imageData += ,$pngMs.ToArray()
    $pngMs.Dispose()
    $bmp.Dispose()
}

# Calculate offset
$offset = 6 + ($sizes.Count * 16)

# Write directory entries
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $sz = $sizes[$i]
    $data = $imageData[$i]
    $w = if ($sz -eq 256) { 0 } else { $sz }
    $h = if ($sz -eq 256) { 0 } else { $sz }
    $writer.Write([byte]$w)
    $writer.Write([byte]$h)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([int16]1)
    $writer.Write([int16]32)
    $writer.Write([int32]$data.Length)
    $writer.Write([int32]$offset)
    $offset += $data.Length
}

# Write image data
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $writer.Write($imageData[$i])
}

$writer.Flush()
[System.IO.File]::WriteAllBytes('C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets\edrphone.ico', $ms.ToArray())
$ms.Dispose()
$src.Dispose()
Write-Host "ICO cree avec succes"
