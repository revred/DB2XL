# SqliteXport Console Tool - Claude Integration Guide

A comprehensive command-line tool for SQLite database analysis, debugging, and data extraction designed for AI assistant integration and deep database inspection workflows.

> **🎯 Goal**: Provide Claude and other AI assistants with a powerful SQLite inspection tool that can quickly export, analyze, and transform database contents for debugging, machine learning, log analysis, and insight extraction.

---

## 🤖 Claude Integration Overview

SqliteXport Console is designed to be a core tool in Claude's toolbox for:

- **Database Debugging**: Quick export and analysis of SQLite databases in debug scenarios
- **Log Analysis**: Transform SQLite-based logs into human-readable Excel/JSONL formats
- **Machine Learning Data Prep**: Export and transform training/testing datasets
- **Fault Finding**: Rapid database inspection and data extraction
- **Insight Extraction**: Convert opaque database values into analyzable formats
- **Data Pipeline Debugging**: Inspect intermediate data transformations

---

## 🚀 Quick Start for AI Assistants

### Basic Database Export

```bash
# Export entire database to Excel with human-readable transformations
sqlitexport export mydb.sqlite output.xlsx --transform

# Export to JSONL for LLM processing
sqlitexport export mydb.sqlite output/ --format jsonl --transform

# Quick analysis with metadata
sqlitexport analyze mydb.sqlite
```

### Debug-Focused Commands

```bash
# Export with comprehensive metadata for debugging
sqlitexport export mydb.sqlite debug.xlsx --dual-sheets --metadata --transform

# Export recent logs (assuming timestamp columns)
sqlitexport export logs.db recent.xlsx --where "timestamp > datetime('now', '-1 hour')"

# Export specific tables for targeted analysis
sqlitexport export app.db tables.xlsx --tables "users,sessions,errors" --transform
```

### Machine Learning Workflows

```bash
# Export training data with transformations
sqlitexport export ml_data.db training.jsonl --format jsonl --transform --exclude "id,created_at"

# Export with schema manifest for data lineage
sqlitexport export features.db features.xlsx --manifest --transform
```

---

## 📋 Current API (SqliteXport Library)

The console tool will be built on top of the existing SqliteXport API:

### Core Export Functions

```csharp
// Excel export with full options
SqliteToExcel.Export(dbPath, xlsxPath, options);
SqliteToExcel.ExportWithManifest(dbPath, xlsxPath, options);

// JSONL export for AI/ML workflows  
JsonLinesExporter.Export(dbPath, outputDir, options);
JsonLinesExporter.ExportWithManifest(dbPath, outputDir, options);

// Schema analysis and validation
var manifest = SqliteToExcel.GenerateManifest(dbPath, xlsxPath, options);
var validation = SqliteToExcel.ValidateExport(xlsxPath);
```

### Transformation System

```csharp
// Built-in transformer registry
var registry = TransformerRegistryBuilder.CreateDefault();

// Configuration-driven transformations
var config = ConfigurationLoader.LoadFromFile("transforms.json");
var pipeline = new TransformationPipeline(config, registry);
```

### Analysis and Discovery

```csharp
// Database structure discovery
var tables = DatabaseDiscovery.GetObjects(connection, filter, includeViews);
var columns = DatabaseDiscovery.GetColumns(connection, tableName);

// Schema analysis
var schema = SchemaAnalyzer.AnalyzeDatabase(connection, dbPath, options, pipeline);
```

---

## 🛠️ Planned Console Tool Architecture

### Separation Strategy

```
Current State:
├── SqliteXport/              # Core library
│   ├── SqliteToExcel.cs     # Excel export API
│   ├── JsonLinesExporter.cs # JSONL export API
│   ├── Transformers/        # Transformation system
│   └── Configuration/       # Config loading
└── SqliteXport.Tests/       # API tests

Target State:
├── SqliteXport/              # Core library (unchanged)
│   ├── SqliteToExcel.cs     # Excel export API
│   ├── JsonLinesExporter.cs # JSONL export API
│   ├── Transformers/        # Transformation system
│   └── Configuration/       # Config loading
├── SqliteXport.Console/      # NEW: Console application
│   ├── Program.cs           # Entry point
│   ├── Commands/            # Command implementations
│   ├── Options/             # CLI option models
│   └── Helpers/             # Console utilities
└── SqliteXport.Tests/       # API tests (unchanged)
```

### Command Structure

```bash
sqlitexport <command> [options]

Commands:
  export      Export database to Excel/JSONL formats
  analyze     Analyze database structure and content
  transform   Apply transformations to data
  validate    Validate exports against manifests
  schema      Generate schema documentation
  help        Show help information
```

---

## 🎯 Console Commands Specification

### 1. Export Command

Primary command for database export and transformation.

```bash
sqlitexport export <database> <output> [options]

Arguments:
  database    Path to SQLite database file
  output      Output file (.xlsx) or directory (.jsonl)

Options:
  --format <excel|jsonl>           Output format (default: auto-detect from extension)
  --transform                      Apply intelligent transformations
  --config <file>                  Transformation configuration file
  --dual-sheets                    Export both raw and transformed data
  --dual-workbooks                 Export to separate workbooks
  --metadata                       Include comprehensive metadata sheet
  --manifest                       Generate schema and provenance manifest
  
  # Data filtering
  --tables <list>                  Comma-separated list of tables to export
  --exclude-tables <list>          Tables to exclude from export
  --where <clause>                 SQL WHERE clause for row filtering
  --max-rows <number>              Maximum rows per table
  --include-views                  Include database views in export
  
  # Column filtering  
  --columns <list>                 Specific columns to include
  --exclude-columns <list>         Columns to exclude
  
  # Format options
  --write-all-as-text             Force all values as text (default: true)
  --preserve-numeric-types        Preserve numeric types in Excel
  --blob-mode <skip|hex|base64>   How to handle BLOB data
  --split-oversized               Split large tables across sheets
  
  # Performance
  --batch-size <number>           Rows per processing batch
  --parallel                      Enable parallel processing
  --timeout <seconds>             Command timeout
  
Examples:
  sqlitexport export app.db report.xlsx --transform --metadata
  sqlitexport export logs.db logs/ --format jsonl --where "timestamp > '2024-01-01'"
  sqlitexport export data.db analysis.xlsx --dual-sheets --tables "users,events"
```

### 2. Analyze Command

Quick database inspection and analysis.

```bash
sqlitexport analyze <database> [options]

Arguments:
  database    Path to SQLite database file

Options:
  --output <file>                 Save analysis to file (default: console)
  --format <text|json|yaml>       Analysis output format
  --include-data                  Include data samples in analysis
  --sample-size <number>          Number of sample rows per table
  --check-integrity               Run SQLite integrity checks
  --performance                   Include performance metrics

Output:
  - Database metadata (size, version, journal mode)
  - Table structure and relationships
  - Column data types and constraints
  - Row counts and data distribution
  - Suggested transformations
  - Data quality issues

Examples:
  sqlitexport analyze app.db
  sqlitexport analyze logs.db --output analysis.json --include-data
  sqlitexport analyze data.db --check-integrity --performance
```

### 3. Transform Command

Apply transformations without full export.

```bash
sqlitexport transform <database> <config> [options]

Arguments:
  database    Path to SQLite database file
  config      Transformation configuration file

Options:
  --dry-run                       Show what would be transformed
  --table <name>                  Transform specific table only
  --column <name>                 Transform specific column only
  --output <file>                 Save transformation report
  --validate                      Validate config before applying

Examples:
  sqlitexport transform app.db transforms.json --dry-run
  sqlitexport transform logs.db config.yaml --table events --validate
```

### 4. Schema Command

Generate comprehensive schema documentation.

```bash
sqlitexport schema <database> [options]

Arguments:
  database    Path to SQLite database file

Options:
  --output <file>                 Output file (default: console)
  --format <markdown|html|json>   Documentation format
  --include-indexes              Include index information
  --include-triggers             Include trigger definitions
  --include-views                Include view definitions
  --er-diagram                   Generate entity-relationship diagram

Examples:
  sqlitexport schema app.db --output schema.md --format markdown
  sqlitexport schema data.db --include-indexes --er-diagram
```

### 5. Validate Command

Validate exports against their manifests.

```bash
sqlitexport validate <export> [options]

Arguments:
  export      Path to export file or directory

Options:
  --manifest <file>               Specific manifest file to validate against
  --checksums                     Validate data checksums
  --strict                        Fail on warnings
  --report <file>                 Save validation report

Examples:
  sqlitexport validate report.xlsx
  sqlitexport validate exports/ --checksums --report validation.json
```

---

## 🔧 Implementation Roadmap

### Phase 1: Console Project Setup ✅ (Planned)

1. **Create SqliteXport.Console Project**
   ```bash
   dotnet new console -n SqliteXport.Console
   dotnet add SqliteXport.Console reference SqliteXport
   dotnet add package CommandLineParser
   dotnet add package Spectre.Console  # For rich console output
   ```

2. **Project Structure**
   ```
   SqliteXport.Console/
   ├── Program.cs                    # Entry point and command routing
   ├── Commands/
   │   ├── ExportCommand.cs         # Export command implementation
   │   ├── AnalyzeCommand.cs        # Analysis command
   │   ├── TransformCommand.cs      # Transform command
   │   ├── SchemaCommand.cs         # Schema documentation
   │   └── ValidateCommand.cs       # Validation command
   ├── Options/
   │   ├── ExportOptions.cs         # CLI option models
   │   ├── AnalyzeOptions.cs
   │   └── GlobalOptions.cs
   ├── Helpers/
   │   ├── ConsoleHelper.cs         # Console output utilities
   │   ├── ProgressReporter.cs      # Progress reporting
   │   └── ErrorHandler.cs          # Error handling
   └── SqliteXport.Console.csproj
   ```

3. **Add Console-Specific Tests**
   ```
   SqliteXport.Console.Tests/
   ├── Commands/
   │   ├── ExportCommandTests.cs
   │   └── AnalyzeCommandTests.cs
   ├── Integration/
   │   └── EndToEndTests.cs
   └── Helpers/
       └── TestUtilities.cs
   ```

### Phase 2: Core Commands Implementation 🚧 (Next)

1. **Export Command** (Priority 1)
   - Excel export with all current options
   - JSONL export support
   - Transformation integration
   - Progress reporting
   - Error handling

2. **Analyze Command** (Priority 2)
   - Database structure analysis
   - Data quality assessment
   - Performance metrics
   - Console and file output

3. **Basic Help System**
   - Command help
   - Option descriptions
   - Examples

### Phase 3: Advanced Features 🔮 (Future)

1. **Transform and Schema Commands**
2. **Validation Command**
3. **Rich Console Output** (colors, tables, progress bars)
4. **Configuration File Support**
5. **Plugin System** for custom transformers

### Phase 4: AI Assistant Integration 🤖 (Future)

1. **Claude Tool Definition**
2. **Structured Output Formats**
3. **Error Code Standardization**
4. **Batch Processing Support**
5. **Integration Testing with AI Workflows**

---

## 🎯 AI Assistant Integration Patterns

### Debugging Workflows

```bash
# 1. Quick database inspection
sqlitexport analyze suspicious.db --check-integrity

# 2. Export recent error logs
sqlitexport export error_log.db errors.xlsx --where "level='ERROR' AND timestamp > datetime('now', '-1 day')" --transform

# 3. Generate comprehensive debug report
sqlitexport export app.db debug_report.xlsx --dual-sheets --metadata --manifest
```

### Machine Learning Workflows

```bash
# 1. Export training data with transformations
sqlitexport export ml_features.db training.jsonl --format jsonl --transform --exclude "id,version"

# 2. Generate data lineage documentation
sqlitexport export features.db features.xlsx --manifest --transform

# 3. Validate data integrity
sqlitexport validate training.jsonl --checksums --strict
```

### Log Analysis Workflows

```bash
# 1. Export application logs for analysis
sqlitexport export app_logs.db logs.xlsx --transform --where "timestamp > datetime('now', '-1 week')"

# 2. Generate schema documentation for log structure
sqlitexport schema app_logs.db --output log_schema.md --format markdown

# 3. Extract specific events for investigation
sqlitexport export events.db incidents.xlsx --tables "errors,warnings" --transform
```

---

## 🔍 Claude Usage Examples

### Scenario 1: Application Debug Session

**Claude**: I notice your application is having performance issues. Let me inspect the database:

```bash
# First, analyze the database structure and health
sqlitexport analyze app.db --check-integrity --performance

# Export recent transactions for analysis
sqlitexport export app.db recent_activity.xlsx --where "created_at > datetime('now', '-2 hours')" --transform --metadata

# Generate comprehensive schema documentation
sqlitexport schema app.db --output schema.md --include-indexes
```

### Scenario 2: Log Investigation

**Claude**: I see error patterns in your logs. Let me extract and analyze them:

```bash
# Export error logs with intelligent transformations
sqlitexport export error_logs.db errors.xlsx --where "level IN ('ERROR', 'CRITICAL')" --transform --dual-sheets

# Analyze the log database structure
sqlitexport analyze error_logs.db --include-data --sample-size 100

# Export to JSONL for LLM analysis
sqlitexport export error_logs.db error_analysis.jsonl --format jsonl --transform
```

### Scenario 3: Machine Learning Data Prep

**Claude**: Let me prepare your ML dataset:

```bash
# Export features with full provenance tracking
sqlitexport export features.db ml_data.xlsx --transform --manifest --exclude "internal_id,debug_info"

# Convert to JSONL for model training
sqlitexport export features.db training/ --format jsonl --transform --max-rows 100000

# Validate the export integrity
sqlitexport validate training/ --checksums --report validation.json
```

---

## 📚 Integration Testing Strategy

### Console Application Tests

```csharp
[Test]
public void ExportCommand_ShouldUseCorrectApiMethods()
{
    // Arrange
    var options = new ExportOptions { Database = "test.db", Output = "test.xlsx" };
    
    // Act  
    var result = ExportCommand.Execute(options);
    
    // Assert
    Assert.True(File.Exists("test.xlsx"));
    // Verify SqliteToExcel.Export was called with correct parameters
}

[Test] 
public void AnalyzeCommand_ShouldProvideStructuredOutput()
{
    // Test that analyze command uses DatabaseDiscovery and SchemaAnalyzer APIs
}
```

### API Compatibility Tests

```csharp
[Test]
public void ConsoleOptions_ShouldMapToLibraryOptions()
{
    // Ensure console options correctly map to SqliteToExcelOptions
    var consoleOptions = new ExportOptions();
    var libraryOptions = OptionsMapper.MapToLibraryOptions(consoleOptions);
    
    Assert.Equal(consoleOptions.WriteAllAsText, libraryOptions.WriteAllAsText);
    // ... verify all mappings
}
```

---

## 🎯 Success Metrics

### For AI Assistant Integration

1. **Command Success Rate**: >95% of commands complete successfully
2. **Error Clarity**: All errors provide actionable information
3. **Performance**: Sub-10 second response for databases <100MB
4. **Output Quality**: Structured, parseable output for AI consumption

### For Developer Experience

1. **API Consistency**: Console commands map 1:1 to library APIs
2. **Test Coverage**: >90% coverage for console-specific code
3. **Documentation**: Complete help system and examples
4. **Maintainability**: Clear separation between console and library code

---

## 🚀 Getting Started (When Available)

### Installation (Future)

```bash
# Install as global tool
dotnet tool install -g SqliteXport.Console

# Or run from source
dotnet run --project SqliteXport.Console -- export mydb.sqlite output.xlsx
```

### Quick Test Drive

```bash
# Analyze any SQLite database
sqlitexport analyze your_database.sqlite

# Export with intelligent transformations
sqlitexport export your_database.sqlite readable_output.xlsx --transform

# Generate schema documentation
sqlitexport schema your_database.sqlite --output schema.md
```

---

## 📋 Current Status

### ✅ Completed (API Ready)
- **Complete SqliteXport Library**: All export and transformation APIs implemented
- **Comprehensive Test Suite**: 400+ tests covering all functionality  
- **Transformation System**: 22 built-in transformers with configuration support
- **Dual Export Strategies**: Raw vs transformed data export options
- **Schema Manifests**: Complete data lineage and provenance tracking
- **JSONL Support**: AI/ML-ready output format

### 🚧 In Progress
- **Console Tool Architecture Planning**: This document and roadmap

### 🔮 Planned
- **SqliteXport.Console Project**: Separate console application
- **Command Implementation**: Export, analyze, transform, schema, validate commands
- **AI Integration Testing**: Claude-specific workflow validation
- **Documentation**: Complete help system and examples

---

**The future of SQLite database inspection and AI-assisted debugging starts here! 🚀**