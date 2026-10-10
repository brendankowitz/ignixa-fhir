# SQL Server fixtures

## schema-v5-before-reindex-retirement.dacpac

The database project as it stood at schema version 5, immediately before version 6 retired the legacy
reindex objects. `SchemaDeployerUpgradeTests` deploys it, stamps version 5, and upgrades to the current
version. The test pins the file's SHA-256, so a replaced fixture is a deliberate, reviewed edit:

```text
SHA-256 9928BDAF7C868285A2759D4D2A13E4D2DF1DA853ECFB98C546FC658619978009
```

It differs from the current project only in what versions 6 to 9 changed:

| Version | Present in this fixture | Current project |
|---|---|---|
| 6 | `Tables/ReindexJob.sql`, `Types/BulkReindexResourceTableType_1.sql` and the procedures `AcquireReindexJobs`, `CheckActiveReindexJobs`, `CreateReindexJob`, `GetReindexJobById`, `UpdateReindexJob`; no retirement block in `Scripts/Script.PostDeployment.sql` | Those seven objects are deleted; the post-deployment script refuses a populated `dbo.ReindexJob` and drops them |
| 7 | No `IX_Transactions_SurrogateIdRangeLastValue` | `Tables/Transactions.sql` creates it |
| 8 | `UpdateResourceSearchParams` returns no result set | It ends with `SELECT ResourceSurrogateId FROM @Ids` |
| 9 | No `IX_Transactions_SurrogateIdRangeFirstValue_Incomplete` | `Tables/Transactions.sql` creates it |

### Regenerating

A rebuild never reproduces the same bytes (the package records its build origin), so regenerate only
when the fixture must change, for example for a new DacFx package format, and update the pin.

1. Copy the project next to itself, so it builds under the same `Directory.*.props`, and keep its `<DSP>`
   (`SqlAzureV12`): DacFx refuses to publish a package built for another platform.

   ```powershell
   Copy-Item src\DataLayer\Ignixa.DataLayer.SqlServer.Database src\DataLayer\Ignixa.DataLayer.SqlServer.Database.V5 -Recurse
   ```

2. In the copy, undo the versions 6 to 9 rows of the table above: delete the retirement block (everything
   before the "Hourly partition maintenance" comment) from `Scripts/Script.PostDeployment.sql`, delete the
   two reindex indexes from `Tables/Transactions.sql`, delete the final `SELECT` from
   `StoredProcedures/UpdateResourceSearchParams.sql`, and add the seven legacy objects back. Their exact
   definitions are recorded in this fixture: `model.xml` inside the package (a zip) holds each object's
   script, for example under `<Element Type="SqlProcedure" Name="[dbo].[AcquireReindexJobs]">`.

3. Build, replace the fixture, print the new hash, and delete the copy:

   ```powershell
   dotnet build src\DataLayer\Ignixa.DataLayer.SqlServer.Database.V5\Ignixa.DataLayer.SqlServer.Database.sqlproj -c Release
   Copy-Item src\DataLayer\Ignixa.DataLayer.SqlServer.Database.V5\bin\Release\Ignixa.DataLayer.SqlServer.Database.dacpac test\Ignixa.DataLayer.SqlServer.IntegrationTests\Fixtures\schema-v5-before-reindex-retirement.dacpac
   (Get-FileHash test\Ignixa.DataLayer.SqlServer.IntegrationTests\Fixtures\schema-v5-before-reindex-retirement.dacpac -Algorithm SHA256).Hash
   Remove-Item src\DataLayer\Ignixa.DataLayer.SqlServer.Database.V5 -Recurse
   ```

4. Put the printed hash in this file and in `SchemaDeployerUpgradeTests.SchemaVersionFiveFixtureSha256`.
