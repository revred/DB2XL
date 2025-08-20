# Advanced Filtering with SelectionGrammar

DB2XL provides a powerful JSON-based query language called SelectionGrammar that enables sophisticated database filtering without writing complex SQL. This guide covers everything from basic filters to advanced multi-condition queries.

## Table of Contents

- [Overview](#overview)
- [JSON Filter Structure](#json-filter-structure)
- [Basic Filtering](#basic-filtering)
- [Advanced WHERE Conditions](#advanced-where-conditions)
- [Sorting and Pagination](#sorting-and-pagination)
- [Console Tool Usage](#console-tool-usage)
- [Programmatic API](#programmatic-api)
- [Performance Considerations](#performance-considerations)
- [Security](#security)
- [Troubleshooting](#troubleshooting)

## Overview

SelectionGrammar is a declarative, JSON-based query language that provides:

- **Type-safe filtering** - No SQL injection risks
- **Intuitive syntax** - Easy to read and write
- **Composable conditions** - Build complex queries from simple parts
- **Version control friendly** - Store queries as JSON files
- **IDE support** - JSON schema validation and auto-completion

## JSON Filter Structure

### Basic Structure

```json
{
  "table": "string",           // Required: Table name
  "select": ["column1", "*"],  // Column selection (default: ["*"])
  "where": { },                // WHERE conditions (optional)
  "orderBy": [ ],              // ORDER BY clauses (optional)
  "limit": 100,                // LIMIT rows (optional)
  "offset": 0                  // OFFSET for pagination (optional)
}
```

### Complete Example

```json
{
  "$schema": "https://db2xl.org/schemas/selection-grammar.json",
  "_comment": "Find high-value recent orders",
  "table": "orders",
  "select": ["order_id", "customer_id", "total", "created_at"],
  "where": {
    "type": "and",
    "conditions": [
      {
        "type": "comparison",
        "column": "total",
        "operator": ">=",
        "value": 1000
      },
      {
        "type": "comparison",
        "column": "created_at",
        "operator": ">=",
        "value": "2024-01-01"
      }
    ]
  },
  "orderBy": [
    {
      "column": "total",
      "direction": "desc"
    }
  ],
  "limit": 50
}
```

## Basic Filtering

### Select All Columns

```json
{
  "table": "users",
  "select": ["*"]
}
```

### Select Specific Columns

```json
{
  "table": "users",
  "select": ["id", "name", "email", "created_at"]
}
```

### Simple WHERE Condition

```json
{
  "table": "products",
  "select": ["*"],
  "where": {
    "type": "comparison",
    "column": "price",
    "operator": ">",
    "value": 100
  }
}
```

## Advanced WHERE Conditions

### Comparison Operators

All standard SQL comparison operators are supported:

| Operator | Description | Example |
|----------|-------------|---------|
| `=` | Equal | `"status" = "active"` |
| `!=` | Not equal | `"status" != "deleted"` |
| `>` | Greater than | `"age" > 18` |
| `>=` | Greater or equal | `"price" >= 100` |
| `<` | Less than | `"quantity" < 10` |
| `<=` | Less or equal | `"discount" <= 0.5` |
| `like` | Pattern match | `"name" like "%Smith%"` |
| `in` | In list | `"category" in ["A", "B", "C"]` |

### AND Conditions

Combine multiple conditions that must all be true:

```json
{
  "table": "orders",
  "where": {
    "type": "and",
    "conditions": [
      {
        "type": "comparison",
        "column": "status",
        "operator": "=",
        "value": "pending"
      },
      {
        "type": "comparison",
        "column": "total",
        "operator": ">",
        "value": 1000
      },
      {
        "type": "comparison",
        "column": "created_at",
        "operator": ">=",
        "value": "2024-01-01"
      }
    ]
  }
}
```

### OR Conditions

At least one condition must be true:

```json
{
  "table": "users",
  "where": {
    "type": "or",
    "conditions": [
      {
        "type": "comparison",
        "column": "role",
        "operator": "=",
        "value": "admin"
      },
      {
        "type": "comparison",
        "column": "role",
        "operator": "=",
        "value": "moderator"
      }
    ]
  }
}
```

### Complex Nested Conditions

Combine AND and OR for sophisticated logic:

```json
{
  "table": "transactions",
  "where": {
    "type": "or",
    "conditions": [
      {
        "type": "and",
        "conditions": [
          {
            "type": "comparison",
            "column": "amount",
            "operator": ">",
            "value": 10000
          },
          {
            "type": "comparison",
            "column": "status",
            "operator": "=",
            "value": "pending"
          }
        ]
      },
      {
        "type": "and",
        "conditions": [
          {
            "type": "comparison",
            "column": "risk_score",
            "operator": ">",
            "value": 0.8
          },
          {
            "type": "comparison",
            "column": "verified",
            "operator": "=",
            "value": false
          }
        ]
      }
    ]
  }
}
```

This translates to:
```sql
WHERE (amount > 10000 AND status = 'pending') 
   OR (risk_score > 0.8 AND verified = false)
```

### IN Operator for Lists

```json
{
  "table": "products",
  "where": {
    "type": "comparison",
    "column": "category",
    "operator": "in",
    "value": ["Electronics", "Computers", "Software"]
  }
}
```

### LIKE Pattern Matching

```json
{
  "table": "customers",
  "where": {
    "type": "comparison",
    "column": "email",
    "operator": "like",
    "value": "%@gmail.com"
  }
}
```

Pattern wildcards:
- `%` - Matches any sequence of characters
- `_` - Matches any single character

## Sorting and Pagination

### Single Column Sort

```json
{
  "table": "products",
  "orderBy": [
    {
      "column": "price",
      "direction": "desc"
    }
  ]
}
```

### Multi-Column Sort

```json
{
  "table": "orders",
  "orderBy": [
    {
      "column": "status",
      "direction": "asc"
    },
    {
      "column": "created_at",
      "direction": "desc"
    },
    {
      "column": "total",
      "direction": "desc"
    }
  ]
}
```

### Pagination

Get page 3 with 50 records per page:

```json
{
  "table": "products",
  "orderBy": [{"column": "id", "direction": "asc"}],
  "limit": 50,
  "offset": 100
}
```

### Top N Records

Get top 10 highest value orders:

```json
{
  "table": "orders",
  "orderBy": [{"column": "total", "direction": "desc"}],
  "limit": 10
}
```

## Console Tool Usage

### Basic Usage

```bash
# Apply filter from file
sqlitexport export database.db output.xlsx --filter query.json

# Combine with other options
sqlitexport export database.db output.xlsx \
  --filter complex_query.json \
  --transform \
  --metadata
```

### Filter File Management

```bash
# Validate filter file (dry run)
sqlitexport export database.db test.xlsx --filter query.json --dry-run

# Use different filters for different exports
sqlitexport export db.sqlite pending.xlsx --filter filters/pending_orders.json
sqlitexport export db.sqlite shipped.xlsx --filter filters/shipped_orders.json
sqlitexport export db.sqlite returns.xlsx --filter filters/returns.json
```

### Combining with Delta Exports

```bash
# Filter + Delta export for incremental processing
sqlitexport export database.db changes.xlsx \
  --filter active_users.json \
  --delta \
  --watermark-columns "updated_at"
```

## Programmatic API

### Using SelectionGrammar in Code

```csharp
using DB2XL;
using DB2XL.Query;
using System.Text.Json;

// Load filter from file
var json = File.ReadAllText("filter.json");
var grammar = JsonSerializer.Deserialize<SelectionGrammar>(json);

// Use in export
var options = new SqliteToExcelOptions
{
    SelectionGrammar = grammar,
    WriteAllAsText = true,
    IncludeMetadataSheet = true
};

SqliteToExcel.Export("database.db", "filtered.xlsx", options);
```

### Building Filters Programmatically

```csharp
// Create filter in code
var grammar = new SelectionGrammar
{
    Table = "orders",
    Select = new[] { "order_id", "customer_id", "total", "status" },
    Where = new AndExpression
    {
        Conditions = new IWhereExpression[]
        {
            new ComparisonExpression
            {
                Column = "status",
                Operator = ComparisonOperator.Equal,
                Value = "pending"
            },
            new ComparisonExpression
            {
                Column = "total",
                Operator = ComparisonOperator.GreaterThan,
                Value = 1000
            }
        }
    },
    OrderBy = new[]
    {
        new OrderByClause { Column = "total", Direction = SortDirection.Descending }
    },
    Limit = 100
};

var options = new SqliteToExcelOptions
{
    SelectionGrammar = grammar
};

SqliteToExcel.Export("database.db", "filtered.xlsx", options);
```

### Multiple Table Exports with Different Filters

```csharp
// Export different tables with specific filters
var filters = new Dictionary<string, SelectionGrammar>
{
    ["users"] = LoadFilter("filters/active_users.json"),
    ["orders"] = LoadFilter("filters/recent_orders.json"),
    ["products"] = LoadFilter("filters/in_stock.json")
};

foreach (var (table, grammar) in filters)
{
    var options = new SqliteToExcelOptions
    {
        SelectionGrammar = grammar
    };
    
    SqliteToExcel.Export("database.db", $"{table}_filtered.xlsx", options);
}
```

## Performance Considerations

### Index Usage

SelectionGrammar queries are translated to standard SQL, so they benefit from database indexes:

```json
{
  "table": "large_table",
  "where": {
    "type": "comparison",
    "column": "indexed_column",  // Uses index if available
    "operator": "=",
    "value": "specific_value"
  }
}
```

### Optimizing Complex Queries

1. **Order conditions by selectivity** - Most selective conditions first
2. **Use indexes** - Filter on indexed columns when possible
3. **Limit result sets** - Always use LIMIT for large tables
4. **Avoid LIKE with leading wildcards** - `LIKE '%text'` can't use indexes

### Query Analysis

Use the analyze command to understand query performance:

```bash
# Analyze query performance
sqlitexport analyze database.db \
  --tables "orders" \
  --performance \
  --suggest-indexes
```

## Security

### SQL Injection Protection

SelectionGrammar provides built-in SQL injection protection:

- ✅ **Column names are validated** - Must exist in the table
- ✅ **Values are parameterized** - Never concatenated into SQL
- ✅ **Operators are whitelisted** - Only valid operators allowed
- ✅ **Table names are quoted** - Prevents injection via table names

### Safe Value Handling

```json
{
  "table": "users",
  "where": {
    "type": "comparison",
    "column": "name",
    "operator": "=",
    "value": "O'Brien'; DROP TABLE users; --"  // Safely handled
  }
}
```

This is safely translated to:
```sql
SELECT * FROM "users" WHERE "name" = ?
-- Parameter: "O'Brien'; DROP TABLE users; --"
```

### Validation

The system validates:
- Table existence
- Column existence
- Operator validity
- Value type compatibility
- JSON structure

## Troubleshooting

### Common Issues

#### Invalid JSON Structure

```json
// ❌ Wrong
{
  "table": "users",
  "where": "status = 'active'"  // String instead of object
}

// ✅ Correct
{
  "table": "users",
  "where": {
    "type": "comparison",
    "column": "status",
    "operator": "=",
    "value": "active"
  }
}
```

#### Missing Required Fields

```json
// ❌ Wrong - missing operator
{
  "table": "users",
  "where": {
    "type": "comparison",
    "column": "age",
    "value": 18
  }
}

// ✅ Correct
{
  "table": "users",
  "where": {
    "type": "comparison",
    "column": "age",
    "operator": ">=",
    "value": 18
  }
}
```

#### Invalid Operator for Value Type

```json
// ❌ Wrong - IN requires array
{
  "where": {
    "type": "comparison",
    "column": "status",
    "operator": "in",
    "value": "active"  // Should be array
  }
}

// ✅ Correct
{
  "where": {
    "type": "comparison",
    "column": "status",
    "operator": "in",
    "value": ["active", "pending"]
  }
}
```

### Debugging Tips

1. **Use --dry-run** to preview without executing:
   ```bash
   sqlitexport export db.sqlite test.xlsx --filter query.json --dry-run
   ```

2. **Validate JSON** with online tools or IDE
3. **Start simple** - Test with basic filters first
4. **Check table/column names** - Case sensitivity matters
5. **Review error messages** - They indicate the specific issue

## Examples Repository

Find more examples in the `examples/filters/` directory:

- `simple_filter.json` - Basic table selection
- `date_range_filter.json` - Date filtering with ranges
- `complex_filter.json` - Nested AND/OR conditions
- `pagination_filter.json` - Sorting and pagination

## Related Documentation

- [GETTING_STARTED.md](../GETTING_STARTED.md) - Quick start guide
- [DELTA_EXPORTS.md](DELTA_EXPORTS.md) - Incremental export strategies
- [examples/README.md](../examples/README.md) - All example files
- [Filters.md](../Filters.md) - Original filter specification