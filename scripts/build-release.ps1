# DB2XL Release Build Script
param(
    [string]$Configuration = "Release",
    [string]$OutputPath = "./artifacts",
    [switch]$SkipTests = $false
)

Write-Host "🚀 DB2XL Release Build Script" -ForegroundColor Green
Write-Host "Configuration: $Configuration" -ForegroundColor Cyan
Write-Host "Output Path: $OutputPath" -ForegroundColor Cyan

# Clean previous builds
Write-Host "`n🧹 Cleaning previous builds..." -ForegroundColor Yellow
dotnet clean --configuration $Configuration --verbosity minimal

# Restore dependencies
Write-Host "`n📦 Restoring dependencies..." -ForegroundColor Yellow
dotnet restore --verbosity minimal

# Build solution
Write-Host "`n🔨 Building solution..." -ForegroundColor Yellow
dotnet build --configuration $Configuration --no-restore --verbosity minimal

if ($LASTEXITCODE -ne 0) {
    Write-Host "❌ Build failed!" -ForegroundColor Red
    exit 1
}

# Run tests (unless skipped)
if (-not $SkipTests) {
    Write-Host "`n🧪 Running tests..." -ForegroundColor Yellow
    dotnet test --configuration $Configuration --no-build --verbosity minimal
    
    if ($LASTEXITCODE -ne 0) {
        Write-Host "❌ Tests failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "✅ All tests passed!" -ForegroundColor Green
}

# Create output directory
Write-Host "`n📁 Creating output directory..." -ForegroundColor Yellow
New-Item -Path $OutputPath -ItemType Directory -Force | Out-Null

# Pack NuGet package
Write-Host "`n📦 Creating NuGet package..." -ForegroundColor Yellow
dotnet pack SqliteXport/SqliteXport.csproj `
    --configuration $Configuration `
    --no-build `
    --output $OutputPath `
    --verbosity minimal

# Copy additional files
Write-Host "`n📋 Copying documentation..." -ForegroundColor Yellow
Copy-Item "README.md" -Destination "$OutputPath/"
Copy-Item "LICENSE" -Destination "$OutputPath/"
Copy-Item "CLAUDE.md" -Destination "$OutputPath/"

# Generate build info
$buildInfo = @{
    BuildTime = Get-Date -Format "yyyy-MM-dd HH:mm:ss UTC"
    Configuration = $Configuration
    Version = (Select-Xml -Path "SqliteXport/SqliteXport.csproj" -XPath "//Version").Node.InnerText
    Commit = (git rev-parse HEAD 2>$null) ?? "unknown"
    Branch = (git branch --show-current 2>$null) ?? "unknown"
} | ConvertTo-Json -Depth 2

$buildInfo | Out-File -FilePath "$OutputPath/build-info.json" -Encoding UTF8

Write-Host "`n✅ Release build completed successfully!" -ForegroundColor Green
Write-Host "📁 Artifacts available in: $OutputPath" -ForegroundColor Cyan

# List generated files
Write-Host "`n📋 Generated files:" -ForegroundColor Cyan
Get-ChildItem $OutputPath | ForEach-Object {
    Write-Host "   $($_.Name) ($($_.Length) bytes)" -ForegroundColor Gray
}