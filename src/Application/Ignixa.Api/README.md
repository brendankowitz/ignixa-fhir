# Ignixa.Api

FHIR API endpoints and middleware for Ignixa FHIR Server.

## Overview

`Ignixa.Api` contains all the FHIR endpoints, middleware, and infrastructure needed to build a FHIR server. This package is designed to be consumed by hosting layers (like `Ignixa.Web` for the OSS reference implementation, or customer-specific hosts for Azure/AWS/on-prem deployments).

## Package Distribution

This package is distributed via the internal GitHub Packages feed and contains the proprietary FHIR server implementation specific to Ignixa.

## Key Components

### Endpoints
- FHIR CRUD operations (`FhirEndpoints.cs`)
- Search endpoints
- Operation endpoints ($validate, $everything, etc.)
- Terminology endpoints ($expand, $translate, $subsumes)
- PATCH endpoints (JSON Patch and FHIRPath Patch)
- Compartment search endpoints
- History endpoints (_history)
- Bulk import/export endpoints
- Metadata endpoints (CapabilityStatement)

### Middleware
- `TenantResolutionMiddleware` - Multi-tenant routing
- `FhirExceptionMiddleware` - FHIR-compliant error handling
- `FhirRequestContextMiddleware` - Request context management

### Infrastructure
- DurableTask configuration for background operations
- Startup timing diagnostics
- Background services for index loading and package preloading

## Usage

### In Ignixa.Web (OSS Reference Host)

The `Ignixa.Web` project references `Ignixa.Api` and uses its endpoints directly:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Configure services (from Ignixa.Api)
builder.Services.AddStartupTimingDiagnostics();
// ... additional service registration from Program.cs

var app = builder.Build();

// Use middleware (from Ignixa.Api)
app.UseMiddleware<TenantResolutionMiddleware>();
app.UseMiddleware<FhirExceptionMiddleware>();

// Map endpoints (from Ignixa.Api)
app.MapFhirEndpoints();
app.MapOperationEndpoints();
// ... additional endpoint mapping

app.Run();
```

### In Customer Hosts

Customers can create their own hosting projects and reference `Ignixa.Api`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <ItemGroup>
    <PackageReference Include="Ignixa.Api" Version="1.0.0" />
    <!-- Customer-specific packages -->
    <PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.2.0" />
    <PackageReference Include="Microsoft.Identity.Web" Version="3.2.0" />
  </ItemGroup>
</Project>
```

```csharp
// Custom Program.cs with additional authentication/monitoring
var builder = WebApplication.CreateBuilder(args);

// Customer-specific: Azure AD authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));

// Customer-specific: Application Insights
builder.Services.AddApplicationInsightsTelemetry();

// Core Ignixa services (from Ignixa.Api)
// ... service registration as shown in Ignixa.Web

var app = builder.Build();

// Customer-specific middleware
app.UseAuthentication();
app.UseAuthorization();

// Core Ignixa endpoints
app.MapFhirEndpoints();
// ... endpoint mapping as shown in Ignixa.Web

app.Run();
```

## Dependencies

- `Ignixa.Application` - MediatR handlers and business logic
- `Ignixa.Domain` - Domain models and repository interfaces
- `Ignixa.Application.Operations` - FHIR operations ($everything, $validate, etc.)
- `Ignixa.Application.BackgroundOperations` - DurableTask orchestrations
- `Ignixa.DataLayer.*` - Storage implementations (FileSystem, SqlEntityFramework, BlobStorage)
- Core libraries (Serialization, Specification, Validation, Search, FhirPath, PackageManagement, SqlOnFhir)

## Multi-Tenant Support

The API supports multi-tenant deployments with isolated data stores per tenant. Configure tenants in `appsettings.json`:

```json
{
  "Tenants": {
    "Mode": "Isolated",
    "Configurations": [
      {
        "TenantId": "1",
        "DisplayName": "Clinic A",
        "FhirVersion": "4.0",
        "IsActive": true,
        "Storage": {
          "Type": "SqlEntityFramework",
          "ConnectionString": "Server=...;Database=FhirClinicA;..."
        }
      }
    ]
  }
}
```

Routes will require tenant ID: `/tenant/{tenantId}/Patient/123`

**Note**: Partition 0 is reserved for system use and cannot be accessed via API.

## Background Operations

The API supports bulk $import and $export operations via DurableTask framework:

- **$export** - Export resources to NDJSON or Parquet format in blob storage
- **$import** - Import resources from NDJSON files

Configure DurableTask in `appsettings.json`:

```json
{
  "DurableTask": {
    "Provider": "AzureStorage",
    "AzureStorage": {
      "ConnectionString": "UseDevelopmentStorage=true",
      "TaskHubName": "ignixadev"
    }
  }
}
```

## Support

For issues and questions, see [GitHub Issues](https://github.com/brendankowitz/ignixa-fhir/issues).
