# Code Quality Check
# Comprehensive analysis of the codebase

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Code Quality Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Project structure
Write-Host "[1/7] Project Structure..." -ForegroundColor Yellow
$projects = Get-ChildItem "src\*.csproj" -Recurse
Write-Host "  Projects: $($projects.Count)" -ForegroundColor Gray
$projects | ForEach-Object {
    $name = $_.Directory.Name
    $files = (Get-ChildItem $_.Directory.FullName -Recurse -File -Include "*.cs").Count
    Write-Host "    $name - $files C# files" -ForegroundColor Gray
}
Write-Host ""

# Step 2: Code statistics
Write-Host "[2/7] Code Statistics..." -ForegroundColor Yellow
$csFiles = Get-ChildItem "src\**\*.cs" -Recurse -File
$totalLines = 0
$csFiles | ForEach-Object {
    $totalLines += (Get-Content $_.FullName).Count
}
Write-Host "  Total C# files: $($csFiles.Count)" -ForegroundColor Gray
Write-Host "  Total lines of code: $totalLines" -ForegroundColor Gray
Write-Host "  Average per file: $([math]::Round($totalLines / $csFiles.Count, 0)) lines" -ForegroundColor Gray
Write-Host ""

# Step 3: Check for common issues
Write-Host "[3/7] Checking for common issues..." -ForegroundColor Yellow

# Check for TODO comments
$todos = Select-String -Path "src\**\*.cs" -Pattern "TODO|FIXME|HACK" -CaseSensitive:$false
Write-Host "  TODO/FIXME comments: $($todos.Count)" -ForegroundColor $(if ($todos.Count -gt 10) { "Yellow" } else { "Gray" })

# Check for hardcoded credentials
$credentials = Select-String -Path "src\**\*.cs" -Pattern 'password\s*=|apikey\s*=|secret\s*=' -CaseSensitive:$false
if ($credentials.Count -gt 0) {
    Write-Host "  ⚠ Possible hardcoded credentials: $($credentials.Count)" -ForegroundColor Red
} else {
    Write-Host "  ✓ No hardcoded credentials detected" -ForegroundColor Green
}

# Check for Console.WriteLine (should use ILogger)
$consoleWrites = Select-String -Path "src\**\*.cs" -Pattern 'Console\.WriteLine|Console\.Write(?!Line)' -CaseSensitive
Write-Host "  Console.WriteLine usage: $($consoleWrites.Count)" -ForegroundColor $(if ($consoleWrites.Count -gt 5) { "Yellow" } else { "Gray" })

Write-Host ""

# Step 4: Test coverage
Write-Host "[4/7] Test Coverage..." -ForegroundColor Yellow
$testFiles = Get-ChildItem "tests\**\*.cs" -Recurse -File
$testLines = 0
$testFiles | ForEach-Object {
    $testLines += (Get-Content $_.FullName).Count
}
Write-Host "  Test files: $($testFiles.Count)" -ForegroundColor Gray
Write-Host "  Test code lines: $testLines" -ForegroundColor Gray
$coverage = if ($totalLines -gt 0) { [math]::Round(($testLines / $totalLines) * 100, 1) } else { 0 }
Write-Host "  Test/Code ratio: $coverage%" -ForegroundColor $(if ($coverage -gt 10) { "Green" } elseif ($coverage -gt 5) { "Yellow" } else { "Red" })
Write-Host ""

# Step 5: Build warnings
Write-Host "[5/7] Build Analysis..." -ForegroundColor Yellow
$buildOutput = dotnet build --verbosity quiet 2>&1
$warnings = $buildOutput | Select-String "warning"
$errors = $buildOutput | Select-String "error"

if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Build: SUCCESS" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build: FAILED" -ForegroundColor Red
}

Write-Host "  Warnings: $($warnings.Count)" -ForegroundColor $(if ($warnings.Count -gt 20) { "Red" } elseif ($warnings.Count -gt 5) { "Yellow" } else { "Green" })
Write-Host "  Errors: $($errors.Count)" -ForegroundColor $(if ($errors.Count -gt 0) { "Red" } else { "Green" })

if ($errors.Count -gt 0 -and $errors.Count -le 5) {
    Write-Host ""
    Write-Host "  Error details:" -ForegroundColor Red
    $errors | Select-Object -First 5 | ForEach-Object {
        Write-Host "    $_" -ForegroundColor Red
    }
}

Write-Host ""

# Step 6: Dependencies
Write-Host "[6/7] NuGet Dependencies..." -ForegroundColor Yellow
$packages = Get-Content "src\**\*.csproj" -Recurse | Select-String "PackageReference" | Select-Object -Unique
Write-Host "  Unique package references: $($packages.Count)" -ForegroundColor Gray

# Check for outdated patterns
$efCorePackages = $packages | Select-String "EntityFrameworkCore"
Write-Host "  EF Core packages: $($efCorePackages.Count)" -ForegroundColor Gray

Write-Host ""

# Step 7: Documentation
Write-Host "[7/7] Documentation..." -ForegroundColor Yellow
$readmeExists = Test-Path "README.md"
$licenseExists = Test-Path "LICENSE"
$dockerfileExists = Test-Path "Dockerfile"
$githubWorkflow = Test-Path ".github\workflows"

Write-Host "  README.md: $(if ($readmeExists) { '✓' } else { '✗' })" -ForegroundColor $(if ($readmeExists) { "Green" } else { "Red" })
Write-Host "  LICENSE: $(if ($licenseExists) { '✓' } else { '✗' })" -ForegroundColor $(if ($licenseExists) { "Green" } else { "Yellow" })
Write-Host "  Dockerfile: $(if ($dockerfileExists) { '✓' } else { '✗' })" -ForegroundColor $(if ($dockerfileExists) { "Green" } else { "Yellow" })
Write-Host "  GitHub Actions: $(if ($githubWorkflow) { '✓' } else { '✗' })" -ForegroundColor $(if ($githubWorkflow) { "Green" } else { "Yellow" })

Write-Host ""

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Quality Score" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$score = 0
$maxScore = 100

# Build success: 30 points
if ($LASTEXITCODE -eq 0) { $score += 30 }

# Low warnings: 20 points
if ($warnings.Count -lt 10) { $score += 20 }
elseif ($warnings.Count -lt 30) { $score += 10 }

# Test coverage: 20 points
if ($coverage -gt 20) { $score += 20 }
elseif ($coverage -gt 10) { $score += 15 }
elseif ($coverage -gt 5) { $score += 10 }

# No credentials: 10 points
if ($credentials.Count -eq 0) { $score += 10 }

# Documentation: 10 points
if ($readmeExists -and $licenseExists) { $score += 10 }
elseif ($readmeExists) { $score += 5 }

# Code organization: 10 points
if ($csFiles.Count -gt 50 -and $projects.Count -gt 5) { $score += 10 }

Write-Host ""
Write-Host "Overall Quality Score: $score / $maxScore" -ForegroundColor $(if ($score -gt 80) { "Green" } elseif ($score -gt 60) { "Yellow" } else { "Red" })
Write-Host ""

if ($score -gt 80) {
    Write-Host "✓ Excellent code quality!" -ForegroundColor Green
} elseif ($score -gt 60) {
    Write-Host "✓ Good code quality, some improvements possible" -ForegroundColor Yellow
} else {
    Write-Host "⚠ Code quality needs improvement" -ForegroundColor Red
}

Write-Host ""
