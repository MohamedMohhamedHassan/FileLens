#!/usr/bin/env pwsh
# Setup and configure the FileLens local Python AI worker environment
param(
    [string]$Engine = "paddle",
    [string]$PythonPath = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$workerDir = Join-Path $repoRoot "src\ai-worker"
$venvDir = Join-Path $workerDir ".venv"

Write-Host "FileLens AI Worker Setup" -ForegroundColor Cyan
Write-Host "Worker directory: $workerDir"

# 1. Locate Python 3.12 x64 (prefer explicit executable over broken Store aliases)
$pythonPath = $null

if ($PythonPath -and (Test-Path -LiteralPath $PythonPath)) {
    $pythonPath = (Resolve-Path -LiteralPath $PythonPath).Path
}

$pythonCandidates = @(
    "C:\Users\PC\AppData\Local\Programs\Python\Python312\python.exe",
    "$env:LOCALAPPDATA\Programs\Python\Python312\python.exe",
    "C:\Program Files\Python312\python.exe",
    "C:\Python312\python.exe"
)

foreach ($cand in $pythonCandidates) {
    if (Test-Path $cand) {
        $pythonPath = $cand
        break
    }
}

if (-not $pythonPath) {
    $launcher = "C:\Users\PC\AppData\Local\Programs\Python\Launcher\py.exe"
    if (-not (Test-Path $launcher)) {
        $launcherCmd = Get-Command py -ErrorAction SilentlyContinue
        if ($launcherCmd) { $launcher = $launcherCmd.Source }
    }
    if (Test-Path $launcher) {
        $testPy312 = & $launcher -3.12 -c "import sys; print(sys.executable)" 2>$null
        if ($testPy312 -and (Test-Path $testPy312)) {
            $pythonPath = $testPy312
        }
    }
}

if ($pythonPath) {
    $pythonInfo = & $pythonPath -c "import platform, sys; print(f'{sys.version_info.major}.{sys.version_info.minor}.{sys.version_info.micro}|{platform.architecture()[0]}')"
    if ($LASTEXITCODE -ne 0 -or $pythonInfo -notmatch '^3\.12\.[0-9]+\|64bit$') {
        Write-Host "The selected Python must be CPython 3.12 x64; detected '$pythonInfo' at '$pythonPath'." -ForegroundColor Red
        exit 1
    }
}

if (-not $pythonPath) {
    $pyCmd = Get-Command python -ErrorAction SilentlyContinue
    if ($pyCmd -and ($pyCmd.Source -notmatch "WindowsApps")) {
        $pythonPath = $pyCmd.Source
    }
}

if (-not $pythonPath) {
    Write-Host "Python 3.12 x64 was not located." -ForegroundColor Red
    Write-Host "Please ensure Python 3.12 is installed into %LOCALAPPDATA%\Programs\Python\Python312." -ForegroundColor Yellow
    exit 1
}

Write-Host "Found Python 3.12 at: $pythonPath" -ForegroundColor Green

# 2. Create isolated virtual environment if absent
if (-not (Test-Path $venvDir)) {
    Write-Host "Creating virtual environment at $venvDir..." -ForegroundColor Cyan
    & $pythonPath -m venv $venvDir
}

$venvPython = Join-Path $venvDir "Scripts\python.exe"
if (-not (Test-Path $venvPython)) {
    Write-Host "Virtual environment python was not created at $venvPython." -ForegroundColor Red
    exit 1
}

# Native Python/Paddle tools may write informational diagnostics to stderr.
# Treat those as output and rely on explicit exit-code checks below.
$ErrorActionPreference = "Continue"

# Install only the versions declared by requirements.txt. This is safe to rerun and
# does not upgrade unrelated packages in the environment.
Write-Host "Installing pinned requirements..." -ForegroundColor Cyan
& $venvPython -m pip install --disable-pip-version-check -r "$workerDir\requirements.txt" --quiet
if ($LASTEXITCODE -ne 0) { throw "Pinned AI-worker dependency installation failed." }

# 3. Validation
Write-Host "Validating PaddlePaddle and PaddleOCR runtime..." -ForegroundColor Cyan
$val = & $venvPython -c "import importlib.metadata as m; import cv2, paddle, paddleocr, pypdfium2; print('PaddlePaddle:', paddle.__version__, '| PaddleOCR:', paddleocr.__version__, '| OpenCV:', cv2.__version__, '| pypdfium2:', m.version('pypdfium2'))" 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host "Validation failed: $val" -ForegroundColor Red
    exit 1
}

& $venvPython -c "import paddle; paddle.utils.run_check()" 2>$null
if ($LASTEXITCODE -ne 0) { throw "PaddlePaddle CPU runtime check failed." }
Write-Host "PaddlePaddle CPU run_check passed." -ForegroundColor Green

Write-Host "Validation succeeded: $val" -ForegroundColor Green
Write-Host "AI Worker environment configured successfully." -ForegroundColor Green
