Add-Type -AssemblyName System.Drawing
$src = New-Object System.Drawing.Bitmap('Z:\Downloads\ChatGPT Image 21 mars 2026, 13_53_19.png')
$w = $src.Width
$h = $src.Height
$wm = $w - 1
$hm = $h - 1

Write-Host "Corners:"
$pts = @(
    @(0,0), @(5,5), @(10,10), @(50,50),
    @($wm,0), @($wm,$hm), @(0,$hm),
    @([int]($w/2), 0), @([int]($w/2), $hm),
    @(0, [int]($h/2)), @($wm, [int]($h/2)),
    @(100,5), @(5,100), @(200,5), @(5,200)
)

foreach ($pt in $pts) {
    $c = $src.GetPixel($pt[0], $pt[1])
    Write-Host "  ($($pt[0]),$($pt[1])): R=$($c.R) G=$($c.G) B=$($c.B) A=$($c.A)"
}

$src.Dispose()
