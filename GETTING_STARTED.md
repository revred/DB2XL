# Getting Started with DB2XL

DB2XL is a deterministic SQLite to Excel/JSONL exporter that transforms your database into clean, readable formats. This guide will get you up and running in just a few minutes.

## 🎯 What You'll Learn

By the end of this guide, you'll know how to:
- Export any SQLite database to Excel or JSONL with perfect fidelity
- Use the **console tool** for quick database analysis and export
- Use **advanced transformations** to make data human-readable
- Customize exports with various options
- Verify data integrity with built-in checksums
- Analyze database structure and performance

## 📋 Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download) or later
- A SQLite database file (we'll create one if you don't have one)
- Basic familiarity with C# (optional - examples provided)

## 🚀 Step 1: Installation

### Option A: Clone the Repository (Recommended)
```bash
git clone https://github.com/revred/DB2XL.git
cd DB2XL
dotnet build
```

### Option B: Download and Extract
1. Download the repository as ZIP
2. Extract to your desired location
3. Open terminal/command prompt in the extracted folder
4. Run `dotnet build`

### Verify Installation
```bash
dotnet test
```
You should see: `Passed! - Failed: 0, Passed: 400, Skipped: 0` (100% success rate)

### Build the Console Tool
```bash
dotnet build SqliteXport.Console
```

## 🚀 Step 2: Quick Start with Console Tool

### Analyze Any Database
The fastest way to inspect a SQLite database:

```bash
# Analyze database structure and content
dotnet run --project SqliteXport.Console -- analyze sample.sqlite

# Include data samples and performance metrics
dotnet run --project SqliteXport.Console -- analyze sample.sqlite --include-data --performance

# Check database integrity
dotnet run --project SqliteXport.Console -- analyze sample.sqlite --check-integrity
```

### Export with One Command
```bash
# Export to Excel with intelligent transformations
dotnet run --project SqliteXport.Console -- export sample.sqlite output.xlsx --transform

# Export to JSONL for AI/ML processing
dotnet run --project SqliteXport.Console -- export sample.sqlite output/ --format jsonl --transform

# Export with dual sheets (raw + transformed data)
dotnet run --project SqliteXport.Console -- export sample.sqlite report.xlsx --dual-sheets --metadata
```

## 📊 Step 3: Programmatic API (Your First Export)

### Create a Test Database (Optional)
If you don't have a SQLite database, let's create one:

```csharp
using Microsoft.Data.Sqlite;

// Create a simple test database
using var connection = new SqliteConnection("Data Source=sample.sqlite");
connection.Open();

using var cmd = connection.CreateCommand();
cmd.CommandText = @"
    CREATE TABLE employees (
        id INTEGER PRIMARY KEY,
        name TEXT NOT NULL,
        email TEXT,
        hire_date TEXT,
        salary REAL
    );

    INSERT INTO employees VALUES 
    (1, 'John Doe', 'john@company.com', '2023-01-15', 75000.0),
    (2, 'Jane Smith', 'jane@company.com', '2023-02-01', 82000.0),
    (3, 'Bob Wilson', 'bob@company.com', '2023-03-10', 68000.0);

    CREATE VIEW high_earners AS 
    SELECT name, email, salary FROM employees WHERE salary > 70000;
";
cmd.ExecuteNonQuery();
```

### Basic Export
Now let's export it to Excel:

```csharp
using SqliteXport;

// One line export with default settings
SqliteToExcel.Export("sample.sqlite", "employees.xlsx");

Console.WriteLine("✅ Export completed! Check employees.xlsx");
```

### What You Get
Open `employees.xlsx` and you'll find:
- **employees** sheet with your data (text format for perfect fidelity)
- **_Export_Metadata** sheet with checksums and export details

## ⚙️ Step 4: Custom Configuration

Let's create a more sophisticated export:

```csharp
using SqliteXport;

var options = new SqliteToExcelOptions
{
    WriteAllAsText = true,              // Keep exact data (recommended)
    IncludeViews = true,                // Export database views too
    BlobMode = BlobRenderMode.Hex,      // Show binary data as hex
    IncludeMetadataSheet = true,        // Include verification data
    OrderRowsDeterministically = true,  // Consistent row ordering
    SplitOversizeSheets = true,         // Handle huge tables
    ReadBatchSize = 10000              // Memory optimization
};

SqliteToExcel.Export("sample.sqlite", "employees_advanced.xlsx", options);

Console.WriteLine("✅ Advanced export completed!");
```

### Result
Your Excel file now includes:
- **employees** sheet (data)
- **high_earners** sheet (view data)
- **_Export_Metadata** sheet (checksums, options, database info)

## 🔄 Step 5: Data Transformations (Advanced)

### Console Tool Transformations
The console tool includes **22 built-in transformers** for making data human-readable:

```bash
# Export with automatic transformations
dotnet run --project SqliteXport.Console -- export app.db readable.xlsx --transform

# Use custom transformation config
dotnet run --project SqliteXport.Console -- export app.db custom.xlsx --config transforms.json

# Preview transformations without exporting
dotnet run --project SqliteXport.Console -- export app.db preview.xlsx --dry-run --transform
```

### Configuration-Driven Transformations
Create a `transforms.json` file for custom transformations:

```json
{
  "version": "1.0",
  "global": {
    "enableTransformations": true,
    "errorHandling": "LogAndContinue"
  },
  "tables": {
    "employees": {
      "columns": {
        "hire_date": {
          "transformers": [
            {
              "name": "epoch",
              "config": { "format": "yyyy-MM-dd" }
            }
          ]
        },
        "email": {
          "transformers": [
            {
              "name": "mask",
              "config": { "preserveLength": "true" }
            }
          ]
        }
      }
    }
  }
}
```

### Available Built-in Transformers
#### Time & Date
- **`epoch`** - Unix timestamp → ISO-8601 dates
- **`ticks`** - .NET ticks → ISO-8601 dates
- **`julian-day`** - SQLite Julian Day → ISO-8601 dates
- **`date-format`** - Custom date formatting
- **`date-part`** - Extract year/month/day components

#### JSON Processing
- **`json-pretty`** - Format JSON for readability
- **`json-compact`** - Minify JSON
- **`json-extract`** - Extract specific JSON properties
- **`json-flatten`** - Flatten nested JSON objects
- **`json-validate`** - Validate JSON syntax
- **`json-count`** - Count JSON array elements

#### Text Processing
- **`upper`** - Convert to uppercase
- **`lower`** - Convert to lowercase
- **`title-case`** - Convert to Title Case
- **`trim`** - Remove whitespace
- **`truncate`** - Limit text length
- **`normalize-whitespace`** - Clean whitespace
- **`regex-replace`** - Pattern-based replacements

#### Privacy & Security
- **`mask`** - Mask sensitive data
- **`sanitize`** - Remove/replace unsafe characters

#### Data Quality
- **`coalesce`** - Replace null/empty with defaults

### Programmatic Transformer Usage
```csharp
using DB2XL.Configuration;
using DB2XL.Transformers;

// Load configuration-driven transformations
var config = ConfigurationLoader.LoadFromFile("transforms.json");
var registry = TransformerRegistryBuilder.CreateDefault();

// Use in export with dual strategy (raw + transformed)
var options = new SqliteToExcelOptions
{
    TransformationConfig = config,
    TransformerRegistry = registry,
    DualExportStrategy = DualExportStrategy.DualSheets // Raw and transformed data
};

SqliteToExcel.Export("database.sqlite", "output.xlsx", options);
```

## 🛠️ Step 6: Common Patterns

### Console Tool Patterns
```bash
# Debug application database
dotnet run --project SqliteXport.Console -- analyze app.db --check-integrity --performance
dotnet run --project SqliteXport.Console -- export app.db debug.xlsx --dual-sheets --metadata

# Export logs for analysis
dotnet run --project SqliteXport.Console -- export logs.db recent.xlsx --where "timestamp > datetime('now', '-1 hour')" --transform

# ML data preparation
dotnet run --project SqliteXport.Console -- export features.db training.jsonl --format jsonl --transform --exclude "id,created_at"

# Schema documentation
dotnet run --project SqliteXport.Console -- analyze schema.db --output schema.json --format json
```

### Programmatic Patterns

### Export Multiple Databases
```csharp
var databases = new[] { "sales.sqlite", "inventory.sqlite", "customers.sqlite" };

foreach (var db in databases)
{
    var outputFile = Path.ChangeExtension(db, ".xlsx");
    SqliteToExcel.Export(db, outputFile);
    Console.WriteLine($"✅ Exported {db} → {outputFile}");
}
```

### Handle Large Databases
```csharp
var options = new SqliteToExcelOptions
{
    ReadBatchSize = 50000,          // Larger batches for speed
    SplitOversizeSheets = true,     // Handle >1M row tables
    CommandTimeoutSeconds = 300     // Longer timeout for big queries
};

SqliteToExcel.Export("huge_database.sqlite", "huge_export.xlsx", options);
```

### Export Only Specific Tables
```csharp
var options = new SqliteToExcelOptions
{
    TableNameLikeFilter = "sales_%"  // Only tables starting with "sales_"
};

SqliteToExcel.Export("database.sqlite", "sales_only.xlsx", options);
```

## ✅ Step 7: Verify Your Export

DB2XL includes built-in validation. Check the metadata sheet for:

- **SHA-256 checksums** - Verify data integrity
- **Row counts** - Confirm complete export
- **Export options** - See exactly what settings were used
- **Database info** - Source file details and version

### Programmatic Validation
```csharp
// The test suite shows how to validate exports
// Run tests to see validation examples:
dotnet test --filter "ExportValidator"
```

## 🎯 Step 8: Advanced Features (New!)

### JSON-Based Advanced Filtering
Use SelectionGrammar for sophisticated queries without complex SQL:

```bash
# Create a filter file
cat > query.json << EOF
{
  "table": "orders",
  "select": ["order_id", "customer_id", "total", "status"],
  "where": {
    "type": "and",
    "conditions": [
      {"type": "comparison", "column": "total", "operator": ">", "value": "1000"},
      {"type": "comparison", "column": "status", "operator": "=", "value": "pending"}
    ]
  },
  "orderBy": [{"column": "total", "direction": "desc"}],
  "limit": 100
}
EOF

# Apply the filter
dotnet run --project SqliteXport.Console -- export database.sqlite filtered.xlsx --filter query.json
```

### Delta Exports (Incremental Changes)
Export only what's changed since your last export:

```bash
# Watermark-based delta export (auto-detects timestamp columns)
dotnet run --project SqliteXport.Console -- export db.sqlite changes.xlsx --delta

# Specify watermark columns
dotnet run --project SqliteXport.Console -- export db.sqlite delta.xlsx \
  --delta --watermark-columns "updated_at,modified_at"

# Use checkpoint for true incremental exports
dotnet run --project SqliteXport.Console -- export db.sqlite incremental.xlsx \
  --delta --checkpoint-file last_export.json
```

### Change Log Tracking
Track all database changes with automatic triggers:

```bash
# Install change tracking triggers
dotnet run --project SqliteXport.Console -- export db.sqlite setup.xlsx --install-changelog

# Export captured changes
dotnet run --project SqliteXport.Console -- export db.sqlite changes.xlsx \
  --delta --delta-strategy changelog
```

### Enhanced Database Analysis
Discover primary keys and get performance recommendations:

```bash
# Full PK discovery with quality scores
dotnet run --project SqliteXport.Console -- analyze db.sqlite \
  --pk-discovery --pk-strategy --pk-quality --deterministic-order

# Get index suggestions for large tables
dotnet run --project SqliteXport.Console -- analyze db.sqlite \
  --suggest-indexes --performance

# Export analysis results
dotnet run --project SqliteXport.Console -- analyze db.sqlite \
  --output analysis.json --format json
```

## 🎉 You're Ready!

You now know how to:
- ✅ **Analyze databases** with PK discovery and performance metrics
- ✅ **Export to Excel and JSONL** with perfect fidelity
- ✅ **Use advanced filtering** with JSON SelectionGrammar files
- ✅ **Perform delta exports** for incremental data processing
- ✅ **Track changes** with automatic changelog triggers
- ✅ **Use 22 built-in transformers** for human-readable output
- ✅ **Configure transformations** with JSON/YAML files
- ✅ **Handle large databases** and special cases
- ✅ **Verify export integrity** with checksums and manifests
- ✅ **Debug applications** with comprehensive database analysis

## 🔗 Next Steps

- **[Examples Directory](examples/)** - Ready-to-use examples for all features
  - [Filter Examples](examples/filters/) - JSON SelectionGrammar samples
  - [Delta Export Examples](examples/delta/) - Watermark and changelog strategies
- **[Console Tool Guide](SqliteXport.Console.md)** - Complete console tool documentation
- **[Complete specification](CLAUDE.md)** - Advanced library features
- **[Transformer documentation](TRANSFORMERS.md)** - All 22 built-in transformers
- **[Filters & Advanced Features](Filters.md)** - Force multipliers and future roadmap
- **[Project status](Project_status.md)** - Current implementation status
- **Run the test suite** to see comprehensive examples: `dotnet test --verbosity normal`

## ❓ Need Help?

- **Issues**: Check [GitHub Issues](https://github.com/revred/DB2XL/issues)
- **Examples**: Look at the test files in `SqliteXport.Tests/`
- **Source code**: Everything is documented with XML comments

## 🏃‍♂️ Quick Reference

### Console Tool Commands
```bash
# Quick analysis
dotnet run --project SqliteXport.Console -- analyze database.sqlite

# Export with transformations
dotnet run --project SqliteXport.Console -- export database.sqlite output.xlsx --transform

# JSONL export for AI/ML
dotnet run --project SqliteXport.Console -- export database.sqlite output/ --format jsonl --transform
```

### Programmatic API
```csharp
// Minimal export
SqliteToExcel.Export("database.sqlite", "output.xlsx");

// Full-featured with transformations
var config = ConfigurationLoader.LoadFromFile("transforms.json");
var options = new SqliteToExcelOptions
{
    TransformationConfig = config,
    TransformerRegistry = TransformerRegistryBuilder.CreateDefault(),
    DualExportStrategy = DualExportStrategy.DualSheets,
    IncludeViews = true
};
SqliteToExcel.Export("database.sqlite", "output.xlsx", options);

// JSONL export
var jsonlOptions = new JsonLinesExportOptions
{
    TransformationConfig = config,
    IncludeSchemaManifests = true
};
JsonLinesExporter.Export("database.sqlite", "output/", jsonlOptions);
```

---

**Happy exporting! 🚀** Your SQLite data has never looked better in Excel.