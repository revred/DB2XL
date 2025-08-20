# Change Log Delta Export Examples

## Installing Change Tracking
First, install triggers to track all changes to your tables:

```bash
# Install changelog triggers for all tables
sqlitexport export database.db output.xlsx --install-changelog

# The system creates:
# - __changes table to store all changes
# - INSERT triggers to capture new records
# - UPDATE triggers to capture modifications
# - DELETE triggers to capture deletions
```

## Exporting Changes
After triggers are installed, export only the changes:

```bash
# Export all changes since triggers were installed
sqlitexport export database.db changes.xlsx \
  --delta \
  --delta-strategy changelog

# Export changes since last checkpoint
sqlitexport export database.db incremental.xlsx \
  --delta \
  --delta-strategy changelog \
  --checkpoint-file last_export.checkpoint.json
```

## Change Log Features

### Full Row Data Capture
The changelog captures the complete row data for each change:
- **INSERT**: Full row data of new record
- **UPDATE**: Both old and new values
- **DELETE**: Full row data before deletion

### Change Metadata
Each change record includes:
- `change_id`: Unique identifier for the change
- `table_name`: Table where change occurred
- `operation`: Type of change (INSERT/UPDATE/DELETE)
- `changed_at`: Timestamp of the change
- `primary_key`: PK value(s) of the changed row
- `row_data`: Full row data (JSON)

## Advanced Scenarios

### Audit Trail Export
Export a complete audit trail with all changes:

```bash
# Export changes with metadata sheet showing statistics
sqlitexport export database.db audit_trail.xlsx \
  --delta \
  --delta-strategy changelog \
  --metadata \
  --include-views
```

### Filtered Change Export
Export only specific types of changes:

```bash
# Create filter for only INSERT and UPDATE operations
cat > modifications_only.json << EOF
{
  "table": "__changes",
  "select": ["*"],
  "where": {
    "type": "or",
    "conditions": [
      {
        "type": "comparison",
        "column": "operation",
        "operator": "=",
        "value": "INSERT"
      },
      {
        "type": "comparison",
        "column": "operation",
        "operator": "=",
        "value": "UPDATE"
      }
    ]
  }
}
EOF

sqlitexport export database.db modifications.xlsx \
  --filter modifications_only.json
```

### Cleanup Old Changes
Periodically clean up old change records to manage table size:

```sql
-- Connect to your database and run:
DELETE FROM __changes 
WHERE changed_at < datetime('now', '-30 days');

-- Or export and archive old changes first:
sqlitexport export database.db archive_2024.xlsx \
  --tables "__changes" \
  --where "changed_at < '2025-01-01'"
```