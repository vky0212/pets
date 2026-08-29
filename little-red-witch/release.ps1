param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist')
)

$ErrorActionPreference = 'Stop'

& (Join-Path $PSScriptRoot 'build.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$releaseDirectory = Join-Path $OutputDirectory 'LittleRedWitch'
$archivePath = Join-Path $OutputDirectory 'LittleRedWitch-win-x64.zip'

if (Test-Path -LiteralPath $releaseDirectory) {
    Remove-Item -LiteralPath $releaseDirectory -Recurse -Force
}
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\LittleRedWitch.exe') -Destination $releaseDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'build\LittleRedWitch.Updater.exe') -Destination $releaseDirectory
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination $releaseDirectory

Compress-Archive -LiteralPath $releaseDirectory -DestinationPath $archivePath -CompressionLevel Optimal
Get-Item -LiteralPath $archivePath
