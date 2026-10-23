param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$projectRoot = Split-Path -Parent $PSScriptRoot
$localSdk = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
$dotnetExe = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
& $dotnetExe test (Join-Path $projectRoot 'yukonWinDock.sln') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
if ($Publish) {
    & $dotnetExe publish (Join-Path $projectRoot 'src\YukonWinDock\YukonWinDock.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o (Join-Path $projectRoot 'artifacts\win-x64') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
}
