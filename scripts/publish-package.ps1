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
        -p:PublishSingleFile=false -p:PublishTrimmed=false -o $publishOutput
    if ($LASTEXITCODE -ne 0) { throw "Publish failed for $runtime." }

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
$staging = Join-Path $artifactRoot 'CodexUsage'
if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
New-Item -ItemType Directory -Path $staging -Force | Out-Null
$pluginStaging = Join-Path $staging 'plugins\codex-usage'
New-Item -ItemType Directory -Path $pluginStaging -Force | Out-Null
foreach ($path in @('.codex-plugin', 'assets', 'skills', 'scripts', 'bin', 'docs')) {
    Copy-Item -LiteralPath (Join-Path $pluginRoot $path) -Destination $pluginStaging -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $pluginRoot '.agents') -Destination $staging -Recurse -Force
Copy-Item -LiteralPath (Join-Path $pluginRoot 'scripts\install-macos.sh') -Destination (Join-Path $staging 'install-macos.sh') -Force

$marketplacePath = Join-Path $staging '.agents\plugins\marketplace.json'
$pluginManifestPath = Join-Path $pluginStaging '.codex-plugin\plugin.json'
if (-not (Test-Path -LiteralPath $marketplacePath)) { throw "USB package is missing $marketplacePath." }
if (-not (Test-Path -LiteralPath $pluginManifestPath)) { throw "USB package is missing $pluginManifestPath." }

$marketplace = Get-Content -Raw $marketplacePath | ConvertFrom-Json
$entry = @($marketplace.plugins | Where-Object { $_.name -eq 'codex-usage' }) | Select-Object -First 1
if ($null -eq $entry -or $entry.source.path -ne './plugins/codex-usage') {
    throw 'USB package marketplace entry must point to ./plugins/codex-usage.'
}

$zipPath = Join-Path $artifactRoot ("CodexUsage-" + $manifest.version + '-usb.zip')
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($staging, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    foreach ($entryName in @(
        '.agents/plugins/marketplace.json',
        'install-macos.sh',
        'plugins/codex-usage/.codex-plugin/plugin.json',
        'plugins/codex-usage/bin/osx-arm64/Codex Usage.app/Contents/Info.plist',
        'plugins/codex-usage/bin/osx-arm64/Codex Usage.app/Contents/MacOS/CodexUsage.Desktop',
        'plugins/codex-usage/bin/osx-x64/Codex Usage.app/Contents/Info.plist',
        'plugins/codex-usage/bin/osx-x64/Codex Usage.app/Contents/MacOS/CodexUsage.Desktop',
        'plugins/codex-usage/scripts/hud.sh')) {
        # .NET stores ZIP entry separators as backslashes on Windows; normalize
        # before comparing against the portable forward-slash paths above.
        if (-not ($archive.Entries | Where-Object { ($_.FullName -replace '\\', '/') -eq $entryName })) {
            throw "USB ZIP is missing $entryName."
        }
    }
}
finally {
    $archive.Dispose()
}

Write-Output "USB-test ZIP: $zipPath"
