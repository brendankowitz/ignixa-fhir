# Ignixa.DataLayer.SqlServer.Database

SQL Database Project (SSDT) decomposing the FHIR resource/search schema into one file per
object, used by `SchemaDeployer` (see `Ignixa.DataLayer.SqlServer`) both to provision
brand-new tenant databases and to upgrade existing tenants that are behind the current
schema version.

## Provenance

The bulk of this schema (`Tables`, `Views`, `StoredProcedures`, `Types`, and the partition
functions/schemes/sequence under `Storage`) is vendored from
[`microsoft/fhir-server`](https://github.com/microsoft/fhir-server), specifically the per-object
files under
[`src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql`](https://github.com/microsoft/fhir-server/tree/main/src/Microsoft.Health.Fhir.SqlServer/Features/Schema/Sql)
at commit [`ddc3cb1f`](https://github.com/microsoft/fhir-server/commit/ddc3cb1f5d817c66c182821254aa631b6e23eb0e) —
the commit that produced `Migrations/97.sql`, which is byte-identical to this repo's
`Ignixa.DataLayer.SqlEntityFramework/Resources/97.sql`.

Content differs from the upstream files only in SSDT/DacFx-conventional formatting (no `GO`
batch separators, `CREATE PROCEDURE` instead of `CREATE OR ALTER PROCEDURE`, `OUTPUT` instead of
`OUT`, explicit `INNER JOIN`, column-per-line layout, partition/view/seed-data statements split
out of their parent `CREATE TABLE` file into their own files) — verified file-by-file, not just
assumed.

**Two intentional deviations from upstream, not decomposition artifacts — do not overwrite:**

- `Tables/TokenSearchParam.sql` — adds `IdentifierTypeCode`/`IdentifierTypeSystemId` extension
  columns and supporting indexes for the FHIR `:of-type` search modifier.
- `Tables/UriSearchParam.sql` — adds `Fragment`/`Version` columns.

There is no ongoing generation step: this schema was vendored once and is maintained by hand
going forward, the same way EF Core migrations are.

## Ignixa-owned `$lastn` materialization

The `LastNCodeIdentity`, `LastNObservationCodeMembership`, `LastNCodeEdge`,
`LastNObservationCodeGroup`, `LastNCodeGroupGeneration`, and
`LastNCodeGroupDirtyObservation` tables, `LastNResourceScopeList` TVP, and
`*AndMaintainLastNGroups` and generation procedures are Ignixa-owned additions.
They are not vendored from `microsoft/fhir-server` and must not be overwritten by
an upstream schema refresh.

These objects materialize exact, current Observation code equivalence per
`(ResourceTypeId, SearchParamId)` scope. The wrapper procedures keep the
vendored base procedure bodies and existing TVPs unchanged, acquire
transaction-owned `LastNCodeGroup:{ResourceTypeId}:{SearchParamId}` locks in
lexicographic scope order, and maintain graph rows in the same transaction as
the base resource write. `LastNCodeGroupGeneration` admits reads only when its
state is `Ready`; `Pending`, `Building`, and `Failed` remain explicit unavailable
states until a resumable generation completes. A building row durably stores its
current attempt, bounded lease expiry, snapshot high water, and highest committed
backfill surrogate id. Start serializes live-owner rejection or expired-lease
takeover under the scope lock; every batch advances progress and renews the lease
in the same transaction as materialization. Batch, completion, and failure
procedures require the current attempt id.
