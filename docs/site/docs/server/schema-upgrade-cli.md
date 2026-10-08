---
sidebar_position: 4
title: Schema Upgrade CLI
description: Review and apply tenant schema upgrades the server refuses to apply automatically
---

# Schema Upgrade CLI

`ignixa-schema-upgrade` reviews and applies a pending schema upgrade for a tenant database that the
server's automatic path refused.

The server refuses in two situations, both described in
[SQL Server Schema Deployment](/docs/server/configuration#sql-server-schema-deployment):

- `SqlServer:AutomaticSchemaDeploymentEnabled` is `false` (the default), so the server was never
  permitted to apply schema changes.
- The setting is `true`, but the pending diff did not classify as auto-safe — DacFx flagged it as a
  data issue, or the deploy report could not be read confidently.

In both cases the exception the server raises names this tool. It is the only supported way to apply
a diff the classifier declined, because it puts a human between the diff and the database: it prints
the full DacFx deploy report, states the classification, and asks for confirmation before applying
anything.

## Obtaining the tool

The tool is packaged as a .NET tool (`Ignixa.SchemaUpgrade.Cli`, command `ignixa-schema-upgrade`)
and is **also published into the container image**, so an operator who hit one of the errors above
inside a container can act on it without leaving that container:

```bash
docker exec -w /app <container> dotnet Ignixa.SchemaUpgrade.Cli.dll --tenant-id 1
```

Running it inside the container is usually what you want in a managed environment: the container
holds the managed identity that has rights on the tenant database, and `/app/appsettings.json` plus
the container's own environment variables are the configuration the server itself is running with.
`docker exec` (and the equivalent `az containerapp exec` / `kubectl exec`) inherits those environment
variables, so a connection string supplied with `-e` at run time is picked up without repeating it.

To run it from a workstation or build agent instead, pack and install it from source:

```bash
dotnet pack tools/Ignixa.SchemaUpgrade.Cli
dotnet tool install --global --add-source tools/Ignixa.SchemaUpgrade.Cli/nupkg Ignixa.SchemaUpgrade.Cli
ignixa-schema-upgrade --tenant-id 1
```

Or run it directly from the repository without installing:

```bash
dotnet run --project tools/Ignixa.SchemaUpgrade.Cli -- --tenant-id 1
```

## Options

| Option | Required | Description |
|--------|----------|-------------|
| `--tenant-id` | Yes | The tenant ID to upgrade |
| `--confirm` | No | Apply the upgrade without an interactive prompt (for scripted/CI use) |
| `--allow-data-loss` | No | Permit the deploy to proceed even when SqlPackage/DacFx would otherwise block it as possibly data-lossy. Required to apply diffs flagged unsafe by the deploy-report classifier |
| `--allow-incompatible-platform` | No | Permit the deploy to proceed when the target server's platform differs from the dacpac's target platform |
| `--config` | No | Path to a JSON configuration file with tenant connection settings. Defaults to `appsettings.json` in the current working directory |

### `--allow-incompatible-platform`

The schema targets **Azure SQL Database**, because that is the production deployment target. A box
SQL Server — local development, on-premises, or a test container — is therefore a different DacFx
platform, and DacFx refuses to deploy across that boundary unless told otherwise. Pass this flag when
the target is box SQL Server. A production deploy is Azure-to-Azure and does not need it; if it
appears necessary against a production target, the target is not a platform this schema is built for.

### Configuration resolution

`--config` is loaded as a **required** JSON file relative to the current working directory, then
layered with `appsettings.{ASPNETCORE_ENVIRONMENT}.json` if present (defaulting to `Production` when
`ASPNETCORE_ENVIRONMENT` is unset), then environment variables. Environment variables win, so a
tenant connection string supplied as
`Tenants__Configurations__1__Storage__ConnectionString` overrides the file.

## What it does

1. Resolves the tenant's connection string from the configuration above.
2. Generates a DacFx deploy report for the pending diff and prints the XML in full.
3. Prints the classification of that diff:
   - **auto-safe** — the automatic path should have applied it; applying it here is redundant but
     harmless.
   - **not auto-safe** — names what DacFx flagged, and notes that if DacFx still blocks the deploy
     citing possible data loss, you need `--allow-data-loss`.
   - **could not be classified** — the usual data-loss signal could not be verified, so the XML needs
     especially careful review. This is deliberately a prompt rather than a hard stop: the whole
     point of the tool is to let an operator decide what the automatic path would not.
4. Prompts `Apply this diff? [y/N]` unless `--confirm` was passed. Only `y` (case-insensitive)
   proceeds; anything else aborts.
5. Applies the deploy and records the new schema version, as one paired operation.

:::note
`--allow-data-loss` only relaxes DacFx's own `BlockOnPossibleDataLoss` guard. It does not change the
classification that is printed, and the classification never suppresses the prompt.
:::

## Exit codes

| Code | Meaning |
|------|---------|
| `0` | Applied. The tenant's database is on the current schema and the version is recorded |
| `1` | Aborted at the confirmation prompt. **Nothing was applied** |
| `2` | The schema **was applied**, but recording the version in `dbo.SchemaVersion` failed |

:::warning
Exit code `2` does **not** mean nothing happened. The schema change committed; only the version
record is missing. Do not re-run the tool expecting the schema change to be re-applied — after a
destructive `--allow-data-loss` run, that assumption is how you apply a data-lossy change twice.
Resolve the underlying error and re-run, or insert the version row manually.
:::

The distinction matters because `1` and `2` are the two non-success outcomes an automated caller
sees, and they require opposite responses: `1` is safe to retry, `2` is not a retry at all.

## Scripted use

```bash
ignixa-schema-upgrade --tenant-id 1 --confirm --config /app/appsettings.json
case $? in
  0) echo "upgrade applied" ;;
  1) echo "aborted, nothing applied"; exit 1 ;;
  2) echo "SCHEMA APPLIED but version stamp failed - do not retry blindly"; exit 2 ;;
esac
```

Run it once per tenant. Each tenant has its own database and its own schema version, so upgrading one
tenant says nothing about the others.

## Related

- [Configuration](/docs/server/configuration) - `SqlServer:AutomaticSchemaDeploymentEnabled`
- [Multi-Tenancy](/docs/server/multi-tenancy) - Why schema version is per-tenant
- [Docker Deployment](/docs/server/deployment/docker) - Running the tool inside the container
