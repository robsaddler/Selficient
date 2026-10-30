<#
.SYNOPSIS
    Runs the NBomber load scenarios against a Release build hosted in-process on Kestrel.
.DESCRIPTION
    The targets from plans/build-plan-v1.md section 6 are checked at the end; a miss exits
    non-zero. Pass a duration in seconds (default 20). Reports land in artifacts/load.
#>
param([int] $Seconds = 20, [double] $Scale = 1)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build tests/Selficient.Load/Selficient.Load.csproj --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

dotnet run --project tests/Selficient.Load/Selficient.Load.csproj --configuration Release --no-build -- $Seconds $Scale
if ($LASTEXITCODE -ne 0) { throw "A load target was missed." }
