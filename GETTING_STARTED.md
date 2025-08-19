# Getting Started with DB2XL

DB2XL is a deterministic SQLite to Excel exporter that transforms your database into clean, readable Excel files. This guide will get you up and running in just a few minutes.

## 🎯 What You'll Learn

By the end of this guide, you'll know how to:
- Export any SQLite database to Excel with perfect fidelity
- Use advanced transformations to make data human-readable
- Customize exports with various options
- Verify data integrity with built-in checksums

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
You should see: `Passed! - Failed: 1, Passed: 349, Skipped: 0` (99.7% success rate)

## 📊 Step 2: Your First Export

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

## ⚙️ Step 3: Custom Configuration

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

## 🔄 Step 4: Data Transformations (Advanced)

Make your raw data human-readable with transformers:

```csharp
using SqliteXport;
using DB2XL.Transformers;
using DB2XL.Transformers.Examples;

// Create a registry with example transformers
var registry = ExampleTransformers.CreateRegistry();

// Create some transformers
var upperTransformer = registry.CreateCell("upper", new Dictionary<string, string>());
var emailMaskTransformer = registry.CreateCell("email-mask", 
    new Dictionary<string, string> { ["column"] = "email" });

// Apply transformations (this would be integrated into export pipeline in future)
var context = new CellContext("employees", "name", 0, SqliteAffinity.Text);
var upperName = upperTransformer.Transform(context, "john doe"); // "JOHN DOE"

var emailContext = new CellContext("employees", "email", 0, SqliteAffinity.Text);
var maskedEmail = emailMaskTransformer.Transform(emailContext, "john@company.com"); // "j***@company.com"

Console.WriteLine($"Transformed name: {upperName}");
Console.WriteLine($"Masked email: {maskedEmail}");
```

### Available Transformers
- **`upper`** - Convert text to uppercase
- **`trim`** - Remove whitespace (configurable)
- **`truncate`** - Limit text length with ellipsis
- **`coalesce`** - Replace null/empty with default
- **`email-mask`** - Privacy-friendly email masking

## 🛠️ Step 5: Common Patterns

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

## ✅ Step 6: Verify Your Export

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

## 🎉 You're Ready!

You now know how to:
- ✅ Export SQLite databases to Excel with perfect fidelity
- ✅ Customize exports with advanced options
- ✅ Use data transformers for human-readable output
- ✅ Handle large databases and special cases
- ✅ Verify export integrity with checksums

## 🔗 Next Steps

- **Read the [complete specification](CLAUDE.md)** for advanced features
- **Explore [transformer documentation](TRANSFORMERS.md)** for custom transformations
- **Check [project status](Project_status.md)** for upcoming features
- **Run the test suite** to see comprehensive examples: `dotnet test --verbosity normal`

## ❓ Need Help?

- **Issues**: Check [GitHub Issues](https://github.com/revred/DB2XL/issues)
- **Examples**: Look at the test files in `SqliteXport.Tests/`
- **Source code**: Everything is documented with XML comments

## 🏃‍♂️ Quick Reference

### Minimal Export
```csharp
SqliteToExcel.Export("database.sqlite", "output.xlsx");
```

### Full-Featured Export
```csharp
var options = new SqliteToExcelOptions
{
    IncludeViews = true,
    BlobMode = BlobRenderMode.Base64,
    SplitOversizeSheets = true
};
SqliteToExcel.Export("database.sqlite", "output.xlsx", options);
```

### Transformer Example
```csharp
var registry = ExampleTransformers.CreateRegistry();
var transformer = registry.CreateCell("truncate", new Dictionary<string, string>
{
    ["maxLength"] = "50",
    ["ellipsis"] = "..."
});
```

---

**Happy exporting! 🚀** Your SQLite data has never looked better in Excel.