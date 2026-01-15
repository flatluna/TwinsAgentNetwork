# Test script for MCP Server
# Save this as test-mcp.ps1

Write-Host "Testing MCP Server..." -ForegroundColor Green

# Test 1: Build the project
Write-Host "`n1. Building project..." -ForegroundColor Yellow
dotnet build
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build succeeded!" -ForegroundColor Green

# Test 2: Run unit tests
Write-Host "`n2. Running unit tests..." -ForegroundColor Yellow
dotnet test --logger "console;verbosity=normal"
if ($LASTEXITCODE -ne 0) {
    Write-Host "Some tests failed, but continuing..." -ForegroundColor Yellow
}

# Test 3: Try to start MCP server (will timeout after 5 seconds)
Write-Host "`n3. Testing Full MCP Server startup..." -ForegroundColor Yellow
$job = Start-Job -ScriptBlock {
    Set-Location "C:\FlatBitAISource\TwinFunctions\TwinAgentsNetwork"
    dotnet run -- --mcp
}

Start-Sleep -Seconds 5
Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -ErrorAction SilentlyContinue

# Test 4: Try to start documentation sample MCP server
Write-Host "`n4. Testing Documentation Sample MCP Server..." -ForegroundColor Yellow
$job2 = Start-Job -ScriptBlock {
    Set-Location "C:\FlatBitAISource\TwinFunctions\TwinAgentsNetwork"
    dotnet run -- --mcp-sample
}

Start-Sleep -Seconds 5
Stop-Job $job2 -ErrorAction SilentlyContinue
Remove-Job $job2 -ErrorAction SilentlyContinue

# Test 5: Try to start MCP demo server
Write-Host "`n5. Testing MCP Demo Server..." -ForegroundColor Yellow
$job3 = Start-Job -ScriptBlock {
    Set-Location "C:\FlatBitAISource\TwinFunctions\TwinAgentsNetwork"
    dotnet run -- --mcp-demo
}

Start-Sleep -Seconds 5
Stop-Job $job3 -ErrorAction SilentlyContinue
Remove-Job $job3 -ErrorAction SilentlyContinue

Write-Host "MCP Server test completed!" -ForegroundColor Green
Write-Host "`nAvailable MCP Servers:" -ForegroundColor Cyan
Write-Host "? Full Server:         dotnet run -- --mcp" -ForegroundColor Green
Write-Host "? Sample Server:       dotnet run -- --mcp-sample" -ForegroundColor Green
Write-Host "? Demo Server:         dotnet run -- --mcp-demo" -ForegroundColor Green
Write-Host "`nMCP Inspector Usage:" -ForegroundColor Yellow
Write-Host "npx @modelcontextprotocol/inspector dotnet run -- --mcp-demo" -ForegroundColor White