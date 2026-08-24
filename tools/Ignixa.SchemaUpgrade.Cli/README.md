# Ignixa.SchemaUpgrade.Cli

Operator-triggered CLI for reviewing and applying a pending tenant database schema upgrade that
`SchemaDeployer`'s automatic path refused to apply (for example, because DacFx classified the diff
as possibly data-lossy, or the diff could not be classified at all).

## Installation

```bash
dotnet tool install --global Ignixa.SchemaUpgrade.Cli
```

## Usage

```bash
ignixa-schema-upgrade --tenant-id 1
```

The tool loads the tenant's connection string, generates a DacFx deploy report comparing the
tenant's database against the server's embedded schema dacpac, prints the diff, and classifies it
as auto-safe, unsafe, or unclassifiable. It then prompts for confirmation before applying the
upgrade (unless `--confirm` is passed) and stamps the tenant's schema version once the deploy
succeeds.

## Options

| Option | Description |
|--------|--------------|
| `--tenant-id` | The tenant ID to upgrade (required). |
| `--confirm` | Apply the upgrade without an interactive prompt (for scripted/CI use). |
| `--allow-data-loss` | Permit the deploy to proceed even when SqlPackage/DacFx would otherwise block it as possibly data-lossy. Required to apply diffs flagged unsafe by `DeployReportClassifier`. |
| `--allow-incompatible-platform` | Permit the deploy to proceed when the target server's platform differs from the dacpac's target platform. The schema targets Azure SQL Database, so this is required when deploying to a box SQL Server (local development, on-premises, or a test container). |
| `--config` | Path to a JSON configuration file with tenant connection settings. Defaults to `appsettings.json` in the current working directory. |

## Configuration

The configuration file must describe the target tenant under `Tenants:Configurations`, matching
the same shape the server itself reads its tenant configuration from:

```json
{
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": 1,
        "DisplayName": "Example Tenant",
        "FhirVersion": "4.0",
        "Storage": {
          "Type": "SqlServer",
          "ConnectionString": "Server=...;Database=...;..."
        }
      }
    ]
  }
}
```

`"Type": "SqlServer"` and `"Type": "SqlEntityFramework"` are accepted as the same storage type by
`TenantConnectionStringResolver.IsSqlServerStorage`. Every `appsettings*.json` under
`src/Application/Ignixa.Web/` uses `SqlEntityFramework`, so if you copy `Storage` from a running
server's own configuration (as this section recommends), expect to see that value, not `SqlServer`.

The configuration file passed via `--config` is not the only source of settings: `Program.cs` also
layers `appsettings.{ASPNETCORE_ENVIRONMENT ?? "Production"}.json` (optional, same directory) and
then environment variables on top of it, exactly like the server does. If `ASPNETCORE_ENVIRONMENT`
is set in the shell you run this tool from -- common in a deployed environment -- values from that
environment-specific file or from environment variables can silently override what `--config`
specifies.

Only the tenant identified by `--tenant-id` needs a `SqlServer`/`SqlEntityFramework` storage type
with a connection string; the tool resolves that single tenant's connection string and does not
touch any other tenant's database -- **except for `--tenant-id 0`**. Tenant 0 is the reserved
system partition, and if it has no `ConnectionString` of its own, `TenantConnectionStringResolver`
resolves it by inheriting the connection string of another tenant, named by
`Storage:InheritConnectionStringFromTenant` (defaults to tenant 1). In that configuration,
`--tenant-id 0` deploys schema to that other tenant's database, not a system-only database --
including under `--allow-data-loss`. This is deliberate single-tenant-deployment behavior (see the
comments in `TenantConnectionStringResolver.ResolveAsync`), but it means the blast radius of a
`--tenant-id 0` run depends on how the target tenant configures that setting. See #395 for a
related issue with how `InheritConnectionStringFromTenant` binds from configuration.

## Exit Codes

- `0` - The schema was applied and the tenant's schema version was recorded.
- `1` - The operator declined the confirmation prompt; nothing was applied.
- `2` - The schema WAS applied, but recording the new schema version in `dbo.SchemaVersion` failed.
  The database is up to date; only the version record is missing. Do not re-run the tool expecting
  the schema change to be re-applied -- resolve the underlying error first, or insert the version
  row manually.
- `3` - The tool failed before or during setup and nothing was attempted: an unknown or inactive
  tenant, a tenant not configured for `SqlServer`/`SqlEntityFramework` storage, a missing or
  unparseable connection string, a `--config` file that could not be found, or a missing embedded
  schema dacpac. The error is written to stderr. This is deliberately a different code from `1`:
  a scripted caller relying on `--confirm` needs to be able to tell "the operator declined" apart
  from "the tool crashed before doing anything".

## License

MIT License. See LICENSE file in the repository root.
