$srcDll = "src\DuskersCoopMod\bin\Release\net35\DuskersCoopMod.dll"
$destDll = "releases\BepInEx\plugins\DuskersCoopMod.dll"
Copy-Item $srcDll $destDll -Force

$zipPath = "releases\DuskersCoopMod_v1.1.0.zip"
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

$tempDir = "releases\temp_zip_pack"
if (Test-Path $tempDir) { Remove-Item $tempDir -Recurse -Force }
New-Item -ItemType Directory -Path $tempDir | Out-Null

Copy-Item "releases\BepInEx" $tempDir -Recurse
Copy-Item "releases\steam_appid.txt" $tempDir -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory((Resolve-Path $tempDir).Path, (Resolve-Path "releases").Path + "\DuskersCoopMod_v1.1.0.zip")
Remove-Item $tempDir -Recurse -Force

Write-Host "Updated zip created successfully:"
Get-Item $zipPath | Select-Object Name, Length, LastWriteTime
