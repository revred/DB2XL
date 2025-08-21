# DB2XL — Bundled Export Scaffold (CLI + MCP + Partitions + Index Workbook)

**License:** Proprietary.  
**Goal:** Implement big‑data friendly exports that pair a lightweight **Index Workbook (Excel)** with scalable **JSONL/Parquet** partitions, deterministic manifests, and MCP tool entry points.

---

## 1) Output Contract (Deterministic Layout)

```
/export_run_YYYY-MM-DDTHH-mm-ssZ/
  index.xlsx
  manifest/
    schema.json
    provenance.json
    partitions.json
    pii_report.csv
    delta.json                # optional (when delta mode used)
  tables/
    <table-name>/
      <table>_<partition-label>.jsonl
      <table>_<partition-label>.parquet
      sample_<table>_head_10k.jsonl
```

- **Relative paths only** inside manifests & Excel hyperlinks.
- Every artifact must have a **sha256** and **row count** recorded.

---

## 2) CLI Additions (System.CommandLine)

```
sqlite2bundle --db data.db \
  --xlsx index.xlsx \
  --jsonl-dir tables/ --parquet-dir tables/ \
  --partition "orders:by=quarter,field=created_at" \
  --partition "logs:by=day,field=ts,filter=level in ('WARN','ERROR')" \
  --sample "orders:head=10000 -> tables/orders/sample_orders_head_10k.jsonl" \
  --manifest --pii redactions.yaml --delta watermark=updated_at
```

**Notes**
- `--partition` may repeat per table.
- `--delta watermark=<column>` or `--delta trigger=__changes`.

---

## 3) MCP Tools (no HTTP service)

### 3.1 `db2xl.export`
**Input**
```json
{
  "sources": [{"type":"sqlite","path":"/data/app.db"}],
  "selections": [{"table":"orders"},{"table":"logs"}],
  "partitions": [
    {"table":"orders","by":"quarter","field":"created_at"},
    {"table":"logs","by":"day","field":"ts","filter":"level in ('WARN','ERROR')"}
  ],
  "out": ["xlsx","jsonl","parquet"],
  "samples": [{"table":"orders","head":10000}],
  "manifests": true,
  "delta": {"mode":"watermark","column":"updated_at"}
}
```
**Output**
```json
{
  "root":"/export_run_2025-08-21T08-45-00Z/",
  "artifacts":["index.xlsx","manifest/schema.json","tables/orders/orders_2025Q1.parquet"],
  "checksums":{"index.xlsx":"sha256:..."}
}
```

### 3.2 `db2xl.preview`
- Streams JSONL for a selection; accepts `limit`, reuses transforms/annotations.

### 3.3 `db2xl.delta`
- Runs incremental export using `delta.json` checkpoints; returns only new partitions.

---

## 4) Core Data Structures (C#)

```csharp
public record ExportPlan(
    string RootDir,
    List<TablePlan> Tables,
    ManifestBundle Manifests);

public record TablePlan(
    string Name,
    List<PartitionSpec> Partitions,
    SampleSpec? Sample);

public enum PartitionBy { None, Day, Week, Month, Quarter, Year, RowCount, Filter }

public record PartitionSpec(
    PartitionBy By,
    string Field,
    string? Filter,
    int? RowsPerFile);

public record Partition(
    string Table,
    string Label,
    string RelativePath,
    string PredicateSql,
    List<SqliteParameter> Args,
    long Rows,
    string Sha256);

public record ManifestBundle(
    SchemaManifest Schema,
    ProvenanceManifest Provenance,
    PartitionsManifest Partitions,
    PiiReport Pii,
    DeltaCheckpoint? Delta);
```

---

## 5) Partition Planner

```csharp
public static class PartitionPlanner
{
    public static IEnumerable<Partition> PlanTime(string table, string field, PartitionBy by,
        DateTime min, DateTime max)
    {
        for (var (cur, next) = (Align(min, by), Next(min, by)); cur <= max; (cur, next) = (next, Next(next, by)))
        {
            yield return new Partition(
                Table: table,
                Label: Label(cur, by),
                RelativePath: $"tables/{table}/{table}_{Label(cur, by)}.jsonl", // or .parquet
                PredicateSql: $"{Q(field)} >= @p0 AND {Q(field)} < @p1",
                Args: new() { new("@p0", cur), new("@p1", next) },
                Rows: 0,
                Sha256: string.Empty
            );
        }
    }

    public static IEnumerable<Partition> PlanRowCount(string table, int rowsPerFile)
    {
        int p = 0; long offset = 0;
        while (true)
        {
            var label = $"p{p:00000}";
            yield return new Partition(
                Table: table,
                Label: label,
                RelativePath: $"tables/{table}/{table}_{label}.jsonl",
                PredicateSql: "1=1 LIMIT @limit OFFSET @offset",
                Args: new() { new("@limit", rowsPerFile), new("@offset", (int)offset) },
                Rows: 0,
                Sha256: string.Empty
            );
            p++; offset += rowsPerFile;
        }
    }

    static string Q(string id) => "\"" + id.Replace("\"", "\"\"") + "\"";
    // Align/Next/Label helpers omitted for brevity.
}
```

---

## 6) JSONL Streaming Writer

```csharp
public sealed class JsonlWriter : IAsyncDisposable
{
    private readonly StreamWriter _sw;
    public long RowsWritten { get; private set; }

    public JsonlWriter(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        _sw = new StreamWriter(File.Open(path, FileMode.Create, FileAccess.Write, FileShare.Read),
                               new UTF8Encoding(encoderShouldEmitUTF8Identifier:false));
    }

    public async Task WriteRowAsync(IReadOnlyDictionary<string, object?> row)
    {
        await _sw.WriteLineAsync(System.Text.Json.JsonSerializer.Serialize(row));
        RowsWritten++;
    }

    public async ValueTask DisposeAsync() => await _sw.DisposeAsync();
}
```

**Usage**
```csharp
await using var w = new JsonlWriter(part.RelativePath);
await foreach (var row in reader.ReadRowsAsync(selection, batchSize: 10_000))
{
    await w.WriteRowAsync(row);
    if (w.RowsWritten % 200_000 == 0) RollToNextFile();
}
```

---

## 7) Parquet Adapter (optional)

```csharp
public interface IParquetWriter : IAsyncDisposable
{
    Task WriteBatchAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows);
}

public sealed class ParquetWriterNet : IParquetWriter
{
    // using Parquet.Net
    public ParquetWriterNet(string path, Schema schema) { /* ... */ }
    public Task WriteBatchAsync(IEnumerable<IReadOnlyDictionary<string, object?>> rows) { /* ... */ return Task.CompletedTask; }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

> Keep schemas explicit; cast date‑times to UTC ISO strings or Parquet timestamp types consistently.

---

## 8) Index Workbook Generator (ClosedXML)

```csharp
public static class IndexWorkbook
{
    public static void Generate(string xlsxPath, PartitionsManifest manifest)
    {
        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.Worksheets.Add("Datasets");
        ws.Cell(1,1).Value = "Table";
        ws.Cell(1,2).Value = "Partition";
        ws.Cell(1,3).Value = "Rows";
        ws.Cell(1,4).Value = "File";
        ws.Cell(1,5).Value = "SHA-256";
        int r = 2;
        foreach (var p in manifest.AllPartitions())
        {
            ws.Cell(r,1).Value = p.Table;
            ws.Cell(r,2).Value = p.Label;
            ws.Cell(r,3).Value = p.Rows;
            ws.Cell(r,4).Hyperlink = new XLHyperlink(p.RelativePath);
            ws.Cell(r,4).Value = p.RelativePath;
            ws.Cell(r,5).Value = p.Sha256;
            r++;
        }
        wb.SaveAs(xlsxPath);
    }
}
```

Add separate sheets for **Overview**, **Samples**, and **Provenance** as needed.

---

## 9) Manifests & Checksums

```csharp
public static class Sha256Util
{
    public static string OfFile(string path)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = sha.ComputeHash(fs);
        return "sha256:" + BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
    }
}

public record PartitionsManifest(Dictionary<string, List<Partition>> Tables)
{
    public IEnumerable<Partition> AllPartitions() => Tables.Values.SelectMany(x => x);
}
```

**`provenance.json`**
```json
{
  "exportUtc":"2025-08-21T08:45:00Z",
  "toolVersion":"DB2XL 1.0.0",
  "sources":[{"type":"sqlite","path":"/data/app.db","sha256":"..."}],
  "selectionHash":"...",
  "annotationHash":"..."
}
```

---

## 10) Delta Checkpointing

```csharp
public record DeltaCheckpoint(string SelectionHash, string AnnotationHash, List<TableDelta> Tables);
public record TableDelta(string Name, string Mode, string Column, string LastValue, string? LastPk);
```

- Update `delta.json` after each successful partition write.
- In watermark mode use clause `(ts > @last) OR (ts = @last AND pk > @last_pk)`.

---

## 11) PII Governance

- Accept `--pii redactions.yaml` mapping columns → `mask|hash|drop`.
- Emit `pii_report.csv` detailing applied redactions.

---

## 12) Definition of Done

- Deterministic directory structure and **relative** hyperlinks in `index.xlsx`.
- JSONL/Parquet partitions with checksums recorded in `partitions.json`.
- `schema.json`, `provenance.json`, (optional) `delta.json`, `pii_report.csv` produced.
- **Streaming** writers; Excel auto‑split at 1,048,576 rows.
- Samples written per table; Sigma.js can open slices instantly.
- Re‑runs with unchanged data produce **identical** manifests and hashes.

---

## 13) Worked Example (Logs + Orders)

**CLI**
```
sqlite2bundle --db app.db \
  --xlsx index.xlsx \
  --jsonl-dir tables/ --parquet-dir tables/ \
  --partition "orders:by=quarter,field=created_at" \
  --partition "logs:by=day,field=ts,filter=level in ('WARN','ERROR')" \
  --sample "orders:head=10000 -> tables/orders/sample_orders_head_10k.jsonl" \
  --manifest --delta watermark=updated_at
```

**Artifacts**
- `tables/orders/orders_2025Q1.parquet` (184,231 rows)
- `tables/logs/logs_2025-08-20_WARN.jsonl` (50,231 rows)
- `manifest/partitions.json` with checksums
- `index.xlsx` linking to each file

---

## 14) Integration Notes

- Keep Excel minimal (index + summaries); never load GBs into a single sheet.
- Prefer **JSONL for LLMs**, **Parquet for analytics**; ship both when flags request.
- Always record **PK strategy** and **ordering rule** in metadata to ensure reproducibility.

