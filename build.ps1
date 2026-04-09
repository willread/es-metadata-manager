# Framework-dependent single-file build (requires .NET 8 runtime)
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o dist

Write-Host ""
$size = (Get-Item dist\ESMetadataManager.exe).Length / 1MB
Write-Host ("Built: dist\ESMetadataManager.exe ({0:N1} MB)" -f $size) -ForegroundColor Green
