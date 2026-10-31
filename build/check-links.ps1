<#
.SYNOPSIS
    Opens every link in the kit catalogue in a real browser and reports which land on the item.
.DESCRIPTION
    The commercial surface of the site: a link that lands on a homepage, a "no results" page or a
    404 sends a household off to search for itself without the affiliate tag. The report lands in
    artifacts/links/report.md and the script exits non-zero when any link fails, so a schedule can
    raise it. Not part of the gate because it depends on the shops being up.
#>
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

dotnet build tests/Selficient.E2E.Tests/Selficient.E2E.Tests.csproj --configuration Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$playwright = Join-Path $root 'artifacts/bin/Selficient.E2E.Tests/release/playwright.ps1'
if (Test-Path $playwright) {
    if ($env:CI) { & $playwright install --with-deps chromium } else { & $playwright install chromium }
    if ($LASTEXITCODE -ne 0) { throw "Playwright browser install failed." }
}

dotnet test --project tests/Selficient.E2E.Tests/Selficient.E2E.Tests.csproj --configuration Release --no-build -- --filter-trait "Category=Links"
$code = $LASTEXITCODE
if (Test-Path artifacts/links/report.md) { Get-Content artifacts/links/report.md }
if ($code -ne 0) { throw "A catalogue link does not land on its item. The report above says which." }
