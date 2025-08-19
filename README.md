# DB2XL - Deterministic SQLite to Excel Exporter

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?style=flat-square)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-Proprietary-red?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-Passing-brightgreen?style=flat-square)](#testing)

A robust, deterministic SQLite to Excel exporter that converts every table in a SQLite database to a multi-sheet Excel (.xlsx) file with **byte-for-byte consistent output**.

## 🎯 Key Features

- **🔒 Deterministic Output**: Same database → same Excel file, bit-for-bit identical
- **📊 Fidelity First**: Default text-only mode preserves exact data representation
- **🚀 Robust & Scalable**: Handles large tables with automatic sheet splitting
- **📋 Complete Metadata**: SHA-256 checksums and export provenance tracking
- **⚙️ Simple API**: One method call with sensible defaults
- **🔍 Safe Operations**: Read-only database access with snapshot consistency

## 🚀 Quick Start

### Installation

```bash
# Clone the repository
git clone https://github.com/revred/DB2XL.git
cd DB2XL

# Build the solution
dotnet build

# Run tests
dotnet test
```

### Basic Usage

```csharp
using DB2XL;

// Export with default settings (maximum fidelity)
SqliteToExcel.Export(
    sqlitePath: "path/to/database.sqlite",
    xlsxPath: "path/to/output.xlsx"
);
```

### Advanced Usage

```csharp
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
│   └── ...
├── SqliteXport.Tests/        # Test suite
│   ├── ExportTests.cs        # Integration tests
│   ├── ExportValidator.cs    # Data integrity validation
│   ├── SampleDatabaseGenerator.cs # Test data creation
│   └── ...
└── CLAUDE.md                 # Complete specification
```

## 🧪 Testing

The project includes comprehensive tests covering:

- **Data Integrity**: Round-trip validation with checksums
- **Edge Cases**: Unicode, special characters, NULL values, empty tables
- **Performance**: Large datasets (1K-10K+ rows) with timing metrics
- **Excel Limits**: Sheet splitting for oversized tables
- **Metadata Validation**: Complete export provenance tracking

### Running Tests

```bash
# Run all tests
dotnet test

# Run specific test categories
dotnet test --filter "Export_DatabaseWithSize_ShouldCreateValidExcelFile"

# Run with detailed output
dotnet test --verbosity normal
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

1. Check existing [Issues](https://github.com/revred/DB2XL/issues)
2. Review the [complete specification](CLAUDE.md)
3. Run the test suite to verify your environment
4. Create a new issue with:
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