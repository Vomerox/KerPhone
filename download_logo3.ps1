# Le site redirige vers kerbzh.bzh mais le DNS ne resout pas
# Essayer kerbzh.fr directement
$destDir = "C:\Users\Pierre\Documents\SoftPhone\src\SoftPhone\Assets"

try {
    $html = Invoke-WebRequest -Uri "https://kerbzh.fr" -UseBasicParsing -TimeoutSec 15 -MaximumRedirection 0 -ErrorAction SilentlyContinue
    Write-Host "Status: $($html.StatusCode)"
    Write-Host "Headers Location: $($html.Headers.Location)"
} catch {
    $resp = $_.Exception.Response
    if ($resp) {
        Write-Host "Status: $($resp.StatusCode)"
        Write-Host "Location: $($resp.Headers.Location)"
    } else {
        Write-Host "Erreur: $_"
    }
}

# Essayer aussi directement des URLs connues
$tryUrls = @(
    "https://kerbzh.fr/assets/images/favicon.svg",
    "https://kerbzh.fr/favicon.ico",
    "https://kerbzh.fr/favicon.png"
)

foreach ($url in $tryUrls) {
    try {
        Invoke-WebRequest -Uri $url -OutFile "$destDir\test_dl.tmp" -UseBasicParsing -TimeoutSec 5
        $size = (Get-Item "$destDir\test_dl.tmp").Length
        Write-Host "OK: $url ($size bytes)"
        Remove-Item "$destDir\test_dl.tmp" -ErrorAction SilentlyContinue
    } catch {
        Write-Host "FAIL: $url"
    }
}
