# SqliteXport.Test - DB2XL Test Harness

A comprehensive test harness for validating the DB2XL SQLite to Excel exporter.

## Features

- **Sample Database Generation**: Creates a rich test database with various data types and edge cases
- **Export Execution**: Runs the DB2XL exporter with configurable options
- **Data Validation**: Verifies data integrity between SQLite and Excel
- **Checksum Verification**: Ensures deterministic, byte-for-byte consistent exports
- **Performance Metrics**: Measures export time and file sizes

## Test Database Contents

The sample database includes:

1. **Standard Tables**:
   - `Customers` - Customer information with various text fields
   - `Products` - Product catalog with prices and inventory
   - `Orders` - Order transactions with dates and shipping info
   - `OrderDetails` - Line items with composite primary keys
   - `Employees` - Employee records with hierarchical relationships

2. **Edge Case Tables**:
   - `SpecialCases` - NULL values, empty strings, special characters, long text
   - `UnicodeTest` - Multi-language text, emojis, RTL scripts
   - `NumericTypes` - Various numeric formats, scientific notation, infinity
   - `BlobData` - Binary data with different rendering modes
   - `LargeData` - 1000+ rows for testing chunking and performance
   - `EmptyTable` - Table with schema but no data

3. **Views**:
   - `CustomerOrderSummary` - Aggregated view for testing view exports

## Usage

### Quick Test (Default)
```bash
dotnet run --project SqliteXport.Test
```
Creates a temporary sample database, exports it, validates, and cleans up.

### Keep Files for Inspection
```bash
dotnet run --project SqliteXport.Test -- -k
```
Same as above but retains the generated .db and .xlsx files.

### Export Existing Database
```bash
dotnet run --project SqliteXport.Test -- -d mydata.db
```
Exports an existing SQLite database and validates the result.

### Full Control
```bash
dotnet run --project SqliteXport.Test -- -d input.db -e output.xlsx -k
```
Specify input database, output Excel file, and keep files after validation.

## Validation Checks

The validator performs these integrity checks:

1. **Row Count Verification**: Ensures all rows are exported
2. **Column Count Verification**: Validates all columns are present
3. **Column Name Matching**: Verifies headers match database schema
4. **Sheet Splitting**: Validates proper handling of large tables
5. **Metadata Sheet**: Checks for complete export metadata
6. **Checksum Validation**: Compares SHA-256 checksums for data integrity
7. **File Integrity**: Verifies Excel file is valid and readable

## Validation Report

The tool generates a detailed report showing:
- Overall validation status (PASSED/FAILED)
- File sizes and paths
- Errors and warnings
- Per-table validation results
- Row/column counts comparison
- Data integrity issues

## Exit Codes

- `0` - Success: Export and validation passed
- `1` - Failure: Export failed or validation errors detected

## Building and Running

### Prerequisites
- .NET 8.0 SDK or later
- NuGet packages will be restored automatically

### Build
```bash
dotnet build SqliteXport.Test
```

### Run Tests
```bash
dotnet run --project SqliteXport.Test
```

### Run as Console App
```bash
cd SqliteXport.Test
dotnet run -- [options]
```

## Sample Output

```
DB2XL Export Test Harness
================================================================================

📁 Creating sample database...
   Database created: sample_abc123.db

📊 Database Information:
   Path: C:\Temp\sample_abc123.db
   Size: 45,056 bytes

⚙️ Export Options:
   Write All As Text: True
   Include Metadata: True
   BLOB Mode: Hex
   Include Views: True
   Order Deterministically: True

🚀 Starting export...

✅ Export completed successfully!
   Output: C:\Temp\sample_abc123.xlsx
   Size: 67,234 bytes
   Time: 245 ms

🔍 Validating export...

================================================================================
EXPORT VALIDATION REPORT
================================================================================

Validation Status: ✅ PASSED

Database: sample_abc123.db
  Size: 45,056 bytes

Excel File: sample_abc123.xlsx
  Size: 67,234 bytes

📊 Table Validation Results:
--------------------------------------------------------------------------------
Table                          DB Rows    XL Rows    Columns     Status
--------------------------------------------------------------------------------
BlobData                             5          5          4         ✅
Customers                            5          5         11         ✅
CustomerOrderSummary                5          5          4         ✅
EmptyTable                           0          0          2         ✅
Employees                            5          5         15         ✅
LargeData                        1,000      1,000          5         ✅
NumericTypes                         5          5          8         ✅
OrderDetails                         5          5          5         ✅
Orders                               3          3         14         ✅
Products                             5          5          9         ✅
SpecialCases                         3          3         11         ✅
UnicodeTest                         10         10          4         ✅

================================================================================

🎉 Export validation PASSED - Data integrity confirmed!
```

## Troubleshooting

### Common Issues

1. **File Access Errors**: Ensure the database file exists and is readable
2. **Memory Issues**: For very large databases, consider increasing process memory
3. **Excel Limits**: Tables exceeding 1,048,576 rows will be split across sheets
4. **Character Encoding**: UTF-8 encoding is used throughout for Unicode support

### Debug Mode

Set environment variable for detailed logging:
```bash
set DOTNET_ENVIRONMENT=Development
dotnet run --project SqliteXport.Test
```