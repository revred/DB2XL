# Delta Exports: Incremental Data Processing

DB2XL provides powerful delta export capabilities that allow you to export only the data that has changed since your last export. This is essential for large databases where you need to process incremental changes efficiently without re-exporting the entire dataset every time.

## Table of Contents

- [Overview](#overview)
- [Delta Export Strategies](#delta-export-strategies)
- [Watermark Strategy](#watermark-strategy)
- [Change Log Strategy](#change-log-strategy)
- [Console Tool Usage](#console-tool-usage)
- [Programmatic API](#programmatic-api)
- [Checkpoint Management](#checkpoint-management)
- [Performance Considerations](#performance-considerations)
- [Best Practices](#best-practices)
- [Troubleshooting](#troubleshooting)

## Overview

Delta exports solve the common problem of processing large datasets where you only need to handle changes since the last processing run. Instead of exporting and processing millions of rows every time, delta exports identify and export only:

- **New records** - Recently inserted rows
- **Modified records** - Updated rows since last export
- **Deleted records** - Rows that were removed (change log strategy only)

### Key Benefits

- **Dramatically reduced export time** - Process only changed data
- **Lower resource usage** - Minimize CPU, memory, and storage requirements
- **Incremental processing** - Enable continuous data pipeline workflows
- **Change tracking** - Maintain full audit trails of database modifications
- **Resume capability** - Pick up where you left off after interruptions

## Delta Export Strategies

DB2XL supports two main delta export strategies, each optimized for different use cases:

### Strategy Comparison

| Feature | Watermark | Change Log |
|---------|-----------|-------------|
| **Setup Required** | None | Triggers must be installed |
| **Detects Deletes** | No | Yes |
| **Performance Impact** | Minimal | Low (trigger overhead) |
| **Storage Overhead** | None | Change log table |
| **Best For** | Append-heavy workloads | Full audit requirements |
| **Data Fidelity** | Current state only | Complete change history |

## Watermark Strategy

The watermark strategy uses timestamp or auto-incrementing ID columns to identify records that have changed since the last export. This is the simplest and most efficient approach for most use cases.

### How Watermark Works

1. **Column Detection** - System identifies the best watermark column(s)
2. **Checkpoint Creation** - Last export values are saved to a checkpoint file
3. **Incremental Query** - Next export queries for records newer than checkpoint
4. **Checkpoint Update** - New maximum values are saved for next iteration

### Automatic Column Detection

The system automatically selects the best watermark columns in priority order:

1. **Modification timestamps** - `updated_at`, `modified_at`, `last_modified`
2. **Creation timestamps** - `created_at`, `created_on`, `timestamp`
3. **Auto-increment IDs** - Primary key columns with INTEGER AUTOINCREMENT
4. **Other timestamps** - Any datetime/timestamp columns
5. **Numeric sequences** - Monotonically increasing numeric columns

### Basic Watermark Usage

```bash
# First export - creates checkpoint automatically
sqlitexport export database.db initial.xlsx --delta

# Subsequent exports - uses checkpoint to get only changes
sqlitexport export database.db changes.xlsx --delta
```

### Manual Column Selection

```bash
# Specify watermark columns explicitly
sqlitexport export database.db delta.xlsx \
  --delta \
  --watermark-columns "updated_at,last_modified"

# Use auto-increment ID as watermark
sqlitexport export database.db delta.xlsx \
  --delta \
  --watermark-columns "id"
```

### Multiple Table Watermarks

```bash
# Delta export for specific tables only
sqlitexport export database.db multi_delta.xlsx \
  --delta \
  --tables "users,orders,products" \
  --checkpoint-file multi_table.checkpoint.json
```

### Watermark Strategy Examples

#### Time-based Watermark
```json
// Generated checkpoint file: database.checkpoint.json
{
  "version": "1.0",
  "strategy": "watermark",
  "created_at": "2024-01-15T10:30:00Z",
  "last_export_at": "2024-01-15T14:22:00Z",
  "table_watermarks": {
    "orders": {
      "watermark_columns": ["updated_at"],
      "last_values": {
        "updated_at": "2024-01-15T14:22:00Z"
      }
    },
    "users": {
      "watermark_columns": ["last_modified"],
      "last_values": {
        "last_modified": "2024-01-15T14:21:30Z"
      }
    }
  },
  "total_records_exported": 1247
}
```

#### ID-based Watermark
```json
{
  "version": "1.0",
  "strategy": "watermark",
  "created_at": "2024-01-15T10:30:00Z",
  "last_export_at": "2024-01-15T14:22:00Z",
  "table_watermarks": {
    "transactions": {
      "watermark_columns": ["id"],
      "last_values": {
        "id": 1247893
      }
    }
  }
}
```

## Change Log Strategy

The change log strategy installs database triggers to capture all changes (INSERT, UPDATE, DELETE) into a dedicated change log table. This provides complete audit trail capabilities but requires initial setup.

### Change Log Features

- **Complete change history** - Every modification is captured
- **Delete detection** - Captures data before deletion
- **Full row data** - Both old and new values for updates
- **Change metadata** - Timestamps, operation types, primary keys
- **Resumable exports** - Pick up from any point in the change log

### Installing Change Log Triggers

```bash
# Install triggers for all tables
sqlitexport export database.db setup.xlsx --install-changelog

# The system creates:
# - __changes table to store all modifications
# - INSERT triggers for each table
# - UPDATE triggers for each table  
# - DELETE triggers for each table
```

### Change Log Table Structure

The `__changes` table contains:

| Column | Type | Description |
|--------|------|-------------|
| `change_id` | INTEGER PRIMARY KEY | Unique change identifier |
| `table_name` | TEXT | Table where change occurred |
| `operation` | TEXT | INSERT, UPDATE, or DELETE |
| `changed_at` | TEXT | ISO-8601 timestamp of change |
| `primary_key` | TEXT | JSON array of PK values |
| `row_data` | TEXT | JSON object with full row data |

### Change Log Export Usage

```bash
# Export all changes since triggers were installed
sqlitexport export database.db changes.xlsx \
  --delta \
  --delta-strategy changelog

# Export changes since specific checkpoint
sqlitexport export database.db incremental.xlsx \
  --delta \
  --delta-strategy changelog \
  --checkpoint-file last_export.checkpoint.json
```

### Change Log Strategy Examples

#### Basic Change Record
```json
{
  "change_id": 1001,
  "table_name": "orders",
  "operation": "INSERT",
  "changed_at": "2024-01-15T14:22:00Z",
  "primary_key": [12345],
  "row_data": {
    "id": 12345,
    "customer_id": 789,
    "total": 1599.99,
    "status": "pending",
    "created_at": "2024-01-15T14:22:00Z"
  }
}
```

#### Update Record (Before/After)
```json
{
  "change_id": 1002,
  "table_name": "orders", 
  "operation": "UPDATE",
  "changed_at": "2024-01-15T14:25:00Z",
  "primary_key": [12345],
  "row_data": {
    "old": {
      "id": 12345,
      "status": "pending",
      "updated_at": "2024-01-15T14:22:00Z"
    },
    "new": {
      "id": 12345,
      "status": "confirmed",
      "updated_at": "2024-01-15T14:25:00Z"
    }
  }
}
```

## Console Tool Usage

### Quick Start Commands

```bash
# Auto-detect watermark strategy
sqlitexport export database.db delta.xlsx --delta

# Explicit watermark strategy
sqlitexport export database.db delta.xlsx \
  --delta \
  --delta-strategy watermark

# Change log strategy
sqlitexport export database.db delta.xlsx \
  --delta \
  --delta-strategy changelog
```

### Advanced Console Options

```bash
# Combine with filtering
sqlitexport export database.db filtered_delta.xlsx \
  --delta \
  --filter high_value_orders.json \
  --checkpoint-file orders.checkpoint.json

# Include metadata and transformations
sqlitexport export database.db delta.xlsx \
  --delta \
  --metadata \
  --transform \
  --dual-sheets

# Specify custom checkpoint location
sqlitexport export database.db delta.xlsx \
  --delta \
  --checkpoint-file /backups/checkpoints/db.checkpoint.json
```

### Checkpoint File Management

```bash
# Use named checkpoint files for different workflows
sqlitexport export database.db daily.xlsx \
  --delta \
  --checkpoint-file daily.checkpoint.json

sqlitexport export database.db hourly.xlsx \
  --delta \
  --checkpoint-file hourly.checkpoint.json

# Reset checkpoint to start fresh
rm database.checkpoint.json
sqlitexport export database.db full_export.xlsx --delta
```

### Dry Run Mode

```bash
# Preview what would be exported without actual export
sqlitexport export database.db preview.xlsx \
  --delta \
  --dry-run
```

## Programmatic API

### Watermark Delta Export

```csharp
using DB2XL.DeltaExport;
using SqliteXport;

// Basic watermark delta export
var deltaOptions = new DeltaExportOptions
{
    Strategy = DeltaStrategy.Watermark,
    CheckpointFile = "export.checkpoint.json",
    WatermarkColumns = new[] { "updated_at", "created_at" }
};

var exportOptions = new SqliteToExcelOptions
{
    DeltaExport = deltaOptions,
    IncludeMetadataSheet = true
};

SqliteToExcel.Export("database.db", "delta.xlsx", exportOptions);
```

### Change Log Delta Export

```csharp
// Install change log triggers first
var changeLogService = new ChangeLogDeltaService();
using var connection = new SqliteConnection("Data Source=database.db");
connection.Open();

// Install triggers for all tables
await changeLogService.InstallTriggersAsync(connection);

// Export changes
var deltaOptions = new DeltaExportOptions
{
    Strategy = DeltaStrategy.ChangeLog,
    CheckpointFile = "changelog.checkpoint.json"
};

var exportOptions = new SqliteToExcelOptions
{
    DeltaExport = deltaOptions
};

SqliteToExcel.Export("database.db", "changes.xlsx", exportOptions);
```

### Custom Watermark Logic

```csharp
// Create custom watermark strategy
var customWatermark = new WatermarkDeltaService(connectionString);

// Load existing checkpoint
var checkpoint = DeltaCheckpoint.LoadFromFile("custom.checkpoint.json");

// Get delta records with custom logic
var deltaRecords = await customWatermark.GetDeltaRecordsAsync(
    tableName: "events",
    watermarkColumns: new[] { "event_time", "sequence_id" },
    lastCheckpoint: checkpoint
);

// Process delta records and update checkpoint
foreach (var record in deltaRecords)
{
    // Process record
    Console.WriteLine($"Processing record: {record["id"]}");
}

// Save updated checkpoint
checkpoint.SaveToFile("custom.checkpoint.json");
```

## Checkpoint Management

### Checkpoint File Format

```json
{
  "version": "1.0",
  "strategy": "watermark|changelog", 
  "created_at": "2024-01-15T10:30:00Z",
  "last_export_at": "2024-01-15T14:22:00Z",
  "table_watermarks": {
    "table_name": {
      "watermark_columns": ["column1", "column2"],
      "last_values": {
        "column1": "2024-01-15T14:22:00Z",
        "column2": 12345
      }
    }
  },
  "total_records_exported": 1247,
  "export_duration_seconds": 23.7,
  "metadata": {
    "database_file": "/path/to/database.db",
    "database_size_bytes": 104857600,
    "export_options": {
      "writeAllAsText": true,
      "includeMetadata": true
    }
  }
}
```

### Checkpoint Operations

```csharp
using DB2XL.DeltaExport;

// Load checkpoint from file
var checkpoint = DeltaCheckpoint.LoadFromFile("export.checkpoint.json");

// Create new checkpoint
var newCheckpoint = new DeltaCheckpoint
{
    Strategy = DeltaStrategy.Watermark,
    CreatedAt = DateTime.UtcNow
};

// Add table watermark
newCheckpoint.AddTableWatermark("orders", 
    new[] { "updated_at" }, 
    new Dictionary<string, object> { ["updated_at"] = DateTime.UtcNow }
);

// Save checkpoint
newCheckpoint.SaveToFile("new.checkpoint.json");

// Reset checkpoint (start fresh)
newCheckpoint.Reset();
```

## Performance Considerations

### Watermark Strategy Performance

**Advantages:**
- ⚡ **Minimal query overhead** - Simple WHERE clause filtering
- 🚀 **Index-friendly** - Leverages existing timestamp/ID indexes
- 💾 **No storage overhead** - No additional tables required
- 📊 **Predictable performance** - Query time scales with changes, not total data

**Index Recommendations:**
```sql
-- Ensure watermark columns are indexed
CREATE INDEX idx_orders_updated_at ON orders(updated_at);
CREATE INDEX idx_users_last_modified ON users(last_modified);
CREATE INDEX idx_events_created_at ON events(created_at);

-- Composite indexes for better performance
CREATE INDEX idx_orders_status_updated ON orders(status, updated_at);
```

### Change Log Strategy Performance

**Trigger Overhead:**
- INSERT triggers: ~5-10% performance impact
- UPDATE triggers: ~10-15% performance impact  
- DELETE triggers: ~5-10% performance impact

**Storage Requirements:**
- Change log table grows over time
- Approximately 2-3x original row size per change
- Plan for periodic cleanup/archival

**Optimization Tips:**
```sql
-- Index the change log table for faster exports
CREATE INDEX idx_changes_table_changed ON __changes(table_name, changed_at);
CREATE INDEX idx_changes_changeid ON __changes(change_id);

-- Regular cleanup to manage size
DELETE FROM __changes WHERE changed_at < datetime('now', '-90 days');
```

### Large Dataset Best Practices

```bash
# Increase batch size for better throughput
sqlitexport export database.db delta.xlsx \
  --delta \
  --batch-size 50000

# Use parallel processing for multiple tables
sqlitexport export database.db delta.xlsx \
  --delta \
  --parallel

# Set longer timeout for large change sets
sqlitexport export database.db delta.xlsx \
  --delta \
  --timeout 1800
```

## Best Practices

### Strategy Selection Guidelines

**Choose Watermark When:**
- ✅ You primarily need new/updated records (not deletes)
- ✅ Your tables have reliable timestamp or ID columns
- ✅ You want minimal database impact
- ✅ Storage space is a concern
- ✅ Performance is the top priority

**Choose Change Log When:**
- ✅ You need to track deleted records
- ✅ You require complete audit trails
- ✅ You can tolerate slight performance overhead
- ✅ You have storage capacity for change logs
- ✅ Data compliance requires change tracking

### Operational Best Practices

#### 1. Checkpoint Management
```bash
# Use descriptive checkpoint names
sqlitexport export db.db daily.xlsx --checkpoint-file daily_export.checkpoint.json
sqlitexport export db.db hourly.xlsx --checkpoint-file hourly_sync.checkpoint.json

# Backup checkpoint files regularly
cp *.checkpoint.json /backups/checkpoints/
```

#### 2. Error Recovery
```bash
# If export fails, checkpoint is not updated - safe to retry
sqlitexport export db.db delta.xlsx --delta

# For corrupted checkpoints, reset and start fresh
rm database.checkpoint.json
sqlitexport export db.db full_reset.xlsx --delta
```

#### 3. Multi-Environment Workflows
```bash
# Development environment
sqlitexport export dev.db dev_delta.xlsx --checkpoint-file dev.checkpoint.json

# Staging environment  
sqlitexport export stage.db stage_delta.xlsx --checkpoint-file stage.checkpoint.json

# Production environment
sqlitexport export prod.db prod_delta.xlsx --checkpoint-file prod.checkpoint.json
```

#### 4. Monitoring and Alerting
```bash
# Export with metadata to track performance
sqlitexport export db.db delta.xlsx \
  --delta \
  --metadata \
  --output-manifest manifest.json

# Parse manifest for monitoring
cat manifest.json | jq '.export_summary.total_records_exported'
```

### Data Pipeline Integration

```bash
#!/bin/bash
# Example ETL pipeline script

# 1. Export delta changes
sqlitexport export app.db changes.xlsx \
  --delta \
  --checkpoint-file pipeline.checkpoint.json \
  --transform \
  --metadata

# 2. Process exported data
python process_changes.py changes.xlsx

# 3. Update downstream systems
python update_warehouse.py changes.xlsx

# 4. Archive processed export
mv changes.xlsx "processed/changes_$(date +%Y%m%d_%H%M%S).xlsx"
```

## Troubleshooting

### Common Issues

#### No Suitable Watermark Column Found
```
Error: No suitable watermark columns found for table 'logs'
```

**Solutions:**
1. Add a timestamp column:
   ```sql
   ALTER TABLE logs ADD COLUMN created_at TEXT DEFAULT (datetime('now'));
   ```

2. Use explicit column specification:
   ```bash
   sqlitexport export db.db delta.xlsx --watermark-columns "id"
   ```

3. Switch to change log strategy:
   ```bash
   sqlitexport export db.db delta.xlsx --delta-strategy changelog --install-changelog
   ```

#### Checkpoint File Corruption
```
Error: Cannot parse checkpoint file 'export.checkpoint.json'
```

**Solutions:**
1. Validate checkpoint JSON:
   ```bash
   cat export.checkpoint.json | jq .
   ```

2. Reset and start fresh:
   ```bash
   rm export.checkpoint.json
   sqlitexport export db.db fresh_start.xlsx --delta
   ```

3. Restore from backup:
   ```bash
   cp backup/export.checkpoint.json ./
   ```

#### Change Log Trigger Conflicts
```
Error: Trigger '__changes_insert_orders' already exists
```

**Solutions:**
1. Drop existing triggers:
   ```sql
   DROP TRIGGER IF EXISTS __changes_insert_orders;
   DROP TRIGGER IF EXISTS __changes_update_orders;
   DROP TRIGGER IF EXISTS __changes_delete_orders;
   ```

2. Reinstall triggers:
   ```bash
   sqlitexport export db.db setup.xlsx --install-changelog
   ```

#### Large Change Log Table
```
Warning: Change log table contains 10M+ records
```

**Solutions:**
1. Archive old changes:
   ```bash
   sqlitexport export db.db archive_2024.xlsx --tables "__changes" --where "changed_at < '2025-01-01'"
   ```

2. Clean up old records:
   ```sql
   DELETE FROM __changes WHERE changed_at < datetime('now', '-90 days');
   VACUUM;
   ```

### Performance Troubleshooting

#### Slow Watermark Queries
```bash
# Check if watermark columns are indexed
sqlitexport analyze db.db --suggest-indexes --performance

# Add missing indexes
sqlite3 db.db "CREATE INDEX idx_table_watermark ON table_name(watermark_column);"
```

#### Change Log Performance Impact
```bash
# Monitor trigger performance
sqlitexport analyze db.db --performance --include-triggers

# Consider batching large operations outside of trigger hours
# Or temporarily disable triggers for bulk operations
```

## Advanced Scenarios

### Combining Delta with Filtering

```bash
# Export only high-value order changes
cat > high_value_filter.json << EOF
{
  "table": "orders",
  "where": {
    "type": "comparison",
    "column": "total",
    "operator": ">",
    "value": 1000
  }
}
EOF

sqlitexport export db.db high_value_delta.xlsx \
  --filter high_value_filter.json \
  --delta \
  --checkpoint-file high_value.checkpoint.json
```

### Multi-Database Delta Sync

```bash
# Sync changes from multiple databases
databases=("sales.db" "inventory.db" "customers.db")

for db in "${databases[@]}"; do
  echo "Processing delta for $db"
  sqlitexport export "$db" "${db%.db}_delta.xlsx" \
    --delta \
    --checkpoint-file "${db%.db}.checkpoint.json"
done
```

### Real-time Change Processing

```bash
# Continuous monitoring script
while true; do
  sqlitexport export realtime.db changes.xlsx \
    --delta \
    --checkpoint-file realtime.checkpoint.json
    
  if [ -f "changes.xlsx" ]; then
    python process_realtime_changes.py changes.xlsx
    mv changes.xlsx "processed/changes_$(date +%s).xlsx"
  fi
  
  sleep 60
done
```

## Related Documentation

- [ADVANCED_FILTERING.md](ADVANCED_FILTERING.md) - Combine delta exports with sophisticated filtering
- [GETTING_STARTED.md](../GETTING_STARTED.md) - Basic setup and first exports
- [examples/delta/](../examples/delta/) - Ready-to-use delta export examples
- [Console Tool Guide](../SqliteXport.Console.md) - Complete console tool reference
- [CLAUDE.md](../CLAUDE.md) - Technical specifications and API reference

---

**Delta exports make incremental data processing fast and efficient. Track only what changes, when it changes.** 🚀