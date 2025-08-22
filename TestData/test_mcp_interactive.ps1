#!/usr/bin/env pwsh

# Interactive MCP Server Test Script
$dbPath = "C:\code\DB2XL\TestData\mcp_demo.db"
$consolePath = "C:\code\DB2XL\DB2XL.Console\bin\Release\net9.0\DB2XL.Console.exe"

Write-Host "🚀 DB2XL MCP Server Interactive Test" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan

# Remove existing database
if (Test-Path $dbPath) {
    Remove-Item $dbPath
    Write-Host "🗑️  Removed existing database" -ForegroundColor Yellow
}

Write-Host "`n1. Testing MCP Server Capabilities..." -ForegroundColor Green
& $consolePath mcp --capabilities-only | ConvertFrom-Json | ConvertTo-Json -Depth 10 | Out-File "$PSScriptRoot\mcp_capabilities.json"
Write-Host "   ✅ Capabilities saved to mcp_capabilities.json" -ForegroundColor Gray

Write-Host "`n2. Starting MCP Server in stdio mode..." -ForegroundColor Green
Write-Host "   📝 You can now send JSON-RPC commands manually" -ForegroundColor Gray
Write-Host "   📋 Example commands:" -ForegroundColor Gray
Write-Host "   Initialize: " -NoNewline -ForegroundColor Gray
Write-Host '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}' -ForegroundColor White
Write-Host "   List tools: " -NoNewline -ForegroundColor Gray  
Write-Host '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}' -ForegroundColor White
Write-Host "   Execute SQL: " -NoNewline -ForegroundColor Gray
Write-Host '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"execute_query","arguments":{"database_path":"PATH_TO_DB","sql_query":"CREATE TABLE test (id INTEGER, name TEXT);","allow_writes":true}}}' -ForegroundColor White
Write-Host "`n   🛑 Press Ctrl+C to stop the server" -ForegroundColor Red
Write-Host ""

# Start the MCP server
& $consolePath mcp --stdio