# Investigation: Event-Sourced Conformance Management

**Feature**: search, packages
**Status**: Viable
**Created**: 2025-12-20
**Original ADR**: [ADR-2512](../../../adr/adr-2512-event-sourced-conformance.md)

## Executive Summary

Replace the current multi-layered cache-based SearchParameter resolution system with an event-sourced architecture. The current implementation involves 6+ classes, ~2000 lines of code, multiple cache dictionaries, race conditions, and silent failure fallbacks. An event-sourced approach reduces this to ~400 lines, provides deterministic conflict resolution, and enables full audit trails.

**Key Innovation**: A single `SourceEvents` table captures all conformance changes. On startup, events replay into an in-memory projection. No complex cache invalidation, no race conditions, no silent failures.

## Problem Statement

### Current Architecture Complexity

The flow from package load to usable SearchParameter traverses:

1. `LoadPackageHandler` - stores resources, publishes `PackageLoadedEvent`
2. `PackageLoadedSearchParameterSyncHandler` - triggers sync to database
3. `FhirVersionContext` - manages per-tenant CompositeSearchParameterDefinitionManager with locks
4. `CompositeSearchParameterDefinitionManager` - loads all package SPs, parses JSON, merges with base FHIR
5. `SearchParameterConflictResolver` - determines which parameter "wins" when codes collide
6. `SearchIndexReferenceDataCache` - syncs to database, handles OverridesUrl aliasing

**Problems:**
- **Race conditions**: Concurrent package loads can create duplicate managers
- **Silent failures**: Sync handler catches exceptions, logs them, continues
- **N+1 queries**: SyncSearchParametersToDatabase loads ALL existing params, then inserts one-by-one
- **Cache invalidation**: 5 different cache dictionaries require manual invalidation
- **Non-deterministic**: Conflict resolution depends on load order

### The Stance: Package-Controlled vs. API-Controlled

| Resource | If Malformed... | Blast Radius | Verdict |
|----------|-----------------|--------------|---------|
| **SearchParameter** | Indexing crashes | Server-wide | **Package-controlled** |
| **StructureDefinition** | Validation breaks | Server-wide | **Package-controlled** |
| **CompartmentDefinition** | Compartment search breaks | Server-wide | **Package-controlled** |
| **CodeSystem** | Terminology lookups fail | Terminology ops | **Package-controlled** |
| **ViewDefinition** | $export fails for that view | One export | **API-controlled** |
| **Consent** | One patient's access wrong | One patient | **API-controlled** |

**Package = "I'm changing how the server works"** - Versioned, explicit activation

**Database = "I'm storing data the server uses"** - CRUD, runtime lookup

## Solution: Events + In-Memory Projection

### Why This Works for Conformance

Event count is small:
- Base FHIR R4: ~1400 SearchParameters
- Typical IG: 50-200 resources
- 5-10 packages per deployment
- **Total: 2000-5000 events max**

That's trivially replayable on startup (<100ms).

### Schema

```sql
CREATE TABLE SourceEvents (
    EventId         BIGINT IDENTITY(1,1) PRIMARY KEY,
    StreamId        NVARCHAR(256) NOT NULL,  -- e.g., "package:hl7.fhir.us.core@6.1.0"
    EventType       NVARCHAR(100) NOT NULL,
    EventData       NVARCHAR(MAX) NOT NULL,
    Timestamp       DATETIMEOFFSET NOT NULL DEFAULT SYSUTCDATETIME(),

    INDEX IX_Stream (StreamId, EventId)
);
```

### Event Types

```csharp
// Package lifecycle
public record PackageUploaded(
    string PackageId,
    string Version,
    string FhirVersion,
    IReadOnlyList<ResourceManifest> Resources);

public record PackageActivated(
    string PackageId,
    string Version,
    IReadOnlyList<ActivatedResource> Resources);

public record PackageDeactivated(
    string PackageId,
    string Version,
    string Reason);

// SearchParameter lifecycle events
public record SearchParameterActivated(
    string Canonical,
    string Code,
    string ResourceType,
    string Expression,
    SearchParamType ParamType,
    string SourcePackage,
    OverrideInfo? Overrides,
    int SearchParamId);  // Explicit ID - shared across all base types for same canonical

public record SearchParameterReindexStarted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    IReadOnlyList<string> AffectedResourceTypes);

public record SearchParameterReindexCompleted(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    long ResourcesIndexed,
    TimeSpan Duration);

public record SearchParameterReindexFailed(
    string Canonical,
    string Code,
    string ResourceType,
    string JobId,
    string ErrorMessage);

public record SearchParameterDeactivated(
    string Canonical,
    string Code,
    string ResourceType,
    string Reason);  // "package_deactivated", "superseded", "manual"

public record SearchParameterDeleted(
    string Canonical,
    string Code,
    string ResourceType,
    string Reason);  // "package_deleted", "manual"

// StructureDefinition lifecycle events
public record StructureDefinitionActivated(
    string Canonical,
    string Type,
    string Kind,
    string SourcePackage,
    string SnapshotJson);

public record StructureDefinitionDeactivated(
    string Canonical,
    string Reason);

// Supporting types
public record OverrideInfo(string OverridesCanonical, int InheritedParamId);
public record ResourceManifest(string ResourceType, string Canonical, string ContentHash);
public record ActivatedResource(string ResourceType, string Canonical);
```

### SearchParameter States

A SearchParameter transitions through these states based on events:

```
                    ┌─────────────────────────────────────────┐
                    │                                         │
                    ▼                                         │
┌──────────┐   Activated   ┌───────────┐  ReindexStarted  ┌───────────┐
│ (none)   │ ─────────────►│  Pending  │ ────────────────►│ Reindexing│
└──────────┘               └───────────┘                  └───────────┘
                                 │                              │
                                 │ (base FHIR -                 │ ReindexCompleted
                                 │  no reindex needed)          │
                                 │                              ▼
                                 │                        ┌───────────┐
                                 └───────────────────────►│  Enabled  │
                                                          └───────────┘
                                                                │
                                          Deactivated/Deleted   │
                                                                ▼
                                                          ┌───────────┐
                                                          │ Disabled  │
                                                          └───────────┘
```

```csharp
public enum SearchParameterStatus
{
    Pending,      // Activated but not yet indexed (NOT searchable)
    Reindexing,   // Reindex in progress
    Enabled,      // Fully indexed and active (SEARCHABLE)
    Disabled      // Deactivated or deleted (NOT searchable)
}
```

**State rules:**
- **Pending**: SP activated, awaiting reindex. Searches ignore this SP.
- **Reindexing**: Reindex job running. Searches still ignore (partial index is dangerous).
- **Enabled**: Reindex complete. Searches use this SP.
- **Disabled**: SP deactivated/deleted. Searches ignore, but index data may remain for rollback.

**Base FHIR exception**: SearchParameters from `hl7.fhir.r*.core` packages skip Pending/Reindexing and go directly to Enabled (pre-indexed at resource creation time).

### In-Memory Projection

```csharp
public class ActiveSearchParameter
{
    public required int SearchParamId { get; init; }
    public required string Canonical { get; init; }
    public required string Code { get; init; }
    public required string ResourceType { get; init; }
    public required string Expression { get; init; }
    public required SearchParamType ParamType { get; init; }
    public required string SourcePackage { get; init; }
    public string? OverridesCanonical { get; init; }

    // Lifecycle state
    public SearchParameterStatus Status { get; set; }
    public string? ReindexJobId { get; set; }
}

public class ConformanceState
{
    private readonly Dictionary<(string ResourceType, string Code), ActiveSearchParameter> _searchParameters = new();
    private readonly Dictionary<string, ActiveStructureDefinition> _structureDefinitions = new();
    private readonly Dictionary<string, ActivePackage> _packages = new();
    private readonly Dictionary<string, string> _overrideChain = new(); // canonical -> overrides canonical
    private readonly Dictionary<string, int> _canonicalToParamId = new(); // canonical URL -> SearchParamId

    private int _nextSearchParamId = 1;

    /// <summary>
    /// Allocates or retrieves the SearchParamId for a canonical URL.
    /// Multi-base SPs share the same ID across all base types.
    /// </summary>
    public int GetOrAllocateSearchParamId(string canonical, ActiveSearchParameter? existingOverride)
    {
        // If overriding an existing parameter, inherit its ID
        if (existingOverride != null)
        {
            _canonicalToParamId[canonical] = existingOverride.SearchParamId;
            return existingOverride.SearchParamId;
        }

        // If we've already allocated an ID for this canonical (multi-base SP), reuse it
        if (_canonicalToParamId.TryGetValue(canonical, out var existing))
        {
            return existing;
        }

        // Allocate new ID
        var newId = _nextSearchParamId++;
        _canonicalToParamId[canonical] = newId;
        return newId;
    }

    /// <summary>
    /// Find existing SP by canonical URL (for composite validation).
    /// </summary>
    public ActiveSearchParameter? FindByCanonical(string canonical)
    {
        return _searchParameters.Values.FirstOrDefault(sp => sp.Canonical == canonical);
    }

    /// <summary>
    /// Get search parameter by (ResourceType, Code).
    /// </summary>
    public ActiveSearchParameter? GetSearchParameter(string resourceType, string code)
    {
        return _searchParameters.GetValueOrDefault((resourceType, code));
    }

    // Read-only access - only returns Enabled parameters for search
    public IEnumerable<ActiveSearchParameter> EnabledSearchParameters =>
        _searchParameters.Values.Where(sp => sp.Status == SearchParameterStatus.Enabled);

    // Full access for admin/debugging
    public IReadOnlyDictionary<(string ResourceType, string Code), ActiveSearchParameter> AllSearchParameters => _searchParameters;
    public IReadOnlyDictionary<string, ActiveStructureDefinition> StructureDefinitions => _structureDefinitions;
    public IReadOnlyDictionary<string, ActivePackage> Packages => _packages;

    public ActiveSearchParameter? GetEnabledSearchParameter(string resourceType, string code)
    {
        if (_searchParameters.TryGetValue((resourceType, code), out var sp) &&
            sp.Status == SearchParameterStatus.Enabled)
        {
            return sp;
        }
        return null;
    }

    public static async Task<ConformanceState> RebuildFromEventsAsync(
        ISourceEventStore store,
        CancellationToken ct)
    {
        var state = new ConformanceState();

        await foreach (var evt in store.ReadAllAsync(ct))
        {
            state.Apply(evt);
        }

        return state;
    }

    public void Apply(SourceEvent evt)
    {
        switch (evt.Data)
        {
            case SearchParameterActivated sp:
                // SearchParamId comes from the event (pre-allocated during activation)
                var isBaseFhir = IsBaseFhirPackage(sp.SourcePackage.Split('@')[0]);

                _searchParameters[(sp.ResourceType, sp.Code)] = new ActiveSearchParameter
                {
                    SearchParamId = sp.SearchParamId,
                    Canonical = sp.Canonical,
                    Code = sp.Code,
                    ResourceType = sp.ResourceType,
                    Expression = sp.Expression,
                    ParamType = sp.ParamType,
                    SourcePackage = sp.SourcePackage,
                    OverridesCanonical = sp.Overrides?.OverridesCanonical,
                    // Base FHIR params are immediately Enabled; others start Pending
                    Status = isBaseFhir ? SearchParameterStatus.Enabled : SearchParameterStatus.Pending
                };

                // Track canonical→ID mapping for multi-base consistency
                _canonicalToParamId[sp.Canonical] = sp.SearchParamId;

                // Track next available ID during replay
                if (sp.SearchParamId >= _nextSearchParamId)
                {
                    _nextSearchParamId = sp.SearchParamId + 1;
                }

                if (sp.Overrides != null)
                {
                    _overrideChain[sp.Canonical] = sp.Overrides.OverridesCanonical;
                }
                break;

            case SearchParameterReindexStarted reindex:
                if (_searchParameters.TryGetValue((reindex.ResourceType, reindex.Code), out var spReindex))
                {
                    spReindex.Status = SearchParameterStatus.Reindexing;
                    spReindex.ReindexJobId = reindex.JobId;
                }
                break;

            case SearchParameterReindexCompleted completed:
                if (_searchParameters.TryGetValue((completed.ResourceType, completed.Code), out var spCompleted))
                {
                    spCompleted.Status = SearchParameterStatus.Enabled;
                    spCompleted.ReindexJobId = null;
                }
                break;

            case SearchParameterReindexFailed failed:
                if (_searchParameters.TryGetValue((failed.ResourceType, failed.Code), out var spFailed))
                {
                    // Back to Pending - can retry
                    spFailed.Status = SearchParameterStatus.Pending;
                    spFailed.ReindexJobId = null;
                }
                break;

            case SearchParameterDeactivated deactivated:
                if (_searchParameters.TryGetValue((deactivated.ResourceType, deactivated.Code), out var spDeactivated))
                {
                    spDeactivated.Status = SearchParameterStatus.Disabled;
                }
                break;

            case SearchParameterDeleted deleted:
                _searchParameters.Remove((deleted.ResourceType, deleted.Code));
                break;

            case StructureDefinitionActivated sd:
                _structureDefinitions[sd.Canonical] = new ActiveStructureDefinition
                {
                    Canonical = sd.Canonical,
                    Type = sd.Type,
                    Kind = sd.Kind,
                    SourcePackage = sd.SourcePackage,
                    SnapshotJson = sd.SnapshotJson
                };
                break;

            case StructureDefinitionDeactivated sdDeactivated:
                _structureDefinitions.Remove(sdDeactivated.Canonical);
                break;

            case PackageActivated pa:
                _packages[$"{pa.PackageId}@{pa.Version}"] = new ActivePackage
                {
                    PackageId = pa.PackageId,
                    Version = pa.Version,
                    ResourceCount = pa.Resources.Count,
                    ActivatedAt = evt.Timestamp
                };
                break;

            case PackageDeactivated pd:
                _packages.Remove($"{pd.PackageId}@{pd.Version}");
                DeactivateResourcesFromPackage(pd.PackageId, pd.Version);
                break;
        }
    }

    private void DeactivateResourcesFromPackage(string packageId, string version)
    {
        var packageKey = $"{packageId}@{version}";

        foreach (var sp in _searchParameters.Values.Where(sp => sp.SourcePackage == packageKey))
        {
            sp.Status = SearchParameterStatus.Disabled;
        }

        var sdsToRemove = _structureDefinitions
            .Where(kv => kv.Value.SourcePackage == packageKey)
            .Select(kv => kv.Key)
            .ToList();

        foreach (var key in sdsToRemove)
        {
            _structureDefinitions.Remove(key);
        }
    }

    private static bool IsBaseFhirPackage(string packageId)
    {
        return packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
               packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);
    }
}
```

### Activation Pipeline

Reuses existing `SearchParameterResolutionOptions` for priority configuration:

```json
{
  "SearchParameterResolution": {
    "PackagePriorityOrder": ["hl7.fhir.us.core", "hl7.fhir.au.base"],
    "UseSemanticVersioning": true,
    "LogConflicts": true
  }
}
```

```csharp
public class PackageActivationPipeline(
    IPackageResourceRepository packageRepo,
    ISourceEventStore eventStore,
    ConformanceState state,
    SearchParameterResolutionOptions options,
    IMediator mediator)
{
    public async Task<ActivationResult> ActivateAsync(
        string packageId,
        string version,
        CancellationToken ct)
    {
        // 1. Load package resources
        var resources = await packageRepo.GetResourcesAsync(packageId, version, ct);

        // 2. Validate against current state
        var validation = Validate(resources, state);

        if (!validation.Success)
        {
            return ActivationResult.Failed(validation.Issues);
        }

        // 3. Build and append events atomically
        var events = BuildActivationEvents(packageId, version, resources, state);
        await eventStore.AppendAsync(events, ct);

        // 4. Apply to in-memory state
        foreach (var evt in events)
        {
            state.Apply(evt);
        }

        // 5. Trigger reindex if needed
        var reindexNeeded = DetectReindexRequirements(resources, state);
        if (reindexNeeded.Count > 0)
        {
            await mediator.PublishAsync(new ReindexTriggeredEvent(reindexNeeded), ct);
        }

        return ActivationResult.Success();
    }

    private ValidationResult Validate(PackageResources resources, ConformanceState state)
    {
        var issues = new List<ValidationIssue>();

        foreach (var sp in resources.SearchParameters)
        {
            var existing = state.SearchParameters.GetValueOrDefault((sp.ResourceType, sp.Code));

            if (existing != null && !IsOverride(sp, existing))
            {
                issues.Add(new ValidationIssue(
                    $"SearchParameter '{sp.Code}' on {sp.ResourceType} conflicts with " +
                    $"existing parameter from {existing.SourcePackage}"));
            }
        }

        return new ValidationResult(issues.Count == 0, issues);
    }

    private bool IsOverride(SearchParameterInfo newSp, ActiveSearchParameter existing)
    {
        // Explicit derivedFrom relationship
        if (newSp.DerivedFrom == existing.Canonical)
            return true;

        // Same canonical URL (version update)
        if (newSp.Canonical == existing.Canonical)
            return true;

        // Priority-based: new package has higher priority than existing
        if (HasHigherPriority(newSp.SourcePackageId, existing.SourcePackage.Split('@')[0]))
            return true;

        return false;
    }

    private bool HasHigherPriority(string newPackageId, string existingPackageId)
    {
        // Use existing SearchParameterResolutionOptions for priority
        var newRank = options.GetPriorityRank(newPackageId);
        var existingRank = options.GetPriorityRank(existingPackageId);

        // Lower rank = higher priority
        // If new package has lower rank, it can override
        if (newRank < existingRank)
            return true;

        // If ranks are equal, fall back to semantic versioning
        // (handled separately - this just checks priority)
        return false;
    }

    private static bool IsBaseFhirPackage(string packageId)
    {
        // Base FHIR packages: hl7.fhir.r4.core, hl7.fhir.r5.core, etc.
        return packageId.StartsWith("hl7.fhir.r", StringComparison.OrdinalIgnoreCase) &&
               packageId.EndsWith(".core", StringComparison.OrdinalIgnoreCase);
    }

    private List<SourceEvent> BuildActivationEvents(
        string packageId,
        string version,
        PackageResources resources,
        ConformanceState state)
    {
        var events = new List<SourceEvent>();
        var streamId = $"package:{packageId}@{version}";

        foreach (var sp in resources.SearchParameters)
        {
            var existing = state.SearchParameters.GetValueOrDefault((sp.ResourceType, sp.Code));
            OverrideInfo? overrides = null;

            if (existing != null && IsOverride(sp, existing))
            {
                overrides = new OverrideInfo(existing.Canonical, existing.SearchParamId);
            }

            events.Add(new SourceEvent(
                streamId,
                nameof(SearchParameterActivated),
                new SearchParameterActivated(
                    sp.Canonical, sp.Code, sp.ResourceType, sp.Expression, sp.Type,
                    $"{packageId}@{version}", overrides)));
        }

        foreach (var sd in resources.StructureDefinitions)
        {
            events.Add(new SourceEvent(
                streamId,
                nameof(StructureDefinitionActivated),
                new StructureDefinitionActivated(
                    sd.Canonical, sd.Type, sd.Kind, $"{packageId}@{version}", sd.SnapshotJson)));
        }

        events.Add(new SourceEvent(
            streamId,
            nameof(PackageActivated),
            new PackageActivated(packageId, version,
                resources.All.Select(r => new ActivatedResource(r.Type, r.Canonical)).ToList())));

        return events;
    }
}
```

### Startup Registration

```csharp
public static class ConformanceStateRegistration
{
    public static IServiceCollection AddConformanceState(this IServiceCollection services)
    {
        // Rebuild state once at startup, cache for application lifetime
        services.AddSingleton<ConformanceState>(sp =>
        {
            var store = sp.GetRequiredService<ISourceEventStore>();
            return ConformanceState.RebuildFromEventsAsync(store, CancellationToken.None)
                .GetAwaiter().GetResult();
        });

        return services;
    }
}
```

### Query-Time Resolution

**Before (current):**
```csharp
// 6 classes, multiple caches, locks, lazy loading
var manager = await _context.GetSearchParameterDefinitionManager(version, tenantId);
var sp = manager.GetSearchParameter(resourceType, code);
var paramId = await _referenceCache.GetSearchParamIdAsync(sp.Url);
```

**After:**
```csharp
// Dictionary lookup with status check - O(1)
var sp = _state.GetEnabledSearchParameter(resourceType, code);
if (sp == null)
{
    // Parameter doesn't exist or isn't Enabled (Pending/Reindexing/Disabled)
    return SearchParameterNotFound(code);
}
// sp.SearchParamId is already there, status is guaranteed Enabled
```

**Admin queries (show all states):**
```csharp
// Get all parameters regardless of status (for admin UI)
var allParams = _state.AllSearchParameters.Values;

// Get pending parameters (awaiting reindex)
var pending = allParams.Where(sp => sp.Status == SearchParameterStatus.Pending);

// Get currently reindexing
var reindexing = allParams.Where(sp => sp.Status == SearchParameterStatus.Reindexing);
```

## Override Chain Handling

When US Core's `race` overrides base FHIR's `race`:

```
Event 1: SearchParameterActivated { Code: "race", ResourceType: "Patient",
         Canonical: "http://hl7.org/fhir/SearchParameter/Patient-race",
         SourcePackage: "hl7.fhir.r4.core@4.0.1", Overrides: null }

         -> SearchParamId assigned: 42

Event 2: SearchParameterActivated { Code: "race", ResourceType: "Patient",
         Canonical: "http://hl7.org/fhir/us/core/SearchParameter/us-core-race",
         SourcePackage: "hl7.fhir.us.core@6.1.0",
         Overrides: { OverridesCanonical: "http://hl7.org/fhir/SearchParameter/Patient-race",
                      InheritedParamId: 42 } }

         -> SearchParamId inherited: 42 (existing indexed data still works)
```

Multi-layer override chain:
```
hl7.fhir.r4.core        -> SearchParamId: 42
    ^
hl7.fhir.us.core        -> Inherits 42, overrides base
    ^
customer.us.ext         -> Inherits 42, overrides US Core
```

## Package Loading Options

```csharp
public interface IPackageLoader
{
    // NPM package upload
    Task<LoadResult> LoadFromTgzAsync(Stream tgzStream, CancellationToken ct);

    // Bundle upload (no package structure needed)
    Task<LoadResult> LoadFromBundleAsync(Bundle conformanceBundle, CancellationToken ct);

    // Config-based auto-load
    Task<LoadResult> LoadFromRegistryAsync(string packageId, string version, CancellationToken ct);
}

// Bundle upload assigns synthetic package ID
public async Task<LoadResult> LoadFromBundleAsync(Bundle bundle, CancellationToken ct)
{
    var syntheticId = $"custom:{Guid.NewGuid():N}";
    var version = "1.0.0";

    // Store resources
    await _packageRepo.StoreAsync(syntheticId, version, bundle.Entry.Select(e => e.Resource), ct);

    // Activate
    return await _activationPipeline.ActivateAsync(syntheticId, version, ct);
}
```

## Comparison

| Aspect | Current | Event-Sourced |
|--------|---------|---------------|
| **Classes** | 6+ | 3 |
| **Lines of code** | ~2000 | ~400 |
| **Caches** | 5 dictionaries | 1 in-memory state |
| **Cache invalidation** | Manual, error-prone | Automatic on event apply |
| **Conflict detection** | Runtime, race-prone | Activation-time, atomic |
| **Query complexity** | Multi-layer lookup | Dictionary lookup O(1) |
| **Audit trail** | None | Full event history |
| **Debugging** | "Why is this SP active?" | Query events |
| **Startup** | Complex initialization | Simple replay |

## Code to Delete

After migration:
- `CompositeSearchParameterDefinitionManager` (897 lines)
- `SearchParameterConflictResolver` (364 lines)
- `PackageLoadedSearchParameterSyncHandler` (166 lines)
- `SearchIndexReferenceDataCache` caching logic (~300 lines)
- `FhirVersionContext` manager caching (~200 lines)

**Net: ~1,500 lines removed, ~400 lines added**

## Future: Materialized Tables

If startup time becomes a problem (unlikely with <5000 events), add projection tables:

```sql
CREATE TABLE ActiveSearchParameters (
    SearchParamId         INT PRIMARY KEY,
    Canonical             NVARCHAR(512) NOT NULL,
    Code                  NVARCHAR(100) NOT NULL,
    ResourceType          NVARCHAR(64) NOT NULL,
    -- ...
);
```

Update on each event append. But start simple - the in-memory approach is sufficient for conformance data volumes.

## Multi-Instance Synchronization

Each server tracks its last processed `EventId` and catches up on demand:

```csharp
public class ConformanceState
{
    public long LastProcessedEventId { get; private set; }

    public async Task CatchUpAsync(ISourceEventStore store, CancellationToken ct)
    {
        await foreach (var evt in store.ReadFromAsync(LastProcessedEventId + 1, ct))
        {
            Apply(evt);
            LastProcessedEventId = evt.EventId;
        }
    }
}
```

**Current approach (cache invalidation hell):**
```
Server A loads package -> updates 5 caches -> publishes event
Server B receives event -> which caches to invalidate? -> hope nothing races
Server C missed the event -> stale cache until restart
```

**Event-sourced approach:**
```
Server A activates package -> appends events 101-105
Server B polls: "any events after 100?" -> gets 101-105 -> applies
Server C restarts -> replays from 1 -> same state as A and B
```

No distributed cache invalidation. No pub/sub complexity. Just:
- "What's my last EventId?"
- "Give me everything after that"

Catch-up can be triggered by:
- Periodic timer (every 30s)
- Before processing a request that needs conformance data
- After a package operation completes

## Multi-Base SearchParameter Handling

SearchParameters like `identifier` apply to multiple resource types (Patient, Organization, Practitioner, etc.). The approach:

1. **Emit one event per base type**, sharing the same SearchParamId across all
2. **Expression must match** - if an override has a different expression, reject it (it's a new version, not an override)

```csharp
public class MultiBaseSearchParameterHandler
{
    private readonly Dictionary<string, int> _canonicalToParamId = new();

    public IEnumerable<SearchParameterActivated> EmitEventsForMultiBaseParameter(
        SearchParameterInfo sp,
        string packageKey,
        ConformanceState state)
    {
        // Allocate or reuse SearchParamId for this canonical
        if (!_canonicalToParamId.TryGetValue(sp.Canonical, out var paramId))
        {
            // Check if we're overriding an existing parameter
            var existing = FindExistingByCode(sp, state);
            paramId = existing?.SearchParamId ?? state.AllocateNextSearchParamId();
            _canonicalToParamId[sp.Canonical] = paramId;
        }

        // Emit one event per base type, all sharing the same SearchParamId
        foreach (var resourceType in sp.BaseResourceTypes)
        {
            var existing = state.GetSearchParameter(resourceType, sp.Code);
            OverrideInfo? overrides = null;

            if (existing != null)
            {
                // Validate override - expression must match
                var validationResult = ValidateOverride(sp, existing, resourceType);
                if (!validationResult.IsValid)
                {
                    throw new ActivationException(validationResult.Error);
                }

                overrides = new OverrideInfo(existing.Canonical, existing.SearchParamId);
            }

            yield return new SearchParameterActivated(
                Canonical: sp.Canonical,
                Code: sp.Code,
                ResourceType: resourceType,
                Expression: sp.Expression,
                ParamType: sp.Type,
                SourcePackage: packageKey,
                Overrides: overrides,
                SearchParamId: paramId);  // Shared across all base types for same canonical
        }
    }

    private OverrideValidation ValidateOverride(
        SearchParameterInfo newSp,
        ActiveSearchParameter existing,
        string resourceType)
    {
        // Same canonical = version update (always allowed)
        if (newSp.Canonical == existing.Canonical)
        {
            return OverrideValidation.Valid();
        }

        // Different canonical = override via priority or derivedFrom
        // Expression MUST match, or it's a semantic change requiring a new version
        if (!ExpressionsMatch(newSp.Expression, existing.Expression))
        {
            return OverrideValidation.Invalid(
                $"SearchParameter '{newSp.Code}' on {resourceType}: " +
                $"Cannot override {existing.Canonical} with {newSp.Canonical} - " +
                $"expressions differ. This is a semantic change requiring a new " +
                $"SearchParameter URL, not an override. " +
                $"Existing: '{existing.Expression}', New: '{newSp.Expression}'");
        }

        return OverrideValidation.Valid();
    }

    private static bool ExpressionsMatch(string expr1, string expr2)
    {
        // Normalize whitespace and compare
        var normalized1 = NormalizeExpression(expr1);
        var normalized2 = NormalizeExpression(expr2);
        return string.Equals(normalized1, normalized2, StringComparison.Ordinal);
    }

    private static string NormalizeExpression(string expr)
    {
        // Normalize: trim, collapse whitespace, handle common variations
        return string.Join(" ", expr.Split(default(char[]),
            StringSplitOptions.RemoveEmptyEntries));
    }
}
```

**Why reject expression mismatches?**

An override relationship means "I want to take over this slot in the (ResourceType, Code) space, but the semantic meaning is the same." If the expression differs, it's not an override - it's a different parameter that happens to have the same code. That's a conflict, not an override.

**Valid override examples:**
- US Core `race` overrides base FHIR `race` with same expression but better IG metadata
- Custom IG adds `derivedFrom` pointing to base parameter with identical expression

**Invalid override examples (rejected):**
- IG defines `identifier` with expression `Patient.identifier.value` when base uses `Patient.identifier`
- Custom IG tries to "override" `birthdate` but changes from `date` to `string` type

## Composite SearchParameter Handling

Composite SPs reference component SPs by their URL. Components may not exist yet when the composite is first encountered. The solution is **deferred validation**:

```csharp
public class PackageActivationPipeline
{
    public async Task<ActivationResult> ActivateAsync(
        string packageId,
        string version,
        CancellationToken ct)
    {
        var resources = await _packageRepo.GetResourcesAsync(packageId, version, ct);

        // Phase 1: Collect all events (non-composite SPs first)
        var events = new List<SourceEvent>();
        var deferredComposites = new List<SearchParameterInfo>();

        foreach (var sp in resources.SearchParameters)
        {
            if (sp.Type == SearchParamType.Composite)
            {
                deferredComposites.Add(sp);  // Defer for later
            }
            else
            {
                events.AddRange(BuildSearchParameterEvents(sp, state));
            }
        }

        // Phase 2: Apply non-composite events to get intermediate state
        var intermediateState = state.Clone();
        foreach (var evt in events)
        {
            intermediateState.Apply(evt);
        }

        // Phase 3: Validate and add composite SPs
        foreach (var composite in deferredComposites)
        {
            var validation = ValidateCompositeComponents(composite, intermediateState);
            if (!validation.IsValid)
            {
                return ActivationResult.Failed(validation.Issues);
            }

            events.AddRange(BuildSearchParameterEvents(composite, intermediateState));
        }

        // Phase 4: Atomically append all events
        await _eventStore.AppendAsync(events, ct);

        // Phase 5: Apply to real state
        foreach (var evt in events)
        {
            state.Apply(evt);
        }

        return ActivationResult.Success();
    }

    private CompositeValidation ValidateCompositeComponents(
        SearchParameterInfo composite,
        ConformanceState state)
    {
        var issues = new List<string>();

        foreach (var component in composite.Components)
        {
            // Look up component by canonical URL
            var found = state.FindByCanonical(component.DefinitionUrl);

            if (found == null)
            {
                issues.Add(
                    $"Composite SP '{composite.Code}': Component '{component.DefinitionUrl}' not found. " +
                    $"Ensure the component SP is included in this package or a dependency.");
            }
        }

        return issues.Count == 0
            ? CompositeValidation.Valid()
            : CompositeValidation.Invalid(issues);
    }
}
```

**Package load order doesn't matter**: The deferred validation pattern means base FHIR, IGs, and custom packages can be loaded in any order. Composite validation happens at the end of each package activation, by which point all the package's own components are available. Cross-package dependencies are resolved by loading dependencies first (standard package dependency order).

## Thread Safety

The `ConformanceState` needs thread-safe access for concurrent reads during request handling and exclusive access during activation/catch-up.

```csharp
public class ConformanceState
{
    private readonly SemaphoreSlim _activationLock = new(1, 1);

    // Reads are lock-free - callers get a snapshot of current state
    public ActiveSearchParameter? GetEnabledSearchParameter(string resourceType, string code)
    {
        // Dictionary reads are thread-safe when no concurrent writes
        // Activation holds exclusive lock, so this is safe
        if (_searchParameters.TryGetValue((resourceType, code), out var sp) &&
            sp.Status == SearchParameterStatus.Enabled)
        {
            return sp;
        }
        return null;
    }

    // Writes require exclusive lock
    public async Task ApplyEventsAsync(
        IEnumerable<SourceEvent> events,
        CancellationToken cancellationToken)
    {
        await _activationLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var evt in events)
            {
                Apply(evt);
            }
        }
        finally
        {
            _activationLock.Release();
        }
    }

    public async Task CatchUpAsync(
        ISourceEventStore store,
        CancellationToken cancellationToken)
    {
        await _activationLock.WaitAsync(cancellationToken);
        try
        {
            await foreach (var evt in store.ReadFromAsync(LastProcessedEventId + 1, cancellationToken))
            {
                Apply(evt);
                LastProcessedEventId = evt.EventId;
            }
        }
        finally
        {
            _activationLock.Release();
        }
    }
}
```

**Why SemaphoreSlim over ReaderWriterLockSlim?**

Activations and catch-ups are rare (<1/minute in production). Request reads are frequent (1000s/second). With ~2000-5000 SPs total, dictionary lookup is O(1) and takes microseconds. The overhead of reader/writer semantics isn't worth it. Simple exclusive lock during writes, lock-free reads during normal operation.

## Open Questions

1. **Base FHIR loading**: Emit synthetic `PackageActivated` for `hl7.fhir.r4.core` on first startup?

## Next Steps

1. Write ADR if approach is accepted
2. Implement `ISourceEventStore` with SQL backend
3. Implement `ConformanceState` with in-memory projection
4. Implement `PackageActivationPipeline`
5. Wire up startup registration
6. Migrate package loading to emit events
7. Switch query paths to use `ConformanceState`
8. Delete legacy code
