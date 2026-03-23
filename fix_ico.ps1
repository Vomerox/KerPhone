Add-Type -AssemblyName System.Drawing

$destDir = "C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets"
$logoPng = "$destDir\kerphone_logo.png"
$icoPath = "$destDir\kerphone.ico"

$srcBmp = [System.Drawing.Bitmap]::FromFile($logoPng)

$sizes = @(16, 32, 48, 256)
$pngDataList = New-Object System.Collections.ArrayList

foreach ($sz in $sizes) {
    $tmp = New-Object System.Drawing.Bitmap($srcBmp, $sz, $sz)
    $ms = New-Object System.IO.MemoryStream
    $tmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    [void]$pngDataList.Add($ms.ToArray())
    $tmp.Dispose()
    $ms.Dispose()
}
$srcBmp.Dispose()

# Construire le fichier ICO manuellement
$numImages = $sizes.Count
$headerSize = 6
$dirEntrySize = 16
$dataOffset = $headerSize + ($numImages * $dirEntrySize)

$output = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter($output)

# ICO Header
$writer.Write([UInt16]0)            # Reserved
$writer.Write([UInt16]1)            # Type (ICO)
$writer.Write([UInt16]$numImages)   # Number of images

$currentOffset = $dataOffset
for ($i = 0; $i -lt $numImages; $i++) {
    $sz = $sizes[$i]
    $data = $pngDataList[$i]

    $writer.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))  # Width
    $writer.Write([byte]$(if ($sz -ge 256) { 0 } else { $sz }))  # Height
    $writer.Write([byte]0)        # Color palette
    $writer.Write([byte]0)        # Reserved
    $writer.Write([UInt16]1)      # Color planes
    $writer.Write([UInt16]32)     # Bits per pixel
    $writer.Write([UInt32]$data.Length)   # Image data size
    $writer.Write([UInt32]$currentOffset) # Offset
    $currentOffset += $data.Length
}

# Write image data
for ($i = 0; $i -lt $numImages; $i++) {
    $writer.Write([byte[]]$pngDataList[$i])
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $output.ToArray())
$writer.Dispose()
$output.Dispose()

Write-Host "ICO cree: $icoPath ($([System.IO.File]::ReadAllBytes($icoPath).Length) bytes)"
