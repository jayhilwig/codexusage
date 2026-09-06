param(
    [Parameter(Mandatory)]
    [ValidateSet('Start', 'Stop', 'Status')]
    [string]$Action
)

$ErrorActionPreference = 'Stop'
$processName = 'CodexUsage.Desktop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$isWindowsHost = ($env:OS -eq 'Windows_NT') -or [bool]$IsWindows
$isMacHost = [bool]$IsMacOS

if ($isWindowsHost) {
    $runtimeId = 'win-x64'
    $executableName = 'CodexUsage.Desktop.exe'
}
elseif ($isMacHost) {
    $runtimeId = if ([System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture -eq 'Arm64') { 'osx-arm64' } else { 'osx-x64' }
    $executableName = 'CodexUsage.Desktop'
}
else {
    throw 'Codex Usage currently ships bundled helpers for Windows and macOS only.'
}

$outputDirectory = Join-Path $pluginRoot (Join-Path 'bin' $runtimeId)
$executablePath = Join-Path $outputDirectory $executableName

function Get-HudProcess {
    Get-Process -Name $processName -ErrorAction SilentlyContinue | Where-Object {
        try {
            $_.Path -eq $executablePath -or $_.Path.StartsWith($pluginRoot, [StringComparison]::OrdinalIgnoreCase)
        }
        catch {
            $false
        }
    }
}

if ($Action -eq 'Status') {
    $running = @(Get-HudProcess)
    if ($running.Count -eq 0) { Write-Output 'Codex Usage is not running.' }
    else { Write-Output "Codex Usage is running (PID $($running[0].Id))." }
    exit 0
}

if ($Action -eq 'Stop') {
    $running = @(Get-HudProcess)
    if ($running.Count -eq 0) {
        Write-Output 'Codex Usage is already stopped.'
        exit 0
    }

    $running | Stop-Process
    $running | Wait-Process -Timeout 5
    Write-Output 'Codex Usage stopped.'
    exit 0
}

$running = @(Get-HudProcess)
if ($running.Count -gt 0) {
    Write-Output "Codex Usage is already running (PID $($running[0].Id))."
    exit 0
}

if (-not (Test-Path -LiteralPath $executablePath)) {
    throw "Bundled Codex Usage helper was not found at $executablePath. Run scripts/publish-package.ps1 before installation."
}

$startParameters = @{ FilePath = $executablePath; WorkingDirectory = $outputDirectory; PassThru = $true }
if ($isWindowsHost) { $startParameters.WindowStyle = 'Hidden' }
$process = Start-Process @startParameters
Write-Output "Codex Usage started (PID $($process.Id))."
