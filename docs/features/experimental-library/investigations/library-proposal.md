# Investigation: Experimental Library Architecture

**Feature**: experimental-library
**Status**: Viable
**Created**: 2025-12-18

## Executive Summary

This proposal outlines the creation of a new `Ignixa.Application.Experimental` library to house experimental features in an isolated, self-contained manner. The library will include all necessary components (endpoints, handlers, models, services) and integrate with the main application through a configuration-driven feature flag system.

**Initial Features to Migrate**:
- **MCP Server** - Model Context Protocol integration
- **$transform Operation** - FHIR Mapping Language transformation
- **Terminology Operations** - $expand, $translate, $subsumes

**Default Behavior**: Experimental mode will be **enabled by default** in the Docker image to provide full functionality out of the box.

## Motivation

### Problems with Current Approach

1. **No Clear Experimental Boundary**: Experimental features like MCP, $transform, and terminology operations are currently embedded in `Ignixa.Application` and `Ignixa.Application.Operations`, making it harder to:
   - Identify what's experimental vs. stable
   - Disable experimental features cleanly
   - Manage risk in production environments

2. **Feature Coupling**: Experimental features may have dependencies that aren't needed in production builds:
   - MCP requires `ModelContextProtocol.AspNetCore`
   - $transform requires `Ignixa.FhirMappingLanguage`
   - Terminology requires complex ValueSet expansion infrastructure

3. **Graduation Path Unclear**: No formal process to promote features from experimental to stable

### Goals

- **Isolation**: Self-contained experimental features with minimal coupling to core code
- **Toggle Control**: Configuration-driven enable/disable at both global and per-feature level
- **Production Safety**: Easy to completely disable all experimental features
- **Clear Graduation**: Defined process to promote stable features to main libraries

## Proposed Architecture

### Project Structure

```
src/Application/
├── Ignixa.Application/                    # Core application (unchanged)
├── Ignixa.Application.Operations/         # Core operations: $validate, Patient/$everything
├── Ignixa.Application.BackgroundOperations/  # Background ops (unchanged)
└── Ignixa.Application.Experimental/       # NEW: Experimental features
    ├── Ignixa.Application.Experimental.csproj
    ├── Configuration/
    │   └── ExperimentalOptions.cs
    ├── Features/
    │   ├── Mcp/                           # Moved from Ignixa.Application
    │   │   ├── Tools/
    │   │   │   ├── FhirOperations/
    │   │   │   ├── PackageManagement/
    │   │   │   └── TenantManagement/
    │   │   ├── Authorization/
    │   │   ├── Dtos/
    │   │   └── Endpoints/
    │   ├── Transform/                     # Moved from Ignixa.Application.Operations
    │   │   ├── TransformResourceCommand.cs
    │   │   ├── TransformResourceHandler.cs
    │   │   ├── ConceptMapResolverService.cs
    │   │   ├── FhirPathEvaluatorWithTimeout.cs
    │   │   ├── FhirPathExpressionCache.cs
    │   │   ├── MapRegistryCache.cs
    │   │   ├── StructureMapTransformFeature.cs
    │   │   └── Endpoints/
    │   ├── Terminology/                   # Moved from Ignixa.Application.Operations
    │   │   ├── Expand/
    │   │   │   ├── ExpandValueSetQuery.cs
    │   │   │   └── ExpandValueSetHandler.cs
    │   │   ├── Translate/
    │   │   │   ├── TranslateCodeCommand.cs
    │   │   │   └── TranslateCodeHandler.cs
    │   │   ├── Subsumes/
    │   │   │   ├── SubsumesQuery.cs
    │   │   │   └── SubsumesHandler.cs
    │   │   └── Endpoints/
    │   └── Summary/                       # Future: $summary operation
    │       └── ...
    ├── Infrastructure/
    │   ├── ExperimentalServiceRegistration.cs
    │   └── ExperimentalEndpointRegistration.cs
    └── README.md

test/
└── Ignixa.Application.Experimental.Tests/  # NEW: Experimental tests
    ├── Features/
    │   ├── Mcp/
    │   ├── Transform/
    │   └── Terminology/
    └── Ignixa.Application.Experimental.Tests.csproj
```

### Configuration Schema

```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": {
        "Enabled": true,
        "Transport": "http"
      },
      "Transform": {
        "Enabled": true,
        "TimeoutSeconds": 30
      },
      "Terminology": {
        "Enabled": true,
        "EnableAutoImport": false
      },
      "Summary": {
        "Enabled": false,
        "MaxResources": 1000,
        "AllowedResourceTypes": ["Patient", "Observation", "Condition"]
      }
    }
  }
}
```

### Options Classes

```csharp
// File: Configuration/ExperimentalOptions.cs
namespace Ignixa.Application.Experimental.Configuration;

public class ExperimentalOptions
{
    public const string SectionName = "Experimental";

    /// <summary>
    /// Master switch to enable experimental features.
    /// When false, no experimental features are loaded regardless of individual settings.
    /// Default: true (enabled by default in Docker image)
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Individual feature configurations.
    /// </summary>
    public ExperimentalFeaturesOptions Features { get; set; } = new();
}

public class ExperimentalFeaturesOptions
{
    public McpExperimentalOptions Mcp { get; set; } = new();
    public TransformExperimentalOptions Transform { get; set; } = new();
    public TerminologyExperimentalOptions Terminology { get; set; } = new();
    public SummaryExperimentalOptions Summary { get; set; } = new();
}

public class McpExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public string Transport { get; set; } = "http";
}

public class TransformExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public int TimeoutSeconds { get; set; } = 30;
}

public class TerminologyExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public bool EnableAutoImport { get; set; } = false;
}

public class SummaryExperimentalOptions
{
    public bool Enabled { get; set; } = false;
    public int MaxResources { get; set; } = 1000;
    public ICollection<string> AllowedResourceTypes { get; } = [];
}
```

## Features to Migrate

### 1. MCP Server (Model Context Protocol)

**Current Location**:
| File/Directory | Location |
|----------------|----------|
| MCP Tools & DTOs | `Ignixa.Application/Features/Mcp/` |
| Job Management Tools | `Ignixa.Application.BackgroundOperations/JobManagement/*Tool.cs` |
| Endpoints | `Ignixa.Api/Endpoints/McpEndpoints.cs` |
| Service Registration | `Ignixa.Api/Registrations/BackgroundServicesRegistration.cs` |

**Files to Move** (30 files):
- `Tools/DiagnosticTool.cs`, `Tools/DiagnosticResult.cs`
- `Tools/TenantAwareMcpTool.cs`
- `Tools/FhirOperations/*` (8 files)
- `Tools/PackageManagement/*` (4 files)
- `Tools/TenantManagement/*` (1 file)
- `Authorization/IMcpAuthorizationService.cs`, `McpAuthorizationService.cs`
- `Dtos/*` (10 files)

**Dependencies**:
- `ModelContextProtocol.AspNetCore` (NuGet)
- Core application services (via interfaces)

---

### 2. $transform Operation (FHIR Mapping Language)

**Current Location**:
| File | Location |
|------|----------|
| `TransformResourceCommand.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `TransformResourceHandler.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `ConceptMapResolverService.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `FhirPathEvaluatorWithTimeout.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `FhirPathExpressionCache.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `MapRegistryCache.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| `StructureMapTransformFeature.cs` | `Ignixa.Application.Operations/Features/Transform/` |
| Endpoints | `Ignixa.Api/Endpoints/OperationEndpoints.cs` (partial - $transform routes) |

**Endpoints to Move**:
- `POST /StructureMap/$transform` (type-level)
- `POST /StructureMap/{id}/$transform` (instance-level)
- Tenant-explicit variants: `/tenant/{tenantId}/StructureMap/$transform`

**Dependencies**:
- `Ignixa.FhirMappingLanguage` (project reference)
- `Ignixa.FhirPath` (project reference)

---

### 3. Terminology Operations ($expand, $translate, $subsumes)

**Current Location**:
| Feature | Location |
|---------|----------|
| `ExpandValueSetQuery.cs` | `Ignixa.Application.Operations/Features/Terminology/Expand/` |
| `ExpandValueSetHandler.cs` | `Ignixa.Application.Operations/Features/Terminology/Expand/` |
| `TranslateCodeCommand.cs` | `Ignixa.Application.Operations/Features/Terminology/Translate/` |
| `TranslateCodeHandler.cs` | `Ignixa.Application.Operations/Features/Terminology/Translate/` |
| `SubsumesQuery.cs` | `Ignixa.Application.Operations/Features/Terminology/Subsumes/` |
| `SubsumesHandler.cs` | `Ignixa.Application.Operations/Features/Terminology/Subsumes/` |
| Endpoints | `Ignixa.Api/Endpoints/TerminologyEndpoints.cs` |

**Endpoints to Move**:
- `GET /ValueSet/$expand`
- `POST /ConceptMap/$translate`
- `POST /CodeSystem/$subsumes`
- All tenant-explicit variants

**Dependencies**:
- Terminology services in `Ignixa.Validation`
- ValueSet expansion infrastructure

---

### 4. What Stays in Core Operations

The following operations remain in `Ignixa.Application.Operations` as they are considered stable:

| Operation | Reason |
|-----------|--------|
| `$validate` | Core FHIR compliance requirement |
| `Patient/$everything` | Standard operation, widely used |

## Integration Points

### 1. Service Registration (IServiceCollection)

```csharp
// File: Ignixa.Application.Experimental/Infrastructure/ExperimentalServicesRegistration.cs
namespace Ignixa.Application.Experimental.Infrastructure;

public static class ExperimentalServicesRegistration
{
    public static IServiceCollection AddExperimentalServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check
        if (!options.Enabled)
        {
            return services;
        }

        // Register options
        services.Configure<ExperimentalOptions>(
            configuration.GetSection(ExperimentalOptions.SectionName));

        // Feature: MCP
        if (options.Features.Mcp.Enabled)
        {
            services.AddExperimentalMcpServices(configuration);
        }

        // Feature: Transform (no IServiceCollection registrations needed)

        // Feature: Terminology
        if (options.Features.Terminology.Enabled && options.Features.Terminology.EnableAutoImport)
        {
            services.AddHostedService<TerminologyImportBootstrapService>();
        }

        return services;
    }

    private static IServiceCollection AddExperimentalMcpServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services
            .AddMcpServer()
            .WithHttpTransport()
            .WithToolsFromAssembly(
                typeof(Ignixa.Application.Experimental.Features.Mcp.Tools.DiagnosticTool).Assembly);

        return services;
    }
}
```

### 2. Autofac Registration (ContainerBuilder)

```csharp
// File: Ignixa.Application.Experimental/Infrastructure/ExperimentalAutofacRegistration.cs
namespace Ignixa.Application.Experimental.Infrastructure;

public static class ExperimentalAutofacRegistration
{
    public static ContainerBuilder RegisterExperimentalServices(
        this ContainerBuilder builder,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check
        if (!options.Enabled)
        {
            return builder;
        }

        // Feature: MCP
        if (options.Features.Mcp.Enabled)
        {
            builder.RegisterMcpHandlers();
        }

        // Feature: Transform
        if (options.Features.Transform.Enabled)
        {
            builder.RegisterTransformHandlers();
        }

        // Feature: Terminology
        if (options.Features.Terminology.Enabled)
        {
            builder.RegisterTerminologyHandlers();
        }

        return builder;
    }

    private static void RegisterMcpHandlers(this ContainerBuilder builder)
    {
        builder.RegisterType<McpAuthorizationService>()
            .As<IMcpAuthorizationService>()
            .InstancePerLifetimeScope();
    }

    private static void RegisterTransformHandlers(this ContainerBuilder builder)
    {
        builder.RegisterType<TransformResourceHandler>()
            .As<IRequestHandler<TransformResourceCommand, ResourceJsonNode>>()
            .InstancePerLifetimeScope();

        builder.RegisterType<ConceptMapResolverService>()
            .AsSelf()
            .InstancePerLifetimeScope();

        builder.RegisterType<MapRegistryCache>()
            .AsSelf()
            .SingleInstance();

        builder.RegisterType<FhirPathExpressionCache>()
            .AsSelf()
            .SingleInstance();
    }

    private static void RegisterTerminologyHandlers(this ContainerBuilder builder)
    {
        builder.RegisterType<ExpandValueSetHandler>()
            .As<IRequestHandler<ExpandValueSetQuery, ExpandValueSetResult>>()
            .InstancePerDependency();

        builder.RegisterType<TranslateCodeHandler>()
            .As<IRequestHandler<TranslateCodeCommand, TranslateCodeResult>>()
            .InstancePerDependency();

        builder.RegisterType<SubsumesHandler>()
            .As<IRequestHandler<SubsumesQuery, SubsumesQueryResult>>()
            .InstancePerDependency();
    }
}
```

### 3. Endpoint Registration

```csharp
// File: Ignixa.Application.Experimental/Infrastructure/ExperimentalEndpointExtensions.cs
namespace Ignixa.Application.Experimental.Infrastructure;

public static class ExperimentalEndpointExtensions
{
    public static WebApplication MapExperimentalEndpoints(
        this WebApplication app,
        IConfiguration configuration)
    {
        var options = configuration
            .GetSection(ExperimentalOptions.SectionName)
            .Get<ExperimentalOptions>() ?? new ExperimentalOptions();

        // Master switch check
        if (!options.Enabled)
        {
            return app;
        }

        // Feature: MCP
        if (options.Features.Mcp.Enabled)
        {
            app.MapMcpEndpoints();
        }

        // Feature: Transform
        if (options.Features.Transform.Enabled)
        {
            app.MapTransformEndpoints();
        }

        // Feature: Terminology
        if (options.Features.Terminology.Enabled)
        {
            app.MapTerminologyEndpoints();
        }

        return app;
    }
}
```

### 4. Program.cs Integration

```csharp
// In Ignixa.Web/Program.cs

// Service registration
builder.Services.AddIgnixaApi(builder.Configuration, builder.Environment);
builder.Services.AddExperimentalServices(builder.Configuration);  // NEW

// Autofac registration
builder.Host.ConfigureContainer<ContainerBuilder>(containerBuilder =>
{
    containerBuilder.RegisterIgnixaServices(
        builder.Configuration,
        builder.Environment.EnvironmentName);
    containerBuilder.RegisterExperimentalServices(builder.Configuration);  // NEW
});

// Endpoint registration
app.MapIgnixaEndpoints(builder.Configuration);
app.MapExperimentalEndpoints(builder.Configuration);  // NEW
```

## Project File

The project follows the same structure as `Ignixa.Application.csproj`, inheriting common packaging configuration from `Directory.Build.props` (NuGet metadata, versioning, source link, reproducible builds).

```xml
<!-- Ignixa.Application.Experimental.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <ItemGroup>
    <!-- Core dependencies - reference Application for shared infrastructure -->
    <ProjectReference Include="..\Ignixa.Application\Ignixa.Application.csproj" />
    <ProjectReference Include="..\Ignixa.Domain\Ignixa.Domain.csproj" />

    <!-- Transform dependencies -->
    <ProjectReference Include="..\..\Core\Ignixa.FhirMappingLanguage\Ignixa.FhirMappingLanguage.csproj" />
    <ProjectReference Include="..\..\Core\Ignixa.FhirPath\Ignixa.FhirPath.csproj" />
    <ProjectReference Include="..\..\Core\Ignixa.Validation\Ignixa.Validation.csproj" />
  </ItemGroup>

  <ItemGroup>
    <!-- MCP dependencies -->
    <PackageReference Include="ModelContextProtocol.AspNetCore" />

    <!-- Standard dependencies (versions from Directory.Packages.props) -->
    <PackageReference Include="Medino" />
    <PackageReference Include="Microsoft.AspNetCore.Http" />
    <PackageReference Include="Microsoft.AspNetCore.Http.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Configuration.Binder" />
    <PackageReference Include="Microsoft.Extensions.Hosting.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  </ItemGroup>

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>

</Project>
```

**Note**: Package versions are centrally managed via `Directory.Packages.props`. Add a `README.md` to the project folder for NuGet packaging (auto-included by `Directory.Build.props`).

## Test Project File

```xml
<!-- Ignixa.Application.Experimental.Tests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <IsPackable>false</IsPackable>
    <!-- Suppress test-specific code analysis warnings -->
    <NoWarn>$(NoWarn);CA1707;CA1849;CA2000;CA2012;CS8604;CS8625</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="coverlet.collector" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="NSubstitute" />
    <PackageReference Include="Shouldly" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Application\Ignixa.Application.Experimental\Ignixa.Application.Experimental.csproj" />
    <ProjectReference Include="..\..\src\Core\Ignixa.Specification\Ignixa.Specification.csproj" />
  </ItemGroup>

</Project>
```

## Migration Checklist

### Phase 1: Create Project Structure
- [ ] Create `Ignixa.Application.Experimental.csproj`
- [ ] Create directory structure
- [ ] Create `ExperimentalOptions.cs` and related config classes
- [ ] Create registration infrastructure
- [ ] Add project reference to `Ignixa.Api`
- [ ] Add to `All.sln`

### Phase 2: Migrate MCP
- [ ] Move `Features/Mcp/` from `Ignixa.Application`
- [ ] Move `McpEndpoints.cs`
- [ ] Update service registrations
- [ ] Update namespace references
- [ ] Move MCP tests
- [ ] Verify MCP works with `Experimental:Features:Mcp:Enabled = true`

### Phase 3: Migrate Transform
- [ ] Move `Features/Transform/` from `Ignixa.Application.Operations`
- [ ] Extract `$transform` endpoints from `OperationEndpoints.cs`
- [ ] Create `TransformEndpoints.cs` in experimental
- [ ] Update handler registrations
- [ ] Move transform tests
- [ ] Verify transform works

### Phase 4: Migrate Terminology
- [ ] Move `Features/Terminology/` from `Ignixa.Application.Operations`
- [ ] Move `TerminologyEndpoints.cs`
- [ ] Update handler registrations
- [ ] Move terminology tests
- [ ] Verify terminology operations work

### Phase 5: Cleanup & Documentation
- [ ] Remove old code from source projects
- [ ] Update `CLAUDE.md` with experimental info
- [ ] Add README.md to experimental project
- [ ] Verify all tests pass
- [ ] Verify Docker image works with default config

## Advantages

| Benefit | Description |
|---------|-------------|
| **Isolation** | Experimental features don't pollute stable codebase |
| **Safety** | Master switch instantly disables all experimental features |
| **Flexibility** | Per-feature toggles allow granular control |
| **Testing** | Separate test project for experimental code |
| **Documentation** | Clear experimental status visible in project structure |
| **Graduation** | Defined path to promote features to stable |
| **Dependencies** | Experimental-only packages isolated from core |

## Risks and Mitigations

| Risk | Mitigation |
|------|------------|
| **Dependency on core changes** | Keep experimental features loosely coupled; use interfaces |
| **Feature creep in experimental** | Regular reviews; graduation criteria defined |
| **Configuration complexity** | Sensible defaults; experimental enabled by default |
| **Testing gaps** | Require tests for all experimental features |

## Appendix: Configuration Examples

### Default (Docker Image) - All Experimental Features Enabled
```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": { "Enabled": true },
      "Transform": { "Enabled": true },
      "Terminology": { "Enabled": true }
    }
  }
}
```

### Production (Experimental Disabled)
```json
{
  "Experimental": {
    "Enabled": false
  }
}
```

### Production (Selective Experimental)
```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "Mcp": { "Enabled": false },
      "Transform": { "Enabled": true },
      "Terminology": { "Enabled": true }
    }
  }
}
```

## Decision

**Recommended**: Proceed with this proposal to create `Ignixa.Application.Experimental` as a self-contained library for experimental features including MCP, $transform, and terminology operations.
