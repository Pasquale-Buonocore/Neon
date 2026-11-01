param([string]$ExecutableName = 'NeonMonitor.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
& (Join-Path $projectRoot 'build.ps1') -ExecutableName $ExecutableName
$artifactRoot = Join-Path $projectRoot 'artifacts'
New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null
foreach ($mode in @('self-test', 'ui-test', 'snapshot', 'gallery', 'menu-preview', 'icon-preview')) {
    $filename = switch ($mode) {
        'self-test' { 'verification.json' }
        'ui-test' { 'ui-verification.txt' }
        'snapshot' { 'neon-preview.png' }
        'gallery' { 'neon-themes.png' }
        'menu-preview' { 'neon-menu.png' }
        'icon-preview' { 'neon-icon.png' }
    }
    $resultPath = Join-Path $artifactRoot $filename
    $process = Start-Process -FilePath (Join-Path $projectRoot "bin\$ExecutableName") -ArgumentList "--$mode", ('"' + $resultPath + '"') -WindowStyle Hidden -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Verifica $mode fallita. Controlla bin\neon-error.log." }
    if (!(Test-Path -LiteralPath $resultPath)) { throw "Risultato mancante: $resultPath" }
    Write-Host "OK: $mode"
}
