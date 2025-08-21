# DB2XL - Enterprise SQLite Export Platform

[![.NET](https://img.shields.io/badge/.NET-9.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Proprietary-red?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-812%2F829%20Passing-brightgreen?style=flat-square)](#testing)
[![Coverage](https://img.shields.io/badge/Coverage-72%25-green?style=flat-square)](#testing)

An enterprise-grade SQLite export platform with **8-component modular architecture**, advanced data transformation, and AI-ready export formats. Convert SQLite databases to Excel/JSONL with **byte-for-byte deterministic output**.

## 🚀 Getting Started

### Prerequisites
- [.NET 9.0 SDK](https://dotnet.microsoft.com/download) or later
- A SQLite database file to export

### 30-Second Quick Start

1. **Clone and build**:
   ```bash
   git clone https://github.com/revred/DB2XL.git
   cd DB2XL
   dotnet build
   ```

2. **Console Tool (Recommended)**:
   ```bash
   # Simple Excel export
   dotnet run --project DB2XL.Console export database.sqlite output.xlsx
   
   # With advanced features
   dotnet run --project DB2XL.Console export database.sqlite output.xlsx --transform --include-views
   ```

3. **Programmatic API**:
   ```csharp
   using DB2XL.Export.Legacy;
   
   // Legacy compatibility layer
   SqliteToExcel.Export("database.sqlite", "output.xlsx");
   
   // Modern modular approach
   using DB2XL.Export.Excel;
   var exporter = new ExcelExporter();
   await exporter.ExportAsync("database.sqlite", "output.xlsx");
   ```

4. **Open the Excel file** - each table becomes a worksheet with comprehensive metadata!

> 📚 **New to DB2XL?** Check out the [comprehensive Getting Started guide](GETTING_STARTED.md) for step-by-step tutorials and examples!

### Installation Options

**Option 1: Clone Repository (Recommended)**
```bash
git clone https://github.com/revred/DB2XL.git
cd DB2XL
dotnet build
# Reference DB2XL.Export.Legacy.dll or include as project reference
```

**Option 2: Direct Project Reference**
```xml
<!-- Add to your .csproj -->
<ProjectReference Include="path/to/DB2XL/DB2XL.Export.Legacy/DB2XL.Export.Legacy.csproj" />
```

**Option 3: Copy Source Files**
- Copy the `DB2XL.Export.Legacy/` folder to your solution
- Add as a new project or include source files directly

### First Export Example

Create a simple console application:

```csharp
using DB2XL.Export.Legacy;
using System;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            // Basic export with default settings
            SqliteToExcel.Export(
                sqlitePath: "sample.sqlite",
                xlsxPath: "export.xlsx"
            );
            
            Console.WriteLine("✅ Export completed successfully!");
            Console.WriteLine("Check export.xlsx for results.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Export failed: {ex.Message}");
        }
    }
}
```

### What You Get

DB2XL creates an Excel file with:
- **📋 One worksheet per table/view** with proper column headers
- **🔍 Exact data representation** - no precision loss or type coercion
- **📈 Metadata sheet** with checksums, export options, and database info
- **🔁 Deterministic output** - same database always produces identical Excel
- **⚡ Performance optimized** - handles large tables via streaming

## 🎯 Key Features

- **🔒 Deterministic Output**: Same database → same Excel file, bit-for-bit identical
- **📊 Fidelity First**: Default text-only mode preserves exact data representation
- **🚀 Robust & Scalable**: Handles large tables with automatic sheet splitting
- **📋 Complete Metadata**: SHA-256 checksums and export provenance tracking
- **⚙️ Simple API**: One method call with sensible defaults
- **🔍 Safe Operations**: Read-only database access with snapshot consistency
- **🔄 Data Transformation**: Advanced transformer system for human-readable output
- **🤖 AI-Ready**: JSONL export format with schema manifests for LLM workflows
- **🛠️ Console Tool**: Command-line interface designed for AI assistant integration

## 📚 Usage Examples

### Basic Export

```csharp
using DB2XL.Export.Legacy;

// Export with default settings (maximum fidelity)
SqliteToExcel.Export(
    sqlitePath: "path/to/database.sqlite",
    xlsxPath: "path/to/output.xlsx"
);

// Alternative using full namespace
DB2XL.Export.Legacy.SqliteToExcel.Export("database.sqlite", "output.xlsx");
```

### Advanced Configuration

```csharp
using DB2XL.Export.Legacy;

var options = new SqliteToExcelOptions
{
    WriteAllAsText = true,              // Prime directive: preserve exact data
    IncludeMetadataSheet = true,        // Add export metadata and checksums
    BlobMode = BlobRenderMode.Hex,      // Render BLOBs as hex strings
    IncludeViews = true,                // Export database views
    OrderRowsDeterministically = true,  // Consistent row ordering
    SplitOversizeSheets = true,         // Handle tables > 1M rows
    ReadBatchSize = 25000              // Memory-efficient processing
};

SqliteToExcel.Export("database.sqlite", "export.xlsx", options);
```

### Data Transformation (Advanced)

DB2XL includes a comprehensive transformer system with 15+ built-in transformers for making raw database values human-readable:

```csharp
using DB2XL.Transformers;
using DB2XL.Configuration;

// Create a registry with built-in transformers
var registry = new TransformerRegistryBuilder()
    .AddTextTransformers()
    .AddTimeTransformers() 
    .AddJsonTransformers()
    .AddBinaryTransformers()
    .Build();

// Transform Unix timestamp to readable date
var epochTransformer = registry.CreateCell("epoch", new Dictionary<string, string>
{
    ["unit"] = "ms",
    ["format"] = "yyyy-MM-dd HH:mm:ss",
    ["tz"] = "UTC"
});

var context = new CellContext("events", "timestamp", 0, SqliteAffinity.Integer);
var result = epochTransformer.Transform(context, "1692100856000"); 
// Returns: "2023-08-15 12:00:56"

// Pretty-print JSON data
var jsonTransformer = registry.CreateCell("json-pretty", new Dictionary<string, string>
{
    ["indent"] = "  ",
    ["maxDepth"] = "5"
});

// Mask sensitive information
var maskTransformer = registry.CreateCell("mask", new Dictionary<string, string>
{
    ["type"] = "email"  // auto-detects email format
});
var maskedEmail = maskTransformer.Transform(
    new CellContext("users", "email", 0, SqliteAffinity.Text),
    "john.doe@example.com"
);
// Returns: "jo*********@example.com"
```

**Built-in Transformer Categories:**

**Text Transformers (10 transformers):**
- `upper`, `lower`, `title-case` - Case conversion with culture support
- `trim`, `truncate`, `coalesce` - Text processing and cleanup
- `regex-replace`, `mask` - Pattern matching and PII protection
- `normalize-whitespace`, `sanitize` - Text normalization and sanitization

**Date/Time Transformers (5 transformers):**
- `epoch` - Unix timestamps (seconds/milliseconds/microseconds/nanoseconds)
- `ticks` - .NET ticks to ISO 8601
- `julian-day` - SQLite Julian Day conversion
- `date-format` - Format and timezone conversion
- `date-part` - Extract components (year, month, day, etc.)

**JSON Transformers (6 transformers):**
- `json-compact`, `json-pretty` - Formatting and whitespace control
- `json-extract`, `json-flatten` - Data extraction and restructuring
- `json-validate`, `json-count` - Validation and analysis

**Binary/Encoding Transformers (1 transformer):**
- `binary-json-decode` - Auto-detect and decode Base64/Hex JSON

### Configuration-Driven Transformations

For complex scenarios, use JSON/YAML configuration files:

```json
{
  "version": "1.0",
  "global": {
    "enableTransformations": true,
    "errorHandling": "LogAndContinue"
  },
  "tables": {
    "events": {
      "columns": {
        "timestamp": [
          {
            "name": "epoch",
            "config": {
              "unit": "ms",
              "format": "yyyy-MM-dd HH:mm:ss",
              "tz": "UTC"
            }
          }
        ],
        "payload": [
          {
            "name": "json-pretty",
            "config": {
              "indent": "  "
            }
          }
        ]
      }
    }
  }
}
```

```csharp
// Load configuration and create pipeline
var config = await ConfigurationLoader.LoadFromFileAsync("transformations.json");
var pipeline = new TransformationPipeline(config, registry);

// Transform data using pipeline
var transformedValue = pipeline.TransformCell(
    "events", 
    "timestamp", 
    "1692100856000",
    new CellContext("events", "timestamp", 0, SqliteAffinity.Integer)
);
```

> 📚 **Complete Transformer Guide**: See [**TRANSFORMERS.md**](TRANSFORMERS.md) for comprehensive documentation of all 15+ built-in transformers, configuration options, performance tuning, and custom transformer development.

## 📖 Documentation

### Core Principles

1. **Fidelity First**: By default, everything is written as text to guarantee exact data representation
2. **Deterministic**: Identical databases produce identical Excel files (excluding timestamps)
3. **Robust**: Graceful handling of edge cases, large data, and Excel limitations
4. **Safe**: Read-only database access with transaction snapshots

### Export Process

1. **Database Discovery**: Enumerate tables and views with deterministic ordering
2. **Schema Analysis**: Extract column information and primary key structure  
3. **Data Streaming**: Process tables in batches with consistent row ordering
4. **Excel Generation**: Create worksheets with proper formatting and metadata
5. **Validation**: Generate SHA-256 checksums for data integrity verification

### Excel Output Structure

- **One sheet per table/view** with sanitized names
- **Header row** with column names (bold, gray background)
- **Data rows** with consistent text formatting
- **Metadata sheet** (`_Export_Metadata`) containing:
  - Database information (path, size, version)
  - Export options and timestamp
  - Per-table statistics and checksums
  - Data integrity verification hashes

### Data Type Handling

| SQLite Type | Default Output | With PreserveNumericTypes |
|-------------|----------------|---------------------------|
| NULL        | Empty cell     | Empty cell               |
| TEXT        | Text string    | Text string              |
| INTEGER     | Text string    | Excel number             |
| REAL        | Text string    | Excel number             |
| BLOB        | Hex/Base64     | Hex/Base64               |

## 🏗️ Architecture - 8-Component Modular System

```
DB2XL Enterprise Platform
├── Core Foundation Layer
│   ├── DB2XL.Core/                    # 🏛️ Foundational models & interfaces
│   │   ├── Models/                    #    ColumnInfo, TableInfo, ExportResult
│   │   ├── Enums/                     #    BlobRenderMode, OrderMode  
│   │   ├── Exceptions/                #    ExportException, ValidationException
│   │   └── Interfaces/                #    IExporter, core contracts
│   ├── DB2XL.Data/                    # 💾 Schema discovery & data access
│   │   ├── Schema/                    #    SqliteSchemaReader, PrimaryKeyDiscovery
│   │   ├── Query/                     #    SqlQueryBuilder
│   │   └── Checksum/                  #    DataChecksumCalculator
│   └── DB2XL.Query/                   # 🔍 Advanced querying & security
│       ├── SecurityFilter.cs          #    SQL injection protection
│       ├── QueryPlanAnalyzer.cs       #    Performance analysis
│       ├── MissingIndexDetector.cs    #    Optimization suggestions
│       └── SelectionGrammar.cs        #    Query DSL parsing
├── Transformation Engine
│   └── DB2XL.Transform/               # ⚡ 15+ built-in transformers
│       ├── BuiltIns/                  #    Text, JSON, DateTime, Binary transformers
│       ├── Configuration/             #    JSON/YAML config loading
│       ├── Interfaces/                #    ICellTransformer, IRowTransformer
│       ├── Registry/                  #    TransformerRegistry, factory pattern
│       └── TypeDetection/             #    SqliteTypeHelper, affinity detection
├── Export Engines  
│   ├── DB2XL.Export.Excel/            # 📊 High-performance Excel export
│   │   ├── ExcelExporter.cs           #    ClosedXML-based implementation
│   │   └── ExcelExportOptions.cs      #    Configuration record
│   └── DB2XL.Export.JsonLines/        # 🤖 JSONL for LLM/AI processing
│       └── JsonLinesExporter.cs       #    AI-ready output format
├── Advanced Features
│   └── DB2XL.Delta/                   # 📈 Delta exports & change tracking
│       ├── ChangeLogDeltaService.cs   #    Changelog-based deltas
│       ├── WatermarkDeltaService.cs   #    Timestamp-based deltas
│       └── DeltaExportService.cs      #    Unified delta export API
├── User Interface
│   └── DB2XL.Console/                 # 🖥️ Rich CLI with colored output
│       ├── Commands/                  #    ExportCommand, AnalyzeCommand
│       ├── Options/                   #    Command-line argument parsing
│       └── Helpers/                   #    ConsoleHelper, formatting utilities
├── Legacy Compatibility
│   └── DB2XL.Export.Legacy/           # 🔄 Backward compatibility layer
│       ├── SqliteToExcel.cs           #    Legacy static API
│       ├── DataConverter.cs           #    Type conversion utilities
│       ├── JsonLinesExporter.cs       #    JSONL export implementation
│       └── Schema/                    #    Legacy schema analysis
└── Test Infrastructure
    ├── DB2XL.Core.Tests/              # 🧪 Foundation component tests (137 tests)
    ├── DB2XL.Query.Tests/             # 🔒 Security & performance tests (262 tests)  
    └── DB2XL.Integration.Tests/       # 🚀 Integration & transformation tests (430 tests)
```

### Component Responsibilities

**Core Foundation**:
- **DB2XL.Core**: Shared models, enums, and interfaces across all components
- **DB2XL.Data**: Database schema discovery, query building, and data access patterns
- **DB2XL.Query**: Advanced querying capabilities with security and performance analysis

**Transformation Engine**:
- **DB2XL.Transform**: Complete framework with 15+ built-in transformers and configuration system

**Export Engines**:
- **DB2XL.Export.Excel**: High-performance Excel export with deterministic output
- **DB2XL.Export.JsonLines**: AI-ready JSONL export with schema manifests

**Advanced Features**:
- **DB2XL.Delta**: Delta export capabilities for incremental data processing

**User Interface & Compatibility**:
- **DB2XL.Console**: Feature-rich CLI tool with AI assistant integration
- **DB2XL.Export.Legacy**: Legacy compatibility layer maintaining backward compatibility

## 🧪 Testing - Comprehensive Test Coverage

**812 of 829 tests passing (97.9% success rate)** with 72.0% code coverage across all components.

### Test Distribution by Component

**DB2XL.Core.Tests (137/137 tests - 100% success)**:
- **Foundation Models**: Complete coverage of ColumnInfo, TableInfo, OrderInfo, ExportResult
- **Exception Handling**: ExportException, ValidationException, DataConversionException  
- **Data Services**: PrimaryKeyDiscoveryService, SyntheticPrimaryKeyGenerator
- **Edge Cases**: Record equality, with expressions, null handling

**DB2XL.Query.Tests (261/262 tests - 99.6% success)**:
- **Security Features**: SQL injection protection, parameter validation
- **Performance Analysis**: Query plan analysis, missing index detection
- **Selection Grammar**: Query DSL parsing and validation
- **Integration Testing**: Real database scenarios with complex queries

**DB2XL.Integration.Tests (414/430 tests - 96.3% success)**:
- **Core Export Engine**: Data integrity validation with checksums
- **Transformation System**: All 15+ transformers with edge cases
- **Configuration System**: JSON/YAML loading and validation
- **Performance Testing**: Large datasets (10K+ rows) with timing metrics
- **Integration Workflows**: End-to-end export and transformation scenarios

### Test Categories

**Unit Testing**:
- All interfaces, models, and core logic thoroughly tested
- Edge cases: Unicode, special characters, NULL values, empty tables
- Type detection: SQLite affinity handling across all scenarios

**Integration Testing**:
- Real database scenarios with complex data sets
- Transformer pipeline execution with error handling
- Console integration tests with command-line parsing

**Performance Testing**:
- Validated for enterprise-scale workloads (10K+ operations/second)
- Memory usage patterns and batch processing efficiency
- Concurrent access and thread safety verification

**Security Testing**:
- SQL injection protection and parameter validation
- Safe data handling and read-only database access patterns

### Running Tests

```bash
# Run all tests (829 total across 3 test projects)
dotnet test

# Run with code coverage
dotnet test --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# Generate coverage report
dotnet tool install -g dotnet-reportgenerator-globaltool
reportgenerator -reports:"TestResults/*/coverage.cobertura.xml" -targetdir:"TestResults/CoverageReport"

# Run specific test projects
dotnet test DB2XL.Core.Tests/
dotnet test DB2XL.Query.Tests/
dotnet test DB2XL.Integration.Tests/

# Run specific test categories
dotnet test --filter "PrimaryKeyDiscovery"
dotnet test --filter "TransformerRegistry" 
dotnet test --filter "SecurityFilter"
dotnet test --filter "ExportTests"

# Run with detailed output
dotnet test --verbosity normal

# Performance test with timing
dotnet test --filter "Performance" --logger:console;verbosity=detailed
```

### Test Data

The test suite includes:
- **Standard Tables**: Customers, Products, Orders (business data)
- **Edge Cases**: Unicode text, special characters, long strings
- **Performance Data**: Configurable large datasets (1K-10K+ rows)
- **Binary Data**: BLOB handling with various rendering modes
- **Views**: Database view export testing

## 📋 Configuration Options

### SqliteToExcelOptions

| Property | Default | Description |
|----------|---------|-------------|
| `WriteAllAsText` | `true` | **Prime directive**: Write all data as text for fidelity |
| `PreserveNumericTypes` | `false` | Allow Excel numeric formatting (may lose precision) |
| `IncludeMetadataSheet` | `true` | Add comprehensive export metadata |
| `MetadataSheetName` | `"_Export_Metadata"` | Name for metadata worksheet |
| `IncludeViews` | `false` | Export database views as sheets |
| `BlobMode` | `BlobRenderMode.Hex` | BLOB rendering: Skip/Hex/Base64 |
| `OrderRowsDeterministically` | `true` | Consistent row ordering via PK/rowid |
| `SplitOversizeSheets` | `true` | Split tables exceeding Excel limits |
| `ReadBatchSize` | `25000` | Rows per memory batch |
| `CommandTimeoutSeconds` | `180` | Database query timeout |

### BLOB Rendering Modes

- **Skip**: Leave BLOB cells empty
- **Hex**: Uppercase hexadecimal representation (e.g., `0A3F...`)
- **Base64**: Standard base64 encoding

### Transformation Configuration

For advanced data transformation, see the [**Transformer System Guide**](TRANSFORMERS.md) which covers:

- **15+ Built-in Transformers**: Complete reference with examples
- **Configuration Format**: JSON/YAML structure and options
- **Performance Settings**: Batch processing and parallel execution
- **Error Handling**: Multiple strategies for robust processing
- **Custom Transformers**: Extension and plugin development
- **Best Practices**: Security, performance, and testing guidelines

## 🔧 Limitations & Considerations

### Excel Constraints
- **Maximum rows per sheet**: 1,048,576 (automatic splitting available)
- **Maximum columns**: 16,384 (error if exceeded)
- **Sheet name length**: 31 characters (automatic truncation/sanitization)

### Performance Guidelines
- **Memory usage**: Grows with batch size and table width
- **Large exports**: Consider streaming variant for >1M rows
- **Storage**: Place database on fast local storage (SSD recommended)

### Data Fidelity Notes
- **Leading zeros**: Preserved in text mode, lost in numeric mode
- **Date formats**: Remain as text unless post-processed
- **Scientific notation**: Preserved as text representation
- **Unicode**: Full support including RTL scripts and emojis

## 🤝 Contributing

This is a proprietary project. Please contact the maintainer for contribution guidelines.

## 📜 License

**Proprietary Software** - Unauthorized copying, modification, or distribution is prohibited without explicit written consent from the owner.

## 🆘 Support

For issues, questions, or feature requests:

1. **Start with the [Getting Started Guide](GETTING_STARTED.md)** for tutorials and examples
2. Check existing [Issues](https://github.com/revred/DB2XL/issues)
3. Review the [complete specification](CLAUDE.md) for advanced features
4. Explore [transformer documentation](TRANSFORMERS.md) for data transformation
5. **AI Assistant Integration**: DB2XL.Console includes rich CLI designed for Claude and AI debugging workflows
6. Run the test suite to verify your environment: `dotnet test`
7. Create a new issue with:
   - Database schema details
   - Error messages and stack traces
   - Expected vs actual behavior
   - Test database (if possible)

## 📊 Example Output

### Console Output
```
🚀 Large Database Export Test Results:
📝 Large database for performance testing
📁 Database: 2,547,892 bytes
📊 Excel: 3,422,156 bytes  
⏱️ Export Time: 1,247 ms
📈 Rows per second: 8,019
📍 Location: /tmp/large_test_abc123.xlsx

📋 Tables found: 1
📋 Validation: ✅ PASSED
   ✅ LargePerformanceTest: 10000→10000 rows, 7→7 cols

💡 Export completed - check file for results!
```

### Metadata Sheet Content
```
Export Metadata
===============

Database Information
Database Path: /path/to/sample.sqlite
File Size (bytes): 45,056
Last Modified (UTC): 2024-08-19 12:34:56
Journal Mode: delete
User Version: 42
Schema Version: 1

Export Options
Write All As Text: Yes
Preserve Numeric Types: No
Include Views: Yes
BLOB Mode: Hex

Table Export Summary
Table Name | Type  | Row Count | Column Count | Split Sheets | Order Mode | SHA256 Checksum
-----------|-------|-----------|--------------|--------------|------------|----------------
Customers  | table | 5         | 11           | 1            | PrimaryKey | A1B2C3D4E5F6...
Products   | table | 5         | 9            | 1            | PrimaryKey | F6E5D4C3B2A1...
...
```

---

**Made with ❤️ for reliable data export workflows**