# Publishes a new version on GitHub. The app's updater picks it up from the latest release.
#   1. Bump <Version> in MicMute.csproj and commit your changes.
#   2. .\release.ps1 -Notes "Hva er nytt"      (or leave out -Notes to use GitHub's generated notes)
param([string]$Notes)
$root = $PSScriptRoot
Set-Location $root

# Runs a native command; stops on a non-zero exit code (git writes progress to stderr, which is fine).
function Run([string]$exe) {
    $ErrorActionPreference = 'Continue'
    & $exe @args 2>&1 | ForEach-Object { "$_" }
    if ($LASTEXITCODE -ne 0) { throw "$exe $args feilet ($LASTEXITCODE)" }
}

if (git status --porcelain) { throw "Det finnes endringer som ikke er committet. Commit dem først." }

[xml]$proj = Get-Content "$root\MicMute.csproj"
$version = ($proj.Project.PropertyGroup | ForEach-Object { $_.Version } | Where-Object { $_ } | Select-Object -First 1)
$tag = "v$version"
if (git tag --list $tag) { throw "$tag finnes allerede. Øk <Version> i MicMute.csproj." }

& "$root\build.ps1"
if (-not $?) { throw "Bygging feilet" }
$setup = "$root\installer\Output\MicMute-Setup-$version.exe"

Run git push
Run git tag $tag
Run git push origin $tag

if ($Notes) { Run gh release create $tag $setup --title "MicMute $version" --notes $Notes }
else { Run gh release create $tag $setup --title "MicMute $version" --generate-notes }
Write-Host "Publisert $tag" -ForegroundColor Green
