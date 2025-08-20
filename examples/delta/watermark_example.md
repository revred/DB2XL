# Watermark Delta Export Examples

## Basic Watermark Export
Export only records that have changed since the last export based on timestamp columns:

```bash
# Initial export - creates checkpoint file automatically
sqlitexport export database.db changes.xlsx \
  --delta \
  --delta-strategy watermark \
  --watermark-columns "updated_at,created_at"

# Subsequent exports - uses checkpoint to get only new/changed records
sqlitexport export database.db changes_new.xlsx \
  --delta \
  --delta-strategy watermark \
  --checkpoint-file changes.checkpoint.json
```

## Auto-detected Watermark Columns
Let the system automatically detect the best watermark columns:

```bash
sqlitexport export database.db auto_delta.xlsx \
  --delta \
  --delta-strategy watermark
```

The system prioritizes columns in this order:
1. `updated_at`, `modified_at`, `last_modified`
2. `created_at`, `created_on`, `timestamp`
3. Auto-incrementing `id` columns
4. Other timestamp/datetime columns

## Multiple Table Delta Export
Export changes from specific tables only:

```bash
sqlitexport export database.db multi_delta.xlsx \
  --delta \
  --delta-strategy watermark \
  --tables "users,orders,products" \
  --checkpoint-file multi.checkpoint.json
```

## Combining with Filters
Use delta exports with advanced filtering:

```bash
# Create a filter file for high-value changes
cat > high_value_filter.json << EOF
{
  "table": "orders",
  "select": ["*"],
  "where": {
    "type": "comparison",
    "column": "total_amount",
    "operator": ">",
    "value": "1000"
  }
}
EOF

# Export only high-value order changes
sqlitexport export database.db high_value_delta.xlsx \
  --filter high_value_filter.json \
  --delta \
  --delta-strategy watermark
```