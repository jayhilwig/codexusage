param()

$ErrorActionPreference = 'Stop'
$packageRoot = $PSScriptRoot
$marketplacePath = Join-Path $packageRoot '.agents\plugins\marketplace.json'
$pluginManifestPath = Join-Path $packageRoot 'plugins\codex-usage\.codex-plugin\plugin.json'

$hasMarketplace = Test-Path -LiteralPath $marketplacePath
$hasPluginManifest = Test-Path -LiteralPath $pluginManifestPath
if (-not $hasMarketplace -or -not $hasPluginManifest) {
    throw 'This folder is not a complete Codex Usage package.'
}

$codexCommand = Get-Command codex -ErrorAction SilentlyContinue
$codexExecutable = if ($codexCommand) { $codexCommand.Source } else { $null }
if (-not $codexExecutable) {
    $binRoot = Join-Path $env:LOCALAPPDATA 'OpenAI\Codex\bin'
    if (Test-Path -LiteralPath $binRoot) {
        $codexExecutable = Get-ChildItem -LiteralPath $binRoot -Filter codex.exe -Recurse -File |
            Sort-Object LastWriteTimeUtc -Descending |
            Select-Object -First 1 -ExpandProperty FullName
    }
}

if (-not $codexExecutable) {
    throw 'Codex CLI was not found on PATH or in the installed Codex application.'
}

$previousErrorActionPreference = $ErrorActionPreference
try {
    $ErrorActionPreference = 'SilentlyContinue'
    & $codexExecutable plugin remove codex-usage@codex-usage-local *> $null
    & $codexExecutable plugin marketplace remove codex-usage-local *> $null
}
finally {
    $ErrorActionPreference = $previousErrorActionPreference
}

& $codexExecutable plugin marketplace add $packageRoot
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to add the Codex Usage marketplace.'
}

& $codexExecutable plugin add codex-usage@codex-usage-local
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to install Codex Usage.'
}

Write-Output 'Codex Usage installed. Restart Codex, open a new task, and use: @Codex Usage Start!'
