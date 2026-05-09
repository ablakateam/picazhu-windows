$root = Join-Path $env:LocalAppData "Picazhu"
$db = Join-Path $root "db\\catalog.db"
$thumbs = Join-Path $root "thumbs"

if (Test-Path $db) {
    Remove-Item -LiteralPath $db -Force
}

if (Test-Path $thumbs) {
    Remove-Item -LiteralPath $thumbs -Recurse -Force
}

Write-Host "PICAZHU catalog and thumbnail cache removed from $root"
