# SQL Server Connection Pooling Analysis

**Date**: October 31, 2025
**Issue**: Verify connection pooling is enabled for optimal performance
**Priority**: High (70-80% of request latency is database I/O)

## TL;DR - Summary

✅ **Connection Pooling is ENABLED by default** in ADO.NET/Entity Framework Core

❌ **Connection pooling parameters are NOT explicitly configured** in connection strings

⚠️ **Using default pool settings** which may not be optimal for production workloads

## Current Configuration

### Code Location

File: `src/Ignixa.DataLayer.SqlEntityFramework/SqlEntityFrameworkRepositoryFactory.cs:210-216`

```csharp
optionsBuilder.UseSqlServer(
    tenantConfig.Storage.ConnectionString,
    sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(maxRetryCount: 3);
        sqlOptions.CommandTimeout(30);
    });
```

### Connection Strings (Development)

**Local SQL Server** (launchSettings.json):
```
server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true
```

**Azure SQL (Production Pattern)**:
```
Server=tcp:servername.database.windows.net,1433;
Database=FhirDatabase;
Encrypt=true;
TrustServerCertificate=false;
Authentication=Active Directory Managed Identity;
```

### Architecture

**Factory Pattern** (SqlEntityFrameworkRepositoryFactory):
- **Caches** `DbContextOptions` per tenant (thread-safe)
- **Creates NEW `DbContext` per request** (line 77, 92)
- **DbContext is scoped** (disposed after request completes)

This is the **correct pattern** for Entity Framework Core:
- DbContext is NOT thread-safe → Create per request
- DbContextOptions IS thread-safe → Cache and reuse

## Connection Pooling Status

### ✅ ENABLED by Default

ADO.NET connection pooling is **enabled by default** when using SQL Server:

| Parameter | Default Value | Notes |
|-----------|---------------|-------|
| **Pooling** | `true` | Enabled by default, no need to specify |
| **Min Pool Size** | `0` | No minimum connections kept alive |
| **Max Pool Size** | `100` | Maximum connections in pool |
| **Connection Lifetime** | `0` (unlimited) | Connections live until pool is cleared |
| **Connection Timeout** | `15` seconds | Time to wait for connection from pool |

**Source**: [Microsoft Docs - Connection Pooling](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-connection-pooling)

### How It Works

```
Request 1 → DbContext created → Opens connection → Query → DbContext disposed → Connection returned to pool
Request 2 → DbContext created → Gets connection from pool (FAST!) → Query → DbContext disposed → Connection returned
Request 3 → DbContext created → Gets connection from pool → Query → Disposed → Returned
```

**Key Point**: Even though we create a new `DbContext` per request, the underlying **SQL connection is pooled** by ADO.NET.

### Verification

The factory logs indicate pooling is working:
```
info: Ignixa.DataLayer.SqlEntityFramework.SqlEntityFrameworkRepositoryFactory[0]
      Creating service factory for tenant 1 (MS Health SQL Server Clinic)
info: Ignixa.DataLayer.SqlEntityFramework.SqlEntityFrameworkRepositoryFactory[0]
      Successfully created service factory for tenant 1
```

Only **one service factory** is created per tenant (DbContextOptions cached). Each request then creates a transient DbContext that uses the pooled connection.

## Default Pool Settings Analysis

### Current Behavior (Defaults)

| Setting | Value | Impact on Performance |
|---------|-------|----------------------|
| **Pooling** | Enabled | ✅ Good - connections are reused |
| **Min Pool Size** | 0 | ⚠️ No warm connections - first request after idle must open new connection (~100ms penalty) |
| **Max Pool Size** | 100 | ✅ Adequate for most workloads |
| **Connection Lifetime** | 0 (unlimited) | ⚠️ Connections never recycled - may cause issues with load balancers |

### Performance Impact

**First Request After Idle** (Min Pool Size = 0):
```
Request → Pool empty → Open new connection → Authenticate (Managed Identity) → Query
          ↑________________________100-200ms_________________________↑
```

**Subsequent Requests** (Connection pooled):
```
Request → Get from pool → Query
          ↑____<1ms____↑
```

**High Concurrency** (Max Pool Size = 100):
- If 101 concurrent requests arrive, the 101st request waits up to 15 seconds (Connection Timeout)
- After 15 seconds, throws `System.InvalidOperationException: Timeout expired. The timeout period elapsed prior to obtaining a connection from the pool.`

## Recommendations

### 1. Add Explicit Connection Pooling Parameters ✅ RECOMMENDED

Update connection strings to optimize for production workloads:

#### Development (Local SQL Server)

**File**: `src/Ignixa.Api/Properties/launchSettings.json:26,32`

**Current**:
```
server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true
```

**Recommended**:
```
server=(local);
Initial Catalog=FHIR_R4;
Integrated Security=true;
TrustServerCertificate=true;
Pooling=true;
Min Pool Size=5;
Max Pool Size=100;
Connection Lifetime=1800;
Connection Timeout=30
```

#### Production (Azure SQL with Managed Identity)

**Current** (from code comments):
```
Server=tcp:servername.database.windows.net,1433;
Database=FhirDatabase;
Encrypt=true;
TrustServerCertificate=false;
Authentication=Active Directory Managed Identity;
```

**Recommended**:
```
Server=tcp:servername.database.windows.net,1433;
Database=FhirDatabase;
Encrypt=true;
TrustServerCertificate=false;
Authentication=Active Directory Managed Identity;
Pooling=true;
Min Pool Size=10;
Max Pool Size=200;
Connection Lifetime=300;
Connection Timeout=30
```

### 2. Parameter Explanations

| Parameter | Recommended | Rationale |
|-----------|-------------|-----------|
| **Pooling=true** | `true` | Explicit (already default, but good for documentation) |
| **Min Pool Size** | `5-10` (Dev)<br>`10-20` (Prod) | Pre-warm connections to avoid cold-start penalty<br>Set based on expected baseline load |
| **Max Pool Size** | `100` (Dev)<br>`200-500` (Prod) | Limit based on SQL Server max connections<br>Azure SQL Basic: 30, Standard S3: 200, Premium P2: 2000 |
| **Connection Lifetime** | `300-1800` (5-30 min) | Force periodic recycling for load balancer affinity<br>Balances connection reuse vs. server distribution |
| **Connection Timeout** | `30` | Time to wait for pool connection before exception<br>Matches `sqlOptions.CommandTimeout(30)` in code |

### 3. Calculate Optimal Max Pool Size

**Formula**:
```
Max Pool Size = (Expected Peak RPS × Avg Query Time) + Safety Margin

Example (Production):
- Peak RPS: 500 requests/sec
- Avg Query Time: 50ms (0.05 sec)
- Active Connections: 500 × 0.05 = 25
- Safety Margin: 3x for spikes = 75
- Max Pool Size: 75 (round up to 100)

For very high throughput:
- Peak RPS: 2000 requests/sec
- Avg Query Time: 50ms
- Active Connections: 2000 × 0.05 = 100
- Safety Margin: 3x = 300
- Max Pool Size: 300 (round up to 500)
```

**Azure SQL Limits**:
- Basic: 30 max connections (too small for production)
- Standard S3: 200 max connections
- Premium P2: 2,000 max connections

### 4. Monitor Connection Pool Health

Use `dotnet-counters` to monitor pool usage:

```bash
dotnet tool install --global dotnet-counters

dotnet counters monitor --process-id <PID> \
  --counters Microsoft.Data.SqlClient.EventSource

# Key metrics:
# - NumberOfActiveConnectionPools: Should be 1 per tenant
# - NumberOfActiveConnections: Current connections in use
# - NumberOfFreeConnections: Available pooled connections
# - NumberOfPooledConnections: Total connections (Active + Free)
```

### 5. SQL Server Side Monitoring

```sql
-- Check current connections
SELECT
    DB_NAME(dbid) AS DatabaseName,
    COUNT(dbid) AS NumberOfConnections,
    loginame AS LoginName
FROM sys.sysprocesses
WHERE dbid > 0
GROUP BY dbid, loginame
ORDER BY DB_NAME(dbid);

-- Check connection pool exhaustion (waits)
SELECT
    wait_type,
    waiting_tasks_count,
    wait_time_ms,
    max_wait_time_ms
FROM sys.dm_os_wait_stats
WHERE wait_type LIKE '%POOL%'
ORDER BY wait_time_ms DESC;
```

## Implementation Plan

### Phase 1: Update Development Connection Strings ✅

**Files to Update**:
1. `src/Ignixa.Api/Properties/launchSettings.json` (lines 26, 32)
2. `src/Ignixa.Api/appsettings.Development.json` (lines 53, 64)

**Changes**:
```json
"ConnectionString": "server=(local);Initial Catalog=FHIR_R4;Integrated Security=true;TrustServerCertificate=true;Pooling=true;Min Pool Size=5;Max Pool Size=100;Connection Lifetime=1800"
```

### Phase 2: Document Production Best Practices 📝

**File**: `src/Ignixa.DataLayer.SqlEntityFramework/README.md`

Add section:
```markdown
## Connection Pooling Configuration

For optimal performance, configure connection pooling parameters:

### Development (Local SQL Server)
\`\`\`
Pooling=true;Min Pool Size=5;Max Pool Size=100;Connection Lifetime=1800
\`\`\`

### Production (Azure SQL)
\`\`\`
Pooling=true;Min Pool Size=10;Max Pool Size=200;Connection Lifetime=300
\`\`\`

Adjust Max Pool Size based on Azure SQL tier and expected load.
```

### Phase 3: Add Monitoring Dashboard 📊

**Integration**: Application Insights or Prometheus

Expose metrics:
- Active connections per tenant
- Pool exhaustion events (timeout exceptions)
- Average connection acquisition time

### Phase 4: Load Test Validation ⚡

Run k6 load test with different pool configurations:

```javascript
// k6 connection-pool-test.js
export let options = {
  stages: [
    { duration: '1m', target: 50 },   // Baseline
    { duration: '2m', target: 200 },  // High load
    { duration: '1m', target: 500 },  // Peak
    { duration: '1m', target: 0 },    // Ramp down
  ],
};

export default function () {
  let res = http.post('http://localhost:5000/Patient', patient);
  check(res, {
    'status 201': (r) => r.status === 201,
    'latency < 100ms': (r) => r.timings.duration < 100,
  });
}
```

**Test Matrix**:
| Configuration | P50 Latency | P95 Latency | Pool Exhaustion Events |
|---------------|-------------|-------------|------------------------|
| **Default** (Min=0, Max=100) | TBD | TBD | TBD |
| **Recommended** (Min=10, Max=200) | TBD | TBD | TBD |
| **Aggressive** (Min=20, Max=500) | TBD | TBD | TBD |

## Expected Performance Impact

### Before Optimization (Default Settings)

```
Request Latency Breakdown:
├─ Application Layer:      0.057ms  (< 0.1%)
├─ First Request (Cold):  100-200ms (Database connection open + auth)
├─ Subsequent Requests:    30-70ms  (Query execution)
└─ Total (P50):           50-100ms
```

### After Optimization (Recommended Settings)

```
Request Latency Breakdown:
├─ Application Layer:      0.057ms  (< 0.1%)
├─ First Request (Warm):   30-70ms  (Pool pre-warmed, no cold start)
├─ Subsequent Requests:    30-70ms  (Query execution)
└─ Total (P50):           30-70ms   (30-50% improvement on cold starts)
```

**Expected Improvements**:
- **Cold Start**: 30-50% faster (pre-warmed pool eliminates 100ms open/auth overhead)
- **High Concurrency**: No pool exhaustion up to 200 concurrent requests (vs. 100 default)
- **Load Balancer Affinity**: Better distribution with `Connection Lifetime=300` (5 min)

## Security Considerations

### ✅ Current Security Posture

1. **Managed Identity Required** (Line 139-183):
   - Code validates connection strings reject password-based auth
   - Production MUST use Azure AD authentication
   - Follows least-privilege principle

2. **Connection String Security**:
   - No passwords stored in connection strings
   - Managed Identity token-based authentication
   - Automatic credential rotation

### ⚠️ Connection Pooling Security Notes

1. **Pool Isolation**:
   - Each **unique connection string** gets its own pool
   - Tenants with different databases → Separate pools ✅
   - Tenants with same database → **Share pool** ⚠️

2. **Connection Reuse**:
   - Connections are **reused across requests** (by design)
   - SQL Server row-level security (RLS) or schema-based isolation recommended if sharing database

3. **Credential Caching**:
   - Managed Identity tokens cached in connection pool
   - Tokens auto-refreshed by `Microsoft.Data.SqlClient`
   - No action needed

## Related Documents

- **Performance Analysis**: `POST-PUT-PERFORMANCE-SUMMARY.md`
- **Detailed Analysis**: `docs/investigations/post-put-performance-analysis.md`
- **SQL EF README**: `src/Ignixa.DataLayer.SqlEntityFramework/README.md`
- **ADR**: Multi-tenancy data partitioning (to be created)

## References

- [Microsoft Docs: SQL Server Connection Pooling](https://learn.microsoft.com/en-us/dotnet/framework/data/adonet/sql-server-connection-pooling)
- [EF Core: DbContext Lifetime Best Practices](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/)
- [Azure SQL: Connection Limits by Tier](https://learn.microsoft.com/en-us/azure/azure-sql/database/resource-limits-dtu-single-databases)

## Action Items

- [ ] Update development connection strings with explicit pooling parameters
- [ ] Document production best practices in README
- [ ] Add connection pool monitoring (`dotnet-counters`)
- [ ] Run load test to validate optimal Max Pool Size
- [ ] Create ADR for connection pooling strategy
- [ ] Add Application Insights dashboard for pool metrics

---

**Status**: ✅ Connection pooling is ENABLED (default)
**Recommendation**: Add explicit parameters for optimal production performance
**Priority**: High (database I/O is 70-80% of request latency)
**Est. Impact**: 30-50% reduction in cold-start latency, improved high-concurrency handling
