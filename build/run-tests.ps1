<#
.SYNOPSIS
    Builds Selficient, runs every test suite, and fails if coverage slips.

.DESCRIPTION
    One command for the whole gate, so that "it passes locally" and "it passes in CI" mean the same
    thing. Coverage is asserted per production assembly, lines and branches, never as an average.

.PARAMETER SkipFrontEnd
    Skip the JavaScript type check and lint, which need Node and an npm install.

.EXAMPLE
    ./build/run-tests.ps1
#>
[CmdletBinding()]
param(
    [switch] $SkipFrontEnd
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
Set-Location $repository

$results = Join-Path $repository 'TestResults'
if (Test-Path $results) { Remove-Item $results -Recurse -Force }

Write-Host "`n=== Building ===" -ForegroundColor Cyan
dotnet build Selficient.slnx --configuration Release -v minimal
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$suites = @(
    @{ Name = 'Domain';         Project = 'tests/Selficient.Domain.Tests/Selficient.Domain.Tests.csproj';                 Report = 'domain.cobertura.xml' }
    @{ Name = 'Application';    Project = 'tests/Selficient.Application.Tests/Selficient.Application.Tests.csproj';       Report = 'application.cobertura.xml' }
    @{ Name = 'Infrastructure'; Project = 'tests/Selficient.Infrastructure.Tests/Selficient.Infrastructure.Tests.csproj'; Report = 'infrastructure.cobertura.xml' }
    @{ Name = 'Web';            Project = 'tests/Selficient.Web.Tests/Selficient.Web.Tests.csproj';                       Report = 'web.cobertura.xml' }
    @{ Name = 'Architecture';   Project = 'tests/Selficient.Architecture.Tests/Selficient.Architecture.Tests.csproj';     Report = 'architecture.cobertura.xml' }
)

foreach ($suite in $suites) {
    Write-Host "`n=== Testing $($suite.Name) ===" -ForegroundColor Cyan

    # --results-directory is a dotnet test option and must precede the "--" that hands the rest to
    # the Microsoft Testing Platform; after it, the SDK misreads the path as a bare directory.
    dotnet test --project $suite.Project --configuration Release --no-build --results-directory $results `
        -- --coverage --coverage-output-format cobertura --coverage-output $suite.Report

    if ($LASTEXITCODE -ne 0) { throw "$($suite.Name) tests failed." }
}

Write-Host "`n=== Coverage ===" -ForegroundColor Cyan

$reports = ($suites | ForEach-Object { Join-Path $results $_.Report }) -join ','

# Merged across suites: a line reached by any suite is covered. Gated assemblies must be present and
# at 100% lines and branches. Selficient.Application joins the gate with its first use case in M1.
node build/coverage-report.mjs $reports `
    Selficient.Domain=1 Selficient.Infrastructure=1 Selficient.Web=1

if ($LASTEXITCODE -ne 0) { throw "Coverage is below threshold." }

if (-not $SkipFrontEnd) {
    Write-Host "`n=== Front end: types and lint ===" -ForegroundColor Cyan

    if (-not (Test-Path 'node_modules')) { npm ci --no-audit --no-fund }
    npm run check
    if ($LASTEXITCODE -ne 0) { throw "Front-end checks failed." }
}

Write-Host "`nAll suites passed." -ForegroundColor Green
