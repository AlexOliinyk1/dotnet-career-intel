# Test Docker Build Locally
# This verifies your Dockerfile works before deploying to the cloud

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Docker Build Test" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if Docker is installed
if (-not (Get-Command docker -ErrorAction SilentlyContinue)) {
    Write-Host "✗ Docker is not installed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Install Docker Desktop from: https://www.docker.com/products/docker-desktop" -ForegroundColor Yellow
    Write-Host ""
    exit 1
}

Write-Host "✓ Docker is installed" -ForegroundColor Green
Write-Host ""

# Build the Docker image
Write-Host "Building Docker image..." -ForegroundColor Yellow
Write-Host ""

$buildResult = docker build -t careerintel:test . 2>&1

if ($LASTEXITCODE -eq 0) {
    Write-Host "✓ Docker build successful!" -ForegroundColor Green
} else {
    Write-Host "✗ Docker build failed" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error details:" -ForegroundColor Red
    Write-Host $buildResult -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Ready to Deploy!" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Your Docker image is ready for deployment to:" -ForegroundColor Green
Write-Host "  • Railway.app" -ForegroundColor Gray
Write-Host "  • Render.com" -ForegroundColor Gray
Write-Host "  • DigitalOcean App Platform" -ForegroundColor Gray
Write-Host "  • Azure Container Apps" -ForegroundColor Gray
Write-Host ""
Write-Host "To test locally, run:" -ForegroundColor Yellow
Write-Host "  docker run -p 5050:5050 careerintel:test" -ForegroundColor Gray
Write-Host "  Then open: http://localhost:5050" -ForegroundColor Gray
Write-Host ""
Write-Host "To deploy to Railway:" -ForegroundColor Yellow
Write-Host "  1. Push to GitHub: git push origin master" -ForegroundColor Gray
Write-Host "  2. Go to: https://railway.app" -ForegroundColor Gray
Write-Host "  3. Connect your GitHub repo" -ForegroundColor Gray
Write-Host "  4. Done! Live in 5 minutes" -ForegroundColor Gray
Write-Host ""
