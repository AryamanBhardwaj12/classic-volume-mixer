param([ValidatePattern('^(-[a-z0-9]+)*$')][string]$Suffix = '')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = Join-Path $projectRoot 'dist/ClassicVolumeMixer.exe'
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build the executable first.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($executable).FileVersion
$releaseRoot = Join-Path $projectRoot 'releases'
$staging = Join-Path $releaseRoot ('ClassicVolumeMixer-' + $version + '-win-x64' + $Suffix)
if (Test-Path -LiteralPath $staging) { throw 'This version is already packaged. Use a new version or archive the previous output before packaging again.' }
New-Item -ItemType Directory -Path (Join-Path $staging 'dist'),(Join-Path $staging 'Code'),(Join-Path $staging 'Docs') -Force | Out-Null
Copy-Item -LiteralPath $executable -Destination (Join-Path $staging 'dist')
foreach ($script in @('Install.ps1','Enable-Startup.ps1')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $script) -Destination (Join-Path $staging 'Code') }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $staging
foreach ($file in @('Install.cmd','START-HERE.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $projectRoot $file) -Destination $staging }
Copy-Item -LiteralPath (Join-Path $projectRoot 'Docs/quick-start.svg') -Destination (Join-Path $staging 'Docs')
Get-ChildItem -LiteralPath $staging -Recurse -File | Sort-Object FullName | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256
    $hash.Hash + '  ' + $_.FullName.Substring($staging.Length + 1)
} | Set-Content -LiteralPath (Join-Path $staging 'SHA256SUMS.txt') -Encoding ASCII
$archive = $staging + '.zip'
Compress-Archive -LiteralPath $staging -DestinationPath $archive
Get-FileHash -LiteralPath $archive -Algorithm SHA256
