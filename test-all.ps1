# Comprehensive Test Suite for CareerIntel
# Tests all pages, engines, repositories, and scrapers

param(
    [switch]$Verbose,
    [string]$BaseUrl = "https://localhost:5050"
)

$ErrorActionPreference = "Continue"
$results = @()

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "CareerIntel Comprehensive Test Suite" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Test 1: Check if server is running
Write-Host "[1/5] Testing server availability..." -ForegroundColor Yellow
try {
    $response = Invoke-WebRequest -Uri "$BaseUrl/" -Method GET -SkipCertificateCheck -TimeoutSec 5 -UseBasicParsing
    if ($response.StatusCode -eq 200 -or $response.StatusCode -eq 302) {
        Write-Host "  ✓ Server is running" -ForegroundColor Green
        $serverRunning = $true
    }
} catch {
    Write-Host "  ✗ Server is NOT running at $BaseUrl" -ForegroundColor Red
    Write-Host "  Please run: dotnet run --project src/CareerIntel.Web" -ForegroundColor Yellow
    $serverRunning = $false
}
Write-Host ""

# Test 2: Test all pages
Write-Host "[2/5] Testing all pages..." -ForegroundColor Yellow
$pages = @(
    @{Path="/"; Name="Dashboard"},
    @{Path="/jobs"; Name="Job Browser"},
    @{Path="/jobs/remote"; Name="Remote Jobs"},
    @{Path="/jobs/tech-demand"; Name="Tech Demand"},
    @{Path="/jobs/stack"; Name="Stack Analysis"},
    @{Path="/applications"; Name="Pipeline"},
    @{Path="/applications/decisions"; Name="Decisions"},
    @{Path="/companies"; Name="Company List"},
    @{Path="/companies/scraper"; Name="Company Scraper"},
    @{Path="/interview/prep"; Name="Interview Prep"},
    @{Path="/interview/questions"; Name="Questions"},
    @{Path="/interview/feedback"; Name="Feedback"},
    @{Path="/interview/insights"; Name="Insights"},
    @{Path="/learning"; Name="Learn Plan"},
    @{Path="/learning/resources"; Name="Resources"},
    @{Path="/learning/adaptive"; Name="Adaptive Plan"},
    @{Path="/learning/micro"; Name="Micro Learn"},
    @{Path="/learning/progress"; Name="Progress"},
    @{Path="/resume"; Name="Resume Builder"},
    @{Path="/resume/simulator"; Name="Simulator"},
    @{Path="/salary"; Name="Salary Intel"},
    @{Path="/salary/negotiate"; Name="Negotiate"},
    @{Path="/salary/compare"; Name="Compare Offers"},
    @{Path="/linkedin"; Name="Proposals"},
    @{Path="/linkedin/profile"; Name="Profile Review"},
    @{Path="/linkedin/connections"; Name="Connection Targets"},
    @{Path="/monitor"; Name="Watch Panel"},
    @{Path="/monitor/schedule"; Name="Schedule"},
    @{Path="/settings/profile"; Name="Profile"},
    @{Path="/settings"; Name="Config"},
    @{Path="/career/evolution"; Name="Career Evolution"}
)

$pageResults = @()
$passedPages = 0

if ($serverRunning) {
    foreach ($page in $pages) {
        try {
            $response = Invoke-WebRequest -Uri "$BaseUrl$($page.Path)" -Method GET -SkipCertificateCheck -TimeoutSec 10 -UseBasicParsing
            $status = $response.StatusCode
            $passed = $status -eq 200

            if ($passed) {
                $passedPages++
                Write-Host "  ✓ $($page.Name) ($status)" -ForegroundColor Green
            } else {
                Write-Host "  ✗ $($page.Name) ($status)" -ForegroundColor Red
            }

            $pageResults += @{
                Page = $page.Name
                Path = $page.Path
                Status = $status
                Passed = $passed
            }
        } catch {
            Write-Host "  ✗ $($page.Name) (ERROR: $($_.Exception.Message))" -ForegroundColor Red
            $pageResults += @{
                Page = $page.Name
                Path = $page.Path
                Status = "ERROR"
                Passed = $false
                Error = $_.Exception.Message
            }
        }
    }
    Write-Host "  Pages: $passedPages/$($pages.Count) passed" -ForegroundColor $(if ($passedPages -eq $pages.Count) { "Green" } else { "Yellow" })
}
Write-Host ""

# Test 3: Test Database
Write-Host "[3/5] Testing database..." -ForegroundColor Yellow
$dbPath = "src\CareerIntel.Web\data\career-intel.db"
if (Test-Path $dbPath) {
    $dbSize = (Get-Item $dbPath).Length
    Write-Host "  ✓ Database exists ($([math]::Round($dbSize/1KB, 2)) KB)" -ForegroundColor Green

    # Check if database has tables
    try {
        $tables = & sqlite3 $dbPath ".tables" 2>$null
        if ($tables) {
            Write-Host "  ✓ Database has tables: $($tables -split '\s+' | Where-Object { $_ } | Select-Object -First 5 | Join-String -Separator ', ')..." -ForegroundColor Green
        }
    } catch {
        Write-Host "  ! Could not read database tables (sqlite3 not installed)" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ✗ Database not found at $dbPath" -ForegroundColor Red
}
Write-Host ""

# Test 4: Test Scrapers Configuration
Write-Host "[4/5] Testing scraper configuration..." -ForegroundColor Yellow
$scraperFiles = Get-ChildItem "src\CareerIntel.Scrapers\*Scraper.cs" -File
Write-Host "  ✓ Found $($scraperFiles.Count) scraper files" -ForegroundColor Green

# Check for common scraper issues
$scraperIssues = 0
foreach ($file in $scraperFiles) {
    $content = Get-Content $file.FullName -Raw

    # Check for hardcoded URLs
    if ($content -match 'http://|https://') {
        if ($Verbose) {
            Write-Host "  ✓ $($file.BaseName) has URLs configured" -ForegroundColor Green
        }
    } else {
        Write-Host "  ✗ $($file.BaseName) missing URL configuration" -ForegroundColor Red
        $scraperIssues++
    }
}

if ($scraperIssues -eq 0) {
    Write-Host "  ✓ All scrapers have URL configuration" -ForegroundColor Green
}
Write-Host ""

# Test 5: Test Profile Configuration
Write-Host "[5/5] Testing profile configuration..." -ForegroundColor Yellow
$profilePath = "src\CareerIntel.Web\data\profile.json"
if (Test-Path $profilePath) {
    $profile = Get-Content $profilePath -Raw | ConvertFrom-Json

    if ($profile.Name -and $profile.Title) {
        Write-Host "  ✓ Profile configured: $($profile.Name) - $($profile.Title)" -ForegroundColor Green
    } else {
        Write-Host "  ✗ Profile incomplete" -ForegroundColor Red
    }

    if ($profile.Skills -and $profile.Skills.Count -gt 0) {
        Write-Host "  ✓ Skills: $($profile.Skills -join ', ')" -ForegroundColor Green
    } else {
        Write-Host "  ✗ No skills configured" -ForegroundColor Red
    }

    if ($profile.MinimumSalary -and $profile.TargetSalary) {
        Write-Host "  ✓ Salary preferences: $($profile.MinimumSalary)-$($profile.TargetSalary) USD" -ForegroundColor Green
    } else {
        Write-Host "  ! Salary preferences not set" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ✗ Profile not found at $profilePath" -ForegroundColor Red
}
Write-Host ""

# Summary
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Test Summary" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

if ($serverRunning) {
    Write-Host "Server:  ✓ Running" -ForegroundColor Green
    Write-Host "Pages:   $passedPages/$($pages.Count) passed" -ForegroundColor $(if ($passedPages -eq $pages.Count) { "Green" } else { "Yellow" })
} else {
    Write-Host "Server:  ✗ Not running" -ForegroundColor Red
}

Write-Host "DB:      $(if (Test-Path $dbPath) { '✓ Present' } else { '✗ Missing' })" -ForegroundColor $(if (Test-Path $dbPath) { "Green" } else { "Red" })
Write-Host "Scrapers: $($scraperFiles.Count) found" -ForegroundColor Green
Write-Host "Profile: $(if (Test-Path $profilePath) { '✓ Configured' } else { '✗ Missing' })" -ForegroundColor $(if (Test-Path $profilePath) { "Green" } else { "Red" })

Write-Host ""

# Recommendations
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Recommendations" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

if ($passedPages -lt $pages.Count) {
    Write-Host "⚠ Some pages failed to load. Check server logs for errors." -ForegroundColor Yellow
}

if (-not (Test-Path $dbPath)) {
    Write-Host "⚠ Database missing. Run migrations: dotnet ef database update" -ForegroundColor Yellow
}

# Check why scrapers returned 0 results
Write-Host ""
Write-Host "Scraper Diagnostic:" -ForegroundColor Cyan
Write-Host "  If scrapers return 0 results, possible reasons:" -ForegroundColor Yellow
Write-Host "  1. Network connectivity - scrapers need internet access" -ForegroundColor White
Write-Host "  2. Rate limiting - job sites may block too many requests" -ForegroundColor White
Write-Host "  3. Changed HTML structure - scrapers parse specific HTML patterns" -ForegroundColor White
Write-Host "  4. Authentication required - some sites require login" -ForegroundColor White
Write-Host "  5. Profile mismatch - filters too strict (check Min/Max salary, skills)" -ForegroundColor White
Write-Host ""
Write-Host "To test individual scraper:" -ForegroundColor Cyan
Write-Host "  cd src/CareerIntel.Web" -ForegroundColor White
Write-Host "  dotnet run -- scan --platform LinkedIn" -ForegroundColor White
Write-Host ""
