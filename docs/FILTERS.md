# DB2XL — Force Multipliers (Column/Row Selectors, Graph‑Style API, Deltas, MCP)

> **Purpose:** Supercharge the SQLite → Excel/JSONL exporter with **surgical extraction**, **LLM‑ready packaging**, and **service endpoints** (CLI + HTTP + MCP) while preserving **determinism** and **proprietary licensing**.

**Scope**: This doc defines the architecture and implementation plan for:
- Column/row/table **selection** with advanced filters
- **Graph‑style discovery** & querying (“GraphAPI‑like”, not GraphQL‑only)
- **Primary Key (PK) discovery** and stable row identifiers
- **Delta exports** (watermark, triggers, changesets)
- **Log & trades** power workflows
- **Console** and **MCP service** surfaces
- **LLM packaging** (JSONL, manifests) and safety

> **Note:** Direct repo introspection wasn’t available in this session. The “Repo Health & Technical Debt” section reflects your notes (e.g., `Sqlite.Console` not separated from `SqliteExport`) plus common issues we’ll verify once code access is provided.

---

## 1) Architecture Overview

```
DB2XL.sln
├─ DB2XL.Core               // discovery, selection, filtering, transforms, checksums, delta engine
├─ DB2XL.Export.Excel       // ClosedXML default; OpenXML streaming optional
├─ DB2XL.Transformers       // pluggable pure transformers (time, json, enum, redact, etc.)
├─ DB2XL.Query              // selection grammar → parameterized SQL builder
├─ DB2XL.Console            // CLI: sqlite2xlsx/sqlite2jsonl (split from SqliteExport)
├─ DB2XL.Service.Http       // ASP.NET Core Minimal API (Graph‑style endpoints)
├─ DB2XL.Service.MCP        // MCP tools: db2xl.introspect/query/export
└─ DB2XL.Tests              // unit, property, integration, scale
```

**Design tenets**
- **Deterministic by default** (stable ORDER BY; canonical string rendering)  
- **Fidelity‑first** (raw preserved; transforms are opt‑in and reversible)  
- **Parametric security** (no raw string concatenation; parameterized SQL)  
- **Streaming** (rows → writer; constant memory options)  
- **Provenance** (metadata + checksums for raw & transformed)  

---

## 2) Feature Set

### 2.1 Selection (Columns/Rows/Tables)
- **Introspection**: enumerate tables, columns (affinity, nullability, default, PK order, FKs, indexes)
- **Projection**: `select: ["colA", "colB as label", "json_extract(payload,'$.user.id') as user_id"]`
- **Filtering**: `where`: safe expression language mapped to parameters  
  Examples: `timestamp >= '2025-08-01T00:00:00Z' AND level IN ('WARN','ERROR')`
- **Ordering/Limit/Offset**: stable and deterministic by PK/rowid fallback
- **Sampling**: `countOnly` fast path; `firstN` previews

### 2.2 Graph‑Style Discovery API
Expose a thin, discoverable surface similar to a Graph/REST hybrid:
- `GET /introspect` → db metadata snapshot
- `GET /tables` / `GET /tables/{table}` → columns, PKs, indexes, row estimates
- `POST /query` → selection grammar → streaming JSON or JSONL
- `POST /export` → selection + output format (`xlsx` | `jsonl`) + transform config id
- `GET /manifest` → schema + provenance (for LLMs)

### 2.3 Primary Key Discovery
- Prefer **PRAGMA table_info** `pk > 0` (ordered).  
- Else discover **unique index** with NOT NULL columns.  
- Else `rowid` unless `WITHOUT ROWID`.  
- Else synthesize `_pk` (stable hash of concatenated columns).  
- Emit PK strategy in metadata.

### 2.4 Delta Exports
Modes:
1) **Watermark column** (e.g., `updated_at`, `id`): `WHERE col > :last` (supports `>=` with PK disambiguation).  
2) **Change Log Table** via triggers: capture `INSERT/UPDATE/DELETE` into `__changes(table, op, pk, ts, txid)`.
3) **SQLite Session Changesets** (advanced/optional): if `sqlite3session` available.

**Checkpointing**: store `delta.json` with `{ table, mode, last_pk, last_ts, export_utc, checksum }`.  
**Rewind** support: explicit `--since` overrides checkpoint.

### 2.5 Transformers (Human‑Readable & LLM‑Ready)
- Column/Row transformers (epoch→ISO, ticks→ISO, json‑flatten, enum‑map, redact…)
- Dual‑sheet or dual‑workbook strategy: `Table` (raw) and `Table~T` (transformed)
- Per‑table **JSONL** export, schema/provenance manifests

---

## 3) Selection Grammar (Neutral & Safe)

```json
{
  "table": "logs",
  "select": [
    "ts_iso",
    "level",
    "message",
    "json_extract(payload,'$.requestId') as request_id"
  ],
  "where": {
    "and": [
      {"col": "ts", "op": ">=", "val": "2025-08-01T00:00:00Z"},
      {"col": "level", "op": "in", "val": ["WARN","ERROR"]}
    ]
  },
  "orderBy": [{"col":"ts","dir":"asc"},{"col":"rowid","dir":"asc"}],
  "limit": 10000,
  "offset": 0
}
```

**Operators**: `=`, `!=`, `<`, `<=`, `>`, `>=`, `in`, `not in`, `like`, `glob`, `between`, `is null`, `is not null`.  
**JSON**: if `json1` available, allow `json_extract` in `select` with a safelist.  
**Param binding**: all `val` become parameters; SQL is built via quoted identifiers + placeholders.

---

## 4) Console (DB2XL.Console)

```
sqlite2xlsx --db data.db --out out.xlsx \
  --table logs --select ts_iso,level,message \
  --where "level IN ('WARN','ERROR') AND ts >= '2025-08-01'" \
  --order ts:asc,rowid:asc --limit 100000 \
  --transform config.json --jsonl out_dir --manifest

sqlite2jsonl --db data.db --table trades \
  --where "brokerage > 25 OR slippage_bp > 40" \
  --delta since=2025-08-01T00:00:00Z by=updated_at \
  --select id,symbol,entry_ts,brokerage,slippage_bp,notes
```

**Notes**
- Use `System.CommandLine` for strong typing and help.  
- `--count` returns a fast count (uses estimated or exact based on size).  
- `--dry-run` prints the planned SQL and parameter list.  
- `--strict` fails on transformer error; default logs error, writes raw.

---

## 5) HTTP Service (DB2XL.Service.Http)

**Minimal API** example (ASP.NET Core):
```csharp
app.MapGet("/introspect", IntrospectHandler);
app.MapGet("/tables", ListTables);
app.MapGet("/tables/{table}", DescribeTable);
app.MapPost("/query", QueryHandler);       // streams ndjson (JSONL)
app.MapPost("/export", ExportHandler);     // creates xlsx/jsonl bundle
app.MapGet("/manifest", ManifestHandler);
```

**Streaming JSONL**: `Content-Type: application/x-ndjson` with one JSON per line for LLM‑friendly consumption.

**Fast count**: `HEAD /tables/{table}/query?...` with headers `X-Row-Count`.

**Auth**: bearer token or API key; CORS for local tools.

---

## 6) MCP Service (DB2XL.Service.MCP)

**Tools**
- `db2xl.introspect` `{ dbPath } → { tables[], columns[], pkStrategy }`
- `db2xl.query` `{ dbPath, selection, limit, cursor } → { rows[], cursor }` (cursor = PK watermark)
- `db2xl.export` `{ dbPath, selection, outFormat, transformConfig } → { artifactPaths[], manifest }`

**Safety**: timeouts, max rows per call, allowlist for tables, scrubbed errors.

---

## 7) PK Discovery & Stable IDs

```sql
PRAGMA table_info("{table}");           -- pk > 0 → ordered composite PK
SELECT name FROM sqlite_master
 WHERE type='index' AND tbl_name=@t AND sql LIKE '%UNIQUE%';
```
- If none, and not WITHOUT ROWID → include `rowid` in `ORDER BY` and expose as `_rid`.
- If WITHOUT ROWID + no unique index → synthesize `_pk = sha256(concat_ws('\x1F', all_columns))`.

Expose `pkStrategy` in metadata/manifest.

---

## 8) Delta Export Design

### 8.1 Watermark Mode
- Config: `{ table: "trades", mode: "watermark", column: "updated_at", format: "iso8601" }`
- SQL: `WHERE (updated_at > @last) OR (updated_at = @last AND pk > @last_pk)`

### 8.2 Trigger Change Log Mode
**Install once** per table (opt‑in):
```sql
CREATE TABLE IF NOT EXISTS __changes(
  table_name TEXT, op TEXT, pk TEXT, ts TEXT DEFAULT (datetime('now')), txid INTEGER);

CREATE TRIGGER IF NOT EXISTS trades_i AFTER INSERT ON trades BEGIN
  INSERT INTO __changes(table_name,op,pk) VALUES('trades','I', NEW.id);
END;
CREATE TRIGGER IF NOT EXISTS trades_u AFTER UPDATE ON trades BEGIN
  INSERT INTO __changes(table_name,op,pk) VALUES('trades','U', NEW.id);
END;
CREATE TRIGGER IF NOT EXISTS trades_d AFTER DELETE ON trades BEGIN
  INSERT INTO __changes(table_name,op,pk) VALUES('trades','D', OLD.id);
END;
```
Export engine reads `__changes WHERE ts > @last` and fetches rows by PK.

### 8.3 Changesets (Advanced)
If compiled with `sqlite3session`, support changeset blobs per table; apply/inspect externally. Optional.

**Manifests** store last checkpoint and data hash for replay safety.

---

## 9) Worked Scenarios

### 9.1 Logs Triage (Context‑Window Friendly)
- Table: `logs(ts INTEGER, level TEXT, service TEXT, payload JSON, message TEXT)`
- Selection: WARN/ERROR last 48h, flatten `payload.requestId` and `payload.user.id`, export **JSONL** chunks (e.g., 50k lines/file) + **Excel** transformed sheet.
- Result: Minimal, surgical dataset ready for Notebook LM with stable keys and ISO timestamps.

### 9.2 Trades Anomaly Hunt
- Table: `trades(id INT PK, symbol, entry_ts, brokerage, slippage_bp, vix, note)`
- Filters: `brokerage > expected_brokerage(symbol)` or `slippage_bp > 40` or `(vix < 12 AND strategy='vol-harvest')`
- Output: Excel transformed + JSONL; manifest pinning the selection + parameters for exact replay.

---

## 10) Performance & Determinism
- Always **parameterize**; pre‑compile commands.  
- Push predicates into SQL (avoid client‑side filtering).  
- Use **indexes**: surface missing‑index suggestions in `/explain` endpoint.  
- Deterministic `ORDER BY` (PK → rowid → synthetic); record in metadata.  
- Streaming writers (OpenXML or JSONL) to cap memory.

---

## 11) Security & Privacy
- Table/column allowlists; `--deny` for sensitive fields.  
- Redaction transformers (`email`, `phone`); emit `pii_report.csv`.  
- Rate‑limit service endpoints; timeouts; file‑system sandboxing.

---

## 12) LLM Packaging
- **JSONL per table** with transformed rows; UTF‑8, newline‑normalized.  
- `schema.json` (columns, types, pk, transformers).  
- `provenance.json` (db hash, export time, selection grammar, checksums).  
- Optional **embeddings‑ready** payload: parallel text fields with normalized strings.

---

## 13) Repo Health & Technical Debt (to verify post‑access)
- **Separation**: `Sqlite.Console` still intertwined with `SqliteExport` → split into `DB2XL.Console` thin layer.
- **Query builder** logic mixed with export code → extract to `DB2XL.Query` with tests.
- **Transformers** intertwined with IO → enforce pure interfaces & deterministic outputs; central registry.
- **Options sprawl** → single `DB2XL.Options` with defaults + layered config (CLI flags > file > env > code).
- **No streaming path** in Excel writer for huge tables → add OpenXML writer variant.
- **Insufficient tests**: add property tests for ordering, checksums, deltas, and JSON‑edge cases.
- **Logging**: unify via abstractions; optional Serilog sink; structured event IDs.
- **Versioning**: SemVer + changelog; include tool version and options in manifests.

---

## 14) Roadmap (90‑Day)

### Phase 1 — Foundations (Weeks 1–3)
- Extract **DB2XL.Console**; wire **DB2XL.Query** (selection grammar + SQL builder)
- Implement **/introspect**, **/tables**, **/query** (JSONL stream)
- PK discovery + deterministic ordering + metadata

### Phase 2 — Power Features (Weeks 4–7)
- Column/Row transformers GA (epoch, ticks, json‑flatten, enum‑map, redact)
- `/export` (xlsx/jsonl) with dual‑sheet strategy
- Delta: **watermark mode** + checkpoint manifests

### Phase 3 — Service & Deltas (Weeks 8–10)
- Trigger‑based **change log mode** + installer
- MCP tools (`introspect`, `query`, `export`)
- `/explain` endpoint for index suggestions

### Phase 4 — Hardening (Weeks 11–13)
- Scale + soak tests; OpenXML streaming option
- Security hardening; allow/deny lists; rate limits
- Docs + examples + sample configs for Logs/Trades

---

## 15) Acceptance Tests
- Same DB + same selection + same transforms → identical checksums for raw/transformed.
- Delta re‑runs fetch only new rows, never drop existing; idempotent manifests.
- Large JSON payloads flatten deterministically; invalid JSON handled gracefully.
- Logs/trades scenarios produce expected row counts and stable IDs.

---

## 16) Appendix A — Selection Grammar (EBNF)

```
selection    := '{' table ',' select (',' where)? (',' orderBy)? (',' limit)? (',' offset)? '}'
table        := '"table"' ':' string
select       := '"select"' ':' '[' (selcol (',' selcol)*)? ']'
selcol       := string | string ' as ' ident
where        := '"where"' ':' expr
expr         := comp | and | or | not | isnull | between | in
comp         := '{' '"col"' ':' ident ',' '"op"' ':' op ',' '"val"' ':' value '}'
and          := '{' '"and"' ':' '[' expr (',' expr)* ']' '}'
or           := '{' '"or"'  ':' '[' expr (',' expr)* ']' '}'
not          := '{' '"not"' ':' expr '}'
orderBy      := '"orderBy"' ':' '[' order (',' order)* ']'
order        := '{' '"col"' ':' ident ',' '"dir"' ':' ('"asc"'|'"desc"') '}'
limit        := '"limit"' ':' int
offset       := '"offset"' ':' int
```

---

## 17) Appendix B — MCP Tool Signatures

**db2xl.introspect**
```json
{
  "type": "object",
  "properties": {"dbPath": {"type": "string"}},
  "required": ["dbPath"]
}
```
**db2xl.query**
```json
{
  "type": "object",
  "properties": {
    "dbPath": {"type": "string"},
    "selection": {"type": "object"},
    "limit": {"type": "integer"},
    "cursor": {"type": "string"}
  },
  "required": ["dbPath", "selection"]
}
```
**db2xl.export**
```json
{
  "type": "object",
  "properties": {
    "dbPath": {"type": "string"},
    "selection": {"type": "object"},
    "outFormat": {"type": "string", "enum": ["xlsx","jsonl"]},
    "transformConfig": {"type": "string"}
  },
  "required": ["dbPath", "selection", "outFormat"]
}
```

---

## 18) Licensing
- **Proprietary**. Internal use and authorized distributions only. Include license header in all packages and generated manifests.

---

**Outcome:** With these force multipliers, DB2XL becomes a **Swiss‑knife** for AI‑assisted investigation and reporting — deterministically extracting only what matters, transforming it for humans and LLMs, and serving it via console, HTTP, or MCP without sacrificing data fidelity.

