# Ignixa.Api.OpenIddict

Embedded OpenIddict OAuth 2.0 server for Ignixa FHIR Server. Provides SMART on FHIR authentication for development and self-hosted scenarios.

## Why Use This Package?

- **Embedded auth server**: No external IdP required for dev/self-hosted
- **SMART on FHIR v2**: Full scope support including CRUDS permissions
- **Multi-version FHIR**: Scopes generated from R4, R4B, R5, R6 schema providers
- **Zero config start**: In-memory storage with pre-seeded clients

## Installation

Add project reference to your web host:

```xml
<ProjectReference Include="..\Ignixa.Api.OpenIddict\Ignixa.Api.OpenIddict.csproj" />
```

## Quick Start

### Program.cs

```csharp
using Ignixa.Api.OpenIddict.Extensions;

// Add services
builder.Services.AddIgnixaOpenIddict(builder.Configuration, schemaProviders);

// Map endpoints
app.MapIgnixaOpenIddictEndpoints();

// Initialize (seeds clients)
await app.Services.InitializeOpenIddictAsync();
```

### appsettings.json

```json
{
  "OpenIddict": {
    "Enabled": true,
    "UseInMemoryStorage": true,
    "DisableHttpsRequirement": true,
    "ClientApplications": [
      {
        "ClientId": "fhir-client",
        "ClientSecret": "fhir-client",
        "GrantTypes": ["client_credentials"],
        "Scopes": ["system/*.cruds"]
      }
    ]
  }
}
```

### Get a Token

```bash
curl -X POST http://localhost:5000/connect/token \
  -d "grant_type=client_credentials" \
  -d "client_id=fhir-client" \
  -d "client_secret=fhir-client" \
  -d "scope=system/*.cruds"
```

## Dependencies

- `Ignixa.Application` - FHIR claim types
- `Ignixa.Specification` - Schema providers for scope generation
- `OpenIddict.AspNetCore` - OAuth 2.0 server
- `OpenIddict.EntityFrameworkCore` - Token storage

## License

MIT License - see LICENSE file in repository root
