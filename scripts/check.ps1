$ErrorActionPreference = 'Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
$env:DOTNET_CLI_HOME = Join-Path $PWD '.dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet build PaymentHub.Simple.Victoria.sln --verbosity quiet
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet run --project tests/PaymentHub.Simple.Checks --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
python scripts/http-smoke.py
exit $LASTEXITCODE
