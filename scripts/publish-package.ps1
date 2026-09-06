param()

$ErrorActionPreference = 'Stop'
$pluginRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $pluginRoot 'src\CodexUsage.Desktop\CodexUsage.Desktop.csproj'
$manifest = Get-Content -Raw (Join-Path $pluginRoot '.codex-plugin\plugin.json') | ConvertFrom-Json
$bundleVersion = $manifest.version.Split('+')[0]
$runtimes = @('win-x64', 'osx-arm64', 'osx-x64')

foreach ($runtime in $runtimes) {
    $output = Join-Path $pluginRoot (Join-Path 'bin' $runtime)
    if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
    $publishOutput = if ($runtime.StartsWith('osx-')) { Join-Path $output 'publish' } else { $output }
    & dotnet restore $projectPath --runtime $runtime
    if ($LASTEXITCODE -ne 0) { throw "Restore failed for $runtime." }
    & dotnet publish $projectPath -c Release -r $runtime --self-contained true --no-restore `
        -p:PublishSingleFile=false -p:PublishTrimmed=false `
        -p:DebugType=None -p:DebugSymbols=false -o $publishOutput
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }
    Get-ChildItem -LiteralPath $publishOutput -Filter '*.pdb' -Recurse -File |
        Remove-Item -Force

    if ($runtime.StartsWith('osx-')) {
        $appBundle = Join-Path $output 'Codex Usage.app'
        $contents = Join-Path $appBundle 'Contents'
        $macOs = Join-Path $contents 'MacOS'
        $resources = Join-Path $contents 'Resources'
        New-Item -ItemType Directory -Path $macOs -Force | Out-Null
        New-Item -ItemType Directory -Path $resources -Force | Out-Null
        Get-ChildItem -LiteralPath $publishOutput -Force | Move-Item -Destination $macOs -Force
        Remove-Item -LiteralPath $publishOutput -Force

        @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleDevelopmentRegion</key>
  <string>en</string>
  <key>CFBundleDisplayName</key>
  <string>Codex Usage</string>
  <key>CFBundleExecutable</key>
  <string>CodexUsage.Desktop</string>
  <key>CFBundleIdentifier</key>
  <string>com.jayhilwig.codexusage</string>
  <key>CFBundleInfoDictionaryVersion</key>
  <string>6.0</string>
  <key>CFBundleName</key>
  <string>Codex Usage</string>
  <key>CFBundlePackageType</key>
  <string>APPL</string>
  <key>CFBundleShortVersionString</key>
  <string>$bundleVersion</string>
  <key>CFBundleVersion</key>
  <string>$bundleVersion</string>
  <key>LSMinimumSystemVersion</key>
  <string>11.0</string>
  <key>LSUIElement</key>
  <true/>
  <key>NSHighResolutionCapable</key>
  <true/>
</dict>
</plist>
"@ | Set-Content -LiteralPath (Join-Path $contents 'Info.plist') -Encoding utf8
        New-Item -ItemType File -Path (Join-Path $resources '.keep') -Force | Out-Null
    }
}

$artifactRoot = Join-Path $pluginRoot 'artifacts'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$packageNames = @{
    'win-x64' = 'windows-x64'
    'osx-arm64' = 'macos-arm64'
    'osx-x64' = 'macos-x64'
}

foreach ($runtime in $runtimes) {
    $packageName = $packageNames[$runtime]
    $staging = Join-Path $artifactRoot "CodexUsage-$packageName"
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }

    $pluginStaging = Join-Path $staging 'plugins\codex-usage'
    New-Item -ItemType Directory -Path $pluginStaging -Force | Out-Null
    foreach ($path in @('.codex-plugin', 'assets', 'skills', 'scripts', 'docs')) {
        Copy-Item -LiteralPath (Join-Path $pluginRoot $path) -Destination $pluginStaging -Recurse -Force
    }

    $runtimeStaging = Join-Path $pluginStaging (Join-Path 'bin' $runtime)
    New-Item -ItemType Directory -Path (Split-Path -Parent $runtimeStaging) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $pluginRoot (Join-Path 'bin' $runtime)) `
        -Destination $runtimeStaging -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $pluginRoot '.agents') -Destination $staging -Recurse -Force

    $installerName = if ($runtime -eq 'win-x64') { 'install-windows.ps1' } else { 'install-macos.sh' }
    Copy-Item -LiteralPath (Join-Path $pluginRoot (Join-Path 'scripts' $installerName)) `
        -Destination (Join-Path $staging $installerName) -Force

    $marketplacePath = Join-Path $staging '.agents\plugins\marketplace.json'
    $pluginManifestPath = Join-Path $pluginStaging '.codex-plugin\plugin.json'
    if (-not (Test-Path -LiteralPath $marketplacePath)) {
        throw "Codex Usage package is missing $marketplacePath."
    }
    if (-not (Test-Path -LiteralPath $pluginManifestPath)) {
        throw "Codex Usage package is missing $pluginManifestPath."
    }

    $marketplace = Get-Content -Raw $marketplacePath | ConvertFrom-Json
    $entry = @($marketplace.plugins | Where-Object { $_.name -eq 'codex-usage' }) | Select-Object -First 1
    if ($null -eq $entry -or $entry.source.path -ne './plugins/codex-usage') {
        throw 'Codex Usage marketplace entry must point to ./plugins/codex-usage.'
    }

    $zipPath = Join-Path $artifactRoot ("CodexUsage-" + $manifest.version + "-$packageName.zip")
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $staging,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $expectedExecutable = if ($runtime -eq 'win-x64') {
        "plugins/codex-usage/bin/$runtime/CodexUsage.Desktop.exe"
    }
    else {
        "plugins/codex-usage/bin/$runtime/Codex Usage.app/Contents/MacOS/CodexUsage.Desktop"
    }
    $expectedEntries = @(
        '.agents/plugins/marketplace.json',
        $installerName,
        'plugins/codex-usage/.codex-plugin/plugin.json',
        $expectedExecutable
    )

    $archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        $archiveEntries = @($archive.Entries | ForEach-Object { $_.FullName -replace '\\', '/' })
        foreach ($entryName in $expectedEntries) {
            if ($entryName -notin $archiveEntries) {
                throw "Codex Usage package is missing $entryName."
            }
        }
    }
    finally {
        $archive.Dispose()
    }

    Write-Output "Codex Usage package: $zipPath"
}
