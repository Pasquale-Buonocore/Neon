param([string]$ExecutableName = 'NeonMonitor.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1') -ExecutableName $ExecutableName
$artifactRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
foreach ($mode in @('self-test', 'ui-test', 'desktop-test', 'settings-test', 'snapshot', 'gallery', 'menu-preview', 'icon-preview', 'settings-preview')) {
    $filename = switch ($mode) {
        'self-test' { 'verification.json' }
        'ui-test' { 'ui-verification.txt' }
        'desktop-test' { 'desktop-verification.txt' }
        'settings-test' { 'settings-verification.txt' }
        'snapshot' { 'neon-preview.png' }
        'gallery' { 'neon-themes.png' }
        'menu-preview' { 'neon-menu.png' }
        'icon-preview' { 'neon-icon.png' }
        'settings-preview' { 'neon-settings.png' }
    }
    $resultPath = Join-Path $artifactRoot $filename
    $process = Start-Process -FilePath (Join-Path $projectRoot "bin\$ExecutableName") -ArgumentList "--$mode", ('"' + $resultPath + '"') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Verification $mode failed. Check bin\neon-error.log." }
    if (!(Test-Path -LiteralPath $resultPath)) { throw "Missing result: $resultPath" }
    Write-Host "OK: $mode"
}
