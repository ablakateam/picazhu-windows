$thumbs = Join-Path $env:LocalAppData "Picazhu\\thumbs"

if (Test-Path $thumbs) {
    Remove-Item -LiteralPath $thumbs -Recurse -Force
    New-Item -ItemType Directory -Path $thumbs | Out-Null
}

Write-Host "PICAZHU thumbnail cache cleared from $thumbs"
