# DB2XL Examples

This directory contains examples demonstrating the advanced features of DB2XL SqliteXport Console.

## 📁 Directory Structure

```
examples/
├── filters/              # JSON SelectionGrammar filter examples
│   ├── simple_filter.json
│   ├── date_range_filter.json
│   ├── complex_filter.json
│   └── pagination_filter.json
├── delta/               # Delta export examples
│   ├── watermark_example.md
│   └── changelog_example.md
└── README.md           # This file
```

## 🚀 Quick Start Examples

### Basic Export
```bash
# Export entire database to Excel
sqlitexport export mydata.db output.xlsx

# Export to JSONL format
sqlitexport export mydata.db output.jsonl

# Export with transformations
sqlitexport export mydata.db output.xlsx --transform
```

### Advanced Filtering

#### Using JSON Filter Files
```bash
# Simple table export with column selection
sqlitexport export db.sqlite users.xlsx --filter filters/simple_filter.json

# Date range filtering with sorting
sqlitexport export db.sqlite transactions.xlsx --filter filters/date_range_filter.json

# Complex multi-condition filtering
sqlitexport export db.sqlite orders.xlsx --filter filters/complex_filter.json

# Pagination for large datasets
sqlitexport export db.sqlite products_page3.xlsx --filter filters/pagination_filter.json
```

#### Command-Line Filtering
```bash
# WHERE clause filtering
sqlitexport export db.sqlite high_value.xlsx --where "amount > 10000"

# Order by with descending sort
sqlitexport export db.sqlite recent.xlsx --order-by "created_at" --order-desc

# Combine multiple filters
sqlitexport export db.sqlite filtered.xlsx \
  --tables "users,orders" \
  --where "status = 'active'" \
  --order-by "created_at,name" \
  --max-rows 1000
```

### Delta Exports

#### Watermark Strategy
```bash
# Auto-detect watermark columns
sqlitexport export db.sqlite delta.xlsx --delta

# Specify watermark columns
sqlitexport export db.sqlite delta.xlsx \
  --delta \
  --watermark-columns "updated_at,modified_at"

# Use checkpoint for incremental exports
sqlitexport export db.sqlite incremental.xlsx \
  --delta \
  --checkpoint-file last_export.json
```

#### Change Log Strategy
```bash
# Install change tracking triggers
sqlitexport export db.sqlite output.xlsx --install-changelog

# Export captured changes
sqlitexport export db.sqlite changes.xlsx \
  --delta \
  --delta-strategy changelog
```

## 📊 Database Analysis

### Primary Key Discovery
```bash
# Analyze all PK strategies
sqlitexport analyze db.sqlite --pk-discovery --pk-strategy

# Check PK quality scores
sqlitexport analyze db.sqlite --pk-quality

# Get deterministic ordering info
sqlitexport analyze db.sqlite --deterministic-order
```

### Performance Analysis
```bash
# Get index suggestions
sqlitexport analyze db.sqlite --suggest-indexes

# Full performance analysis
sqlitexport analyze db.sqlite --performance

# Include data samples
sqlitexport analyze db.sqlite --include-data --sample-size 10
```

### Export Analysis Results
```bash
# Save analysis as JSON
sqlitexport analyze db.sqlite --output analysis.json --format json

# Save as YAML
sqlitexport analyze db.sqlite --output analysis.yaml --format yaml
```

## 🔧 Advanced Options

### Dual Export Strategies
```bash
# Export raw and transformed data to separate sheets
sqlitexport export db.sqlite output.xlsx --transform --dual-sheets

# Export to separate workbooks
sqlitexport export db.sqlite output.xlsx --transform --dual-workbooks
```

### BLOB Handling
```bash
# Skip BLOB columns
sqlitexport export db.sqlite output.xlsx --blob-mode skip

# Export BLOBs as Base64
sqlitexport export db.sqlite output.xlsx --blob-mode base64

# Export BLOBs as Hex (default)
sqlitexport export db.sqlite output.xlsx --blob-mode hex
```

### Performance Tuning
```bash
# Adjust batch size for large tables
sqlitexport export db.sqlite output.xlsx --batch-size 50000

# Set custom timeout
sqlitexport export db.sqlite output.xlsx --timeout 600

# Enable parallel processing
sqlitexport export db.sqlite output.xlsx --parallel
```

## 📝 JSON Filter File Format

The SelectionGrammar JSON format supports:

- **Table Selection**: Specify which table to query
- **Column Selection**: Choose specific columns or use "*" for all
- **WHERE Conditions**: Complex filtering with AND/OR logic
- **Sorting**: Multiple ORDER BY columns with direction
- **Pagination**: LIMIT and OFFSET for result sets

### Comparison Operators
- `=` : Equal
- `!=` : Not equal  
- `>` : Greater than
- `>=` : Greater than or equal
- `<` : Less than
- `<=` : Less than or equal
- `in` : In list of values
- `like` : SQL LIKE pattern matching

### Example Structure
```json
{
  "table": "table_name",
  "select": ["col1", "col2", "*"],
  "where": {
    "type": "comparison|and|or",
    "column": "column_name",
    "operator": "=|!=|>|>=|<|<=|in|like",
    "value": "literal_value",
    "conditions": []  // for and/or types
  },
  "orderBy": [
    {
      "column": "column_name",
      "direction": "asc|desc"
    }
  ],
  "limit": 100,
  "offset": 0
}
```

## 🔐 Security Considerations

- All WHERE clauses are parameterized to prevent SQL injection
- Table and column names are properly quoted
- Filter files are validated before execution
- Use `--dry-run` to preview operations without executing

## 📚 More Information

- See [GETTING_STARTED.md](../GETTING_STARTED.md) for installation and setup
- See [ADVANCED_FILTERING.md](../docs/ADVANCED_FILTERING.md) for detailed filtering guide
- See [DELTA_EXPORTS.md](../docs/DELTA_EXPORTS.md) for delta export strategies
- See [CLAUDE.md](../CLAUDE.md) for technical specifications