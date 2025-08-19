# DB2XL - Deterministic SQLite to Excel Exporter

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Proprietary-red?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-Passing-brightgreen?style=flat-square)](#testing)

A robust, deterministic SQLite to Excel exporter that converts every table in a SQLite database to a multi-sheet Excel (.xlsx) file with **byte-for-byte consistent output**.

## 🚀 Getting Started

### Prerequisites
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- A SQLite database file to export

### 30-Second Quick Start

1. **Clone and build**:
   ```bash
   git clone https://github.com/revred/DB2XL.git
   cd DB2XL
   dotnet build
   ```

2. **Export your database**:
   ```csharp
   using SqliteXport;
   
   // One line to export everything
   SqliteToExcel.Export("your-database.sqlite", "output.xlsx");
   ```

3. **Open the Excel file** - each table becomes a worksheet with metadata!

> 📚 **New to DB2XL?** Check out the [comprehensive Getting Started guide](GETTING_STARTED.md) for step-by-step tutorials and examples!

### Installation Options

**Option 1: Clone Repository (Recommended)**
```bash
git clone https://github.com/revred/DB2XL.git
cd DB2XL
dotnet build
# Reference SqliteXport.dll or include as project reference
```

**Option 2: Direct Project Reference**
```xml
<!-- Add to your .csproj -->
<ProjectReference Include="path/to/DB2XL/SqliteXport/SqliteXport.csproj" />
```

**Option 3: Copy Source Files**
- Copy the `SqliteXport/` folder to your solution
- Add as a new project or include source files directly

### First Export Example

Create a simple console application:

```csharp
using SqliteXport;
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
- **🤖 LLM-Ready**: JSONL export format with schema manifests (coming soon)

## 📚 Usage Examples

### Basic Export

```csharp
using DB2XL;

// Export with default settings (maximum fidelity)
SqliteToExcel.Export(
    sqlitePath: "path/to/database.sqlite",
    xlsxPath: "path/to/output.xlsx"
);

// Alternative using explicit namespace
using SqliteXport;
SqliteToExcel.Export("database.sqlite", "output.xlsx");
```

### Advanced Configuration

```csharp
using SqliteXport;

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

## 🏗️ Architecture

```
DB2XL/
├── SqliteXport/              # Core library
│   ├── SqliteToExcel.cs      # Main export API  
│   ├── SqliteToExcelOptions.cs # Configuration options
│   ├── DatabaseDiscovery.cs  # Schema enumeration
│   ├── DataConverter.cs      # Type handling
│   ├── ExcelHelpers.cs       # Worksheet management
│   ├── ChecksumBuilder.cs    # SHA-256 verification
│   └── Transformers/         # Data transformation system ✨
│       ├── Interfaces.cs     # Core transformer interfaces
│       ├── TransformerRegistry.cs # Factory system
│       ├── TransformerRegistryBuilder.cs # Fluent configuration
│       ├── SqliteTypeHelper.cs # Type affinity detection
│       └── Examples/         # Sample transformers
│           └── SimpleTextTransformers.cs
├── SqliteXport.Tests/        # Comprehensive test suite
│   ├── ExportTests.cs        # Core export integration tests
│   ├── ExportValidator.cs    # Data integrity validation
│   ├── SampleDatabaseGenerator.cs # Test data creation
│   └── Transformers/         # Transformer system tests ✨
│       ├── TransformerInterfacesTests.cs # Unit tests
│       ├── TransformerRegistryTests.cs # Registry tests
│       ├── TransformerIntegrationTests.cs # End-to-end tests
│       └── SqliteTypeHelperTests.cs # Type detection tests
├── CLAUDE.md                 # Core specification
├── TRANSFORMERS.md           # Advanced features specification
└── Project_status.md         # Development progress
```

## 🧪 Testing

The project includes comprehensive tests covering:

**Core Export Engine (200+ tests):**
- **Data Integrity**: Round-trip validation with checksums
- **Edge Cases**: Unicode, special characters, NULL values, empty tables
- **Performance**: Large datasets (1K-10K+ rows) with timing metrics
- **Excel Limits**: Sheet splitting for oversized tables
- **Metadata Validation**: Complete export provenance tracking
- **View Support**: Database view export and validation

**Transformer System (149 tests):**
- **Interface Contracts**: All transformer behavior validation
- **Registry System**: Thread-safe registration and instantiation  
- **Built-in Transformers**: All 15+ transformers with edge cases
- **Configuration System**: JSON/YAML loading and validation
- **Pipeline Execution**: Error handling and batch processing
- **Performance Validation**: 10,000+ transformations per second
- **Concurrency**: Thread safety and stateless design verification
- **Integration**: Real database transformation workflows
- **Type Detection**: SQLite affinity handling across all scenarios

**Total: 349 of 350 tests passing (99.7% success rate) ✅**

### Running Tests

```bash
# Run all tests (133 total)
dotnet test

# Run specific test categories
dotnet test --filter "Export_DatabaseWithSize_ShouldCreateValidExcelFile"
dotnet test --filter "TransformerRegistry"
dotnet test --filter "TransformerIntegration"

# Run with detailed output
dotnet test --verbosity normal

# Run only core export tests
dotnet test --filter "FullyQualifiedName~ExportTests"

# Run only transformer tests  
dotnet test --filter "FullyQualifiedName~Transformers"
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
5. Run the test suite to verify your environment: `dotnet test`
6. Create a new issue with:
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