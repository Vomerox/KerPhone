$pjdir = "C:\Users\Pierre\Documents\SoftPhone\deps\pjproject"
$count = 0

Get-ChildItem -Path $pjdir -Filter "*.vcxproj" -Recurse | ForEach-Object {
    $content = [System.IO.File]::ReadAllText($_.FullName)
    $original = $content
    
    # Replace PlatformToolset
    $content = $content -replace '<PlatformToolset>v\d+</PlatformToolset>', '<PlatformToolset>v143</PlatformToolset>'
    
    # Handle WindowsTargetPlatformVersion
    if ($content -match '<WindowsTargetPlatformVersion>') {
        $content = $content -replace '<WindowsTargetPlatformVersion>[^<]+</WindowsTargetPlatformVersion>', '<WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>'
    } else {
        # Insert as new line after PlatformToolset
        $content = $content -replace '(<PlatformToolset>v143</PlatformToolset>)', "`$1`r`n    <WindowsTargetPlatformVersion>10.0</WindowsTargetPlatformVersion>"
    }
    
    if ($content -ne $original) {
        [System.IO.File]::WriteAllText($_.FullName, $content)
        $count++
    }
}

Write-Host "Retargeted $count .vcxproj files"
