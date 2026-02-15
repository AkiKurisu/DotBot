# DotBot Wrapper Script (PowerShell)
# This script calls DotBot.exe from the same directory

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$exePath = Join-Path $scriptDir "DotBot.exe"

if (Test-Path $exePath) {
    & $exePath $args
    exit $LASTEXITCODE
} else {
    Write-Host "Error: DotBot.exe not found in $scriptDir" -ForegroundColor Red
    exit 1
}
