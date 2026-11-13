param([string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'ClassicVolumeMixer'), [switch]$NoLaunch)
$ErrorActionPreference = 'Stop'
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist/ClassicVolumeMixer.exe'
if (-not (Test-Path -LiteralPath $source)) { throw 'The release executable is missing. Extract the entire release ZIP, or build from source first.' }
$destination = Join-Path ([IO.Path]::GetFullPath($InstallDirectory)) 'ClassicVolumeMixer.exe'
$running = @(Get-Process ClassicVolumeMixer -ErrorAction SilentlyContinue | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId })
foreach ($app in $running) {
    if ($app.Path -eq $destination) { throw 'Exit Classic Volume Mixer from its tray menu before updating, then run setup again.' }
}
$startupScript = Join-Path $PSScriptRoot 'Enable-Startup.ps1'
& $startupScript -ExecutablePath $destination -ReplaceExistingTarget -ValidateOnly
New-Item -ItemType Directory -Path $InstallDirectory -Force | Out-Null
$installLock = [IO.File]::Open((Join-Path $InstallDirectory '.install.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::Write,[IO.FileShare]::None)
$staged = Join-Path $InstallDirectory ('ClassicVolumeMixer-' + [guid]::NewGuid().ToString('N') + '.new')
try {
    if (Test-Path -LiteralPath $destination) {
        if ([Diagnostics.FileVersionInfo]::GetVersionInfo($destination).ProductName -ne 'Classic Volume Mixer') {
            throw 'An unrecognized executable exists at the install destination. Leaving it untouched.'
        }
    }
    if ([IO.Path]::GetFullPath($source) -ne $destination) {
        Copy-Item -LiteralPath $source -Destination $staged
        if ((Get-FileHash -LiteralPath $staged).Hash -ne (Get-FileHash -LiteralPath $source).Hash) { throw 'Staged executable verification failed.' }
        if (Test-Path -LiteralPath $destination) { [IO.File]::Replace($staged,$destination,[NullString]::Value) }
        else { [IO.File]::Move($staged,$destination) }
    }
} finally {
    if (Test-Path -LiteralPath $staged) { Remove-Item -LiteralPath $staged }
    $installLock.Dispose()
}
& (Join-Path $PSScriptRoot 'Enable-Startup.ps1') -ExecutablePath $destination -ReplaceExistingTarget
Write-Output "Installed for this user: $destination"
if ($running.Count -gt 0) {
    Write-Output 'A portable copy is still running. Exit it from its tray menu, then launch the installed executable above. Startup is now configured for the installed copy.'
} elseif (-not $NoLaunch) {
    Start-Process -FilePath $destination
}
