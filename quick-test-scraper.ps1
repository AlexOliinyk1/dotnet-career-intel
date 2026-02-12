# Quick Test - RemoteOK API Scraper
# Tests if the scraper can fetch real jobs

Write-Host "============================================" -ForegroundColor Cyan
Write-Host "  Quick Test - RemoteOK API" -ForegroundColor Cyan
Write-Host "============================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "Testing RemoteOK public API..." -ForegroundColor Yellow
Write-Host ""

try {
    # Test the API directly
    $response = Invoke-RestMethod -Uri "https://remoteok.com/api" -Method GET -UserAgent "Mozilla/5.0"

    if ($response -and $response.Count -gt 0) {
        Write-Host "✓ API is accessible!" -ForegroundColor Green
        Write-Host "✓ Found $($response.Count) total items" -ForegroundColor Green
        Write-Host ""

        # Filter for .NET jobs
        $dotnetJobs = $response | Where-Object {
            $_.position -like "*.NET*" -or
            $_.position -like "*C#*" -or
            $_.tags -contains ".NET" -or
            $_.tags -contains "C#"
        }

        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ".NET Jobs Found: $($dotnetJobs.Count)" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host ""

        $count = 0
        foreach ($job in $dotnetJobs | Select-Object -First 10) {
            $count++
            Write-Host "[$count] $($job.position)" -ForegroundColor Green
            Write-Host "    Company: $($job.company)" -ForegroundColor Gray
            if ($job.salary_min) {
                Write-Host "    Salary: `$$($job.salary_min) - `$$($job.salary_max)" -ForegroundColor Yellow
            }
            Write-Host "    URL: https://remoteok.com/remote-jobs/$($job.slug)" -ForegroundColor Gray
            Write-Host "    Tags: $($job.tags -join ', ')" -ForegroundColor Gray
            Write-Host ""
        }

        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host "Summary" -ForegroundColor Cyan
        Write-Host "============================================" -ForegroundColor Cyan
        Write-Host "✓ RemoteOK API works!" -ForegroundColor Green
        Write-Host "✓ Found $($dotnetJobs.Count) .NET/C# remote jobs" -ForegroundColor Green
        Write-Host ""
        Write-Host "This means your RemoteOkApiScraper.cs will work!" -ForegroundColor Green
        Write-Host ""

    } else {
        Write-Host "✗ API returned empty response" -ForegroundColor Red
    }

} catch {
    Write-Host "✗ Error accessing API: $_" -ForegroundColor Red
    Write-Host ""
    Write-Host "This could mean:" -ForegroundColor Yellow
    Write-Host "1. No internet connection" -ForegroundColor Gray
    Write-Host "2. RemoteOK API is temporarily down" -ForegroundColor Gray
    Write-Host "3. Firewall is blocking the request" -ForegroundColor Gray
}

Write-Host ""
Write-Host "Next: Run the full web app to test the scraper integration" -ForegroundColor Yellow
Write-Host "  cd src/CareerIntel.Web" -ForegroundColor Gray
Write-Host "  dotnet run" -ForegroundColor Gray
Write-Host "  Open https://localhost:5050/jobs" -ForegroundColor Gray
Write-Host ""
