#!/usr/bin/env pwsh
# Setup and configure the FileLens local Python AI worker environment
param(
    [string]$Engine = "auto"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workerDir = Join-Path $repoRoot "src\ai-worker"
$venvDir = Join-Path $workerDir ".venv"

Write-Host "FileLens AI Worker Setup" -ForegroundColor Cyan
Write-Host "Worker directory: $workerDir"

# Check if Python is available
$pythonCmd = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCmd) {
    Write-Host "Python 3 is not currently found on the PATH." -ForegroundColor Yellow
    Write-Host "FileLens will operate in deterministic offline/fallback mode." -ForegroundColor Yellow
    Write-Host "To enable full PaddleOCR or Tesseract capabilities, install Python 3.10-3.12 and rerun this script."
    exit 0
}

Write-Host "Found Python at: $($pythonCmd.Source)" -ForegroundColor Green

# Create virtual environment if absent
if (-not (Test-Path $venvDir)) {
    Write-Host "Creating virtual environment at $venvDir..." -ForegroundColor Cyan
    & python -m venv $venvDir
}

$venvPython = Join-Path $venvDir "Scripts\python.exe"
if (Test-Path $venvPython) {
    Write-Host "Upgrading pip..." -ForegroundColor Cyan
    & $venvPython -m pip install --upgrade pip --quiet
    
    Write-Host "Installing requirements..." -ForegroundColor Cyan
    & $venvPython -m pip install -r "$workerDir\requirements.txt" --quiet
    
    Write-Host "AI Worker environment configured successfully." -ForegroundColor Green
}

