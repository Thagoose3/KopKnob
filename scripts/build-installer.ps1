# Builds artifacts\KnobSetup-<version>.exe: one file that installs Knob on any Windows 10/11 PC.
#   1. publish Knob self-contained (no .NET needed on the target PC), x64 only
#   2. build the uninstaller (.NET Framework 4.8, built into Windows) and add it as "Uninstall Knob.exe"
#   3. zip that folder and embed it in the installer (also .NET Framework 4.8)
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

[xml]$props = Get-Content (Join-Path $root 'Directory.Build.props')
$version = $props.Project.PropertyGroup.Version
$work = Join-Path $root 'artifacts\installer'
$app = Join-Path $work 'app'
$zip = Join-Path $work 'payload.zip'
$out = Join-Path $root "artifacts\KnobSetup-$version.exe"

Write-Host "Knob $version" -ForegroundColor Cyan
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $app | Out-Null

Write-Host '1/3 Publishing Knob (self-contained)...'
# No compression inside the exe: the installer's zip already compresses, and an uncompressed bundle is
# memory-mapped instead of unpacked into private memory at every start (measured: 111 MB → ~31 MB private).
dotnet publish src/Knob.App -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false -p:DebugType=none -o $app --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Publishing Knob failed' }
Remove-Item (Join-Path $app 'x86') -Recurse -Force -ErrorAction SilentlyContinue   # 64-bit only

Write-Host '2/3 Building the uninstaller...'
dotnet build src/Knob.Uninstall -c Release --nologo -v q
if ($LASTEXITCODE -ne 0) { throw 'Building the uninstaller failed' }
Copy-Item (Join-Path $root 'src\Knob.Uninstall\bin\Release\net48\KnobUninstall.exe') (Join-Path $app 'Uninstall Knob.exe')

Write-Host '3/3 Building the installer...'
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($app, $zip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
dotnet build src/Knob.Setup -c Release --nologo -v q "-p:PayloadZip=$zip"
if ($LASTEXITCODE -ne 0) { throw 'Building the installer failed' }
Copy-Item (Join-Path $root 'src\Knob.Setup\bin\Release\net48\KnobSetup.exe') $out -Force

$mb = [math]::Round((Get-Item $out).Length / 1MB, 1)
Write-Host "Done: $out ($mb MB)" -ForegroundColor Green
