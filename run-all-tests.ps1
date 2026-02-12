# Run All Tests - Comprehensive Testing Script
# Runs both unit tests and integration tests for CareerIntel

param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Continue"

Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "  CareerIntel - Comprehensive Test Suite" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Run Unit Tests
Write-Host "[1/2] Running Unit Tests..." -ForegroundColor Yellow
Write-Host ""

if (-not $SkipBuild) {
    Write-Host "Building test project..." -ForegroundColor Gray
    dotnet build tests/CareerIntel.Tests --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host "✗ Build failed" -ForegroundColor Red
        exit 1
    }
}

dotnet test tests/CareerIntel.Tests --no-build --verbosity minimal
$unitTestResult = $LASTEXITCODE

if ($unitTestResult -eq 0) {
    Write-Host "✓ Unit tests PASSED" -ForegroundColor Green
} else {
    Write-Host "✗ Unit tests FAILED" -ForegroundColor Red
}

Write-Host ""

# Step 2: Run Integration Tests (if server is running)
Write-Host "[2/2] Running Integration Tests..." -ForegroundColor Yellow
Write-Host ""

try {
    $response = Invoke-WebRequest -Uri "https://localhost:5050/" -Method GET -SkipCertificateCheck -TimeoutSec 5 -UseBasicParsing -ErrorAction SilentlyContinue
    $serverRunning = $true
} catch {
    $serverRunning = $false
}

if ($serverRunning) {
    Write-Host "Server is running, executing integration tests..." -ForegroundColor Gray
    & .\test-all.ps1
    $integrationTestResult = $LASTEXITCODE
} else {
    Write-Host "⚠ Server not running at https://localhost:5050" -ForegroundColor Yellow
    Write-Host "  Start the server with: dotnet run --project src/CareerIntel.Web" -ForegroundColor Gray
    Write-Host "  Integration tests skipped" -ForegroundColor Yellow
    $integrationTestResult = 0
}

Write-Host ""

# Summary
Write-Host "==========================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "==========================================" -ForegroundColor Cyan

if ($unitTestResult -eq 0) {
    Write-Host "Unit Tests:        ✓ PASSED" -ForegroundColor Green
} else {
    Write-Host "Unit Tests:        ✗ FAILED" -ForegroundColor Red
}

if ($serverRunning) {
    if ($integrationTestResult -eq 0) {
        Write-Host "Integration Tests: ✓ PASSED" -ForegroundColor Green
    } else {
        Write-Host "Integration Tests: ✗ FAILED" -ForegroundColor Red
    }
} else {
    Write-Host "Integration Tests: ⊝ SKIPPED" -ForegroundColor Yellow
}

Write-Host ""

# Exit with failure if any tests failed
if ($unitTestResult -ne 0 -or ($serverRunning -and $integrationTestResult -ne 0)) {
    exit 1
}

exit 0
