$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$outputDirectory = Join-Path $projectRoot 'dist'
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$executable = Join-Path $outputDirectory 'ClassicVolumeMixer.exe'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warnaserror+ /reference:System.Windows.Forms.dll /reference:System.Drawing.dll "/out:$executable" (Join-Path $PSScriptRoot 'ClassicVolumeMixer.cs')
if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
Write-Output $executable
