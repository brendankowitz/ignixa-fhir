---
sidebar_position: 1
title: Docker Deployment
description: Deploy Ignixa using Docker
---

# Docker Deployment

Run Ignixa with Docker and SQL Server.

## Quick Start

```bash
docker pull ghcr.io/brendankowitz/ignixa-fhir:release
```

| Tag | Description |
|-----|-------------|
| `release` | Latest stable release |
| `latest` | Latest build from main branch |

## Docker Compose

The recommended way to run Ignixa locally with SQL Server.

### docker-compose.yml

```yaml
services:
  ignixa:
    image: ghcr.io/brendankowitz/ignixa-fhir:release
    ports:
      - "8080:8080"
    environment:
      - Tenants__Configurations__1__Storage__ConnectionString=Server=sql;Database=FHIR_R4;User Id=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=true
    depends_on:
      sql:
        condition: service_healthy
    healthcheck:
      test: curl -f http://localhost:8080/health/check || exit 1
      interval: 30s
      timeout: 10s
      retries: 3

  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=${SQL_SA_PASSWORD}
    volumes:
      - sql-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "${SQL_SA_PASSWORD}" -C -Q "SELECT 1"
      interval: 10s
      retries: 10

volumes:
  sql-data:
```

### .env

```bash
SQL_SA_PASSWORD=<your-password>
```

### Run

```bash
docker compose up -d
```

Access at `http://localhost:8080/metadata`.

## With Azure Storage Emulator

For bulk operations, add Azurite:

```yaml
services:
  ignixa:
    image: ghcr.io/brendankowitz/ignixa-fhir:release
    ports:
      - "8080:8080"
    environment:
      - Tenants__Configurations__1__Storage__ConnectionString=Server=sql;Database=FHIR_R4;User Id=sa;Password=${SQL_SA_PASSWORD};TrustServerCertificate=true
      - BlobStorage__Provider=Azure
      - AzureBlobStorage__ConnectionString=DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://azurite:10000/devstoreaccount1
    depends_on:
      sql:
        condition: service_healthy

  sql:
    image: mcr.microsoft.com/mssql/server:2022-latest
    environment:
      - ACCEPT_EULA=Y
      - SA_PASSWORD=${SQL_SA_PASSWORD}
    volumes:
      - sql-data:/var/opt/mssql
    healthcheck:
      test: /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "${SQL_SA_PASSWORD}" -C -Q "SELECT 1"
      interval: 10s
      retries: 10

  azurite:
    image: mcr.microsoft.com/azure-storage/azurite
    command: azurite-blob --blobHost 0.0.0.0
    volumes:
      - azurite-data:/data

volumes:
  sql-data:
  azurite-data:
```

## Environment Variables

| Variable | Description |
|----------|-------------|
| `Tenants__Configurations__1__Storage__ConnectionString` | SQL Server connection string (required) |
| `BlobStorage__Provider` | `Azure` or `Local` |
| `AzureBlobStorage__ConnectionString` | Azure Storage connection string |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | Set `true` behind reverse proxy |
| `SqlServer__AutomaticSchemaDeploymentEnabled` | Allow the server to deploy and upgrade tenant schema itself (default `false`). See the note in [Database Schema](#database-schema) about box SQL Server targets |

See [Configuration](/docs/server/configuration) for all options.

## Database Schema

The tenant database must contain the Ignixa schema before the first request for that tenant is
served. By default the server will not create it — `SqlServer__AutomaticSchemaDeploymentEnabled` is
`false`, so an uninitialized or out-of-date tenant database fails the request with an error naming
the remedy. See
[SQL Server Schema Deployment](/docs/server/configuration#sql-server-schema-deployment).

:::note Target platform
The schema is built for **Azure SQL Database**, which is a distinct DacFx target platform from a
box SQL Server (including the `mcr.microsoft.com/mssql/server` image in the compose file above).
When the server runs as `Production` against a box SQL Server, automatic deployment is refused on
platform grounds even with `SqlServer__AutomaticSchemaDeploymentEnabled` set to `true` — this is
deliberate, and there is no configuration setting that overrides it. Apply the schema with the
[Schema Upgrade CLI](/docs/server/schema-upgrade-cli) using `--allow-incompatible-platform`
instead. Non-production hosts (for example `ASPNETCORE_ENVIRONMENT=Development`) allow it
automatically, which is what local development and the test containers rely on.
:::

The schema-upgrade CLI is published into this image alongside the server, so you can act on that
error from inside the running container:

```bash
docker exec -w /app ignixa dotnet Ignixa.SchemaUpgrade.Cli.dll --tenant-id 1
```

The container's environment variables — including the connection string you passed with `-e` — are
inherited by `docker exec`, so the tool resolves the same tenant configuration the server is using.
Against a box SQL Server (the `mssql/server` image above, or local development) add
`--allow-incompatible-platform`: the schema targets Azure SQL Database.

See [Schema Upgrade CLI](/docs/server/schema-upgrade-cli) for every option and its exit codes.

## Health Check

```bash
curl http://localhost:8080/health/check
# Returns: {"status":"healthy","timestamp":"...","version":"0.1.0"}
```

## Related

- [Configuration](/docs/server/configuration)
- [Schema Upgrade CLI](/docs/server/schema-upgrade-cli)
- [Azure Deployment](/docs/server/deployment/azure)
