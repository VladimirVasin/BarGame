param([switch]$Validate)
$ErrorActionPreference = 'Stop'
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$output = Join-Path $repository 'Captures/AudioVhs/native-build'
$plugin = Join-Path $repository 'Assets/Plugins/AudioVhs/x86_64/AudioPluginIntoxicationVhs.dll'
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer/vswhere.exe is required.' }
$visualStudio = & $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $visualStudio) { throw 'Install the Visual Studio Desktop development with C++ workload.' }
$developerCommand = Join-Path $visualStudio 'Common7/Tools/VsDevCmd.bat'
$env:PATH = (Split-Path $vswhere) + ';' + $env:PATH
$environmentLines = & cmd.exe /d /s /c "`"$developerCommand`" -arch=x64 -host_arch=x64 >nul && set"
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the MSVC x64 toolchain.' }
foreach ($line in $environmentLines) {
    if ($line -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$builtPlugin = Join-Path $output 'AudioPluginIntoxicationVhs.dll'
$compilerArguments = @('/nologo', '/O2', '/std:c++17', '/EHsc', '/MT', '/W4', '/WX',
    '/fp:precise', '/D_CRT_SECURE_NO_WARNINGS', '/LD',
    (Join-Path $PSScriptRoot 'AudioPluginIntoxicationVhs.cpp'),
    "/Fo$output/", "/Fe$builtPlugin", '/link', '/Brepro', '/INCREMENTAL:NO',
    "/IMPLIB:$output/AudioPluginIntoxicationVhs.lib")
& cl.exe @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'Native audio compilation failed.' }
Copy-Item -LiteralPath $builtPlugin -Destination $plugin -Force
Get-FileHash -LiteralPath $plugin -Algorithm SHA256 | Format-List
if ($Validate) {
    & python (Join-Path $PSScriptRoot 'validate.py') --plugin $plugin --output (Join-Path $repository 'Captures/AudioVhs')
    if ($LASTEXITCODE -ne 0) { throw 'Native audio validation failed.' }
}
