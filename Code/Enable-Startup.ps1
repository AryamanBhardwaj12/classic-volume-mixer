param([string]$ExecutablePath, [switch]$ReplaceExistingTarget, [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$executable = if ($ExecutablePath) { [IO.Path]::GetFullPath($ExecutablePath) } else { Join-Path $projectRoot 'dist/ClassicVolumeMixer.exe' }
if (-not $ValidateOnly -and -not (Test-Path -LiteralPath $executable)) { throw 'Build ClassicVolumeMixer.exe first.' }
$startupFolder = [Environment]::GetFolderPath('Startup')
if (-not $startupFolder) { throw 'Windows did not provide the current user Startup folder.' }
$shortcutPath = Join-Path $startupFolder 'Classic Volume Mixer.lnk'
$shell = New-Object -ComObject WScript.Shell
try {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    if ((Test-Path -LiteralPath $shortcutPath) -and $shortcut.TargetPath -ne $executable) {
        if (-not $ReplaceExistingTarget -or [IO.Path]::GetFileName($shortcut.TargetPath) -ne 'ClassicVolumeMixer.exe' -or $shortcut.Description -ne 'Classic Volume Mixer tray icon') {
            throw 'A shortcut with this name targets another location. Only a verified app shortcut can be repaired with -ReplaceExistingTarget.'
        }
    }
    if ($ValidateOnly) { return }
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = Split-Path $executable -Parent
    $shortcut.Description = 'Classic Volume Mixer tray icon'
    $shortcut.Save()
    Write-Output $shortcutPath
} finally {
    if ($null -ne $shortcut) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shortcut) }
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)
}
