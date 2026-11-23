$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$demoData = Join-Path $PWD 'data'
dotnet run --project src/PaymentHub.Simple.Gateway -- --Demo:DataDirectory $demoData
exit $LASTEXITCODE
