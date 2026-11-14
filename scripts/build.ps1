#!/usr/bin/env pwsh
# Build the FileLens solution
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

Write-Host "Building FileLens ($Configuration)..." -ForegroundColor Cyan
dotnet build "$repoRoot\FileLens.slnx" --configuration $Configuration -m:1 -p:BuildInParallel=false --no-restore

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build FAILED" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "Build succeeded." -ForegroundColor Green
