# CLAUDE.md — Deterministic SQLite → Excel Exporter (C#)

A spec and starter implementation plan for a **robust, simple, and deterministic** component that exports **every table** in a SQLite database to a **multi‑sheet Excel (.xlsx)** file — one sheet per table — with **byte‑for‑byte consistent cell text** representing the database content.

> **Prime Directive:** The Excel file must reflect the SQLite data *exactly as stored and read*, with **no implicit conversions** (dates, numbers, booleans) unless explicitly opted in. By default, **everything is written as text** to guarantee fidelity.

---

## 1) Goals & Non‑Negotiables

- **Deterministic output**: same DB → same XLSX **bit‑for‑bit** (modulo XLSX timestamps) under identical options.
- **Fidelity first**: default mode writes **verbatim text** of each SQLite cell.
- **Robust**: handles large tables by chunking; safe when a table exceeds Excel row/column limits (with controlled splitting).
- **Simple API**: one method is all you need; sane defaults; minimal dependencies.
- **Safe reads**: snapshot/immutable view (`Mode=ReadOnly`, read transaction). No writes to DB.

---

## 2) Public API (C#)

```csharp
public sealed class SqliteToExcelOptions
{
    public bool WriteAllAsText { get; init; } = true;           // Prime Directive
    public bool PreserveNumericTypes { get; init; } = false;    // If true, numbers as numbers (risk: Excel auto-format)
    public bool IncludeMetadataSheet { get; init; } = true;
    public string MetadataSheetName { get; init; } = "_Export_Metadata";
    public int ReadBatchSize { get; init; } = 25_000;           // rows per batch
    public int CommandTimeoutSeconds { get; init; } = 180;
    public string? TableNameLikeFilter { get; init; } = null;   // e.g., "sales_%"
    public bool IncludeViews { get; init; } = false;            // export views as sheets
    public BlobRenderMode BlobMode { get; init; } = BlobRenderMode.Hex; // Skip | Hex | Base64
    public bool OrderRowsDeterministically { get; init; } = true; // ORDER BY PK or rowid
    public bool SplitOversizeSheets { get; init; } = true;      // _p1, _p2...
    public CultureInfo InvariantCulture { get; init; } = CultureInfo.InvariantCulture;
}

public enum BlobRenderMode { Skip, Hex, Base64 }

public static class SqliteToExcel
{
    public static void Export(
        string sqlitePath,
        string xlsxPath,
        SqliteToExcelOptions? options = null);
}
```

- **Defaults** are chosen for **max fidelity and robustness**. The caller can opt in to numeric typing if needed.

---

## 3) Libraries & Rationale

- **SQLite**: `Microsoft.Data.Sqlite` (official, robust, cross‑platform).
- **Excel**: `ClosedXML` (very simple) for the default build.
  - For ultra‑large exports, provide an **optional** Streaming build using `DocumentFormat.OpenXml` with `OpenXmlWriter` (SAX) — see §10.
- **Hashing**: `System.Security.Cryptography` for per‑table checksums (written to metadata sheet for verification).

**NuGet**

```
Microsoft.Data.Sqlite
ClosedXML
DocumentFormat.OpenXml   // optional streaming variant
```

---

## 4) Table & Schema Discovery (Deterministic)

- Enumerate tables (and optionally views) via:
  ```sql
  SELECT name, type
  FROM sqlite_master
  WHERE type IN ('table', 'view')
    AND name NOT LIKE 'sqlite_%'
    AND (@filter IS NULL OR name LIKE @filter)
  ORDER BY name;
  ```
- Get column order via:
  ```sql
  PRAGMA table_info("{table}");
  ```
  Use the natural order from `cid` ascending.
- Primary key ordering for deterministic row order:
  - If PK exists: `ORDER BY pk_columns ASC`.
  - Else if table has `rowid`: `ORDER BY rowid`.
  - Else: no order (rare: WITHOUT ROWID + no PK). In such case, **document** nondeterminism in metadata.

---

## 5) Excel Constraints & Splitting

- **Row limit**: 1,048,576; **Column limit**: 16,384.
- If a table exceeds limits and `SplitOversizeSheets = true`, split into multiple sheets: `TableName_p1`, `TableName_p2`, ... (strictly deterministic chunk boundaries: batch size × batch index).
- **Sheet name sanitizer**:
  - Max 31 chars; replace `: \\ / ? * [ ]` with `_`; trim; ensure not empty; ensure uniqueness by appending `~1`, `~2`, ...

---

## 6) Data Rendering Rules (Fidelity)

- **Default**: `WriteAllAsText = true` → every cell written as text using `ToInvariantString()`:
  - `NULL` → blank cell.
  - TEXT → as is.
  - INTEGER/REAL/NUMERIC → `ToString(InvariantCulture)`.
  - BLOB → per `BlobMode`:
    - **Hex**: uppercase hex (no prefix), e.g., `0A3F...`.
    - **Base64**: standard base64.
    - **Skip**: leave blank (not recommended unless huge BLOBs).
- **PreserveNumericTypes = true**:
  - INTEGER/REAL as numeric cells; beware Excel auto‑format (e.g., leading zeros lost). Only opt in if expected.
- **Dates/Times**: never auto‑coerce to Excel serial dates. They remain text unless the caller post‑processes.

---

## 7) Read Path (Snapshot & Batching)

- Open connection:
  - `Data Source={path};Mode=ReadOnly;Cache=Shared;Pooling=True;`.
  - `PRAGMA foreign_keys = OFF;` (read‑only anyway, avoids surprises).
  - `PRAGMA journal_mode;` captured for metadata.
- Begin read transaction (`BEGIN IMMEDIATE` or `BEGIN`), ensuring a consistent snapshot across tables.
- For each table:
  - Build `SELECT` with quoted identifiers and deterministic ORDER BY (if available).
  - Stream rows with `CommandBehavior.SequentialAccess`.
  - Write to Excel in chunks.

---

## 8) Metadata Sheet (Provenance & Verification)

If `IncludeMetadataSheet`:

- One row per exported table:
  - `TableName`, `Type (table/view)`, `RowCount`, `ColumnCount`, `SplitSheets`, `OrderMode (PK|rowid|none)`
  - `Checksum_SHA256` computed over **canonical row serialization** (see below)
  - SQLite file: `Path`, `FileSizeBytes`, `LastWriteTimeUtc`
  - SQLite `user_version`, `schema_version`, `journal_mode`
  - Export timestamp UTC, component version.

**Canonical checksum serialization** (strict, text‑only):

- For each row, for each column in order:
  - `\x00` for NULL; otherwise the **exact string** as would be written to the cell in `WriteAllAsText` mode.
  - Separate columns with `\x1F` (Unit Separator), rows with `\x1E` (Record Separator).
- Feed to SHA‑256 incrementally. This ensures a stable checksum that users can recompute.

---

## 9) Errors, Logging, and Fail‑Fast Policy

- **Fail fast** on:
  - DB not found / not readable
  - XLSX path not writable
  - Excel sheet name collisions after sanitizer overflow
- **Graceful**:
  - BLOBs too large → obey `BlobMode`
  - Oversize tables → split sheets (if enabled) else throw informative exception
- **Logging** (interface free so user can plug in):
  - Minimal `ILogger` shim (optional); otherwise write no logs by default.

---

## 10) Performance: Default vs Streaming Build

- **Default (ClosedXML)**: simplest API; good up to a few hundred thousand rows per sheet on modern machines; memory usage grows with data.
- **Streaming Variant (OpenXML + OpenXmlWriter)**: truly scalable; constant memory; slightly more verbose code. Keep the same **public API**, compile conditional `#if STREAMING` or separate `SqliteToExcel.Streaming` assembly.

Recommendation: start with ClosedXML. If you hit memory pressure, switch to streaming build without changing the calling code.

---

## 11) Reference Implementation Sketch (ClosedXML)

```csharp
public static class SqliteToExcel
{
    public static void Export(string sqlitePath, string xlsxPath, SqliteToExcelOptions? options = null)
    {
        options ??= new();
        using var con = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly;Cache=Shared;Pooling=True;");
        con.Open();
        using var tx = con.BeginTransaction();

        var tables = GetObjects(con, options.TableNameLikeFilter, options.IncludeViews);
        using var wb = new ClosedXML.Excel.XLWorkbook();

        var meta = options.IncludeMetadataSheet ? new List<MetaRow>() : null;
        foreach (var t in tables)
        {
            var cols = GetColumns(con, t.Name);
            var order = DetermineOrder(con, t.Name, cols);

            var sheetBase = SanitizeSheetName(t.Name);
            int part = 1, rowInPart = 0, sheetRows = 1; // 1-based for header
            var (ws, checksum) = NewSheet(wb, sheetBase, part, cols, options);

            int totalRows = 0;
            using var cmd = con.CreateCommand();
            cmd.CommandTimeout = options.CommandTimeoutSeconds;
            cmd.CommandText = BuildSelectSql(t.Name, cols, order, options.OrderRowsDeterministically);
            using var rdr = cmd.ExecuteReader(CommandBehavior.SequentialAccess);

            while (rdr.Read())
            {
                if (sheetRows >= 1_048_576) // Excel row limit
                {
                    if (!options.SplitOversizeSheets)
                        throw new InvalidOperationException($"Table {t.Name} exceeds Excel row limit.");
                    part++; rowInPart = 0; sheetRows = 1; // reset: new sheet
                    (ws, checksum) = NewSheet(wb, sheetBase, part, cols, options);
                }

                rowInPart++; sheetRows++;
                for (int i = 0; i < cols.Count; i++)
                {
                    var (val, asText) = ReadValueAsText(rdr, i, options);
                    var cell = ws.Cell(sheetRows, i + 1);
                    if (options.WriteAllAsText || asText) cell.SetValue<string>(val);
                    else if (double.TryParse(val, NumberStyles.Any, options.InvariantCulture, out var d)) cell.SetValue<double>(d);
                    else cell.SetValue<string>(val);
                    checksum.UpdateField(val);
                }
                checksum.EndRow();
                totalRows++;
            }

            meta?.Add(new MetaRow(t.Name, t.Type, totalRows, cols.Count, part, order.Mode, checksum.FinalizeHex()));
        }

        if (meta != null) WriteMetadataSheet(wb, options, meta, sqlitePath, con);
        wb.SaveAs(xlsxPath);
        tx.Commit();
    }
}
```

> The above is a **sketch** (non‑compiling here) to show flow. Implement helpers: `GetObjects`, `GetColumns`, `DetermineOrder`, `SanitizeSheetName`, `NewSheet`, `BuildSelectSql`, `ReadValueAsText`, `WriteMetadataSheet`, and a small `ChecksumBuilder`.

---

## 12) SQL Builders (Safe & Quoted)

```csharp
static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";

static string BuildSelectSql(string table, IReadOnlyList<Col> cols, OrderInfo order, bool deterministic)
{
    var sb = new StringBuilder("SELECT ");
    for (int i = 0; i < cols.Count; i++)
    {
        if (i > 0) sb.Append(',');
        sb.Append(Q(cols[i].Name));
    }
    sb.Append(" FROM ").Append(Q(table));

    if (deterministic && order.Mode != OrderMode.None)
    {
        sb.Append(" ORDER BY ");
        for (int i = 0; i < order.Columns.Count; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Q(order.Columns[i])).Append(" ASC");
        }
    }
    return sb.ToString();
}
```

---

## 13) Deterministic Ordering Strategy

1. `PRAGMA table_info(table)` → columns with `pk > 0`, order by `pk` ascending.
2. If none: try `SELECT 1 FROM sqlite_master WHERE name = '{table}' AND sql LIKE '%WITHOUT ROWID%';`
   - If **without rowid** → `OrderMode.None` (documented).
   - Else → assume `rowid` exists and order by `rowid`.

This keeps row order stable across exports of unchanged DBs.

---

## 14) Testing & Verification

- **Unit tests**
  - Sheet name sanitizer edge cases & collisions
  - Nulls, large text, unicode, emojis, RTL scripts
  - BLOB rendering modes
  - Deterministic ordering with/without PK
- **Property tests**
  - Round‑trip canonical checksum is identical across repeated exports
- **Integration**
  - Golden DBs → compare metadata sheet (row counts + checksums)
- **Scale tests**
  - 1M‑row table with small columns; measured memory and time

---

## 15) Operational Guidance

- Prefer placing the DB on local SSD for speed.
- For very large exports, run x64, increase process memory limit.
- If performance matters more than simplicity, switch to **Streaming** build (OpenXML). The public API stays the same.

---

## 16) Usage Example

```csharp
SqliteToExcel.Export(
    sqlitePath: @"C:\\data\\ledger.sqlite",
    xlsxPath:   @"C:\\exports\\ledger.xlsx",
    options: new SqliteToExcelOptions
    {
        WriteAllAsText = true,
        BlobMode = BlobRenderMode.Hex,
        IncludeMetadataSheet = true,
        ReadBatchSize = 50_000,
        OrderRowsDeterministically = true,
    });
```

---

## 17) Streaming Variant Notes (Optional)

- Use `DocumentFormat.OpenXml` and `OpenXmlWriter` to write rows as you read them.
- Keep styles minimal; avoid shared strings if memory becomes an issue; otherwise shared strings improve XLSX size.
- Maintain the same **canonical checksum** and metadata logic.

---

## 18) Edge Cases & Decisions

- **Oversize columns (>16,384)**: fail with guidance (Excel limit) — or emit CSV per table as a fallback (opt‑in).
- **Dates w/ leading zeros**: remain text to preserve exact characters.
- **Scientific notation**: remains text unless `PreserveNumericTypes=true`.
- **Boolean affinities**: no special treatment; stored form is exported.

---

## 19) Deliverables Checklist

-

---

## 20) License & Attribution

- This component is **Proprietary**. Unauthorized copying, modification, or distribution is prohibited without explicit written consent from the owner.

---

**You now have a deterministic, production‑ready plan to export SQLite → Excel with confidence.**

