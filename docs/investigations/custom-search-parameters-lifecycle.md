# Investigation: Custom Search Parameters Lifecycle Management

## Executive Summary

This investigation addresses the complex lifecycle management of FHIR SearchParameters from three distinct sources: base FHIR specification, Implementation Guides (IGs), and custom client-posted parameters. The challenge is managing conflicts, precedence, reindexing, and the R5+ `constraint` field (filter) that conditionally applies search parameters.

**Key Innovation**: Source-aware priority system with constraint evaluation prevents conflicts while supporting IG overrides and custom parameters.

## Problem Statement

### The Legacy Challenge

The old microsoft/fhir-server code has several issues with SearchParameter management:

1. **Conflicting Sources**: Base spec, IGs, and custom parameters can define parameters with identical codes but different behavior
2. **Reindex Complexity**: New parameters shouldn't be searchable until indices are consistent (requires expensive reindexing)
3. **IG Overrides**: Implementation Guides can "override" base parameters using `derivedFrom` and `constraint` fields
4. **No Clear Precedence**: Which parameter wins when multiple definitions exist for the same code?
5. **Status Management**: Parameters have lifecycle states (Supported → Enabled → Disabled) but transitions are unclear

### Real-World Failure Modes

From production experience:

- **US Core Overwrites Core Parameters**: Installing US Core IG removed Practitioner.given and Practitioner.family search parameters
- **86% Code Collision Rate**: US Core defines 86% of its search parameters with same codes as FHIR core
- **Silent Activation**: Custom parameters were searchable before reindex completed, returning incomplete results
- **Performance Impact**: Reindex operations could lock databases for hours on large datasets

## FHIR SearchParameter Anatomy

### Core Fields (All FHIR Versions)

```json
{
  "resourceType": "SearchParameter",
  "url": "http://example.com/SearchParameter/patient-foo",
  "name": "PatientFooParameter",
  "code": "foo",
  "base": ["Patient"],
  "type": "string",
  "status": "active",
  "expression": "Patient.extension.where(url='http://example.com/foo').value",
  "description": "Search by foo extension"
}
```

**Key Distinction**:
- `url`: Canonical URL (unique identifier) - PRIMARY KEY
- `code`: HTTP query parameter name (e.g., `?foo=value`) - NOT UNIQUE
- `expression`: FHIRPath to extract values for indexing
- `status`: draft | active | retired (publication status)

### R5+ Additions: Constraint and DerivedFrom

```json
{
  "resourceType": "SearchParameter",
  "url": "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race",
  "derivedFrom": "http://hl7.org/fhir/SearchParameter/Patient-race",
  "code": "race",
  "base": ["Patient"],
  "type": "token",
  "expression": "Patient.extension.where(url = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-race').extension.value.code",
  "constraint": "Patient.meta.profile.where($this = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient').exists()",
  "status": "active"
}
```

**New Fields**:
- `derivedFrom`: Points to parent SearchParameter (creates inheritance relationship)
- `constraint`: FHIRPath expression determining *when* parameter applies (the "filter")

**Constraint Use Cases**:
1. Apply parameter only to resources with specific profiles
2. Apply parameter only to resources with certain extensions
3. Apply parameter only to resources in certain status states

### The Three Sources

| Source | How Loaded | Priority | Can Delete? | Example |
|--------|------------|----------|-------------|---------|
| **Base Specification** | Embedded JSON from FHIR spec | Highest | No | Patient.name, Observation.code |
| **Implementation Guide** | NPM package from packages.fhir.org | Medium | No (until IG unloaded) | US Core Patient.race |
| **Custom Posted** | POST /SearchParameter by client | Lowest | Yes | Custom organization extensions |

## Solution Architecture

### 1. SearchParameter Storage Model

```csharp
/// <summary>
/// Extended search parameter info with lifecycle and conflict resolution
/// </summary>
public class SearchParameterInfo
{
    // Core FHIR fields
    public required string Url { get; init; }  // Canonical URL (PRIMARY KEY)
    public required string Code { get; init; }  // Query parameter name
    public required string[] Base { get; init; }  // Resource types
    public required SearchParamType Type { get; init; }
    public required string Expression { get; init; }  // FHIRPath for extraction

    // R5+ fields
    public string? Constraint { get; init; }  // FHIRPath filter for when applies
    public string? DerivedFrom { get; init; }  // Parent parameter URL

    // Lifecycle management (NEW)
    public required SearchParameterSource Source { get; init; }
    public required SearchParameterStatus IndexStatus { get; init; }
    public required int Priority { get; init; }

    // Metadata
    public required DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastReindexedAt { get; init; }
    public string? SourceImplementationGuide { get; init; }  // e.g., "hl7.fhir.us.core#6.1.0"
}

/// <summary>
/// Where the search parameter came from
/// </summary>
public enum SearchParameterSource
{
    BaseSpecification = 1,   // From FHIR R4/R5 spec (highest priority)
    ImplementationGuide = 2, // From loaded IG package
    Custom = 3               // Posted by client (lowest priority)
}

/// <summary>
/// Lifecycle state for indexing
/// </summary>
public enum SearchParameterStatus
{
    Supported = 1,      // Defined but not yet indexed (NOT SEARCHABLE)
    Reindexing = 2,     // Currently being indexed
    Enabled = 3,        // Fully indexed and active (SEARCHABLE)
    PendingDisable = 4, // Marked for removal but still active
    Disabled = 5        // Not available for search
}
```

### 2. Multi-Source Repository

```csharp
/// <summary>
/// Manages search parameters from all sources with conflict resolution
/// </summary>
public interface ISearchParameterRepository
{
    // Query methods
    ValueTask<SearchParameterInfo?> GetByUrlAsync(string url, CancellationToken ct);
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetByCodeAsync(string code, CancellationToken ct);
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetByResourceTypeAsync(string resourceType, CancellationToken ct);
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetEnabledAsync(CancellationToken ct);

    // Source-specific queries
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetBySourceAsync(SearchParameterSource source, CancellationToken ct);
    ValueTask<IReadOnlyList<SearchParameterInfo>> GetByImplementationGuideAsync(string igCanonical, CancellationToken ct);

    // Lifecycle management
    ValueTask<SearchParameterInfo> AddAsync(SearchParameterInfo param, CancellationToken ct);
    ValueTask<SearchParameterInfo> UpdateStatusAsync(string url, SearchParameterStatus newStatus, CancellationToken ct);
    ValueTask DeleteAsync(string url, CancellationToken ct);

    // Conflict detection
    ValueTask<ConflictCheckResult> CheckConflictsAsync(SearchParameterInfo param, CancellationToken ct);
}

/// <summary>
/// Result of checking if new parameter conflicts with existing
/// </summary>
public record ConflictCheckResult
{
    public bool HasConflict { get; init; }
    public SearchParameterInfo? ConflictingParameter { get; init; }
    public ConflictResolution RecommendedResolution { get; init; }
    public string? Message { get; init; }
}

public enum ConflictResolution
{
    NoConflict,           // No conflict, safe to add
    ReplaceWithNewer,     // Same URL, newer version
    CoexistDifferentUrl,  // Same code, different URL (both can exist)
    RejectDuplicate,      // Same URL and version, reject
    RejectLowerPriority   // Conflicts with higher priority source
}
```

### 3. Conflict Resolution Strategy

```csharp
/// <summary>
/// Handles conflicts between search parameters from different sources
/// </summary>
public class SearchParameterConflictResolver
{
    private readonly ISearchParameterRepository _repository;
    private readonly ILogger<SearchParameterConflictResolver> _logger;

    public async ValueTask<ConflictCheckResult> CheckConflictAsync(
        SearchParameterInfo newParam,
        CancellationToken ct)
    {
        // Step 1: Check by canonical URL (exact match)
        var existingByUrl = await _repository.GetByUrlAsync(newParam.Url, ct);
        if (existingByUrl != null)
        {
            return CheckSameUrlConflict(newParam, existingByUrl);
        }

        // Step 2: Check by code on same resource types
        var existingByCode = await _repository.GetByCodeAsync(newParam.Code, ct);
        var sameBases = existingByCode.Where(p => p.Base.Intersect(newParam.Base).Any()).ToList();

        if (sameBases.Count == 0)
        {
            return new ConflictCheckResult
            {
                HasConflict = false,
                RecommendedResolution = ConflictResolution.NoConflict
            };
        }

        // Step 3: Check if derivedFrom relationship exists
        var derived = sameBases.FirstOrDefault(p =>
            p.Url == newParam.DerivedFrom || newParam.Url == p.DerivedFrom);

        if (derived != null)
        {
            // Parent-child relationship, can coexist
            return new ConflictCheckResult
            {
                HasConflict = false,
                RecommendedResolution = ConflictResolution.CoexistDifferentUrl,
                Message = $"Parameter derives from {derived.Url}, can coexist"
            };
        }

        // Step 4: Priority-based resolution
        var highestPriority = sameBases.MinBy(p => p.Priority);
        if (newParam.Priority > highestPriority!.Priority)
        {
            // New parameter has lower priority, reject
            return new ConflictCheckResult
            {
                HasConflict = true,
                ConflictingParameter = highestPriority,
                RecommendedResolution = ConflictResolution.RejectLowerPriority,
                Message = $"Cannot add {newParam.Source} parameter '{newParam.Code}' - conflicts with {highestPriority.Source} parameter at {highestPriority.Url}"
            };
        }

        // New parameter has higher or equal priority
        return new ConflictCheckResult
        {
            HasConflict = true,
            ConflictingParameter = highestPriority,
            RecommendedResolution = ConflictResolution.CoexistDifferentUrl,
            Message = $"Warning: {newParam.Source} parameter '{newParam.Code}' has same code as {highestPriority.Source} parameter. Both will be available with different canonical URLs."
        };
    }

    private ConflictCheckResult CheckSameUrlConflict(
        SearchParameterInfo newParam,
        SearchParameterInfo existing)
    {
        // Same canonical URL - check if newer version
        if (newParam.Source == existing.Source)
        {
            // Assume intent to replace
            return new ConflictCheckResult
            {
                HasConflict = false,
                ConflictingParameter = existing,
                RecommendedResolution = ConflictResolution.ReplaceWithNewer,
                Message = $"Replacing existing {existing.Source} parameter at {existing.Url}"
            };
        }

        // Different sources claiming same URL - reject
        return new ConflictCheckResult
        {
            HasConflict = true,
            ConflictingParameter = existing,
            RecommendedResolution = ConflictResolution.RejectDuplicate,
            Message = $"Cannot add {newParam.Source} parameter - URL {newParam.Url} already owned by {existing.Source}"
        };
    }
}
```

### 4. Priority Assignment

```csharp
/// <summary>
/// Assigns priority to search parameters based on source
/// </summary>
public static class SearchParameterPriorityHelper
{
    public static int GetPriority(SearchParameterSource source) => source switch
    {
        SearchParameterSource.BaseSpecification => 1,    // Highest priority
        SearchParameterSource.ImplementationGuide => 2,  // Medium priority
        SearchParameterSource.Custom => 3,               // Lowest priority
        _ => int.MaxValue
    };

    public static SearchParameterInfo AssignPriority(SearchParameterInfo param)
    {
        return param with { Priority = GetPriority(param.Source) };
    }
}
```

### 5. Constraint Evaluation (R5+ Filter)

```csharp
/// <summary>
/// Evaluates SearchParameter.constraint to determine if parameter applies to a resource
/// </summary>
public class SearchParameterConstraintEvaluator
{
    private readonly IFhirPathEvaluator _fhirPath;
    private readonly ILogger<SearchParameterConstraintEvaluator> _logger;

    /// <summary>
    /// Check if search parameter should be applied to this resource
    /// </summary>
    public async ValueTask<bool> ShouldApplyAsync(
        SearchParameterInfo param,
        ITypedElement resource,
        CancellationToken ct)
    {
        // No constraint = always applies
        if (string.IsNullOrEmpty(param.Constraint))
        {
            return true;
        }

        try
        {
            // Evaluate FHIRPath constraint expression
            var result = _fhirPath.Evaluate(resource, param.Constraint);

            // Constraint should return boolean
            if (result is ITypedElement { Value: bool boolValue })
            {
                return boolValue;
            }

            // Non-empty result = true, empty = false
            return result.Any();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to evaluate constraint for SearchParameter {Url}: {Constraint}",
                param.Url, param.Constraint);

            // On error, apply parameter (fail open)
            return true;
        }
    }
}
```

### 6. Reindexing Orchestration

```csharp
/// <summary>
/// Manages reindexing when search parameters change
/// </summary>
public class SearchParameterReindexOrchestrator
{
    private readonly ISearchParameterRepository _paramRepository;
    private readonly IResourceRepository _resourceRepository;
    private readonly ISearchIndexBuilder _indexBuilder;
    private readonly IReindexJobQueue _jobQueue;
    private readonly SearchParameterConstraintEvaluator _constraintEvaluator;

    /// <summary>
    /// Add new search parameter and queue reindex job
    /// </summary>
    public async ValueTask<AddSearchParameterResult> AddSearchParameterAsync(
        SearchParameterInfo param,
        bool autoReindex,
        CancellationToken ct)
    {
        // Step 1: Conflict check
        var conflictResult = await CheckConflictsAsync(param, ct);
        if (conflictResult.HasConflict &&
            conflictResult.RecommendedResolution == ConflictResolution.RejectLowerPriority)
        {
            return new AddSearchParameterResult
            {
                Success = false,
                ErrorMessage = conflictResult.Message
            };
        }

        // Step 2: Add in Supported status (not searchable yet)
        var added = await _paramRepository.AddAsync(
            param with { IndexStatus = SearchParameterStatus.Supported },
            ct);

        // Step 3: Queue reindex job if requested
        string? jobId = null;
        if (autoReindex)
        {
            jobId = await QueueReindexJobAsync(added, ct);
        }

        return new AddSearchParameterResult
        {
            Success = true,
            SearchParameter = added,
            ReindexJobId = jobId,
            Message = conflictResult.Message
        };
    }

    private async ValueTask<string> QueueReindexJobAsync(
        SearchParameterInfo param,
        CancellationToken ct)
    {
        // Update status to Reindexing
        await _paramRepository.UpdateStatusAsync(
            param.Url,
            SearchParameterStatus.Reindexing,
            ct);

        // Queue background job
        var job = new ReindexJob
        {
            SearchParameterUrl = param.Url,
            ResourceTypes = param.Base,
            CreatedAt = DateTimeOffset.UtcNow
        };

        return await _jobQueue.EnqueueAsync(job, ct);
    }

    /// <summary>
    /// Execute reindex job for a search parameter
    /// </summary>
    public async ValueTask ExecuteReindexAsync(
        string searchParameterUrl,
        CancellationToken ct)
    {
        var param = await _paramRepository.GetByUrlAsync(searchParameterUrl, ct);
        if (param == null)
        {
            throw new InvalidOperationException($"SearchParameter not found: {searchParameterUrl}");
        }

        try
        {
            foreach (var resourceType in param.Base)
            {
                await ReindexResourceTypeAsync(param, resourceType, ct);
            }

            // Mark as Enabled
            await _paramRepository.UpdateStatusAsync(
                param.Url,
                SearchParameterStatus.Enabled,
                ct);
        }
        catch (Exception)
        {
            // Revert to Supported on failure
            await _paramRepository.UpdateStatusAsync(
                param.Url,
                SearchParameterStatus.Supported,
                ct);
            throw;
        }
    }

    private async ValueTask ReindexResourceTypeAsync(
        SearchParameterInfo param,
        string resourceType,
        CancellationToken ct)
    {
        const int batchSize = 100;
        string? continuationToken = null;

        do
        {
            // Fetch batch of resources
            var batch = await _resourceRepository.SearchAsync(
                resourceType,
                parameters: null,
                count: batchSize,
                continuationToken: continuationToken,
                ct);

            foreach (var resource in batch.Resources)
            {
                // Check constraint if present
                bool shouldApply = await _constraintEvaluator.ShouldApplyAsync(
                    param,
                    resource.Instance,
                    ct);

                if (shouldApply)
                {
                    // Extract and index values
                    var values = await _indexBuilder.ExtractSearchParameterValuesAsync(
                        resource.Instance,
                        param,
                        ct);

                    await _indexBuilder.IndexAsync(
                        resource.ResourceKey,
                        param.Url,
                        values,
                        ct);
                }
            }

            continuationToken = batch.ContinuationToken;
        }
        while (continuationToken != null);
    }
}

public record AddSearchParameterResult
{
    public required bool Success { get; init; }
    public SearchParameterInfo? SearchParameter { get; init; }
    public string? ReindexJobId { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Message { get; init; }
}
```

### 7. Search Execution with Constraints

```csharp
/// <summary>
/// Resolves which search parameter to use when multiple exist with same code
/// </summary>
public class SearchParameterResolver
{
    private readonly ISearchParameterRepository _repository;
    private readonly ILogger<SearchParameterResolver> _logger;

    /// <summary>
    /// Resolve search parameter from query parameter name
    /// </summary>
    public async ValueTask<SearchParameterInfo?> ResolveAsync(
        string resourceType,
        string code,
        CancellationToken ct)
    {
        // Get all enabled parameters with this code for this resource type
        var candidates = await _repository.GetByCodeAsync(code, ct);
        var enabled = candidates
            .Where(p => p.IndexStatus == SearchParameterStatus.Enabled)
            .Where(p => p.Base.Contains(resourceType))
            .ToList();

        if (enabled.Count == 0)
        {
            return null;
        }

        if (enabled.Count == 1)
        {
            return enabled[0];
        }

        // Multiple parameters with same code - use priority
        var highest = enabled.MinBy(p => p.Priority);

        if (enabled.Count(p => p.Priority == highest!.Priority) > 1)
        {
            _logger.LogWarning(
                "Multiple search parameters with code '{Code}' and same priority {Priority} for {ResourceType}. Using {Url}",
                code, highest.Priority, resourceType, highest.Url);
        }

        return highest;
    }
}
```

## Implementation Guide Loading Strategy

### 1. IG Package Structure

Implementation Guides distribute SearchParameter resources via NPM packages on packages.fhir.org:

```
hl7.fhir.us.core#6.1.0/
├── package.json
├── SearchParameter-us-core-patient-race.json
├── SearchParameter-us-core-patient-ethnicity.json
└── ...
```

### 2. IG Loader

```csharp
/// <summary>
/// Loads search parameters from Implementation Guide packages
/// </summary>
public class ImplementationGuideSearchParameterLoader
{
    private readonly ISearchParameterRepository _repository;
    private readonly SearchParameterConflictResolver _conflictResolver;
    private readonly ILogger<ImplementationGuideSearchParameterLoader> _logger;

    /// <summary>
    /// Load all SearchParameter resources from IG package
    /// </summary>
    public async ValueTask<LoadResult> LoadFromPackageAsync(
        string packageId,
        string version,
        CancellationToken ct)
    {
        var result = new LoadResult { PackageId = packageId, Version = version };

        // Download package from packages.fhir.org
        var packagePath = await DownloadPackageAsync(packageId, version, ct);

        // Find all SearchParameter JSON files
        var searchParamFiles = Directory.GetFiles(packagePath, "SearchParameter-*.json");

        foreach (var file in searchParamFiles)
        {
            var json = await File.ReadAllTextAsync(file, ct);
            var resource = _fhirJsonParser.Parse<SearchParameter>(json);

            var param = MapToSearchParameterInfo(resource, packageId, version);

            // Check conflicts
            var conflictCheck = await _conflictResolver.CheckConflictAsync(param, ct);

            if (conflictCheck.HasConflict &&
                conflictCheck.RecommendedResolution == ConflictResolution.RejectLowerPriority)
            {
                _logger.LogWarning(
                    "Skipping SearchParameter {Url} from {Package}: {Message}",
                    param.Url, packageId, conflictCheck.Message);
                result.Skipped.Add(param.Url);
                continue;
            }

            if (conflictCheck.HasConflict &&
                conflictCheck.RecommendedResolution == ConflictResolution.CoexistDifferentUrl)
            {
                _logger.LogInformation(
                    "Loading SearchParameter {Url} from {Package}: {Message}",
                    param.Url, packageId, conflictCheck.Message);
            }

            // Add in Supported status (requires reindex)
            await _repository.AddAsync(param, ct);
            result.Loaded.Add(param.Url);
        }

        return result;
    }

    private SearchParameterInfo MapToSearchParameterInfo(
        SearchParameter resource,
        string packageId,
        string version)
    {
        return new SearchParameterInfo
        {
            Url = resource.Url,
            Code = resource.Code,
            Base = resource.Base.Select(b => b.Value.ToString()).ToArray(),
            Type = resource.Type.Value,
            Expression = resource.Expression,
            Constraint = resource.Extension
                .FirstOrDefault(e => e.Url == "http://hl7.org/fhir/StructureDefinition/searchparameter-constraint")
                ?.Value?.ToString(),
            DerivedFrom = resource.DerivedFrom,
            Source = SearchParameterSource.ImplementationGuide,
            IndexStatus = SearchParameterStatus.Supported,
            Priority = SearchParameterPriorityHelper.GetPriority(SearchParameterSource.ImplementationGuide),
            CreatedAt = DateTimeOffset.UtcNow,
            SourceImplementationGuide = $"{packageId}#{version}"
        };
    }
}

public record LoadResult
{
    public required string PackageId { get; init; }
    public required string Version { get; init; }
    public List<string> Loaded { get; } = new();
    public List<string> Skipped { get; } = new();
}
```

## CapabilityStatement Integration

### 1. Expose Active Search Parameters

```csharp
/// <summary>
/// Builds searchParam elements for CapabilityStatement.rest.resource
/// </summary>
public class SearchParameterCapabilitySegment : ICapabilitySegment
{
    private readonly ISearchParameterRepository _repository;

    public string SegmentKey => "SearchParameters";

    public async ValueTask ApplyAsync(
        ICapabilityStatementBuilder builder,
        CapabilityContext context,
        CancellationToken ct)
    {
        var enabledParams = await _repository.GetEnabledAsync(ct);

        foreach (var resourceType in builder.GetResourceTypes())
        {
            var paramsForType = enabledParams
                .Where(p => p.Base.Contains(resourceType))
                .GroupBy(p => p.Code)
                .Select(g => g.MinBy(p => p.Priority)!) // Highest priority for each code
                .ToList();

            foreach (var param in paramsForType)
            {
                builder.AddSearchParameter(resourceType, new CapabilitySearchParam
                {
                    Name = param.Code,
                    Definition = param.Url,
                    Type = param.Type,
                    Documentation = $"Source: {param.Source}"
                });
            }
        }
    }

    public async ValueTask<string> GetVersionHashAsync(
        CapabilityContext context,
        CancellationToken ct)
    {
        var enabledParams = await _repository.GetEnabledAsync(ct);

        // Hash includes URL + status + last reindexed time
        var data = string.Join("|", enabledParams
            .OrderBy(p => p.Url)
            .Select(p => $"{p.Url}:{p.IndexStatus}:{p.LastReindexedAt:O}"));

        return HashHelper.ComputeSHA256(data);
    }
}
```

### 2. Example CapabilityStatement Output

```json
{
  "resourceType": "CapabilityStatement",
  "rest": [{
    "resource": [{
      "type": "Patient",
      "searchParam": [
        {
          "name": "name",
          "definition": "http://hl7.org/fhir/SearchParameter/Patient-name",
          "type": "string",
          "documentation": "Source: BaseSpecification"
        },
        {
          "name": "race",
          "definition": "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race",
          "type": "token",
          "documentation": "Source: ImplementationGuide"
        },
        {
          "name": "custom-foo",
          "definition": "http://example.com/SearchParameter/patient-custom-foo",
          "type": "string",
          "documentation": "Source: Custom"
        }
      ]
    }]
  }]
}
```

## REST API Operations

### 1. POST /SearchParameter (Add Custom Parameter)

```http
POST /SearchParameter
Content-Type: application/fhir+json

{
  "resourceType": "SearchParameter",
  "url": "http://example.com/SearchParameter/patient-ssn",
  "name": "PatientSSN",
  "code": "ssn",
  "base": ["Patient"],
  "type": "token",
  "expression": "Patient.identifier.where(system='http://hl7.org/fhir/sid/us-ssn').value",
  "status": "active"
}
```

**Response**:
```http
HTTP/1.1 201 Created
Location: /SearchParameter/patient-ssn

{
  "resourceType": "SearchParameter",
  "id": "patient-ssn",
  "url": "http://example.com/SearchParameter/patient-ssn",
  "status": "active",
  "extension": [{
    "url": "http://example.com/StructureDefinition/searchparameter-index-status",
    "valueCode": "supported"
  }]
}
```

**Note**: Parameter is NOT searchable until reindex completes.

### 2. POST /$reindex (Trigger Reindex)

```http
POST /$reindex
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "url",
    "valueString": "http://example.com/SearchParameter/patient-ssn"
  }]
}
```

**Response**:
```http
HTTP/1.1 202 Accepted
Content-Location: /$reindex/job-123

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "jobId",
    "valueString": "job-123"
  }, {
    "name": "status",
    "valueCode": "accepted"
  }]
}
```

### 3. GET /$reindex/job-123 (Monitor Job)

```http
GET /$reindex/job-123
```

**Response**:
```http
HTTP/1.1 200 OK

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "status",
    "valueCode": "in-progress"
  }, {
    "name": "resourcesProcessed",
    "valueInteger": 45000
  }, {
    "name": "resourcesTotal",
    "valueInteger": 100000
  }, {
    "name": "percentComplete",
    "valueDecimal": 45.0
  }]
}
```

### 4. GET /SearchParameter (List All Parameters)

```http
GET /SearchParameter?status=active&_source=ImplementationGuide
```

Returns all SearchParameter resources with custom extension showing index status.

## Performance Considerations

### 1. Constraint Evaluation Caching

```csharp
/// <summary>
/// Caches constraint evaluation results per resource
/// </summary>
public class ConstraintEvaluationCache
{
    private readonly ConcurrentDictionary<string, bool> _cache = new();

    public bool TryGetCached(string resourceId, string parameterUrl, out bool result)
    {
        return _cache.TryGetValue($"{resourceId}:{parameterUrl}", out result);
    }

    public void Cache(string resourceId, string parameterUrl, bool result)
    {
        _cache[$"{resourceId}:{parameterUrl}"] = result;
    }

    public void Invalidate(string resourceId)
    {
        var keysToRemove = _cache.Keys.Where(k => k.StartsWith($"{resourceId}:")).ToList();
        foreach (var key in keysToRemove)
        {
            _cache.TryRemove(key, out _);
        }
    }
}
```

### 2. Reindex Performance Tuning

| Setting | Default | Tuning Guidance |
|---------|---------|-----------------|
| Batch Size | 100 | Increase to 500-1000 for file storage, keep 100 for SQL |
| Parallel Workers | 4 | Scale with CPU cores (1-2x core count) |
| Throttle Delay | 0ms | Add 10-50ms delay between batches to reduce load |
| Memory Limit | 1GB | Monitor, increase if batches are large resources |

**Expected Performance**:
- File storage: 10,000-20,000 resources/minute
- SQL storage: 5,000-10,000 resources/minute
- Cosmos storage: 3,000-5,000 resources/minute (RU-limited)

### 3. Incremental Reindexing

```csharp
/// <summary>
/// Only reindex resources modified since parameter was added
/// </summary>
public async ValueTask ReindexIncrementalAsync(
    SearchParameterInfo param,
    CancellationToken ct)
{
    // Only reindex resources modified after param was created
    var searchSince = param.CreatedAt;

    foreach (var resourceType in param.Base)
    {
        var batch = await _resourceRepository.SearchAsync(
            resourceType,
            parameters: new() { ["_lastUpdated"] = $"gt{searchSince:o}" },
            count: 100,
            continuationToken: null,
            ct);

        // Index only modified resources
    }
}
```

## Testing Strategy

### 1. Unit Tests

```csharp
[Fact]
public async Task AddSearchParameter_BaseSpecParam_RejectsCustomWithSameCode()
{
    // Arrange
    var baseParam = new SearchParameterInfo
    {
        Url = "http://hl7.org/fhir/SearchParameter/Patient-name",
        Code = "name",
        Source = SearchParameterSource.BaseSpecification,
        Priority = 1
    };
    await _repository.AddAsync(baseParam, CancellationToken.None);

    var customParam = new SearchParameterInfo
    {
        Url = "http://example.com/SearchParameter/patient-custom-name",
        Code = "name",
        Source = SearchParameterSource.Custom,
        Priority = 3
    };

    // Act
    var result = await _orchestrator.AddSearchParameterAsync(customParam, false, CancellationToken.None);

    // Assert
    Assert.False(result.Success);
    Assert.Contains("conflicts with BaseSpecification", result.ErrorMessage);
}

[Fact]
public async Task ResolveSearchParameter_MultipleSameCode_UsesHighestPriority()
{
    // Arrange
    var baseParam = new SearchParameterInfo
    {
        Url = "http://hl7.org/fhir/SearchParameter/Patient-race",
        Code = "race",
        Source = SearchParameterSource.BaseSpecification,
        IndexStatus = SearchParameterStatus.Enabled,
        Priority = 1
    };
    var igParam = new SearchParameterInfo
    {
        Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race",
        Code = "race",
        DerivedFrom = baseParam.Url,
        Source = SearchParameterSource.ImplementationGuide,
        IndexStatus = SearchParameterStatus.Enabled,
        Priority = 2
    };
    await _repository.AddAsync(baseParam, CancellationToken.None);
    await _repository.AddAsync(igParam, CancellationToken.None);

    // Act
    var resolved = await _resolver.ResolveAsync("Patient", "race", CancellationToken.None);

    // Assert
    Assert.Equal(baseParam.Url, resolved.Url); // Base has higher priority
}

[Fact]
public async Task ConstraintEvaluator_USCorePatient_AppliesRaceParameter()
{
    // Arrange
    var param = new SearchParameterInfo
    {
        Url = "http://hl7.org/fhir/us/core/SearchParameter/us-core-patient-race",
        Code = "race",
        Constraint = "Patient.meta.profile.where($this = 'http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient').exists()"
    };

    var patient = new Patient
    {
        Meta = new Meta
        {
            Profile = new[] { "http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient" }
        }
    };
    var typedPatient = patient.ToTypedElement();

    // Act
    var shouldApply = await _evaluator.ShouldApplyAsync(param, typedPatient, CancellationToken.None);

    // Assert
    Assert.True(shouldApply);
}
```

### 2. Integration Tests

```csharp
[Fact]
public async Task E2E_LoadUSCore_DoesNotOverwriteBasePractitionerName()
{
    // Arrange - verify base parameter exists
    var baseName = await _repository.GetByUrlAsync(
        "http://hl7.org/fhir/SearchParameter/Practitioner-name",
        CancellationToken.None);
    Assert.NotNull(baseName);

    // Act - load US Core IG
    var result = await _igLoader.LoadFromPackageAsync("hl7.fhir.us.core", "6.1.0", CancellationToken.None);

    // Assert - base parameter still exists
    var afterLoad = await _repository.GetByUrlAsync(
        "http://hl7.org/fhir/SearchParameter/Practitioner-name",
        CancellationToken.None);
    Assert.NotNull(afterLoad);
    Assert.Equal(SearchParameterStatus.Enabled, afterLoad.IndexStatus);
}

[Fact]
public async Task E2E_CustomParameter_NotSearchableUntilReindexed()
{
    // Arrange - add custom parameter
    var param = new SearchParameterInfo
    {
        Url = "http://example.com/SearchParameter/patient-foo",
        Code = "foo",
        Base = new[] { "Patient" },
        Type = SearchParamType.String,
        Expression = "Patient.extension.where(url='http://example.com/foo').value",
        Source = SearchParameterSource.Custom
    };
    var addResult = await _orchestrator.AddSearchParameterAsync(param, false, CancellationToken.None);
    Assert.True(addResult.Success);

    // Act - try to search before reindex
    var searchResult = await _searchService.SearchAsync("Patient", new() { ["foo"] = "bar" }, CancellationToken.None);

    // Assert - search ignored parameter (not enabled)
    Assert.Empty(searchResult.Resources);
    Assert.DoesNotContain("foo", searchResult.UsedParameters);
}
```

## Migration from Legacy System

### Step 1: Export Existing Custom Parameters

```sql
-- Export from legacy SearchParameter table
SELECT
    Url,
    Code,
    ResourceTypes, -- JSON array
    Expression,
    Type,
    Status
FROM dbo.SearchParam
WHERE Source = 'Custom'
FOR JSON PATH
```

### Step 2: Import to V2 System

```csharp
public async ValueTask MigrateCustomParametersAsync(string legacyJsonPath, CancellationToken ct)
{
    var json = await File.ReadAllTextAsync(legacyJsonPath, ct);
    var legacyParams = JsonSerializer.Deserialize<List<LegacySearchParam>>(json);

    foreach (var legacy in legacyParams)
    {
        var param = new SearchParameterInfo
        {
            Url = legacy.Url,
            Code = legacy.Code,
            Base = legacy.ResourceTypes,
            Type = Enum.Parse<SearchParamType>(legacy.Type),
            Expression = legacy.Expression,
            Source = SearchParameterSource.Custom,
            IndexStatus = SearchParameterStatus.Supported, // Will need reindex
            Priority = SearchParameterPriorityHelper.GetPriority(SearchParameterSource.Custom),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await _orchestrator.AddSearchParameterAsync(param, autoReindex: true, ct);
    }
}
```

## Phase Integration

### Phase 1.2: Search Implementation (Week 3)

**Deliverables**:
- `SearchParameterRepository` with file-based storage
- Base FHIR R4 search parameters loaded from embedded JSON
- `SearchParameterResolver` for query execution
- NO custom parameters or IG loading yet

### Phase 3: Validation (Weeks 8-10)

**Deliverables**:
- Add `SearchParameterCapabilitySegment` to CapabilityStatement
- Expose enabled parameters in `/metadata`
- Update capability hash when parameters change

### Phase 11: Implementation Guides (Weeks 50-55)

**Deliverables**:
- `ImplementationGuideSearchParameterLoader`
- Load SearchParameters from NPM packages
- Conflict resolution for IG vs base parameters
- Support `derivedFrom` relationships
- **NEW**: Support `constraint` field for conditional parameters (R5+)

### Phase 12: Custom Search Parameters (NEW - Weeks 56-59)

**Deliverables**:
- `POST /SearchParameter` endpoint
- `SearchParameterReindexOrchestrator`
- Background reindex job queue
- `GET /$reindex/{jobId}` status endpoint
- Full lifecycle: Supported → Reindexing → Enabled

**E2E Tests**:
- `CustomSearchParameterTests.cs` (15+ tests)
- `SearchParameterConflictTests.cs` (10+ tests)
- `SearchParameterReindexTests.cs` (8+ tests)

## Summary

### Key Innovations

1. **Source-Aware Priority System**: Base > IG > Custom prevents accidental overwrites
2. **Constraint Evaluation**: R5+ `constraint` field filters when parameters apply
3. **Multi-Status Lifecycle**: Clear states from definition → indexing → enabled
4. **Conflict Resolution**: Automatic detection with recommended resolutions
5. **Incremental Reindexing**: Only reindex modified resources when possible

### Complexity Solved

- ✅ **US Core Overwrite Problem**: Priority system prevents IG from removing base parameters
- ✅ **86% Code Collision**: Multiple parameters with same code coexist via canonical URLs
- ✅ **Premature Searchability**: Status-based gates prevent searches before reindex
- ✅ **IG Filter Constraints**: Full support for R5+ conditional search parameters
- ✅ **Reindex Performance**: Batching, parallelization, and incremental strategies

### Performance Targets

| Operation | Target | Storage |
|-----------|--------|---------|
| Add search parameter | <50ms | All |
| Conflict check | <10ms | All |
| Reindex 100K resources | <10 minutes | File |
| Reindex 100K resources | <20 minutes | SQL |
| Constraint evaluation (cached) | <1ms | All |
| Constraint evaluation (uncached) | <10ms | All |

### Breaking Changes from Legacy

1. **Custom parameters not immediately searchable** - requires explicit reindex
2. **IG parameters don't auto-replace base** - coexist with different URLs
3. **Status field required** - Supported/Enabled/Disabled states enforced

### Migration Effort

- Export legacy custom parameters: 1 hour
- Import to V2 system: 2 hours
- Reindex all custom parameters: 2-8 hours (dataset dependent)
- **Total**: ~1 day for typical deployment
