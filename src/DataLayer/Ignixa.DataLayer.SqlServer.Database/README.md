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
