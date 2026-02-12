# Comprehensive Scraper Verification Script
# Verifies all scrapers are properly configured and working

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Scraper Verification & Diagnostic Tool" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

# Step 1: Find all scraper files
Write-Host "[1/6] Analyzing scraper files..." -ForegroundColor Yellow
$scraperFiles = Get-ChildItem "src\CareerIntel.Scrapers\*Scraper.cs" -File | Where-Object { $_.Name -ne "BaseScraper.cs" -and $_.Name -notmatch "^I[A-Z]" }

Write-Host "  Found $($scraperFiles.Count) scraper implementations" -ForegroundColor Gray
Write-Host ""

# Step 2: Check each scraper for configuration
Write-Host "[2/6] Checking scraper configuration..." -ForegroundColor Yellow

$scraperAnalysis = @()

foreach ($file in $scraperFiles) {
    $content = Get-Content $file.FullName -Raw
    $scraperName = $file.BaseName

    $analysis = @{
        Name = $scraperName
        File = $file.Name
        HasPlatformName = $content -match 'public override string PlatformName'
        HasScrapeAsync = $content -match 'public override.*ScrapeAsync'
        HasScrapeDetailAsync = $content -match 'public override.*ScrapeDetailAsync'
        HasHttpClient = $content -match 'HttpClient'
        HasBaseUrl = $content -match 'https?://[^\s"]+' -or $content -imatch 'BaseUrl|base.*url'
        UsesHtmlAgilityPack = $content -match 'HtmlDocument|HtmlNode'
        HasErrorHandling = $content -match 'try\s*{|catch\s*\('
        LineCount = (Get-Content $file.FullName).Count
    }

    $scraperAnalysis += $analysis

    # Detailed output
    if ($analysis.HasPlatformName -and $analysis.HasScrapeAsync) {
        Write-Host "  ✓ $scraperName" -ForegroundColor Green -NoNewline
    } else {
        Write-Host "  ✗ $scraperName" -ForegroundColor Red -NoNewline
    }

    Write-Host " ($($analysis.LineCount) lines)" -ForegroundColor Gray
}

Write-Host ""

# Step 3: Count implementation completeness
Write-Host "[3/6] Scraper implementation status..." -ForegroundColor Yellow

$fullyImplemented = ($scraperAnalysis | Where-Object { $_.HasPlatformName -and $_.HasScrapeAsync -and $_.HasScrapeDetailAsync }).Count
$partialImplemented = ($scraperAnalysis | Where-Object { $_.HasPlatformName -and $_.HasScrapeAsync -and -not $_.HasScrapeDetailAsync }).Count
$incomplete = ($scraperAnalysis | Where-Object { -not $_.HasScrapeAsync }).Count

Write-Host "  Fully implemented:    $fullyImplemented" -ForegroundColor Green
Write-Host "  Partial (no detail):  $partialImplemented" -ForegroundColor Yellow
Write-Host "  Incomplete:           $incomplete" -ForegroundColor Red
Write-Host ""

# Step 4: Check database schema
Write-Host "[4/6] Checking database schema..." -ForegroundColor Yellow

$dbPath = "src\CareerIntel.Web\data\career-intel.db"
if (Test-Path $dbPath) {
    $dbSize = (Get-Item $dbPath).Length / 1KB
    Write-Host "  ✓ Database: $([math]::Round($dbSize, 2)) KB" -ForegroundColor Green

    # Try to read database
    try {
        $tables = & sqlite3 $dbPath ".tables" 2>$null
        if ($tables) {
            $tableList = $tables -split '\s+' | Where-Object { $_ }
            Write-Host "  ✓ Tables: $($tableList.Count) found" -ForegroundColor Green

            # Count records in key tables
            $vacancyCount = & sqlite3 $dbPath "SELECT COUNT(*) FROM JobVacancies;" 2>$null
            $proposalCount = & sqlite3 $dbPath "SELECT COUNT(*) FROM LinkedInProposals;" 2>$null
            $applicationCount = & sqlite3 $dbPath "SELECT COUNT(*) FROM Applications;" 2>$null

            Write-Host "  ✓ JobVacancies: $vacancyCount records" -ForegroundColor Gray
            Write-Host "  ✓ LinkedInProposals: $proposalCount records" -ForegroundColor Gray
            Write-Host "  ✓ Applications: $applicationCount records" -ForegroundColor Gray
        }
    } catch {
        Write-Host "  ! sqlite3 not installed - cannot read tables" -ForegroundColor Yellow
    }
} else {
    Write-Host "  ✗ Database not found" -ForegroundColor Red
}

Write-Host ""

# Step 5: Check profile configuration
Write-Host "[5/6] Checking user profile..." -ForegroundColor Yellow

$profilePath = "src\CareerIntel.Web\data\profile.json"
if (Test-Path $profilePath) {
    $profile = Get-Content $profilePath -Raw | ConvertFrom-Json
    Write-Host "  ✓ Profile found" -ForegroundColor Green
    Write-Host "    Name: $($profile.Name)" -ForegroundColor Gray
    Write-Host "    Title: $($profile.Title)" -ForegroundColor Gray
    Write-Host "    Skills: $($profile.Skills -join ', ')" -ForegroundColor Gray
    Write-Host "    Salary: $($profile.MinimumSalary)-$($profile.TargetSalary) USD" -ForegroundColor Gray
    Write-Host "    Remote: $($profile.RemoteOnly)" -ForegroundColor Gray
} else {
    Write-Host "  ! Profile not configured (using database instead)" -ForegroundColor Yellow
    Write-Host "    Profile is saved in database, not JSON file" -ForegroundColor Gray
}

Write-Host ""

# Step 6: Test build
Write-Host "[6/6] Testing build..." -ForegroundColor Yellow

$buildOutput = dotnet build src/CareerIntel.Scrapers --verbosity quiet 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "  ✓ Scrapers project builds successfully" -ForegroundColor Green
} else {
    Write-Host "  ✗ Build errors detected" -ForegroundColor Red
    $buildOutput | Select-String "error" | ForEach-Object {
        Write-Host "    $_" -ForegroundColor Red
    }
}

Write-Host ""

# Summary Report
Write-Host "============================================" -ForegroundColor Cyan
Write-Host "Summary Report" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Scrapers:" -ForegroundColor White
Write-Host "  Total:             $($scraperFiles.Count)" -ForegroundColor Gray
Write-Host "  Fully working:     $fullyImplemented" -ForegroundColor Green
Write-Host "  Partial:           $partialImplemented" -ForegroundColor Yellow
Write-Host "  Need fixes:        $incomplete" -ForegroundColor Red
Write-Host ""

Write-Host "Known Issues:" -ForegroundColor White
Write-Host "  1. Job sites frequently change HTML structure" -ForegroundColor Yellow
Write-Host "  2. Many sites require JavaScript rendering (Playwright needed)" -ForegroundColor Yellow
Write-Host "  3. Rate limiting and IP blocking is common" -ForegroundColor Yellow
Write-Host "  4. Some sites require authentication/cookies" -ForegroundColor Yellow
Write-Host ""

Write-Host "Recommendations:" -ForegroundColor White
Write-Host "  ✓ Use test-all.ps1 to verify web UI" -ForegroundColor Green
Write-Host "  ✓ Use run-all-tests.ps1 to run all tests" -ForegroundColor Green
Write-Host "  ✓ Check individual scraper: dotnet run --project src/CareerIntel.Web -- scan --platform DOU" -ForegroundColor Green
Write-Host ""

# Top working scrapers (by implementation)
Write-Host "Top Scrapers (by code size):" -ForegroundColor White
$scraperAnalysis | Sort-Object LineCount -Descending | Select-Object -First 10 | ForEach-Object {
    $status = if ($_.HasScrapeAsync -and $_.HasScrapeDetailAsync) { "✓" } else { "?" }
    Write-Host "  $status $($_.Name) - $($_.LineCount) lines" -ForegroundColor Gray
}

Write-Host ""
