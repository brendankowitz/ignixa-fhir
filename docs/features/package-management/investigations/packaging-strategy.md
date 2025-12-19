# Packaging Strategy: Separating Hosting from API Logic

**Status**: Proposal
**Date**: 2025-12-09
**Pattern**: Microsoft FHIR Server `Shared.Web` pattern

## Problem

Currently `Ignixa.Api` is both:
1. A hosting layer (Program.cs, config files)
2. FHIR API logic (endpoints, middleware)

This makes it difficult to:
- Deploy to different environments (AppService, AKS, on-prem)
- Distribute to customers who want to self-host
- Reuse API logic across hosting scenarios

## Solution

Split hosting from API logic following the Microsoft FHIR Server pattern:

```
┌─────────────────────────────────────────────────────────┐
│  Ignixa.Api (NuGet Package - GitHub Feed)               │
│  - All endpoints, middleware, FHIR business logic       │
│  - Published to internal GitHub Packages feed           │
└─────────────────────────────────────────────────────────┘
                          │
                          │ Referenced by
                          │
        ┌─────────────────┴─────────────────┐
        │                                   │
        ▼                                   ▼
┌──────────────────┐              ┌──────────────────────┐
│  Ignixa.Web      │              │ Ignixa.Api.Cloud     │
│  (OSS host)      │              │ (Customer host)      │
│                  │              │                      │
│  - Program.cs    │              │ - Program.cs         │
│  - AppService    │              │ - AKS-specific       │
│  - Docker        │              │ - Custom auth        │
│  - NOT packaged  │              │ - NOT packaged       │
└──────────────────┘              └──────────────────────┘
```

## Project Structure

### After Refactoring

```
src/
├── Ignixa.Web/                        ← NEW: OSS reference host
│   ├── Program.cs                     (moved from Ignixa.Api)
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   ├── launchSettings.json
│   └── Dockerfile                     (updated to reference Ignixa.Api)
│
├── Ignixa.Api/                        ← MODIFIED: Now packaged as NuGet
│   ├── Infrastructure/
│   │   ├── FhirEndpoints.cs          (STAYS - all endpoints)
│   │   ├── SearchEndpoints.cs
│   │   ├── OperationEndpoints.cs
│   │   └── ...
│   ├── Middleware/
│   │   ├── TenantResolutionMiddleware.cs
│   │   ├── FhirExceptionMiddleware.cs
│   │   └── ...
│   ├── Extensions/
│   │   ├── ServiceCollectionExtensions.cs   (NEW)
│   │   └── EndpointRouteBuilderExtensions.cs (NEW)
│   └── Ignixa.Api.csproj
│       └── <IsPackable>true</IsPackable>    (ADDED)
│
└── Core Libraries/ (existing)
    ├── Ignixa.Abstractions
    ├── Ignixa.Domain
    ├── Ignixa.Serialization
    └── ...
```

### Customer Host Example (Not in this repo)

```
Ignixa.Api.Cloud/
├── Program.cs
├── appsettings.Production.json
├── Dockerfile
└── Ignixa.Api.Cloud.csproj
    └── <PackageReference Include="Ignixa.Api" Version="1.0.0" />
```

## Package Distribution Strategy

### Public NuGet (nuget.org)

**Core packages** (`src/Core/`) - foundational libraries with no server-specific implementation:

- `Ignixa.Abstractions` - Core interfaces, FhirVersion enum
- `Ignixa.Serialization` - JsonNode serialization
- `Ignixa.FhirPath` - FHIRPath evaluation engine
- `Ignixa.FhirMappingLanguage` - FML parser
- `Ignixa.Validation` - FHIR validation tiers
- `Ignixa.Search` - Search parameter definitions and indexing
- `Ignixa.Specification` - Generated structure providers
- `Ignixa.PackageManagement` - FHIR package loader
- `Ignixa.SqlOnFhir` - SQL on FHIR ViewDefinition support
- `Ignixa.SqlOnFhir.Writers` - SQL on FHIR output writers
- `Ignixa.FhirFakes` - Test fakes for FHIR resources

**Rationale**: These are general-purpose FHIR libraries useful to the broader .NET/FHIR community. Everything in `src/Core/` is designed to be reusable outside of the Ignixa server.

### Internal GitHub Packages Feed

**Application packages** (`src/Application/`, `src/DataLayer/`) - FHIR server implementation:

- `Ignixa.Api` - Web endpoints, middleware
- `Ignixa.Application` - MediatR handlers, business logic
- `Ignixa.Application.BackgroundOperations` - DurableTask orchestrations
- `Ignixa.Application.Operations` - FHIR operations ($everything, $validate, etc.)
- `Ignixa.Domain` - Domain models, repository interfaces
- `Ignixa.DataLayer.SqlEntityFramework` - Entity Framework implementation
- `Ignixa.DataLayer.FileSystem` - File system storage
- `Ignixa.DataLayer.BlobStorage` - Azure Blob storage
- `Ignixa.DataLayer.InMemoryIndex` - In-memory search index

**Rationale**: These contain proprietary FHIR server implementation details specific to Ignixa.

### Not Packaged

**Hosting layers** - each environment gets its own:

- `Ignixa.Web` - OSS reference host (ships as source/Docker)
- Customer hosts (e.g., `Ignixa.Api.Cloud`) - customer-owned

## Implementation

### Step 1: Create Extension Methods in Ignixa.Api

```csharp
// src/Ignixa.Api/Extensions/ServiceCollectionExtensions.cs
namespace Ignixa.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIgnixaApi(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add application layer
        services.AddIgnixaApplication();

        // Add data layer
        services.AddIgnixaDataLayer(configuration);

        // Add middleware dependencies
        services.AddSingleton<TenantResolutionMiddleware>();
        services.AddSingleton<FhirExceptionMiddleware>();

        // Add web infrastructure
        services.AddCors();
        services.AddRouting(options => options.LowercaseUrls = true);

        return services;
    }
}

// src/Ignixa.Api/Extensions/ApplicationBuilderExtensions.cs
namespace Ignixa.Api.Extensions;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseIgnixaApi(this IApplicationBuilder app)
    {
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<FhirExceptionMiddleware>();
        app.UseRouting();
        return app;
    }
}

// src/Ignixa.Api/Extensions/EndpointRouteBuilderExtensions.cs
namespace Ignixa.Api.Extensions;

public static class EndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapIgnixaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapFhirEndpoints();
        endpoints.MapSearchEndpoints();
        endpoints.MapOperationEndpoints();
        endpoints.MapBundleEndpoints();
        endpoints.MapPatchEndpoints();
        return endpoints;
    }
}
```

### Step 2: Create Ignixa.Web Project

```bash
dotnet new web -n Ignixa.Web -o src/Ignixa.Web
dotnet sln All.sln add src/Ignixa.Web/Ignixa.Web.csproj
```

```xml
<!-- src/Ignixa.Web/Ignixa.Web.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
    <DockerDefaultTargetOS>Linux</DockerDefaultTargetOS>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ignixa.Api\Ignixa.Api.csproj" />
  </ItemGroup>
</Project>
```

### Step 3: Move Program.cs and Config

```bash
# Move Program.cs
git mv src/Ignixa.Api/Program.cs src/Ignixa.Web/Program.cs

# Move config files
git mv src/Ignixa.Api/appsettings.json src/Ignixa.Web/appsettings.json
git mv src/Ignixa.Api/appsettings.Development.json src/Ignixa.Web/appsettings.Development.json
git mv src/Ignixa.Api/Properties/launchSettings.json src/Ignixa.Web/Properties/launchSettings.json
```

Update `Ignixa.Web/Program.cs` to use extension methods:

```csharp
using Ignixa.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Ignixa API services
builder.Services.AddIgnixaApi(builder.Configuration);

var app = builder.Build();

// Configure pipeline
app.UseIgnixaApi();

// Map endpoints
app.MapIgnixaEndpoints();

app.Run();
```

### Step 4: Update Ignixa.Api.csproj

```xml
<!-- src/Ignixa.Api/Ignixa.Api.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- NEW: Enable packaging -->
    <IsPackable>true</IsPackable>
    <PackageId>Ignixa.Api</PackageId>
    <Version>1.0.0</Version>
    <Authors>Ignixa Team</Authors>
    <Description>FHIR API endpoints and middleware for Ignixa FHIR Server</Description>
    <PackageTags>FHIR;HL7;Healthcare;API</PackageTags>
  </PropertyGroup>

  <!-- Existing references stay the same -->
  <ItemGroup>
    <ProjectReference Include="..\Ignixa.Application\Ignixa.Application.csproj" />
    <ProjectReference Include="..\Ignixa.Domain\Ignixa.Domain.csproj" />
    <!-- ... -->
  </ItemGroup>
</Project>
```

### Step 5: Update Docker Files

**Ignixa.Web/Dockerfile** (new):

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Ignixa.Web/Ignixa.Web.csproj", "src/Ignixa.Web/"]
COPY ["src/Ignixa.Api/Ignixa.Api.csproj", "src/Ignixa.Api/"]
COPY ["src/Ignixa.Application/Ignixa.Application.csproj", "src/Ignixa.Application/"]
# ... other dependencies

RUN dotnet restore "src/Ignixa.Web/Ignixa.Web.csproj"
COPY . .
WORKDIR "/src/src/Ignixa.Web"
RUN dotnet build "Ignixa.Web.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Ignixa.Web.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Ignixa.Web.dll"]
```

**docker-compose.yml** (update):

```yaml
services:
  ignixa-web:
    build:
      context: .
      dockerfile: src/Ignixa.Web/Dockerfile
    ports:
      - "5000:8080"
    environment:
      - ASPNETCORE_ENVIRONMENT=Development
      - ConnectionStrings__Ignixa=Host=postgres;Database=ignixa;Username=postgres;Password=postgres
    depends_on:
      - postgres
```

### Step 6: Update Test Projects

```xml
<!-- test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj -->
<ItemGroup>
  <!-- Change from Ignixa.Api to Ignixa.Web -->
  <ProjectReference Include="..\..\src\Ignixa.Web\Ignixa.Web.csproj" />
</ItemGroup>
```

Update test fixtures to use `Ignixa.Web` assembly:

```csharp
// test/Ignixa.Api.E2ETests/Infrastructure/WebApplicationFactoryFixture.cs
public class WebApplicationFactoryFixture : WebApplicationFactory<Ignixa.Web.Program>
{
    // ...
}
```

### Step 7: Update CI/CD for Package Publishing

**.github/workflows/publish-packages.yml** (new):

```yaml
name: Publish Packages

on:
  push:
    tags:
      - 'v*'

jobs:
  publish-core:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Pack Core Packages
        run: |
          dotnet pack src/Core/Ignixa.Abstractions -c Release
          dotnet pack src/Core/Ignixa.Serialization -c Release
          dotnet pack src/Core/Ignixa.FhirPath -c Release
          dotnet pack src/Core/Ignixa.FhirMappingLanguage -c Release
          dotnet pack src/Core/Ignixa.Validation -c Release
          dotnet pack src/Core/Ignixa.Search -c Release
          dotnet pack src/Core/Ignixa.Specification -c Release
          dotnet pack src/Core/Ignixa.PackageManagement -c Release
          dotnet pack src/Core/Ignixa.SqlOnFhir -c Release
          dotnet pack src/Core/Ignixa.SqlOnFhir.Writers -c Release
          dotnet pack src/Core/Ignixa.FhirFakes -c Release

      - name: Push to NuGet.org
        run: dotnet nuget push "**/*.nupkg" --api-key ${{ secrets.NUGET_API_KEY }} --source https://api.nuget.org/v3/index.json

  publish-internal:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '9.0.x'

      - name: Pack Application Packages
        run: |
          dotnet pack src/Application/Ignixa.Api -c Release
          dotnet pack src/Application/Ignixa.Application -c Release
          dotnet pack src/Application/Ignixa.Application.BackgroundOperations -c Release
          dotnet pack src/Application/Ignixa.Application.Operations -c Release
          dotnet pack src/Application/Ignixa.Domain -c Release
          dotnet pack src/DataLayer/Ignixa.DataLayer.SqlEntityFramework -c Release
          dotnet pack src/DataLayer/Ignixa.DataLayer.FileSystem -c Release
          dotnet pack src/DataLayer/Ignixa.DataLayer.BlobStorage -c Release
          dotnet pack src/DataLayer/Ignixa.DataLayer.InMemoryIndex -c Release

      - name: Push to GitHub Packages
        run: dotnet nuget push "**/*.nupkg" --api-key ${{ secrets.GITHUB_TOKEN }} --source "https://nuget.pkg.github.com/${{ github.repository_owner }}/index.json"
```

## Package README Files

Internal packages should include README.md files for documentation. Example for `Ignixa.Api`:

**src/Application/Ignixa.Api/README.md:**

```markdown
# Ignixa.Api

FHIR API endpoints and middleware for Ignixa FHIR Server.

## Installation

```bash
dotnet add package Ignixa.Api
```

## Quick Start

```csharp
using Ignixa.Api.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add Ignixa API services
builder.Services.AddIgnixaApi(builder.Configuration);

var app = builder.Build();

// Configure middleware pipeline
app.UseIgnixaApi();

// Map FHIR endpoints
app.MapIgnixaEndpoints();

app.Run();
```

## Configuration

Required configuration in `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "Ignixa": "Host=localhost;Database=ignixa;Username=postgres;Password=postgres"
  },
  "Ignixa": {
    "MultiTenant": false,
    "DefaultFhirVersion": "R4"
  }
}
```

## Extension Methods

### `AddIgnixaApi(IConfiguration)`

Registers all FHIR server services including:
- Application layer (MediatR handlers)
- Data layer (repositories, Entity Framework)
- Middleware (tenant resolution, exception handling)
- CORS and routing

### `UseIgnixaApi()`

Configures the middleware pipeline:
- Tenant resolution middleware
- FHIR exception handling middleware
- Routing

### `MapIgnixaEndpoints()`

Maps all FHIR endpoints:
- `/[resourceType]` - CRUD operations
- `/[resourceType]/_search` - Search
- `/$[operation]` - FHIR operations
- `/Bundle` - Bundle processing
- `/_history` - Resource history

## Multi-Tenant Support

For multi-tenant deployments, set `"MultiTenant": true` in configuration. Routes will require tenant ID:

```
/tenant/{tenantId}/Patient/123
```

Note: Partition 0 is reserved for system use and cannot be accessed via API.

## Customization

You can add custom services before or after `AddIgnixaApi()`:

```csharp
// Custom services before
builder.Services.AddApplicationInsightsTelemetry();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Core Ignixa
builder.Services.AddIgnixaApi(builder.Configuration);

// Custom middleware before MapIgnixaEndpoints
app.UseAuthentication();
app.UseAuthorization();

app.MapIgnixaEndpoints();
app.MapHealthChecks("/health");
```

## Support

For issues and questions, see [GitHub Issues](https://github.com/ignixa/fhir-server-contrib/issues).
```

**Project file reference:**

```xml
<!-- src/Application/Ignixa.Api/Ignixa.Api.csproj -->
<ItemGroup>
  <None Include="README.md" Pack="true" PackagePath="\" />
</ItemGroup>
```

## Customer Usage Example

A customer creating `Ignixa.Api.Cloud` for AKS deployment:

```xml
<!-- Ignixa.Api.Cloud/Ignixa.Api.Cloud.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference from GitHub Packages feed -->
    <PackageReference Include="Ignixa.Api" Version="1.0.0" />

    <!-- Customer-specific packages -->
    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.2.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.2.0" />
  </ItemGroup>
</Project>
```

```csharp
// Ignixa.Api.Cloud/Program.cs
using Ignixa.Api.Extensions;
using Microsoft.Identity.Web;

var builder = WebApplication.CreateBuilder(args);

// Core Ignixa functionality
builder.Services.AddIgnixaApi(builder.Configuration);

// Customer-specific: Azure AD authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Customer-specific: Application Insights
builder.Services.AddApplicationInsightsTelemetry();

// Customer-specific: Health checks
builder.Services.AddHealthChecks()
    .AddSqlServer(builder.Configuration.GetConnectionString("Ignixa"))
    .AddCheck<TenantDatabaseHealthCheck>("tenant-db")
    .AddAzureBlobStorage(builder.Configuration.GetConnectionString("Storage"));

var app = builder.Build();

// Core Ignixa middleware
app.UseIgnixaApi();

// Customer-specific middleware
app.UseAuthentication();
app.UseAuthorization();

// Endpoints
app.MapIgnixaEndpoints();
app.MapHealthChecks("/health");

app.Run();
```

## Migration Checklist

- [ ] Create extension methods in `Ignixa.Api/Extensions/`
  - [ ] `ServiceCollectionExtensions.cs` with `AddIgnixaApi()`
  - [ ] `ApplicationBuilderExtensions.cs` with `UseIgnixaApi()`
  - [ ] `EndpointRouteBuilderExtensions.cs` with `MapIgnixaEndpoints()`
- [ ] Create `Ignixa.Web` project
  - [ ] Add to `All.sln`
  - [ ] Set `<IsPackable>false</IsPackable>`
  - [ ] Reference `Ignixa.Api`
- [ ] Move files from `Ignixa.Api` to `Ignixa.Web`
  - [ ] `Program.cs`
  - [ ] `appsettings.json`
  - [ ] `appsettings.Development.json`
  - [ ] `Properties/launchSettings.json`
- [ ] Update `Ignixa.Web/Program.cs` to use extension methods
- [ ] Update `Ignixa.Api.csproj`
  - [ ] Set `<IsPackable>true</IsPackable>`
  - [ ] Add package metadata
  - [ ] Add `<PackageReadmeFile>README.md</PackageReadmeFile>`
- [ ] Add README files to internal packages
  - [ ] `src/Application/Ignixa.Api/README.md` - Getting started guide
  - [ ] `src/Application/Ignixa.Application/README.md` - Handler patterns
  - [ ] `src/Application/Ignixa.Domain/README.md` - Domain model overview
  - [ ] `src/DataLayer/Ignixa.DataLayer.SqlEntityFramework/README.md` - EF setup
- [ ] Update Docker files
  - [ ] Create `Ignixa.Web/Dockerfile`
  - [ ] Update `docker-compose.yml`
- [ ] Update test projects
  - [ ] Change references from `Ignixa.Api` to `Ignixa.Web`
  - [ ] Update `WebApplicationFactory<T>` to use `Ignixa.Web.Program`
- [ ] Add `<IsPackable>true</IsPackable>` to all library projects
- [ ] Create GitHub Actions workflow for package publishing
- [ ] Test build and packaging locally
- [ ] Test E2E tests still work
- [ ] Update repository README with new structure

## Benefits

1. **Clean separation**: Hosting concerns separate from API logic
2. **Customer flexibility**: Easy to create custom hosts (AKS, AWS, on-prem)
3. **Reusability**: API logic packaged once, used everywhere
4. **OSS reference**: `Ignixa.Web` shows how to host the server
5. **Versioning**: Update API package independently of hosting layer
6. **Distribution**: Core libraries public, implementation internal

## Risks & Mitigations

**Risk**: Breaking changes in extension methods
**Mitigation**: Follow semantic versioning, test against `Ignixa.Web`

**Risk**: Customers customize in ways that break FHIR compliance
**Mitigation**: Document required middleware, provide health checks

**Risk**: Complexity in maintaining two hosts (Ignixa.Web + customer hosts)
**Mitigation**: `Ignixa.Web` is the canonical reference, well-tested via E2E suite

## References

- [Microsoft FHIR Server - Shared.Web](https://github.com/microsoft/fhir-server/tree/main/src/Microsoft.Health.Fhir.Shared.Web)
- [Microsoft FHIR Server - R4 Web Host](https://github.com/microsoft/fhir-server/tree/main/src/Microsoft.Health.Fhir.R4.Web)
- [GitHub Packages - Publishing NuGet packages](https://docs.github.com/en/packages/working-with-a-github-packages-registry/working-with-the-nuget-registry)
