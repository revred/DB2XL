# DB2XL — Bundled Export Specification (Index Workbook + Partitions + Manifests)

**License:** Proprietary  
**Scope:** This spec defines the bundled export behavior for DB2XL: a lightweight **Index Workbook (Excel)** for humans + scalable **partitioned artifacts** (JSONL, optional Parquet) for AI/analytics, with deterministic manifests, deltas, and PII governance. It consolidates and supersedes earlier drafts and aligns with the direction in recent refactors (class deduplication, improved test coverage, removal of duplicate helpers).

> **Note:** Where implementation differs, treat this document as the **source of truth** for behavior and test assertions.

---

## 1) Design Goals

- **Deterministic**: identical inputs + options ⇒ identical file names, contents, checksums.
- **Human‑friendly**: Excel serves as an entry point (index, samples, summaries), not a data lake.
- **Scalable**: large data lives in partitioned JSONL/Parquet with checksums.
- **Provenance**: every bundle ships schema, selection, annotations, PK strategy, and artifact checksums.
- **Safety**: parameterized SQL, allow/deny lists, redactions, strict path handling.

---

## 2) Output Layout (Deterministic, Portable)

```
/export_run_YYYY-MM-DDTHH-mm-ssZ/
  index.xlsx                        # Human entry point
  manifest/
    schema.json                     # Tables, columns, affinities, PK strategy, transforms
    provenance.json                 # Source hashes, selection & annotation hashes, tool version, timestamps
    partitions.json                 # Every artifact path + rows + checksums
    pii_report.csv                  # Optional, when redactions enabled
    delta.json                      # Optional, delta checkpoints
  tables/
    <table-name>/
      <table>_<partition-label>.jsonl
      <table>_<partition-label>.parquet   # optional
      sample_<table>_head_10k.jsonl       # optional sample
```

- All **paths inside Excel and manifests are relative** to the bundle root.
- All artifacts include **sha256** and **rowCount** recorded in `partitions.json`.

---

## 3) Partitioning Rules

### 3.1 Strategies
- **Time‑based**: `by=day|week|month|quarter|year` on a datetime column.
- **Rowcount**: fixed chunk size (e.g., 200k rows per file) for arbitrary tables.
- **Filter‑based**: split by predicate label (e.g., `level in ('WARN','ERROR')`).

### 3.2 Labels & Filenames
- Time partitions: `orders_2025Q1.jsonl`, `logs_2025-08-20_WARN.jsonl`.
- Rowcount partitions: `trades_p00001.jsonl`, `trades_p00002.jsonl`.
- Labels are **ASCII**, deterministic, and unique per table.

### 3.3 Determinism & Ordering
- Every select/export is ordered by **PK** if present; else **rowid**; else **synthetic** `_pk` (sha256 over ordered columns); tiebreakers apply.
- Pagination/splitting must NOT reorder rows across runs.

---

## 4) Index Workbook (Excel)

### 4.1 Sheets
- **Overview**: export timestamp, tool version, source file hashes, counts by table.
- **Datasets**: one row per partition with columns: `Table`, `Partition`, `Rows`, `File (hyperlink)`, `SHA256`.
- **Samples**: optional embedded samples (first 100 rows per table/partition) or links to `sample_*.jsonl`.
- **Provenance**: mirrors `schema.json`, `provenance.json`, and `partitions.json` key fields.

### 4.2 Conventions
- Use **relative hyperlinks** (portable bundles).
- Keep **styles minimal**; no volatile formulas; optional sparklines for partition sizes.
- Sheet auto‑split enforced at Excel limits; Index workbook itself should stay < 20 MB.

---

## 5) Companion Formats

### 5.1 JSONL (LLM‑ready)
- UTF‑8, one JSON object per line, **lowercase snake_case** keys, ISO‑8601 UTC datetimes.
- Include a stable row identifier `_pk` (true PK, rowid, or synthetic) for traceability.
- Chunk by partition; no global buffering; write streaming.

### 5.2 Parquet (analytics, optional)
- Writer may be pluggable; schemas explicit; timestamps are UTC.
- Compression: Snappy/Zstd; dictionary encoding on low cardinality columns.

---

## 6) Manifests

### 6.1 `schema.json`
- Tables with: name, columns (name, affinity, nullable, default, semantic tags), PK strategy, indexes present, transformer pipelines applied.

### 6.2 `provenance.json`
- `exportUtc`, `toolVersion`, `sources` (path + hash), `selectionHash`, `annotationHash`, `optionsHash`.

### 6.3 `partitions.json`
```json
{
  "orders": {
    "strategy": "by=quarter,field=created_at",
    "parts": [
      {"path":"tables/orders/orders_2025Q1.parquet","rows":184231,"sha256":"...","firstPk":"1","lastPk":"184231"}
    ]
  }
}
```
- Each part records optional `firstPk/lastPk` for audit and replay.

### 6.4 `delta.json` (optional)
- Per selection/annotation hash: last watermark per table and last PK for tie‑break.

### 6.5 `pii_report.csv` (optional)
- One row per redacted column: `Table,Column,Mode,Notes,RowsAffected`.

---

## 7) Delta Export (Incremental)

### Modes
- **Watermark column** (e.g., `updated_at`): SQL uses `(ts > @last) OR (ts = @last AND pk > @last_pk)`.
- **Trigger change log**: optional `__changes(table_name, op, pk, ts, txid)`; exporter reads pk list and fetches rows.

### Behavior
- New partitions created only for fresh ranges (e.g., `orders_2025Q3.*`).
- `delta.json` updated after successful write; `partitions.json` appended.

---

## 8) PII Governance

- Accept a redaction map (YAML/JSON): column → `mask|hash|drop|keep`.
- Apply **before** writing artifacts; mirrored in `pii_report.csv`.
- Ensure logs do not emit raw PII.

---

## 9) CLI Contract (System.CommandLine)

```bash
sqlite2bundle --db data.db \
  --xlsx index.xlsx \
  --jsonl-dir tables/ --parquet-dir tables/ \
  --partition "orders:by=quarter,field=created_at" \
  --partition "logs:by=day,field=ts,filter=level in ('WARN','ERROR')" \
  --sample "orders:head=10000 -> tables/orders/sample_orders_head_10k.jsonl" \
  --manifest --delta watermark=updated_at --pii redactions.yaml
```

- Multiple `--partition` flags allowed; samples optional.
- All outputs use relative paths under bundle root.

---

## 10) MCP Tools (no HTTP service)

- `db2xl.export` — produces the full bundle; returns root path + artifact list + checksums.
- `db2xl.preview` — streams JSONL for a selection (limit); same transforms/annotations.
- `db2xl.delta` — runs incremental and amends manifests.

All tools are **pure**, parameterized, and capped by max rows/time to protect the host.

---

## 11) Determinism Details

- Identifier quoting; parameterized SQL throughout.
- Stable ordering: PK → rowid → synthetic `_pk` with byte‑for‑byte canonicalization.
- Canonical string rendering for all cells in Excel (no implicit date/number coercion).
- Hashes computed over **canonical bytes**; manifests include `optionsHash` to avoid accidental drift.

---

## 12) Test Matrix (must pass)

- **Determinism**: repeat runs equal checksums (raw & transformed) and identical `partitions.json` ordering.
- **Excel Limits**: autosplit at 1,048,576 rows; sheet names deterministic.
- **Partitioning**: time/rowcount/filter produce expected counts/labels; edge boundaries inclusive/exclusive validated.
- **Delta**: idempotent re‑runs; only new ranges emitted; `delta.json` correct.
- **PII**: redactions applied; `pii_report.csv` matches rows affected.
- **Manifest Integrity**: all files exist and sha256 matches.
- **Scale**: stream write ≥ 80k rows/sec JSONL on commodity hardware.

---

## 13) Implementation Notes (post‑refactor alignment)

- **Class deduplication**: centralize path building, hashing, and quoting in `DB2XL.Core` (no helpers duplicated in CLI or writers).
- **Writers**: JSONL and Parquet adapters share a common `IRowSink` interface.
- **Workbook**: ClosedXML default; OpenXML streaming for large index sheets (guard rails if needed).
- **Options**: merged `ExportOptions` replaces older per‑module option bags; hashable record for `optionsHash`.

---

## 14) Worked Example

**Inputs**
- `orders(created_at, id, customer_id, amount, updated_at)`
- `logs(ts, level, message)`

**CLI**
```
sqlite2bundle --db app.db \
  --xlsx index.xlsx \
  --jsonl-dir tables/ --parquet-dir tables/ \
  --partition "orders:by=quarter,field=created_at" \
  --partition "logs:by=day,field=ts,filter=level in ('WARN','ERROR')" \
  --manifest --delta watermark=updated_at
```

**Artifacts**
- `tables/orders/orders_2025Q1.parquet` (184,231 rows, sha256:…)
- `tables/logs/logs_2025-08-20_WARN.jsonl` (50,231 rows, sha256:…)
- `manifest/partitions.json` linking to all parts; `index.xlsx` hyperlinks to every artifact.

---

## 15) Definition of Done

- Bundle builds with deterministic layout and relative links.
- All manifests present; checksums verify.
- Excel index opens quickly; artifacts stream efficiently.
- Re‑runs stable; deltas incremental; PII governance optional but verifiable.

---

**End of specification.** This document is the contract for DB2XL bundled export behavior going forward.

