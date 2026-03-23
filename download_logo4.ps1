$destDir = "C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets"

# Telecharger le favicon SVG
Invoke-WebRequest -Uri "https://kerbzh.fr/assets/images/favicon.svg" -OutFile "$destDir\favicon_kerbzh.svg" -UseBasicParsing -TimeoutSec 10
Write-Host "Favicon SVG telecharge"
Get-Content "$destDir\favicon_kerbzh.svg"

Write-Host "`n--- Recherche d'autres images ---"

# Scraper la page
$html = (Invoke-WebRequest -Uri "https://kerbzh.fr" -UseBasicParsing -TimeoutSec 15).Content

# Trouver toutes les URLs d'images
$allRefs = [regex]::Matches($html, '(?:src|href)="(/assets/[^"]*)"', 'IgnoreCase')
foreach ($m in $allRefs) {
    $path = $m.Groups[1].Value
    Write-Host "  Asset: $path"
}

# Essayer de telecharger le logo principal
$logoUrls = @(
    "https://kerbzh.fr/assets/images/logo.png",
    "https://kerbzh.fr/assets/images/logo.svg",
    "https://kerbzh.fr/assets/images/logo-kerbzh.png",
    "https://kerbzh.fr/assets/images/kerbzh.png",
    "https://kerbzh.fr/assets/img/logo.png",
    "https://kerbzh.fr/assets/logo.png",
    "https://kerbzh.fr/images/logo.png"
)

foreach ($url in $logoUrls) {
    try {
        $tmp = "$destDir\test_logo.tmp"
        Invoke-WebRequest -Uri $url -OutFile $tmp -UseBasicParsing -TimeoutSec 5
        $size = (Get-Item $tmp).Length
        if ($size -gt 100) {
            Write-Host "FOUND: $url ($size bytes)"
            Copy-Item $tmp "$destDir\kerbzh_logo_downloaded.png"
        }
        Remove-Item $tmp -ErrorAction SilentlyContinue
    } catch {}
}
