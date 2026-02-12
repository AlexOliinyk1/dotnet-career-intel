$testPages = @(
    "/",
    "/resume",
    "/salary",
    "/interview/prep",
    "/companies",
    "/career/gaps",
    "/learning/resources",
    "/jobs/scan-image"
)

$results = @()
foreach ($page in $testPages) {
    try {
        $response = Invoke-WebRequest -Uri "https://localhost:5050$page" -SkipCertificateCheck -TimeoutSec 10 -ErrorAction Stop
        $results += [PSCustomObject]@{
            Page = $page
            Status = $response.StatusCode
            ContentLength = $response.Content.Length
            HasError = $response.Content -match "error|exception|stack trace"
        }
    }
    catch {
        $results += [PSCustomObject]@{
            Page = $page
            Status = "ERROR"
            ContentLength = 0
            HasError = $true
        }
    }
}

$results | Format-Table -AutoSize
Write-Host ""
Write-Host "Summary:" -ForegroundColor Cyan
$success = ($results | Where-Object { $_.Status -eq 200 -and -not $_.HasError }).Count
$total = $testPages.Count
Write-Host "Success: $success/$total pages loaded successfully" -ForegroundColor Green
