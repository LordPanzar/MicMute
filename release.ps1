# Publishes a new version on GitHub. The app's updater picks it up from the latest release.
#   1. Bump <Version> in MicMute.csproj and commit your changes.
#   2. .\release.ps1 -Notes "Hva er nytt"      (or leave out -Notes to use GitHub's generated notes)
param([string]$Notes)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

if (git status --porcelain) { throw "Det finnes endringer som ikke er committet. Commit dem først." }

[xml]$proj = Get-Content "$root\MicMute.csproj"
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$tag = "v$version"
if (git tag --list $tag) { throw "$tag finnes allerede. Øk <Version> i MicMute.csproj." }

& "$root\build.ps1"
$setup = "$root\installer\Output\MicMute-Setup-$version.exe"

git push
git tag $tag
git push origin $tag

if ($Notes) { gh release create $tag $setup --title "MicMute $version" --notes $Notes }
else { gh release create $tag $setup --title "MicMute $version" --generate-notes }
if ($LASTEXITCODE -ne 0) { throw "gh release create feilet" }
Write-Host "Publisert $tag" -ForegroundColor Green
