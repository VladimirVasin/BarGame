param([switch]$Validate, [switch]$CompileOnly)
$ErrorActionPreference = 'Stop'
if ($Validate -and $CompileOnly) { throw 'Choose validation/publication or -CompileOnly, not both.' }
$repository = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$toolchainScript = Join-Path $repository 'tools/toolchain.py'
$toolchain = Get-Content -LiteralPath (Join-Path $repository 'tools/toolchain.json') -Raw | ConvertFrom-Json
& python $toolchainScript --scope native
if ($LASTEXITCODE -ne 0) { throw 'Native toolchain preflight failed.' }
$output = Join-Path $repository 'Captures/AudioVhs/native-build'
$plugin = Join-Path $repository 'Assets/Plugins/AudioVhs/x86_64/AudioPluginIntoxicationVhs.dll'
$visualStudio = & python $toolchainScript --visual-studio-path
if ($LASTEXITCODE -ne 0 -or -not $visualStudio) { throw 'The pinned MSVC installation is required.' }
$developerCommand = Join-Path $visualStudio 'Common7/Tools/VsDevCmd.bat'
$compilerVersion = $toolchain.native.msvc_tools
$sdkVersion = $toolchain.native.windows_sdk
$environmentLines = & cmd.exe /d /s /c "`"$developerCommand`" -arch=x64 -host_arch=x64 -vcvars_ver=$compilerVersion -winsdk=$sdkVersion >nul && set"
if ($LASTEXITCODE -ne 0) { throw 'Could not initialize the MSVC x64 toolchain.' }
foreach ($line in $environmentLines) {
    if ($line -match '^([^=]+)=(.*)$') {
        [Environment]::SetEnvironmentVariable($Matches[1], $Matches[2], 'Process')
    }
}
if ($env:VCToolsVersion.TrimEnd('\') -ne $compilerVersion -or
    $env:WindowsSDKVersion.TrimEnd('\') -ne $sdkVersion) {
    throw 'Visual Studio initialized a compiler/SDK different from tools/toolchain.json.'
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
if ($CompileOnly) {
    Write-Output "Compiled staging DLL only: $builtPlugin"
    return
}
# Validate the actual candidate through Unity's ABI before touching the shipping DLL.
# -Validate is retained for existing invocations; validation/publication is now the default.
& python (Join-Path $PSScriptRoot 'validate.py') --plugin $builtPlugin --output (Join-Path $repository 'Captures/AudioVhs')
if ($LASTEXITCODE -ne 0) { throw 'Native audio validation failed; the shipping DLL was preserved.' }
& python (Join-Path $repository 'tools/asset_pipeline.py') --source $builtPlugin --destination $plugin
if ($LASTEXITCODE -ne 0) { throw 'Native audio publication failed; the shipping DLL was preserved.' }
Get-FileHash -LiteralPath $plugin -Algorithm SHA256 | Format-List
