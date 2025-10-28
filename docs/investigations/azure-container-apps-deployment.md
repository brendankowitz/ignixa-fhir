# Azure Container Apps Deployment Investigation

**Status**: Research
**Date**: 2025-10-28
**Author**: Investigation for containerizing Ignixa FHIR server and deploying to Azure Container Apps

## Executive Summary

This document outlines the research and strategy for deploying the Ignixa FHIR server to Azure Container Apps using Docker containerization. The approach focuses on production readiness with Azure SQL Database backend, comprehensive observability (health checks, Application Insights, Prometheus metrics), and automated CI/CD via GitHub Actions.

## Why Azure Container Apps?

Azure Container Apps (ACA) is Microsoft's serverless container platform that sits between Azure App Service and Azure Kubernetes Service (AKS):

**Advantages for FHIR Server**:
- **Serverless scaling**: Pay only for active usage, scale to zero when idle
- **Simplified operations**: No Kubernetes complexity (vs AKS)
- **Container benefits**: Full control over runtime environment
- **Cost-effective**: ~$40-80/month for production workloads vs $200+/month for AKS
- **Event-driven**: Native support for DurableTask orchestrations (used for $export/$import)
- **Managed ingress**: HTTPS termination, custom domains, certificates handled
- **Built-in monitoring**: Azure Monitor and Application Insights integration

**Trade-offs**:
- Less control than AKS (but more than App Service)
- Limited to HTTP/HTTPS traffic (sufficient for FHIR REST API)
- Regional availability constraints

## Docker Containerization Strategy

### Multi-Stage Dockerfile Design

The recommended approach uses a 4-stage build optimized for .NET 9:

```dockerfile
# Stage 1: Restore dependencies (optimized for layer caching)
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS restore
WORKDIR /src
COPY ["Directory.Packages.props", "./"]
COPY ["src/Ignixa.Api/Ignixa.Api.csproj", "src/Ignixa.Api/"]
COPY ["src/Ignixa.Application/Ignixa.Application.csproj", "src/Ignixa.Application/"]
# ... copy all .csproj files
RUN dotnet restore "src/Ignixa.Api/Ignixa.Api.csproj"

# Stage 2: Build
FROM restore AS build
COPY . .
WORKDIR "/src/src/Ignixa.Api"
RUN dotnet build "Ignixa.Api.csproj" -c Release -o /app/build --no-restore

# Stage 3: Publish
FROM build AS publish
RUN dotnet publish "Ignixa.Api.csproj" -c Release -o /app/publish \
    --no-restore --no-build \
    /p:UseAppHost=false

# Stage 4: Final runtime image
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Security: Run as non-root user
RUN adduser --disabled-password --gecos "" --uid 1000 appuser
USER appuser

# Copy published app
COPY --from=publish /app/publish .

# Configure ports
EXPOSE 8080 8081
ENV ASPNETCORE_URLS=http://+:8080;https://+:8081

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s \
    CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "Ignixa.Api.dll"]
```

### Base Image Selection

**Research Findings** (January 2025):

| Base Image | Size | Security | Compatibility | Recommendation |
|------------|------|----------|---------------|----------------|
| **Debian (standard)** | ~220 MB | Good | Excellent | ✅ **Recommended** |
| Alpine | ~110 MB | Good | Fair (glibc issues) | ⚠️ Use with caution |
| Chiseled Ubuntu | ~100 MB | Excellent (no shell) | Good | ✅ Best for security-critical |

**Decision**: Use **Debian standard** (`mcr.microsoft.com/dotnet/aspnet:9.0`) for:
- Maximum compatibility with Entity Framework Core and SQL Server
- Established production track record
- Microsoft's default recommendation for ASP.NET Core

**Alternative**: Chiseled Ubuntu for high-security environments (requires testing with EF Core).

### Layer Caching Optimization

Key optimizations:
1. **Separate .csproj restore**: Copy `.csproj` files first, then restore (cached until projects change)
2. **Copy source last**: Application code changes don't invalidate restore layer
3. **Multi-stage efficiency**: Final image only contains runtime + published app (~220 MB vs 2 GB SDK image)

### .dockerignore Configuration

Exclude unnecessary files to optimize build context:

```
# Build outputs
**/bin/
**/obj/
**/out/

# IDE files
.vs/
.vscode/
*.user
*.suo

# Test projects (exclude from production image)
**/test/
**/*Tests*/
**/*.Tests.csproj

# Git and CI
.git/
.github/
*.md
LICENSE

# Local data (not needed in container)
fhir-data/
fhir-exports/
tenants/

# Compatibility test CLI
test/Ignixa.Tests.Compatibility.CLI/
```

## Azure Container Apps Architecture

### Connection to Azure SQL Database

**Recommended Approach**: Managed Identity (passwordless authentication)

```json
// appsettings.Production.json
{
  "Tenants": {
    "Configurations": [
      {
        "TenantId": 2,
        "Storage": {
          "Type": "SqlEntityFramework",
          "ConnectionString": "Server=tcp:{SQL_SERVER}.database.windows.net,1433;Database={DATABASE};Authentication=ActiveDirectoryManagedIdentity;Encrypt=True;TrustServerCertificate=False;"
        }
      }
    ]
  }
}
```

**Setup Steps**:
1. Enable System-Assigned Managed Identity on Container App
2. Grant SQL Database access:
   ```sql
   CREATE USER [container-app-name] FROM EXTERNAL PROVIDER;
   ALTER ROLE db_datareader ADD MEMBER [container-app-name];
   ALTER ROLE db_datawriter ADD MEMBER [container-app-name];
   ALTER ROLE db_ddladmin ADD MEMBER [container-app-name];
   ```
3. No secrets in code/config - authentication handled by Azure

**Benefits**:
- No connection string secrets to manage
- Automatic token rotation
- Audit trail of database access
- Compliant with zero-trust security model

### Environment Variables Configuration

Container Apps support multiple configuration sources:

```yaml
# Container App configuration
env:
  - name: ASPNETCORE_ENVIRONMENT
    value: "Production"
  - name: ApplicationInsights__InstrumentationKey
    secretRef: app-insights-key
  - name: Tenants__Configurations__0__Storage__ConnectionString
    value: "Server=tcp:ignixa-sql.database.windows.net,1433;..."
  - name: ASPNETCORE_URLS
    value: "http://+:8080"
```

**Best Practice**: Use Azure Key Vault references for secrets (SQL connection strings if not using managed identity).

### Health Probes Configuration

Container Apps require health probes for reliability:

```yaml
probes:
  liveness:
    httpGet:
      path: /health/live
      port: 8080
    initialDelaySeconds: 10
    periodSeconds: 30
    failureThreshold: 3

  readiness:
    httpGet:
      path: /health/ready
      port: 8080
    initialDelaySeconds: 5
    periodSeconds: 10
    failureThreshold: 3
```

**Implementation Required**: Add health check endpoints to `Program.cs` (see Observability section).

### Autoscaling Configuration

```yaml
scale:
  minReplicas: 1
  maxReplicas: 10
  rules:
    - name: http-scaling-rule
      http:
        metadata:
          concurrentRequests: 50
    - name: cpu-scaling-rule
      custom:
        type: cpu
        metadata:
          type: Utilization
          value: 70
```

**Recommendations for FHIR Server**:
- **Min replicas**: 1 (or 2 for high availability)
- **Max replicas**: 10 (adjust based on expected load)
- **Scaling trigger**: CPU utilization > 70% OR concurrent requests > 50
- **Scale-to-zero**: Disabled for production (enable for dev/test environments)

## Observability & Monitoring

### ASP.NET Core Health Checks Integration

**NuGet Packages Required**:
```xml
<PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore" />
<PackageReference Include="AspNetCore.HealthChecks.SqlServer" />
<PackageReference Include="AspNetCore.HealthChecks.Prometheus.Metrics" />
<PackageReference Include="Microsoft.ApplicationInsights.AspNetCore" />
```

**Code Changes to Program.cs**:

```csharp
// Add health checks
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database")
    .AddSqlServer(
        connectionString: builder.Configuration["Tenants:Configurations:0:Storage:ConnectionString"],
        name: "sql-server",
        tags: new[] { "db", "sql" })
    .AddCheck("self", () => HealthCheckResult.Healthy("API is running"));

// Add Application Insights
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.InstrumentationKey = builder.Configuration["ApplicationInsights:InstrumentationKey"];
});

var app = builder.Build();

// Map health check endpoints
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => true,
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("live")
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready")
});

// Prometheus metrics endpoint
app.UseHealthChecksPrometheusExporter("/healthmetrics");
```

**Custom Database Health Check**:

```csharp
public class DatabaseHealthCheck : IHealthCheck
{
    private readonly ITenantConfigurationProvider _tenantProvider;
    private readonly IFhirRepositoryFactoryResolver _factoryResolver;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var tenants = _tenantProvider.GetActiveTenants();
            foreach (var tenant in tenants)
            {
                var factory = _factoryResolver.GetFactory(tenant.TenantId);
                var repository = factory.CreateRepository();

                // Test database connectivity
                await repository.GetByIdAsync("Patient", "health-check-test", cancellationToken);
            }

            return HealthCheckResult.Healthy("All tenant databases are accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Database connectivity failed", ex);
        }
    }
}
```

### Prometheus Metrics

Expose `/healthmetrics` endpoint for scraping:

```csharp
app.UseHealthChecksPrometheusExporter("/healthmetrics", options =>
{
    options.ResultStatusCodes[HealthStatus.Healthy] = 200;
    options.ResultStatusCodes[HealthStatus.Degraded] = 200;
    options.ResultStatusCodes[HealthStatus.Unhealthy] = 503;
});
```

**Metrics Exported**:
- `aspnetcore_healthcheck_status` - Health check status (0=Healthy, 1=Degraded, 2=Unhealthy)
- `aspnetcore_healthcheck_duration_seconds` - Time taken for health check

### Application Insights Telemetry

Automatic collection includes:
- **Request telemetry**: HTTP request duration, status codes, URLs
- **Dependency telemetry**: SQL queries, external HTTP calls
- **Exception telemetry**: Unhandled exceptions with stack traces
- **Custom events**: FHIR operation metrics (searches, creates, updates)

**Custom Metrics Example**:

```csharp
public class SearchQueryHandler : IRequestHandler<SearchQuery, SearchResult>
{
    private readonly TelemetryClient _telemetry;

    public async Task<SearchResult> HandleAsync(SearchQuery request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await ExecuteSearchAsync(request, cancellationToken);

            _telemetry.TrackMetric("FhirSearch.ResultCount", result.Total);
            _telemetry.TrackMetric("FhirSearch.Duration", stopwatch.ElapsedMilliseconds);

            return result;
        }
        catch (Exception ex)
        {
            _telemetry.TrackException(ex);
            throw;
        }
    }
}
```

### Docker HEALTHCHECK

Built into Dockerfile for container runtime monitoring:

```dockerfile
HEALTHCHECK --interval=30s --timeout=3s --start-period=40s --retries=3 \
    CMD curl -f http://localhost:8080/health || exit 1
```

**Why Both Docker HEALTHCHECK and Container Apps Probes?**
- Docker HEALTHCHECK: Used by local Docker runtime and Azure Container Registry
- Container Apps probes: Used by Azure orchestration for replica management

## CI/CD with GitHub Actions

### Workflow Structure

**File**: `.github/workflows/docker-build-push.yml`

```yaml
name: Docker Build and Deploy to ACA

on:
  push:
    branches: [main]
  pull_request:
    branches: [main]

env:
  REGISTRY: ignixa.azurecr.io
  IMAGE_NAME: ignixa-fhir-server

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - name: Checkout code
        uses: actions/checkout@v4

      - name: Log in to Azure Container Registry
        uses: azure/docker-login@v1
        with:
          login-server: ${{ env.REGISTRY }}
          username: ${{ secrets.ACR_USERNAME }}
          password: ${{ secrets.ACR_PASSWORD }}

      - name: Build and push Docker image
        run: |
          docker build -t ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }} \
                       -t ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest \
                       -f Dockerfile .
          docker push ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}
          docker push ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:latest

  deploy-to-aca:
    needs: build-and-push
    runs-on: ubuntu-latest
    if: github.ref == 'refs/heads/main'

    steps:
      - name: Azure Login
        uses: azure/login@v1
        with:
          creds: ${{ secrets.AZURE_CREDENTIALS }}

      - name: Deploy to Azure Container Apps
        run: |
          az containerapp update \
            --name ignixa-fhir-server \
            --resource-group ignixa-rg \
            --image ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }}
```

### Required GitHub Secrets

| Secret | Description | Example Value |
|--------|-------------|---------------|
| `ACR_USERNAME` | Azure Container Registry username | `ignixa` |
| `ACR_PASSWORD` | ACR password/token | `***` |
| `AZURE_CREDENTIALS` | Service Principal JSON | `{"clientId":"...","clientSecret":"...","subscriptionId":"...","tenantId":"..."}` |

**Create Service Principal**:
```bash
az ad sp create-for-rbac --name "ignixa-github-actions" \
  --role contributor \
  --scopes /subscriptions/{subscription-id}/resourceGroups/ignixa-rg \
  --sdk-auth
```

## Infrastructure as Code (Bicep)

### Bicep Template Structure

**File**: `deploy/azure-container-apps.bicep`

```bicep
param location string = resourceGroup().location
param environmentName string = 'ignixa-env'
param containerAppName string = 'ignixa-fhir-server'
param sqlServerName string = 'ignixa-sql'
param sqlDatabaseName string = 'FHIR_R4'

// Azure Container Registry
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'ignixa'
  location: location
  sku: {
    name: 'Basic'
  }
  properties: {
    adminUserEnabled: true
  }
}

// Azure SQL Server
resource sqlServer 'Microsoft.Sql/servers@2023-05-01-preview' = {
  name: sqlServerName
  location: location
  properties: {
    administratorLogin: 'sqladmin'
    administratorLoginPassword: 'SecurePassword123!' // Use Key Vault reference
    version: '12.0'
    publicNetworkAccess: 'Enabled'
  }
}

resource sqlDatabase 'Microsoft.Sql/servers/databases@2023-05-01-preview' = {
  parent: sqlServer
  name: sqlDatabaseName
  location: location
  sku: {
    name: 'S0'
    tier: 'Standard'
  }
}

// Application Insights
resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${containerAppName}-insights'
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    Flow_Type: 'Bluefield'
  }
}

// Container Apps Environment
resource environment 'Microsoft.App/managedEnvironments@2023-05-01' = {
  name: environmentName
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

// Container App
resource containerApp 'Microsoft.App/containerApps@2023-05-01' = {
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto'
        traffic: [
          {
            latestRevision: true
            weight: 100
          }
        ]
      }
      registries: [
        {
          server: acr.properties.loginServer
          username: acr.listCredentials().username
          passwordSecretRef: 'acr-password'
        }
      ]
      secrets: [
        {
          name: 'acr-password'
          value: acr.listCredentials().passwords[0].value
        }
        {
          name: 'app-insights-key'
          value: appInsights.properties.InstrumentationKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: containerAppName
          image: '${acr.properties.loginServer}/ignixa-fhir-server:latest'
          resources: {
            cpu: json('0.5')
            memory: '1Gi'
          }
          env: [
            {
              name: 'ASPNETCORE_ENVIRONMENT'
              value: 'Production'
            }
            {
              name: 'ApplicationInsights__InstrumentationKey'
              secretRef: 'app-insights-key'
            }
            {
              name: 'Tenants__Configurations__0__Storage__ConnectionString'
              value: 'Server=tcp:${sqlServer.properties.fullyQualifiedDomainName},1433;Database=${sqlDatabaseName};Authentication=ActiveDirectoryManagedIdentity;Encrypt=True;'
            }
          ]
          probes: [
            {
              type: 'Liveness'
              httpGet: {
                path: '/health/live'
                port: 8080
              }
              initialDelaySeconds: 10
              periodSeconds: 30
            }
            {
              type: 'Readiness'
              httpGet: {
                path: '/health/ready'
                port: 8080
              }
              initialDelaySeconds: 5
              periodSeconds: 10
            }
          ]
        }
      ]
      scale: {
        minReplicas: 1
        maxReplicas: 10
        rules: [
          {
            name: 'http-rule'
            http: {
              metadata: {
                concurrentRequests: '50'
              }
            }
          }
        ]
      }
    }
  }
}

// Grant Container App managed identity access to SQL Database
resource sqlFirewallRule 'Microsoft.Sql/servers/firewallRules@2023-05-01-preview' = {
  parent: sqlServer
  name: 'AllowAzureServices'
  properties: {
    startIpAddress: '0.0.0.0'
    endIpAddress: '0.0.0.0'
  }
}

output containerAppUrl string = containerApp.properties.configuration.ingress.fqdn
output containerAppPrincipalId string = containerApp.identity.principalId
```

### Deployment Commands

```bash
# Create resource group
az group create --name ignixa-rg --location eastus

# Deploy infrastructure
az deployment group create \
  --resource-group ignixa-rg \
  --template-file deploy/azure-container-apps.bicep \
  --parameters environmentName=ignixa-env

# Grant SQL access to Container App managed identity
PRINCIPAL_ID=$(az containerapp show -n ignixa-fhir-server -g ignixa-rg --query identity.principalId -o tsv)

az sql server ad-admin create \
  --resource-group ignixa-rg \
  --server-name ignixa-sql \
  --display-name ignixa-fhir-server \
  --object-id $PRINCIPAL_ID

# Connect to SQL and create user
sqlcmd -S ignixa-sql.database.windows.net -d FHIR_R4 -G -Q "
  CREATE USER [ignixa-fhir-server] FROM EXTERNAL PROVIDER;
  ALTER ROLE db_datareader ADD MEMBER [ignixa-fhir-server];
  ALTER ROLE db_datawriter ADD MEMBER [ignixa-fhir-server];
  ALTER ROLE db_ddladmin ADD MEMBER [ignixa-fhir-server];
"
```

## Cost Estimation (Azure)

Monthly costs for production FHIR server deployment:

| Resource | SKU/Tier | Estimated Cost | Notes |
|----------|----------|----------------|-------|
| **Azure Container Apps** | 2 vCPU, 4GB RAM @ 50% utilization | $40-80/month | Pay-per-use (vCPU-second, GB-second) |
| **Azure SQL Database** | S0 (10 DTUs) | $15/month | Scale to S1/S2 as needed |
| **Application Insights** | 5GB/month ingestion | $2-10/month | First 5GB free |
| **Azure Container Registry** | Basic | $5/month | 10GB storage included |
| **Log Analytics** | 5GB/month | $0 (free tier) | First 5GB free |
| **Outbound Data Transfer** | Varies | $5-20/month | Depends on API usage |
| **Total** | | **$67-130/month** | Production baseline |

**Cost Optimization Tips**:
- Enable scale-to-zero for dev/test environments (reduce to $0 when idle)
- Use Reserved Capacity for SQL Database (save 30-40%)
- Implement response caching to reduce compute time
- Monitor Application Insights ingestion (limit to critical telemetry)

**Comparison**:
- **Azure App Service (P1v2)**: ~$80/month (always-on, less scalable)
- **Azure Kubernetes Service (AKS)**: ~$200+/month (3 nodes minimum)
- **Azure Container Apps**: **$67-130/month** (best value for FHIR workload)

## Security Considerations

### 1. Managed Identity for Database Access

**Benefits**:
- No passwords stored in configuration
- Automatic credential rotation
- Azure AD audit trail
- Least-privilege access (grant only required SQL roles)

**Setup**:
```csharp
// Automatically uses managed identity when connection string has:
// Authentication=ActiveDirectoryManagedIdentity
services.AddDbContext<FhirDbContext>(options =>
    options.UseSqlServer(Configuration["ConnectionStrings:FhirDatabase"]));
```

### 2. Azure Key Vault Integration

Store sensitive configuration in Key Vault:

```csharp
builder.Configuration.AddAzureKeyVault(
    new Uri($"https://ignixa-keyvault.vault.azure.net/"),
    new DefaultAzureCredential());
```

**Key Vault References in Container App**:
```yaml
env:
  - name: Tenants__Configurations__0__Storage__ConnectionString
    secretRef: sql-connection-string

secrets:
  - name: sql-connection-string
    keyVaultUrl: https://ignixa-keyvault.vault.azure.net/secrets/SqlConnectionString
    identity: system
```

### 3. Network Security

**Options**:
1. **Public ingress with firewall** (simplest):
   - External ingress enabled
   - IP restrictions on Container Apps ingress
   - SQL Database firewall rules

2. **Private VNet integration** (recommended for production):
   - Container Apps deployed to custom VNet
   - Internal ingress only
   - VNet service endpoints for SQL Database
   - Azure Firewall for outbound traffic

3. **Private endpoints** (highest security):
   - SQL Database accessible only via private endpoint
   - No public internet exposure
   - Requires VNet integration

### 4. Container Image Security

**Best Practices**:
- Use official Microsoft base images (regularly patched)
- Scan images with Azure Defender for Containers
- Implement least-privilege user (non-root)
- Enable ACR content trust (image signing)
- Regularly update .NET runtime (monthly security patches)

### 5. HTTPS Enforcement

Container Apps automatically provides:
- Free managed TLS certificate
- HTTPS redirect
- TLS 1.2+ enforcement
- Custom domain support

## Implementation Checklist

### Phase 1: Docker Containerization
- [ ] Create `Dockerfile` in repository root
- [ ] Create `.dockerignore` to optimize build context
- [ ] Add NuGet packages for health checks and observability
- [ ] Implement `DatabaseHealthCheck` class
- [ ] Update `Program.cs` with health check endpoints
- [ ] Create `appsettings.Production.json` for Azure configuration
- [ ] Test Docker build locally: `docker build -t ignixa-fhir .`
- [ ] Test Docker run locally: `docker run -p 8080:8080 ignixa-fhir`

### Phase 2: Azure Infrastructure
- [ ] Create Azure resource group
- [ ] Deploy Bicep template (`deploy/azure-container-apps.bicep`)
- [ ] Create Azure Container Registry
- [ ] Create Azure SQL Database (S0 tier for testing)
- [ ] Create Application Insights workspace
- [ ] Configure managed identity on Container App
- [ ] Grant SQL Database access to managed identity
- [ ] Test SQL connection with managed identity

### Phase 3: CI/CD Setup
- [ ] Create `.github/workflows/docker-build-push.yml`
- [ ] Configure GitHub secrets (ACR credentials, Azure credentials)
- [ ] Create service principal for GitHub Actions
- [ ] Test workflow on feature branch
- [ ] Verify automated deployment to Container Apps
- [ ] Add deployment status badge to README

### Phase 4: Observability
- [ ] Verify health check endpoints (`/health`, `/health/ready`, `/health/live`)
- [ ] Configure Prometheus metrics endpoint (`/healthmetrics`)
- [ ] Set up Application Insights dashboards
- [ ] Create Azure Monitor alerts (CPU > 80%, health check failures)
- [ ] Configure log retention policies
- [ ] Test health probes in Container Apps

### Phase 5: Production Hardening
- [ ] Enable HTTPS-only ingress
- [ ] Configure custom domain (optional)
- [ ] Implement rate limiting middleware
- [ ] Add CORS policies for allowed origins
- [ ] Enable Azure Defender for Containers
- [ ] Configure backup strategy for SQL Database
- [ ] Document runbook for incident response

## References

### Official Documentation
- [Azure Container Apps Documentation](https://learn.microsoft.com/en-us/azure/container-apps/)
- [.NET on Azure Container Apps](https://learn.microsoft.com/en-us/azure/container-apps/dotnet-overview)
- [Multi-Stage Docker Builds for .NET](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/docker/building-net-docker-images)
- [Managed Identities with SQL Database](https://learn.microsoft.com/en-us/azure/app-service/tutorial-connect-msi-sql-database)
- [ASP.NET Core Health Checks](https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/health-checks)

### Best Practices
- [Azure Well-Architected Framework: Container Apps](https://learn.microsoft.com/en-us/azure/well-architected/service-guides/azure-container-apps)
- [Container Image Security Best Practices](https://learn.microsoft.com/en-us/azure/container-instances/container-instances-best-practices-and-considerations)
- [Prometheus .NET Metrics](https://github.com/prometheus-net/prometheus-net)
- [Application Insights for ASP.NET Core](https://learn.microsoft.com/en-us/azure/azure-monitor/app/asp-net-core)

### Community Resources
- [Azure Container Apps GitHub](https://github.com/microsoft/azure-container-apps)
- [.NET Docker Samples](https://github.com/dotnet/dotnet-docker)
- [Azure Container Apps Best Practices (Trend Micro)](https://www.trendmicro.com/cloudoneconformity/knowledge-base/azure/ContainerApps/)

## Next Steps

1. **Prototype**: Build Docker image locally and test with local SQL Server
2. **Deploy to Dev**: Create dev environment in Azure Container Apps
3. **Load Testing**: Use Apache Bench or k6 to test autoscaling behavior
4. **Production Deployment**: Follow hardening checklist before production release
5. **Monitoring**: Set up dashboards and alerts in Azure Monitor

## Decision Log

| Date | Decision | Rationale |
|------|----------|-----------|
| 2025-10-28 | Use Azure SQL Database with Managed Identity | Passwordless authentication, production-ready, aligns with existing SqlEntityFramework DataLayer |
| 2025-10-28 | Implement health checks, App Insights, Prometheus | Comprehensive observability required for production reliability |
| 2025-10-28 | GitHub Actions for CI/CD | Automated deployments, already using GitHub for version control |
| 2025-10-28 | Debian base image (not Alpine) | Maximum compatibility with EF Core and SQL Server drivers |

---

**Status**: Investigation complete. Ready for implementation when approved.
