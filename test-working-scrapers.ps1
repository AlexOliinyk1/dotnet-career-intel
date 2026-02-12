# Test Working Scrapers
# Quick test to verify scrapers return real jobs

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Testing Working Scrapers" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

Write-Host "[Step 1/3] Building project..." -ForegroundColor Yellow
$buildOutput = dotnet build src/CareerIntel.Scrapers --verbosity quiet 2>&1

if ($LASTEXITCODE -ne 0) {
    Write-Host "✗ Build failed" -ForegroundColor Red
    Write-Host $buildOutput
    exit 1
}

Write-Host "✓ Build successful" -ForegroundColor Green
Write-Host ""

Write-Host "[Step 2/3] Testing RemoteOK API Scraper..." -ForegroundColor Yellow
Write-Host "This scraper uses RemoteOK's public API (no setup needed)" -ForegroundColor Gray
Write-Host ""

# Create a simple C# test file
$testCode = @'
using CareerIntel.Scrapers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Information);
});

var logger = loggerFactory.CreateLogger<RemoteOkApiScraper>();
var httpClient = new HttpClient();

var scraper = new RemoteOkApiScraper(httpClient, logger);

Console.WriteLine("Fetching jobs from RemoteOK API...");
Console.WriteLine("");

var jobs = await scraper.ScrapeAsync(".NET", maxPages: 2);

Console.WriteLine($"✓ Found {jobs.Count} jobs!");
Console.WriteLine("");

if (jobs.Count > 0)
{
    Console.WriteLine("Sample jobs:");
    Console.WriteLine("============");

    foreach (var job in jobs.Take(10))
    {
        Console.WriteLine($"• {job.Title} at {job.Company}");
        if (job.SalaryMin > 0)
        {
            Console.WriteLine($"  Salary: ${job.SalaryMin:N0} - ${job.SalaryMax:N0}");
        }
        Console.WriteLine($"  URL: {job.Url}");
        Console.WriteLine("");
    }
}
else
{
    Console.WriteLine("No jobs found. This might be because:");
    Console.WriteLine("1. No jobs matching '.NET' keyword");
    Console.WriteLine("2. API is temporarily down");
    Console.WriteLine("3. Network connectivity issue");
}
'@

# Save test code
$testFile = "src\CareerIntel.Scrapers\TestRemoteOk.cs"
Set-Content -Path $testFile -Value $testCode

Write-Host "[Step 3/3] Running test..." -ForegroundColor Yellow
Write-Host ""

# Run the test
dotnet run --project src/CareerIntel.Scrapers

# Cleanup
Remove-Item $testFile -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Next Steps" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "1. If you saw jobs above: ✓ RemoteOK API works!" -ForegroundColor Green
Write-Host "2. Get Adzuna API key for 12,500+ jobs/month:" -ForegroundColor Yellow
Write-Host "   https://developer.adzuna.com/" -ForegroundColor Gray
Write-Host ""
Write-Host "3. Read WORKING_SCRAPERS_GUIDE.md for full setup" -ForegroundColor Yellow
Write-Host ""
