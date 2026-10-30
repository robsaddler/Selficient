<#
.SYNOPSIS
    Runs the end-to-end journeys in a real Chromium at phone width, with axe on every page.
.DESCRIPTION
    Builds the solution, installs Chromium for Playwright if it is missing (with its system
    dependencies when CI is set), and runs the E2E suite. Separate from run-tests.ps1 because it
    drives the whole application through Kestrel and takes a minute or two.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build Selficient.slnx --configuration Release
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$playwright = Join-Path $root 'artifacts/bin/Selficient.E2E.Tests/release/playwright.ps1'
if (Test-Path $playwright) {
    if ($env:CI) { & $playwright install --with-deps chromium } else { & $playwright install chromium }
    if ($LASTEXITCODE -ne 0) { throw "Playwright browser install failed." }
}

dotnet test --project tests/Selficient.E2E.Tests/Selficient.E2E.Tests.csproj --configuration Release --no-build
if ($LASTEXITCODE -ne 0) { throw "E2E tests failed." }

Write-Host "`nE2E journeys passed." -ForegroundColor Green
