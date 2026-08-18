param(
  [ValidateSet('win32', 'darwin')][string]$Platform = 'win32',
  [ValidateSet('x64', 'arm64')][string]$Architecture = 'x64'
)

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot 'release'
$destinationName = if ($Platform -eq 'win32') { 'MC Mod Migrator-win' } else { "MC Mod Migrator-mac-$Architecture" }
$destination = Join-Path $releaseRoot $destinationName
if (Test-Path -LiteralPath $destination) { throw "Release output already exists: $destination" }

$release = Invoke-RestMethod -Uri 'https://api.github.com/repos/electron/electron/releases/latest' -Headers @{ 'User-Agent' = 'MC-Mod-Migrator-Packager' }
$pattern = "^electron-v.+-$($Platform)-$($Architecture)\.zip$"
$asset = $release.assets | Where-Object { $_.name -match $pattern } | Select-Object -First 1
if (-not $asset) { throw "Electron archive not found for $Platform/$Architecture." }

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("mc-mod-migrator-electron-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $tempRoot | Out-Null
try {
  $archive = Join-Path $tempRoot $asset.name
  $ProgressPreference = 'SilentlyContinue'
  & curl.exe --fail --location --silent --show-error --output $archive $asset.browser_download_url
  if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $archive) -or (Get-Item -LiteralPath $archive).Length -lt 1MB) {
    throw 'Electron runtime download failed.'
  }
  Expand-Archive -LiteralPath $archive -DestinationPath $tempRoot
  $electronFolder = Get-ChildItem -LiteralPath $tempRoot -Directory | Where-Object Name -Like 'electron-*' | Select-Object -First 1
  if (-not $electronFolder) { throw 'Unexpected Electron archive layout.' }

  New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
  if ($Platform -eq 'win32') {
    Copy-Item -LiteralPath $electronFolder.FullName -Destination $destination -Recurse
    Rename-Item -LiteralPath (Join-Path $destination 'electron.exe') -NewName 'MC Mod Migrator.exe'
    $appRoot = Join-Path $destination 'resources\app'
  } else {
    $appBundle = Join-Path $electronFolder.FullName 'Electron.app'
    Copy-Item -LiteralPath $appBundle -Destination $destination -Recurse
    $appRoot = Join-Path $destination 'Contents\Resources\app'
  }
  New-Item -ItemType Directory -Path $appRoot | Out-Null
  Copy-Item -LiteralPath (Join-Path $projectRoot 'server.js') -Destination $appRoot
  Copy-Item -LiteralPath (Join-Path $projectRoot 'electron-main.js') -Destination $appRoot
  Copy-Item -LiteralPath (Join-Path $projectRoot 'package.json') -Destination $appRoot
  Copy-Item -LiteralPath (Join-Path $projectRoot 'web') -Destination $appRoot -Recurse
  Write-Host "Release created: $destination" -ForegroundColor Green
} finally {
  Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}
