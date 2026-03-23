# Telecharger les assets depuis kerbzh.bzh
$destDir = "C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets"

# Telecharger le favicon SVG
try {
    Invoke-WebRequest -Uri "https://kerbzh.bzh/assets/images/favicon.svg" -OutFile "$destDir\favicon.svg" -UseBasicParsing -TimeoutSec 10
    Write-Host "favicon.svg telecharge"
    Get-Content "$destDir\favicon.svg" | Select-Object -First 5
} catch { Write-Host "Erreur favicon: $_" }

# Telecharger l'image og
try {
    Invoke-WebRequest -Uri "https://kerbzh.bzh/assets/images/og-default.jpg" -OutFile "$destDir\og-default.jpg" -UseBasicParsing -TimeoutSec 10
    Write-Host "og-default.jpg telecharge: $((Get-Item "$destDir\og-default.jpg").Length / 1KB) KB"
} catch { Write-Host "Erreur og: $_" }

# Chercher plus de logos
try {
    $html = Invoke-WebRequest -Uri "https://kerbzh.bzh" -UseBasicParsing -TimeoutSec 10
    $allImages = [regex]::Matches($html.Content, '(?:src|href)="([^"]*\.(png|jpg|jpeg|svg|webp)(?:\?[^"]*)?)"', 'IgnoreCase')
    foreach ($m in $allImages) {
        $url = $m.Groups[1].Value
        if ($url -notmatch "gravatar|wp-emoji|tracking") {
            Write-Host "  Image: $url"
        }
    }
} catch { Write-Host "Erreur: $_" }
