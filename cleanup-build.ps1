#!/usr/bin/env pwsh
# Clean build artifacts for TwinAgentsNetwork
Write-Host "Cleaning build artifacts..." -ForegroundColor Green

$paths = @(
    "bin",
    "obj",
    "..\TwinAgentsNetworkTests\bin",
    "..\TwinAgentsNetworkTests\obj"
)

foreach ($path in $paths) {
    $fullPath = Join-Path $PSScriptRoot $path
    if (Test-Path $fullPath) {
        Write-Host "Removing: $fullPath" -ForegroundColor Yellow
        Remove-Item -Path $fullPath -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host "? Removed" -ForegroundColor Green
    }
}

Write-Host "Build artifacts cleaned successfully!" -ForegroundColor Green
Write-Host "Running dotnet build..." -ForegroundColor Cyan
dotnet build
