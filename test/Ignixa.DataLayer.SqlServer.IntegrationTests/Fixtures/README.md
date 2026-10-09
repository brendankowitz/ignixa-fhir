# SQL Server fixtures

`schema-v5-before-reindex-retirement.dacpac` is the schema-version 5 fixture
captured from `26beb334c4596bd3f1dd3c90113bae53af6d182f^`, immediately before
that commit retired the legacy reindex objects.

To regenerate it, build the database project from that revision and replace
this file with its Debug output:

```powershell
git worktree add --detach ..\ignixa-schema-v5 26beb334c4596bd3f1dd3c90113bae53af6d182f^
dotnet build ..\ignixa-schema-v5\src\DataLayer\Ignixa.DataLayer.SqlServer.Database\Ignixa.DataLayer.SqlServer.Database.sqlproj -c Debug
Copy-Item ..\ignixa-schema-v5\src\DataLayer\Ignixa.DataLayer.SqlServer.Database\bin\Debug\Ignixa.DataLayer.SqlServer.Database.dacpac .\test\Ignixa.DataLayer.SqlServer.IntegrationTests\Fixtures\schema-v5-before-reindex-retirement.dacpac
git worktree remove ..\ignixa-schema-v5
```
