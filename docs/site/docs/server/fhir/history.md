---
sidebar_position: 6
title: History
description: Version history, accurate totals, time bounds, and paging.
---

# History

History includes stored current versions, superseded versions, and soft-delete tombstones:

```http
GET /tenant/1/Patient/123/_history?_total=accurate&_count=20
GET /tenant/1/Patient/_history?_since=2026-05-01T12:00:00.000Z
GET /tenant/1/_history?_since=2026-05-01T12:00:00.000Z&_until=2026-05-02T12:00:00.000Z
```

The same tenant-routing rules as other resource interactions apply. Unqualified routes are
available only when tenant resolution permits them.

## Reading deleted resources

A current-resource read or version read that selects a deletion tombstone returns **410 Gone**.
GET includes a FHIR `OperationOutcome` with issue code `deleted`, plus the deleted version's
ETag and Last-Modified headers. HEAD returns the same status and headers without a body.
Conditional headers do not convert a deleted-resource read into a successful or not-modified
response; deletion is checked before applying `If-None-Match` or `If-Modified-Since`.

## Totals and pages

- `_count` defaults to 20 and is capped at **1000 entries per page**.
- `_offset` selects the page offset. Follow the returned `next` link to continue.
- `_total=accurate` counts **all matching stored versions**, not just a page or its lookahead.
  SQL Server uses an aggregate without fetching resource bodies. The filesystem prototype scans
  metadata sidecars one at a time without loading NDJSON bodies.
- Counts include tombstones and apply the same time filters as the entries. They ignore page
  size, offset, and sort. Counts are not a resource-body integrity check.
- Count and page queries are separate reads, not a snapshot. Concurrent writes or hard deletion
  can change later pages and totals. Use a fixed `_until` for a bounded incremental traversal;
  it does not provide snapshot isolation.

Counting failures are errors, not partial totals. SQL history body corruption also fails the
affected page rather than silently omitting a version. If a streamed response has already
started, the history bundle carries a fatal error entry and no success pagination links.
A lookahead row proves that another page exists without decoding its body; corruption is
reported when that version is actually requested.

## SQL Server timestamp semantics

`_since` is inclusive (versions **at or after** the instant), and the `_until` extension is
inclusive (versions **at or before** the instant). SQL history uses the persisted timestamp
encoded in `ResourceSurrogateId`, the same timestamp returned as the entry's `lastModified`.
It does not require an associated transaction record: a standalone DELETE is included in
incremental instance, type, and system histories.

Persisted timestamps have millisecond precision. Sub-millisecond query instants are compared
without rounding: `_since` just after a stored millisecond excludes that millisecond, while
`_until` exactly at a stored millisecond includes every version in it, including surrogate-ID
uniquifiers. UTC offsets are normalized. Default ordering is newest first; `_sort=asc` gives
oldest first. Surrogate IDs and resource type IDs provide deterministic tie-breaking.
