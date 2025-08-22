#!/usr/bin/env pwsh

# Test the MCP server functionality
$consolePath = "C:\code\DB2XL\DB2XL.Console\bin\Release\net9.0\DB2XL.Console.exe"
$dbPath = "C:\code\DB2XL\TestData\mcp_demo.db"

Write-Host "🚀 Testing DB2XL MCP Server" -ForegroundColor Cyan
Write-Host "============================" -ForegroundColor Cyan

# Clean up previous test
if (Test-Path $dbPath) {
    Remove-Item $dbPath
    Write-Host "🗑️  Removed existing database" -ForegroundColor Yellow
}

# Function to send JSON-RPC request
function Send-McpRequest {
    param([string]$Request, [string]$Description)
    
    Write-Host "`n🔄 $Description..." -ForegroundColor Green
    Write-Host "   Request: $($Request.Substring(0, [Math]::Min(100, $Request.Length)))..." -ForegroundColor Gray
    
    try {
        $response = $Request | & $consolePath mcp --stdio 2>$null | Select-Object -First 1
        if ($response) {
            $json = $response | ConvertFrom-Json
            if ($json.error) {
                Write-Host "   ❌ Error: $($json.error.message)" -ForegroundColor Red
            } else {
                Write-Host "   ✅ Success" -ForegroundColor Green
                if ($json.result) {
                    Write-Host "   📄 Response: $($response.Substring(0, [Math]::Min(200, $response.Length)))..." -ForegroundColor Gray
                }
            }
            return $json
        } else {
            Write-Host "   ⚠️  No response received" -ForegroundColor Yellow
            return $null
        }
    } catch {
        Write-Host "   ❌ Error: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Test 1: Initialize
$initRequest = '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05"}}'
Send-McpRequest -Request $initRequest -Description "Initialize MCP session"

# Test 2: List tools
$listRequest = '{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}'
Send-McpRequest -Request $listRequest -Description "List available tools"

# Test 3: Create database and table
$createRequest = '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"execute_query","arguments":{"database_path":"C:\\code\\DB2XL\\TestData\\mcp_demo.db","sql_query":"CREATE TABLE users (id INTEGER PRIMARY KEY, name TEXT, email TEXT); INSERT INTO users VALUES (1, '\''Alice'\'', '\''alice@test.com'\''), (2, '\''Bob'\'', '\''bob@test.com'\'');","allow_writes":true}}}'
Send-McpRequest -Request $createRequest -Description "Create database and populate with sample data"

# Check if database was created
if (Test-Path $dbPath) {
    Write-Host "   📁 Database file created successfully" -ForegroundColor Green
} else {
    Write-Host "   ⚠️  Database file not found" -ForegroundColor Yellow
}

# Test 4: Preview database
$previewRequest = '{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"preview_database","arguments":{"database_path":"C:\\code\\DB2XL\\TestData\\mcp_demo.db","max_preview_rows":3,"include_sample_data":true}}}'
$previewResult = Send-McpRequest -Request $previewRequest -Description "Preview database structure and data"

# Test 5: Get schema
$schemaRequest = '{"jsonrpc":"2.0","id":5,"method":"tools/call","params":{"name":"get_schema","arguments":{"database_path":"C:\\code\\DB2XL\\TestData\\mcp_demo.db","include_column_details":true}}}'
Send-McpRequest -Request $schemaRequest -Description "Get detailed database schema"

# Test 6: Export to JSONL
$exportRequest = '{"jsonrpc":"2.0","id":6,"method":"tools/call","params":{"name":"export_database","arguments":{"database_path":"C:\\code\\DB2XL\\TestData\\mcp_demo.db","output_directory":"C:\\code\\DB2XL\\TestData\\export_output","format":"jsonl"}}}'
Send-McpRequest -Request $exportRequest -Description "Export database to JSONL format"

Write-Host "`n🎉 MCP Server test completed!" -ForegroundColor Cyan

# Show final status
if (Test-Path $dbPath) {
    $size = (Get-Item $dbPath).Length
    Write-Host "📊 Final database: $dbPath ($size bytes)" -ForegroundColor Green
}

if (Test-Path "C:\code\DB2XL\TestData\export_output") {
    $files = Get-ChildItem "C:\code\DB2XL\TestData\export_output" -Recurse
    Write-Host "📦 Export files created: $($files.Count) files" -ForegroundColor Green
    $files | ForEach-Object { Write-Host "   - $($_.Name)" -ForegroundColor Gray }
}