# CLAUDE → DB2XL Enhancements — Detailed Requirements (Proprietary)

> **Purpose**: Give CLAUDE a precise, testable specification to implement the next wave of DB2XL features that power MAK3R AI’s MCP‑based, Sigma.js‑driven silo‑stitching workflow. Focus on deterministic, LLM‑ready, human‑readable outputs. **License: Proprietary.**

---

## 0) Non‑Negotiables
- **Deterministic** outputs (same inputs + same options ⇒ same checksums).
- **Fidelity‑first**: raw preserved; transforms are opt‑in and reversible.
- **No HTTP service**: expose capabilities via **MCP tools** and a **console**.
- **Security**: parameterized SQL only; table/column allowlists; redaction support.
- **Provenance**: emit manifests with schema, selection, annotations, PK strategy, checksums.

---

## 1) Project Layout (Targets)

```
DB2XL.sln
├─ DB2XL.Core                # discovery, canonical rendering, checksums, manifests
├─ DB2XL.Query               # selection grammar + SQL builder (joins, filters, paging)
├─ DB2XL.Transformers        # pure idempotent transformers (+ registry)
├─ DB2XL.Export.Excel        # ClosedXML default; OpenXML streaming optional
├─ DB2XL.Graph               # graph JSON exporter (Sigma.js‑ready) + stats
├─ DB2XL.Annotations         # JSON schema + merge + application
├─ DB2XL.Validate            # type/domain/FD/temporal validators + question prompt gen
├─ DB2XL.Delta               # watermarks + trigger‑log + checkpoints
├─ DB2XL.Console             # System.CommandLine CLI wrappers
└─ DB2XL.Service.MCP         # MCP tools: introspect/graph/validate/preview/export/delta
```

---

## 2) MCP Tool Surface (the only service interface)

All tools must be **pure, parameterized, deterministic**, and return paths + checksums where artifacts are produced.

### 2.1 Tool: `db2xl.introspect`
**Input**
```json
{"sources":[{"type":"sqlite","path":"/data/app.db"},{"type":"csv","path":"/data/logs.csv","delimiter":","}],
 "maxSamples": 500}
```
**Output**
```json
{"tables":[{"name":"orders","rows":120345,"columns":[{"name":"id","affinity":"INTEGER","pk":1},{"name":"created_at","affinity":"TEXT","semantic":"datetime"}],
 "indexes":[{"name":"ix_orders_created","columns":["created_at"],"unique":false}]}],
 "pkStrategies":{"orders":"pk(ids)"},
 "stats":{"orders.created_at":{"nullPct":0.0,"distinctPct":0.92,"min":"2021-01-01","max":"2025-08-20"}},
 "manifestPath":"/runs/123/manifest.json","checksum":"sha256:..."}
```

### 2.2 Tool: `db2xl.graph`
**Input**: same `sources`, optional `annotations`, `limit` for sampling.  
**Output**: Sigma.js graph JSON + focus slices.
```json
{"graph":{"nodes":[{"id":"src:app.db","type":"source","label":"app.db"},{"id":"tbl:orders","type":"table","label":"orders","rowCount":120345},{"id":"col:orders.customer_id","type":"column","nullPct":0.0,"distinctPct":0.87}],
 "edges":[{"id":"fk:orders->customers","source":"col:orders.customer_id","target":"col:customers.id","type":"fk","score":0.98},{"id":"cand:logs.requestId~orders.req_id","source":"col:logs.requestId","target":"col:orders.req_id","type":"candidate-join","score":0.72}]},
 "slices":[{"node":"tbl:orders","rowsPath":"/runs/123/slices/orders.jsonl","rowCount":1000}],
 "checksum":"sha256:..."}
```

### 2.3 Tool: `db2xl.validate`
Find type drift, enum inconsistencies, key violations, temporal errors.  
**Output**
```json
{"issues":[{"type":"UNIQUE_VIOLATION","table":"customers","key":["email"],"count":42},
{"type":"ENUM_DRIFT","table":"orders","column":"status","badValues":["delivrd","shippped"]}],
 "questions":[{"id":"q1","ask":"Should I map status 'delivrd' → 'delivered'?"}],
 "reportPath":"/runs/123/validate.md"}
```

### 2.4 Tool: `db2xl.preview`
Return streaming JSONL (bounded by `limit`) after applying **selection + annotations + transforms**.

### 2.5 Tool: `db2xl.export`
Produce XLSX (raw + transformed) and/or JSONL bundle plus manifests and PII report.

### 2.6 Tool: `db2xl.delta`
Run selection in **delta** mode (watermark/trigger). Returns only new/changed rows and updates `delta.json`.

---

## 3) Selection Grammar v2 (joins + filters + paging)

**JSON schema** (excerpt):
```json
{
  "table": "orders",
  "attach": [{"alias":"logs","type":"sqlite","path":"/data/logs.db"}],
  "joins": [
    {"type":"inner","left":{"table":"orders","col":"req_id"},
     "right":{"table":"logs.events","col":"requestId"}}
  ],
  "select": ["orders.id","orders.created_at as created_iso","logs.events.level","json_extract(orders.payload,'$.user.id') as user_id"],
  "where": {"and":[{"col":"orders.created_at","op":">=","val":"2025-08-01"},{"col":"logs.events.level","op":"in","val":["WARN","ERROR"]}]},
  "orderBy": [{"col":"orders.id","dir":"asc"}],
  "limit": 50000,
  "offset": 0
}
```
**Rules**
- **Equality joins only** (`=`) for v2; inner/left supported.
- All values are **parameters**; all identifiers are **quoted**.
- Deterministic pagination: require ordering by **PK** (or synthetic) + tiebreaker.

**C# builder sketch**
```csharp
public sealed class SqlBuilder
{
    public (string sql, List<SqliteParameter> args) Build(Selection sel)
    {
        var sb = new StringBuilder();
        sb.Append("SELECT ").Append(string.Join(",", sel.SelectEscaped())).Append(" FROM ").Append(Q(sel.Table));
        foreach (var j in sel.Joins) sb.Append(j.ToSql());
        var (whereSql, ps) = WhereBuilder.Build(sel.Where);
        if (whereSql.Length > 0) sb.Append(" WHERE ").Append(whereSql);
        sb.Append(sel.OrderBySql());
        if (sel.Limit.HasValue) sb.Append(" LIMIT ").Append(sel.Limit.Value);
        if (sel.Offset.HasValue) sb.Append(" OFFSET ").Append(sel.Offset.Value);
        return (sb.ToString(), ps);
    }
}
```

---

## 4) Annotations (spec + application)

**Goals**: capture user knowledge (links, renames, semantic types, join rules, constraints) and apply it deterministically during preview/export.

**Schema excerpt**
```json
{
  "version": 1,
  "columnTags": {"orders.created_at": ["datetime:iso8601"], "logs.events.payload": ["json"]},
  "renames": {"orders.created_at": "created_iso"},
  "joins": [{"left":"orders.req_id","right":"logs.events.requestId","type":"inner"}],
  "entityLinks": [{"table":"customers","left":"email","right":"alt_email","kind":"same-as"}],
  "constraints": [{"type":"fd","determinant":["orders.id"],"dependent":["orders.customer_id"]},
                   {"type":"temporal","expr":"orders.order_date <= orders.ship_date"}],
  "redactions": [{"col":"customers.email","mode":"mask"}]
}
```

**Application order**: **renames → joins → tags → constraints → redactions**.  
**Provenance**: record the annotations hash in the export manifest.

---

## 5) Graph Exporter (Sigma.js)

**Requirements**
- Emit **graph JSON** with nodes for `source|table|column|entity-sample` and edges for `fk|candidate-join|user-link|co-occurs`.
- Include **metrics**: rowCount, null%, distinct%, top‑k values; edge joinability scores.
- Provide **focus slices** (small JSONL extracts) by node/edge id; deterministic sampling.

**Node example**
```json
{"id":"col:orders.status","type":"column","label":"status","nullPct":0.0,"distinctPct":0.08,"topK":[["delivered",7600],["shipped",1400]]}
```

---

## 6) Transformers (LLM‑ready, reversible)

**Interfaces**
```csharp
public interface ICellTransformer { bool CanApply(CellContext c); string Transform(CellContext c, string? raw); }
public interface IRowTransformer  { bool CanApply(RowContext c); IReadOnlyDictionary<string,string?> Transform(RowContext c, IReadOnlyDictionary<string,string?> row); }
```

**Built‑ins (v1 GA)**
- **Time**: `epoch(s|ms|us|ns)→ISO`, `.NET ticks→ISO`, `sqlite-julianday→ISO`, `tz-shift`.
- **JSON**: `try-parse`, `compact`, `flatten(path,maxDepth)`, `extract(path)`.
- **Semantic**: `enum-map`, `bool-format`, `number-format`, `redact`, `coalesce`, `conditional`.

**Policy**: default **paired columns**: `col` (raw), `col_t` (transformed). Dual sheets: `Table` and `Table~T`.

---

## 7) Delta Engine

**Modes**
1) **Watermark column** (`updated_at` or monotonic id): `(ts > last) OR (ts = last AND pk > last_pk)`.
2) **Trigger change log**: `__changes(table_name, op, pk, ts, txid)` installed per table; exporter reads and fetches rows.

**Checkpoint file** `delta.json` (per selection)
```json
{"selectionHash":"abc","annotationHash":"def","tables":[{"name":"orders","mode":"watermark","col":"updated_at","last":"2025-08-18T23:59:59Z","lastPk":"89234"}]}
```

---

## 8) Excel Writer

- Default **ClosedXML**; optional **OpenXML** streaming for large outputs.
- Hard limits: split sheets at 1,048,576 rows; deterministic chunking `Table_p1`, `Table_p2`.
- **All‑as‑text** by default; numeric types opt‑in. Never auto‑convert dates.
- Metadata sheet records raw & transformed checksums + PK strategy + selection/annotation hashes.

---

## 9) Console (System.CommandLine)

**Examples**
```bash
# Preview WARN/ERROR logs joined to orders, post‑transform, first 5k rows
sqlite2jsonl --db app.db --attach logs=logs.db \
  --table orders \
  --join "inner:orders.req_id=logs.events.requestId" \
  --select "orders.id,orders.created_at as created_iso,logs.events.level" \
  --where "orders.created_at>='2025-08-01' AND logs.events.level IN ('WARN','ERROR')" \
  --transform config.json --limit 5000 --out preview.jsonl

# Full export with manifests and PII report
sqlite2xlsx --project project.yaml --out bundle/ --manifest --jsonl
```

---

## 10) Provenance & Bundles

Produce alongside XLSX/JSONL:
- `schema.json` (columns, affinities, pk strategy, applied transformers)
- `provenance.json` (source hashes, selection, annotations, tool version, checksums)
- `pii_report.csv` (columns redacted + mode)
- `index.json` (artifact list with sizes, checksums)

---

## 11) Validation & Questions

**Validator outputs**
```json
{"issues":[{"type":"TYPE_DRIFT","table":"orders","column":"vix","details":"found TEXT in 3.1% rows"}],
 "questions":[{"id":"q9","ask":"Treat column 'vix' as number and parse using InvariantCulture?"}]}
```

**CLAUDE behavior**: when issues exist, generate **clear, atomic** Qs referencing columns/tables/values; never guess silently.

---

## 12) Security & Privacy
- Parameterized SQL; quoted identifiers; disallow `;` in identifiers.
- Allow/deny lists per source; max rows/bytes/time caps per tool call.
- Redaction transformers; PII report; controlled logs (no raw PII in logs).

---

## 13) Performance Targets
- 10M‑row single table JSONL preview at **≥ 80k rows/sec** on commodity hardware (streaming).
- XLSX streaming path handles **≥ 1M rows/sheet** within memory budget (< 1.5GB).
- Graph export for 500 tables, 5k columns within **< 10s** with stats sampling ≤ 1k rows/table.

---

## 14) Testing (must pass)
- **Determinism**: repeat runs produce identical checksums for raw & transformed.
- **Grammar**: joins, filters, paging; SQL injection attempts fail safe.
- **Transforms**: epoch/ticks/json flatten; idempotent; error rows logged.
- **Delta**: watermark + trigger; idempotent re‑runs; correct `delta.json` updates.
- **Graph**: node/edge counts stable; stats correct on samples.
- **PII**: redactions applied; PII report matches.

---

## 15) Definition of Done (per feature)
- Code + unit/property/integration tests
- Benchmarks meet targets
- CLI and MCP tools wired and documented
- Manifests & checksums verified on golden DBs
- Examples updated in `/examples` (logs, trades, CRM)

---

## 16) Worked Examples

### 16.1 Logs Triage & Stitch
**Selection**
```json
{"table":"orders","joins":[{"type":"inner","left":{"table":"orders","col":"req_id"},"right":{"table":"logs.events","col":"requestId"}}],
 "select":["orders.id","orders.created_at as created_iso","logs.events.level","logs.events.message"],
 "where":{"and":[{"col":"orders.created_at","op":">=","val":"2025-08-01"},{"col":"logs.events.level","op":"in","val":["WARN","ERROR"]}]},
 "orderBy":[{"col":"orders.id","dir":"asc"}],"limit":100000}
```
**Transformers**
```json
{"tables":{"orders":{"columns":{"created_at":[{"type":"epoch","unit":"ms","tz":"UTC"}],
 "payload":[{"type":"json-try-parse"},{"type":"json-flatten","path":"$.user","maxDepth":1}]}}}}
```

### 16.2 Trades Anomaly
```json
{"table":"trades","select":["id","symbol","entry_ts","brokerage","slippage_bp","vix"],
 "where":{"or":[{"col":"brokerage","op":">","val":25},{"col":"slippage_bp","op":">","val":40},{"and":[{"col":"vix","op":"<","val":12},{"col":"strategy","op":"=","val":"vol-harvest"}]}]},
 "orderBy":[{"col":"id","dir":"asc"}]}
```

---

## 17) Implementation Sequence (CLAUDE)
1) **DB2XL.Query v2** (joins + filters + paging) + tests
2) **Annotations** module + application order + hashing
3) **Graph exporter** + stats + slices
4) **Validate** module + question generator
5) **Delta** engine (watermark) + checkpoints; then trigger mode
6) **MCP tools** wiring: introspect/graph/validate/preview/export/delta
7) **Streaming Excel** path + large‑table split; finalize manifests
8) **PII governance** + redactions + report
9) **Benchmarks** and optimization passes

---

## 18) Deliverables Checklist
- [ ] Query v2 w/ tests
- [ ] Annotations schema + merger + hash
- [ ] Graph exporter + slices
- [ ] Validate + Q‑gen
- [ ] Delta (watermark + trigger) + `delta.json`
- [ ] MCP tools (6) implemented
- [ ] ClosedXML + OpenXML writers finalized
- [ ] Transformers GA + docs
- [ ] CLI updated + examples
- [ ] Examples: logs/trades/CRM + manifests

---

**End of spec.** This is the contract for CLAUDE to implement DB2XL enhancements end‑to‑end for MAK3R AI’s MCP‑driven, graph‑assisted insight workflow.

