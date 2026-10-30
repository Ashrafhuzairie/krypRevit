# Close Revit. Default: install the packaged DLL; -BuildFromSource requires Visual Studio Build Tools.
param(
    [string]$RevitInstallDir = 'C:\Program Files\Autodesk\Revit 2024',
    [switch]$BuildFromSource
)
$ErrorActionPreference = 'Stop'
if (Get-Process Revit -ErrorAction SilentlyContinue) { throw 'Close Revit before installing.' }
$dll = Join-Path $PSScriptRoot 'krypRevit.Addin\bin\Release\krypRevit.Addin.dll'
if ($BuildFromSource) {
    if (!(Test-Path (Join-Path $RevitInstallDir 'RevitAPI.dll'))) { throw 'Revit 2024 API assemblies were not found.' }
    $project = Join-Path $PSScriptRoot 'krypRevit.Addin\krypRevit.Addin.csproj'
    & msbuild $project /t:Rebuild /p:Configuration=Release "/p:RevitInstallDir=$RevitInstallDir"
    if ($LASTEXITCODE -ne 0) { throw 'Build failed. Existing installation was not replaced.' }
} else {
    if (!(Test-Path $dll)) { throw 'Packaged DLL missing. Extract the complete release ZIP or use -BuildFromSource.' }
    $checksumFile = Join-Path $PSScriptRoot 'checksums.sha256'
    if (!(Test-Path $checksumFile)) { throw 'Release checksum file is missing.' }
    $entry = @(Get-Content $checksumFile | Where-Object { $_ -match '  krypRevit.Addin/bin/Release/krypRevit.Addin.dll$' })
    if ($entry.Count -ne 1) { throw 'DLL checksum entry is missing or ambiguous.' }
    $expected = ($entry[0] -split '  ', 2)[0]
    if ((Get-FileHash $dll -Algorithm SHA256).Hash -ne $expected) { throw 'Packaged DLL checksum mismatch. Installation stopped.' }
}
$target = Join-Path $env:APPDATA 'Autodesk\Revit\Addins\2024'
New-Item -ItemType Directory -Force -Path $target | Out-Null
# Old manifests share an AddInId and/or port. Do not silently remove existing installations.
$old = @(Get-ChildItem $target -Filter *.addin | Where-Object { $_.Name -ne 'krypRevit.addin' -and (Get-Content $_.FullName -Raw) -match '7D2E4C1A-5B3F-4E8A-9A6C-1F0B2D3E4A57|RevitSem|AequAI' })
$machine = Join-Path $env:ProgramData 'Autodesk\Revit\Addins\2024'
if (Test-Path $machine) { $old += @(Get-ChildItem $machine -Filter *.addin | Where-Object { (Get-Content $_.FullName -Raw) -match '7D2E4C1A-5B3F-4E8A-9A6C-1F0B2D3E4A57|RevitSem|AequAI|krypRevit' }) }
if ($old.Count -gt 0) { throw ('Back up and move these old manifests outside Revit Addins first: ' + ($old.FullName -join ', ')) }
$folder = Join-Path $target 'krypRevit'
if (Test-Path $folder) {
    $backup = Join-Path $env:TEMP ('krypRevit-backup-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    Copy-Item $folder $backup -Recurse
    if (Test-Path (Join-Path $target 'krypRevit.addin')) { Copy-Item (Join-Path $target 'krypRevit.addin') $backup }
    Write-Host "Previous installation backed up to $backup"
}
New-Item -ItemType Directory -Force -Path $folder | Out-Null
Copy-Item $dll $folder -Force
$pdb = [IO.Path]::ChangeExtension($dll, '.pdb')
if (Test-Path $pdb) { Copy-Item $pdb $folder -Force }
[xml]$manifest = Get-Content (Join-Path $PSScriptRoot 'krypRevit.addin')
$manifest.RevitAddIns.AddIn.Assembly = Join-Path $folder 'krypRevit.Addin.dll'
$manifest.Save((Join-Path $target 'krypRevit.addin'))
Write-Host 'Installed. Start Revit 2024 and check krypRevit > MCP Status.'
