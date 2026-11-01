param([string]$ExecutableName = 'NeonMonitor.exe')
$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (!(Test-Path -LiteralPath $compiler)) {
    throw 'Serve .NET Framework 4.x per compilare NEON (incluso in Windows 10/11).'
}
$output = Join-Path $projectRoot 'bin'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$references = @(
    'System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Management.dll',
    'System.Windows.Forms.dll', 'System.Drawing.dll', 'System.Web.Extensions.dll',
    'WPF\WindowsBase.dll', 'WPF\PresentationCore.dll', 'WPF\PresentationFramework.dll'
) | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName }
& (Join-Path $projectRoot 'assets\build-icon.ps1')
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /utf8output "/out:$output\$ExecutableName" "/win32manifest:$projectRoot\src\app.manifest" "/win32icon:$projectRoot\assets\neon.ico" "/resource:$projectRoot\assets\neon.ico,Neon.Icon" "/resource:$projectRoot\src\MenuStyles.xaml,Neon.MenuStyles" $references $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilazione non riuscita.' }
Write-Host "Pronto: $output\$ExecutableName"
