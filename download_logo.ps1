# Tenter de telecharger le logo depuis kerbzh.fr
$urls = @(
    "https://kerbzh.fr/wp-content/uploads/2024/01/logo-kerbzh.png",
    "https://kerbzh.fr/favicon.ico",
    "https://kerbzh.fr/wp-content/uploads/logo.png",
    "https://kerbzh.fr/logo.png",
    "https://kerbzh.fr/wp-content/themes/kerbzh/images/logo.png"
)

# D'abord, essayer de scraper la page d'accueil pour trouver le logo
try {
    $html = Invoke-WebRequest -Uri "https://kerbzh.fr" -UseBasicParsing -TimeoutSec 10
    Write-Host "Page chargee, recherche du logo..."

    # Chercher les images dans le HTML
    $imgMatches = [regex]::Matches($html.Content, 'src="([^"]*\.(png|jpg|jpeg|svg|ico)[^"]*)"', 'IgnoreCase')
    foreach ($m in $imgMatches) {
        $imgUrl = $m.Groups[1].Value
        if ($imgUrl -match "logo|icon|brand|kerbzh" -and $imgUrl -notmatch "gravatar|wp-emoji") {
            Write-Host "  Logo trouve: $imgUrl"
        }
    }

    # Aussi chercher les favicons et og:image
    $metaMatches = [regex]::Matches($html.Content, 'content="([^"]*\.(png|jpg|ico)[^"]*)"', 'IgnoreCase')
    foreach ($m in $metaMatches) {
        Write-Host "  Meta image: $($m.Groups[1].Value)"
    }

    $linkMatches = [regex]::Matches($html.Content, 'href="([^"]*icon[^"]*)"', 'IgnoreCase')
    foreach ($m in $linkMatches) {
        Write-Host "  Icon link: $($m.Groups[1].Value)"
    }
} catch {
    Write-Host "Erreur acces site: $_"
}
