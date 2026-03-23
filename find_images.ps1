$paths = @(
    "C:\Users\Pierre\Downloads",
    "C:\Users\Pierre\Documents",
    "C:\Users\Pierre\Desktop",
    "C:\Users\Pierre\AppData\Local\Temp"
)
foreach ($p in $paths) {
    Get-ChildItem -Path $p -Recurse -Include "*.png","*.jpg","*.jpeg" -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -gt (Get-Date).AddHours(-3) -and $_.Length -gt 10KB } |
        Sort-Object LastWriteTime -Descending |
        ForEach-Object { Write-Host "$($_.FullName) - $([math]::Round($_.Length/1KB)) KB" }
}
