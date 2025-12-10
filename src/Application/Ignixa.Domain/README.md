# Ignixa.Domain

Domain models and repository interfaces for Ignixa FHIR Server. This package defines the contracts that data layer implementations must fulfill.

## Why Use This Package?

- **Repository abstractions**: Define storage contracts without implementation details
- **Multi-tenant support**: Built-in tenant and partition awareness
- **Domain models**: Core types like `ResourceWrapper`, `SearchEntryResult`, `UpdateResult`
- **Exception types**: FHIR-specific exceptions (404, 409, 410)

## Installation

```bash
dotnet add package Ignixa.Domain
```

## Quick Start

### Implementing a Custom Repository

```csharp
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;

public class MyRepository : IFhirRepository
{
    public async ValueTask<SearchEntryResult?> GetAsync(
        ResourceKey key,
        CancellationToken ct = default)
    {
        var resource = await _dataStore.GetResourceAsync(key.ResourceType, key.Id, ct);
        if (resource == null)
            return null;

        return new SearchEntryResult
        {
            ResourceBytes = resource.RawJson,
            ResourceType = key.ResourceType,
            ResourceId = key.Id,
            VersionId = resource.Version,
            LastModified = resource.Timestamp
        };
    }

    public async ValueTask<UpdateResult> CreateOrUpdateAsync(
        ResourceWrapper wrapper,
        CancellationToken ct = default)
    {
        var json = wrapper.Resource.ToJson();
        var bytes = Encoding.UTF8.GetBytes(json);

        var version = await _dataStore.SaveResourceAsync(
            wrapper.ResourceType,
            wrapper.ResourceId,
            bytes,
            ct);

        return new UpdateResult(
            Key: new ResourceKey(wrapper.ResourceType, wrapper.ResourceId, version, null),
            ResourceBytes: bytes,
            LastModified: DateTimeOffset.UtcNow);
    }
}
```

### Using Repository Factory

```csharp
using Ignixa.Domain.Abstractions;

public class MyHandler(IFhirRepositoryFactory repositoryFactory)
{
    public async Task<SearchEntryResult?> GetResourceAsync(
        int tenantId,
        string resourceType,
        string id,
        CancellationToken ct)
    {
        var repository = await repositoryFactory.GetRepositoryAsync(tenantId, ct);
        var key = new ResourceKey(resourceType, id, null, null);
        return await repository.GetAsync(key, ct);
    }
}
```

## Key Interfaces

| Interface | Purpose |
|-----------|---------|
| `IFhirRepository` | CRUD, history, batch operations |
| `IFhirRepositoryFactory` | Multi-tenant repository creation |
| `ISearchService` | Search query execution |
| `ITenantConfigurationStore` | Tenant settings management |

## Key Models

| Model | Purpose |
|-------|---------|
| `ResourceWrapper` | FHIR resource with metadata and search indices |
| `SearchEntryResult` | Search/read result with raw bytes |
| `UpdateResult` | Create/update result with version |
| `ResourceKey` | Resource identifier (type, id, version) |
| `TenantConfiguration` | Tenant settings (FHIR version, validation tier) |

## Dependencies

- `Ignixa.Serialization` - ResourceJsonNode and serialization

## License

MIT License - see LICENSE file in repository root
