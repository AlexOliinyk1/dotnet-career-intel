# Database Analysis Script
# Analyzes the SQLite database content

$dbPath = "src\CareerIntel.Web\data\career-intel.db"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Database Analysis" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

if (-not (Test-Path $dbPath)) {
    Write-Host "✗ Database not found at $dbPath" -ForegroundColor Red
    exit 1
}

$dbSize = (Get-Item $dbPath).Length / 1KB
Write-Host "Database: $([math]::Round($dbSize, 2)) KB" -ForegroundColor Green
Write-Host ""

# Check if sqlite3 is available
$hasSqlite = $null -ne (Get-Command sqlite3 -ErrorAction SilentlyContinue)

if ($hasSqlite) {
    Write-Host "Using sqlite3 to analyze database..." -ForegroundColor Gray
    Write-Host ""

    # Get schema
    Write-Host "[Tables]" -ForegroundColor Yellow
    $tables = & sqlite3 $dbPath ".tables"
    $tableList = $tables -split '\s+' | Where-Object { $_ }
    $tableList | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    Write-Host ""

    # Count records in each table
    Write-Host "[Record Counts]" -ForegroundColor Yellow
    $tableList | ForEach-Object {
        $table = $_
        try {
            $count = & sqlite3 $dbPath "SELECT COUNT(*) FROM [$table];" 2>$null
            if ($count -gt 0) {
                Write-Host "  $table : $count" -ForegroundColor Green
            } else {
                Write-Host "  $table : 0" -ForegroundColor Gray
            }
        } catch {
            Write-Host "  $table : error" -ForegroundColor Red
        }
    }
    Write-Host ""

    # Sample data from JobVacancies
    Write-Host "[Sample JobVacancies]" -ForegroundColor Yellow
    $vacancies = & sqlite3 $dbPath -header -column "SELECT Id, Title, Company, SourcePlatform FROM JobVacancies LIMIT 5;" 2>$null
    if ($vacancies) {
        $vacancies | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    } else {
        Write-Host "  No vacancies found" -ForegroundColor Yellow
    }
    Write-Host ""

    # Sample data from LinkedInProposals
    Write-Host "[Sample LinkedInProposals]" -ForegroundColor Yellow
    $proposals = & sqlite3 $dbPath -header -column "SELECT Id, RecruiterName, Company, JobTitle FROM LinkedInProposals LIMIT 5;" 2>$null
    if ($proposals) {
        $proposals | ForEach-Object { Write-Host "  $_" -ForegroundColor Gray }
    } else {
        Write-Host "  No proposals found" -ForegroundColor Yellow
    }

} else {
    Write-Host "⚠ sqlite3 not available" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "To install sqlite3:" -ForegroundColor White
    Write-Host "  1. Download from: https://www.sqlite.org/download.html" -ForegroundColor Gray
    Write-Host "  2. Extract sqlite3.exe to a folder in PATH" -ForegroundColor Gray
    Write-Host "  3. Or use: winget install SQLite.SQLite" -ForegroundColor Gray
    Write-Host ""
    Write-Host "Alternative: Use DB Browser for SQLite (GUI)" -ForegroundColor White
    Write-Host "  Download from: https://sqlitebrowser.org/" -ForegroundColor Gray
}

Write-Host ""
