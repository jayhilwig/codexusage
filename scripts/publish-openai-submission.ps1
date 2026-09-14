param()

$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $pluginRoot 'src\CodexUsage.Desktop\CodexUsage.Desktop.csproj'
$manifestPath = Join-Path $pluginRoot '.codex-plugin\plugin.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$runtime = 'win-x64'
$workRoot = Join-Path $pluginRoot '.tmp\openai-submission'
$publishRoot = Join-Path $workRoot 'publish\win-x64'
$stagingRoot = Join-Path $workRoot 'plugin'
$artifactRoot = Join-Path $pluginRoot 'artifacts'
$zipPath = Join-Path $artifactRoot "CodexUsage-$($manifest.version)-openai-submission.zip"

if (Test-Path -LiteralPath $workRoot) {
    Remove-Item -LiteralPath $workRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

& dotnet restore $projectPath --runtime $runtime
if ($LASTEXITCODE -ne 0) { throw "Restore failed for $runtime." }

& dotnet publish $projectPath -c Release -r $runtime --self-contained true --no-restore `
    -p:PublishSingleFile=false -p:PublishTrimmed=false `
    -p:DebugType=None -p:DebugSymbols=false `
    -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

Get-ChildItem -LiteralPath $publishRoot -Filter '*.pdb' -Recurse -File |
    Remove-Item -Force

$localRuntimeRoot = Join-Path $pluginRoot 'bin\win-x64'
if (Test-Path -LiteralPath $localRuntimeRoot) {
    Remove-Item -LiteralPath $localRuntimeRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $localRuntimeRoot -Force | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Force |
    Copy-Item -Destination $localRuntimeRoot -Recurse -Force

foreach ($path in @('.codex-plugin', 'assets', 'skills')) {
    Copy-Item -LiteralPath (Join-Path $pluginRoot $path) -Destination $stagingRoot -Recurse -Force
}

$runtimeStaging = Join-Path $stagingRoot 'bin\win-x64'
New-Item -ItemType Directory -Path $runtimeStaging -Force | Out-Null
Get-ChildItem -LiteralPath $publishRoot -Force |
    Copy-Item -Destination $runtimeStaging -Recurse -Force

# Keep Windows-only public-directory wording isolated to the staged submission.
$stagedManifestPath = Join-Path $stagingRoot '.codex-plugin\plugin.json'
$stagedManifest = Get-Content -Raw -LiteralPath $stagedManifestPath | ConvertFrom-Json
$stagedManifest.description = 'Runs a local Windows x64 title-bar HUD for Codex usage and public reset status.'
$stagedManifest.interface.longDescription = 'Starts and manages a private native companion overlay on a local Windows x64 Codex desktop host. This public plugin release does not support macOS, Linux, web, mobile, cloud, or remote hosts.'
$stagedManifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $stagedManifestPath -Encoding utf8

$stagedSkillPath = Join-Path $stagingRoot 'skills\codex-usage-hud\SKILL.md'
@'
---
name: codex-usage-hud
description: Start, stop, or check the Windows x64 Codex title-bar usage companion. Use when the user asks to show, launch, hide, stop, restart, or check the status of the Codex usage overlay.
---

# Codex Usage

Manage the bundled Windows x64 native companion through the launcher resolved relative to this skill directory.

- Start, stop, or check status with `../../scripts/hud.ps1 -Action Start|Stop|Status` directly in the current PowerShell command host. Do not start a second PowerShell process or assume a fixed PowerShell 7 path. If direct invocation is unavailable, resolve the shell once in this order: the current PowerShell executable from `(Get-Process -Id $PID).Path`, `Get-Command pwsh.exe`, `Get-Command powershell.exe`, then `%SystemRoot%\System32\WindowsPowerShell\v1.0\powershell.exe` when present. Windows PowerShell 5.1 is sufficient; use the first resolved executable immediately.
- Restart by stopping, then starting.

Run Start and Restart only from a local Windows x64 Codex desktop host while the Codex desktop app is open. If the current environment is macOS, Linux, web, mobile, cloud, remote, or otherwise cannot launch a native Windows process on the user's interactive desktop, explain that this public plugin release currently supports Windows x64 local Codex desktop hosts only and do not run the launcher.

Starting a native desktop window may require host approval. Request the required GUI or external-process approval before the first Start or Restart attempt, and launch directly in the user's interactive desktop context. Do not first launch inside a restricted sandbox because the process may run without a visible overlay.

For Start, invoke the launcher immediately. Do not search for the launcher first, inspect the environment first, narrate intermediate startup steps, or run a separate Status command after a successful Start. Run Status or perform diagnostic discovery only if Start returns an error or its result is ambiguous.

For Restart, stop and then start immediately. Only run Status afterward if the restart result is ambiguous or reports an error.

The packaged install uses `../../bin/win-x64/CodexUsage.Desktop.exe`. Keep the response concise; do not narrate implementation details unless an error occurs.

Do not read, display, or transmit Codex credentials. Do not send local usage data to any third party. The overlay itself makes only the existing anonymous request to the public codex-resets.com API.

The plugin does not modify or inject into the Codex desktop app. Its visible interface remains a separate native overlay because plugin UI cannot occupy the operating system caption area.
'@ | Set-Content -LiteralPath $stagedSkillPath -Encoding utf8

$scriptStaging = Join-Path $stagingRoot 'scripts'
New-Item -ItemType Directory -Path $scriptStaging -Force | Out-Null
$sourceLauncher = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'hud.ps1')
$platformBlockPattern = '(?s)\$isWindowsHost\s*=.*?(?=\$outputDirectory\s*=)'
$windowsPlatformBlock = @'
$isWindowsHost = ($env:OS -eq 'Windows_NT') -or [bool]$IsWindows
$isWindowsX64 = $isWindowsHost -and (
    [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -eq
        [System.Runtime.InteropServices.Architecture]::X64)
if (-not $isWindowsX64) {
    throw 'This public Codex plugin release currently supports Windows x64 local Codex desktop hosts only.'
}
$runtimeId = 'win-x64'
$executableName = 'CodexUsage.Desktop.exe'

'@
$stagedLauncher = [regex]::Replace(
    $sourceLauncher,
    $platformBlockPattern,
    $windowsPlatformBlock,
    [Text.RegularExpressions.RegexOptions]::None,
    [TimeSpan]::FromSeconds(1))
if ($stagedLauncher -eq $sourceLauncher) {
    throw 'Could not isolate the platform-selection block in scripts/hud.ps1.'
}
$stagedLauncherPath = Join-Path $scriptStaging 'hud.ps1'
$stagedLauncher | Set-Content -LiteralPath $stagedLauncherPath -Encoding utf8

$docsStaging = Join-Path $stagingRoot 'docs'
New-Item -ItemType Directory -Path $docsStaging -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $pluginRoot 'docs\windows-submission.md') `
    -Destination $docsStaging -Force

[xml]$svg = Get-Content -Raw -LiteralPath (Join-Path $stagingRoot 'assets\codex-usage-icon.svg')
$svgWidth = 0.0
$svgHeight = 0.0
$widthIsNumeric = [double]::TryParse(
    $svg.svg.width,
    [Globalization.NumberStyles]::Float,
    [Globalization.CultureInfo]::InvariantCulture,
    [ref]$svgWidth)
$heightIsNumeric = [double]::TryParse(
    $svg.svg.height,
    [Globalization.NumberStyles]::Float,
    [Globalization.CultureInfo]::InvariantCulture,
    [ref]$svgHeight)
if (-not $widthIsNumeric -or -not $heightIsNumeric) {
    throw 'Composer SVG width and height must be numeric.'
}
$viewBox = @($svg.svg.viewBox -split '[,\s]+' | Where-Object { $_ })
$viewBoxNumbers = @()
foreach ($part in $viewBox) {
    $number = 0.0
    if (-not [double]::TryParse(
            $part,
            [Globalization.NumberStyles]::Float,
            [Globalization.CultureInfo]::InvariantCulture,
            [ref]$number)) {
        throw 'Composer SVG viewBox must contain four numeric values.'
    }
    $viewBoxNumbers += $number
}
$svgIsInvalid = $svgWidth -lt 48 `
    -or $svgHeight -lt 48 `
    -or $svgWidth -ne $svgHeight `
    -or $viewBoxNumbers.Count -ne 4 `
    -or $viewBoxNumbers[2] -le 0 `
    -or $viewBoxNumbers[3] -le 0 `
    -or $viewBoxNumbers[2] -ne $viewBoxNumbers[3]
if ($svgIsInvalid) {
    throw 'Composer SVG must be square, at least 48x48, and have a square numeric viewBox.'
}

$launcherText = Get-Content -Raw -LiteralPath $stagedLauncherPath
$launcherUsesExpectedPath = $launcherText.Contains("`$runtimeId = 'win-x64'") `
    -and $launcherText.Contains("`$executableName = 'CodexUsage.Desktop.exe'") `
    -and (Test-Path -LiteralPath (Join-Path $stagingRoot 'bin\win-x64\CodexUsage.Desktop.exe'))
if (-not $launcherUsesExpectedPath) {
    throw 'The staged launcher does not resolve bin/win-x64/CodexUsage.Desktop.exe.'
}

$skillFiles = @(Get-ChildItem -LiteralPath (Join-Path $stagingRoot 'skills') `
    -Recurse -Filter 'SKILL.md' -File)
if ($skillFiles.Count -lt 1) {
    throw 'OpenAI submission package must contain at least one skill.'
}
foreach ($skillFile in $skillFiles) {
    $skillText = Get-Content -Raw -LiteralPath $skillFile.FullName
    if ($skillText -notmatch '(?s)^---\s*\r?\nname:\s*[^\r\n]+\r?\ndescription:\s*[^\r\n]+\r?\n---') {
        throw "Invalid skill front matter: $($skillFile.FullName)"
    }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $stagingRoot,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $entries = @($archive.Entries)
    $entryNames = @($entries | ForEach-Object { $_.FullName.Replace('\', '/') })
    $requiredEntries = @(
        '.codex-plugin/plugin.json',
        'assets/codex-usage-icon.svg',
        'skills/codex-usage-hud/SKILL.md',
        'scripts/hud.ps1',
        'bin/win-x64/CodexUsage.Desktop.exe'
    )
    foreach ($entryName in $requiredEntries) {
        if ($entryName -notin $entryNames) {
            throw "OpenAI submission package is missing $entryName."
        }
    }

    $manifestCount = @($entryNames | Where-Object {
        $_.EndsWith('.codex-plugin/plugin.json', [StringComparison]::OrdinalIgnoreCase)
    }).Count
    $forbiddenEntries = @($entryNames | Where-Object {
        $_.StartsWith('.agents/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.Equals('marketplace.json', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.StartsWith('plugins/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.StartsWith('bin/osx-arm64/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.StartsWith('bin/osx-x64/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.StartsWith('src/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.StartsWith('tests/', [StringComparison]::OrdinalIgnoreCase) `
            -or $_.Contains('/obj/') `
            -or $_.EndsWith('.pdb', [StringComparison]::OrdinalIgnoreCase)
    })
    $allowedRoots = @('.codex-plugin', 'assets', 'skills', 'scripts', 'docs', 'bin')
    $unexpectedRoots = @($entryNames | ForEach-Object {
        ($_ -split '/', 2)[0]
    } | Where-Object { $_ -notin $allowedRoots } | Select-Object -Unique)
    if ($manifestCount -ne 1 -or $forbiddenEntries.Count -ne 0 -or $unexpectedRoots.Count -ne 0) {
        throw 'OpenAI submission package must contain one direct plugin root and no marketplace wrapper, macOS runtime, source, test, obj, or debug content.'
    }

    $extractedBytes = ($entries | Measure-Object Length -Sum).Sum
    $largest = $entries | Sort-Object Length -Descending | Select-Object -First 1
    $compressedBytes = (Get-Item -LiteralPath $zipPath).Length
    if ($compressedBytes -gt 100MB) {
        throw 'OpenAI submission ZIP exceeds 100 MiB compressed.'
    }
    if ($extractedBytes -gt 512MB) {
        throw 'OpenAI submission ZIP exceeds 512 MiB extracted.'
    }
    if ($entries.Count -gt 5000) {
        throw 'OpenAI submission ZIP exceeds 5,000 entries.'
    }
    if ($largest.Length -gt 100MB) {
        throw 'OpenAI submission ZIP contains a member larger than 100 MiB.'
    }

    Write-Output "OpenAI submission package: $zipPath"
    Write-Output ("Compressed MiB: {0:N2}" -f ($compressedBytes / 1MB))
    Write-Output ("Extracted MiB: {0:N2}" -f ($extractedBytes / 1MB))
    Write-Output "Archive entries: $($entries.Count)"
    Write-Output ("Largest member: {0} ({1:N2} MiB)" -f $largest.FullName, ($largest.Length / 1MB))
    Write-Output "Composer SVG: $svgWidth x $svgHeight; viewBox $($viewBoxNumbers -join ' ')"
    Write-Output 'Windows launcher: bin/win-x64/CodexUsage.Desktop.exe'
    Write-Output 'macOS runtimes: absent'
}
finally {
    $archive.Dispose()
}
