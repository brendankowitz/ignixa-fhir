# E2E Testing Guide

## Overview

E2E tests validate the full stack including SQL Server database operations. They use **SQL Server by default** (production configuration) to ensure comprehensive validation of search functionality and database operations.

## Running E2E Tests Locally

### Default Mode (SQL Server - Recommended)

```bash
# Start SQL Server container
docker compose -f docker-compose.test.yml up -d --wait

# Run all E2E tests with SQL Server (default)
dotnet test test/Ignixa.Api.E2ETests/

# Cleanup
docker compose -f docker-compose.test.yml down -v
```

This is the **recommended mode** as it matches production and validates all search functionality.

### Quick Mode (FileSystem - Fast but Limited)

For fast iteration without SQL Server (limited search support):

```bash
# PowerShell
$env:TEST_USE_FILESYSTEM="true"
dotnet test test/Ignixa.Api.E2ETests/

# Bash/Linux
export TEST_USE_FILESYSTEM=true
dotnet test test/Ignixa.Api.E2ETests/
```

**Note:** FileSystem mode is faster (~5 seconds) but has limited search functionality. Use SQL mode for comprehensive validation.

### Custom SQL Server Configuration

Override connection settings with environment variables:

```bash
# Custom SA password (docker-compose uses .env.test)
export SQL_SA_PASSWORD="MyPassword123"
dotnet test test/Ignixa.Api.E2ETests/

# Full custom connection string
export TEST_SQL_CONNECTION_STRING="Server=myserver;Database=TestDB;User Id=sa;Password=pwd;TrustServerCertificate=true"
dotnet test test/Ignixa.Api.E2ETests/
```

## Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `TEST_USE_FILESYSTEM` | Set to "true" to use FileSystem instead of SQL | `false` (SQL Server) |
| `TEST_SQL_CONNECTION_STRING` | Full connection string override | Auto-generated with unique GUID database |
| `SQL_SA_PASSWORD` | SA password for SQL Server | `YourStrong!Passw0rd` |

## CI Behavior

In GitHub Actions PR pipeline:
- **Job 1**: Unit tests (excludes E2E tests) - ~30 seconds, fast feedback
- **Job 2**: E2E tests with SQL Server - ~2-5 minutes, full validation
- **Job 3**: Docker build validation

Filter pattern: `FullyQualifiedName~E2ETests` (matches the `Ignixa.Api.E2ETests` assembly)

## Test Isolation

Each test run uses a unique database to prevent conflicts:
- Database name: `FhirTest_{GUID}` (e.g., `FhirTest_a1b2c3d4e5f6...`)
- Created automatically by `IgnixaApiFixture`
- Schema initialized via Entity Framework migrations
- xUnit collection fixtures prevent parallel database conflicts within a single run

## Troubleshooting

### SQL Server won't start locally

```bash
# Check Docker logs
docker logs ignixa-test-sql

# Restart with fresh state
docker compose -f docker-compose.test.yml down -v
docker compose -f docker-compose.test.yml up -d --wait

# Check health status
docker ps
docker inspect ignixa-test-sql | grep Health -A 10
```

### Tests fail with connection timeout

1. **Verify SQL Server is healthy:**
   ```bash
   docker ps
   # Should show "healthy" status
   ```

2. **Check health check logs:**
   ```bash
   docker inspect ignixa-test-sql | grep Health -A 10
   ```

3. **Increase wait time** (if needed) in `docker-compose.test.yml`:
   ```yaml
   healthcheck:
     retries: 20  # Increase from 12
   ```

### Database schema errors

- Ensure SqlEntityFramework migrations are up to date
- Check `DatabaseInitializer` logs in test output
- Verify Entity Framework migrations run successfully:
  ```bash
  docker exec ignixa-test-sql /opt/mssql-tools18/bin/sqlcmd \
    -S localhost -C -U sa -P "YourStrong!Passw0rd" \
    -Q "SELECT name FROM sys.databases" -b
  ```

### Permission denied on Linux/Mac

If you get permission errors with docker-compose:

```bash
# Ensure Docker daemon is running
sudo systemctl start docker

# Add your user to docker group
sudo usermod -aG docker $USER
newgrp docker
```

## Architecture

### IgnixaApiFixture Modes

The test fixture (`test/Ignixa.Api.E2ETests/Fixtures/IgnixaApiFixture.cs`) supports two modes:

**SQL Server Mode (default):**
- SqlEntityFramework storage
- Real SQL Server 2022 database
- Full EF Core migrations
- Complete search functionality
- Production-accurate validation (~30 seconds - 2 minutes)

**FileSystem Mode (opt-in with TEST_USE_FILESYSTEM=true):**
- FileSystem storage with temp directory
- InMemory search index
- No external dependencies
- Limited search support
- Fast (~5 seconds for full suite)

### Database Lifecycle

1. **Fixture Construction**: Connection string generated with unique GUID
2. **InitializeAsync**: Database created via SqlConnection to master
3. **Test Execution**: EF migrations run on first DbContext usage
4. **Cleanup**: Database persists (unique per run, no conflicts)

## Performance Comparison

| Mode | Duration | Use Case |
|------|----------|----------|
| SQL Server (default) | ~30 seconds - 2 minutes | Default, production-accurate, full search support |
| FileSystem (opt-in) | ~5 seconds | Quick iteration, limited search functionality |

## Best Practices

1. **Default Development**: Use SQL mode (default) for production-accurate validation
2. **Quick Iteration**: Use FileSystem mode (`TEST_USE_FILESYSTEM=true`) when you need fast feedback
3. **Pre-Commit**: Always run SQL mode before committing to catch integration issues
4. **CI**: SQL mode runs automatically in PR pipeline
5. **Debugging**: Use `docker logs ignixa-test-sql` to troubleshoot SQL issues
6. **Clean State**: Always use `docker compose down -v` to remove volumes between runs

## References

- [Docker Compose Configuration](../../docker-compose.test.yml)
- [IgnixaApiFixture Source](../../test/Ignixa.Api.E2ETests/Fixtures/IgnixaApiFixture.cs)
- [PR Build Workflow](../../.github/workflows/pr-build.yml)
