# Builds MicMute (with .NET bundled) into dist\ and the installer into installer\Output\.
#   .\build.ps1               app + installer
#   .\build.ps1 -NoInstaller  app only
param([switch]$NoInstaller)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

[xml]$proj = Get-Content "$root\MicMute.csproj"
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
Write-Host "MicMute $version" -ForegroundColor Cyan

if (Test-Path "$root\dist") { Remove-Item "$root\dist" -Recurse -Force }
dotnet publish "$root\MicMute.csproj" -c Release -o "$root\dist" -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw "dotnet publish feilet" }

if ($NoInstaller) { return }

$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Fant ikke Inno Setup 6 (winget install JRSoftware.InnoSetup)" }

& $iscc "/DAppVersion=$version" "/Q" "$root\installer\MicMute.iss"
if ($LASTEXITCODE -ne 0) { throw "Inno Setup feilet" }

$setup = "$root\installer\Output\MicMute-Setup-$version.exe"
Write-Host "Installer: $setup ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" -ForegroundColor Green
