#!/usr/bin/env pwsh
# Run all FileLens tests
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# Build solution first with single-node MSBuild
& "$repoRoot\scripts\build.ps1" -Configuration $Configuration
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Running FileLens Unit Tests ($Configuration)..." -ForegroundColor Cyan
dotnet run --project "$repoRoot\tests\FileLens.UnitTests\FileLens.UnitTests.csproj" --configuration $Configuration --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Unit Tests FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Running FileLens Integration Tests ($Configuration)..." -ForegroundColor Cyan
dotnet run --project "$repoRoot\tests\FileLens.IntegrationTests\FileLens.IntegrationTests.csproj" --configuration $Configuration --no-build

if ($LASTEXITCODE -ne 0) {
    Write-Host "Integration Tests FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "All tests passed successfully." -ForegroundColor Green
