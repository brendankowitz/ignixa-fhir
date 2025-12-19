# Investigation: MCP Tool Design Guidelines for LLM Optimization

**Feature**: mcp-integration
**Status**: Proposed
**Created**: 2025-11-08

## Overview

MCP tools for Ignixa FHIR Server must be optimized for LLM consumption to minimize token costs and response latency.

---

## Search Result Limits

### Default Limits

| Parameter | Default | Max | Rationale |
|-----------|---------|-----|-----------|
| `_count` | **10** | 50 | LLMs rarely need >10 results; reduces tokens significantly |
| `_total` | `none` | - | Don't calculate totals unless explicitly requested (expensive) |
| `_include` | Not allowed in Phase 1 | - | Prevents accidental data explosion |

### Examples

**Good** (10 results):
```json
{
  "resourceType": "Patient",
  "searchParams": {"name": "Smith"},
  "_count": 10
}
```

**Acceptable** (explicit count):
```json
{
  "resourceType": "Patient",
  "searchParams": {"birthdate": "gt2000"},
  "_count": 25
}
```

**Rejected** (too many):
```json
{
  "_count": 100  // Error: Maximum _count is 50
}
```

---

## Field Selection with `_elements`

### Purpose
Allow LLMs to request only the fields they need, dramatically reducing response size.

### Supported Parameters

| Parameter | Effect | Example |
|-----------|--------|---------|
| `_elements` | Include only specified fields | `id,name,birthDate` |
| `_summary` | Predefined field sets | `true` (core fields only) |

### `_elements` Examples

**Minimal** (just ID and name):
```json
{
  "resourceType": "Patient",
  "searchParams": {"family": "Smith"},
  "_elements": "id,name"
}
```

Response:
```json
{
  "resourceType": "Patient",
  "id": "123",
  "name": [{"family": "Smith", "given": ["John"]}]
  // meta, identifier, etc. excluded
}
```

**Specific use case** (demographics only):
```json
{
  "resourceType": "Patient",
  "searchParams": {"_id": "123"},
  "_elements": "id,name,birthDate,gender,address"
}
```

### `_summary` Values

| Value | Fields Included | Use Case |
|-------|----------------|----------|
| `true` | Core fields only (id, meta, text, top-level attributes) | Quick overview |
| `data` | All fields except `text` narrative | Data extraction |
| `text` | `id`, `meta`, `text` only | Human-readable summary |
| `false` | Full resource (default) | Complete data needed |

**Example with `_summary=true`:**
```json
{
  "resourceType": "Observation",
  "searchParams": {"patient": "123"},
  "_summary": "true"
}
```

Returns: `id`, `meta`, `status`, `code`, `subject`, `value[x]` (core obs fields)
Excludes: `text`, `contained`, `extension`, `component` (unless essential)

---

## DTO Design

### ResourceEntryDto

**Before** (redundant):
```csharp
public class ResourceEntryDto
{
    public string Id { get; init; }           // ❌ Already in Resource JSON
    public string ResourceType { get; init; } // ❌ Already in Resource JSON
    public string VersionId { get; init; }    // ❌ Already in Resource.meta.versionId
    public DateTimeOffset LastModified { get; init; } // ❌ Already in Resource.meta.lastUpdated
    public JsonDocument Resource { get; init; }
}
```

**After** (optimized):
```csharp
public class ResourceEntryDto
{
    public JsonDocument Resource { get; init; }  // ✅ Contains everything
    public string? SearchMode { get; init; }     // ✅ Metadata not in resource
}
```

**Savings**: ~40% fewer tokens per entry!

---

## Tool Parameter Design

### SearchResourcesTool

```csharp
[McpServerTool]
[Description("Search FHIR resources. Returns max 10 results by default. Use _elements to limit fields.")]
public async Task<SearchResultsDto> SearchResourcesAsync(
    [Description("Resource type: Patient, Observation, etc.")]
    string resourceType,

    [Description("Search parameters: {'name': 'Smith', 'birthdate': 'gt2000'}")]
    Dictionary<string, string> searchParams,

    [Description("Max results (default: 10, max: 50)")]
    int? count = null,

    [Description("Comma-separated fields to include: 'id,name,birthDate'")]
    string? elements = null,

    [Description("Summary mode: true, data, text, false")]
    string? summary = null,

    [Description("Tenant ID (optional, auto-detected if single tenant)")]
    int? tenantId = null,

    CancellationToken cancellationToken = default)
{
    // Enforce limits
    var effectiveCount = Math.Min(count ?? 10, 50);

    // Build search params with _count, _elements, _summary
    var fhirParams = new Dictionary<string, string>(searchParams);
    fhirParams["_count"] = effectiveCount.ToString();

    if (!string.IsNullOrEmpty(elements))
        fhirParams["_elements"] = elements;

    if (!string.IsNullOrEmpty(summary))
        fhirParams["_summary"] = summary;

    // Invoke Medino handler
    var query = new SearchResourcesQuery(
        TenantId: resolvedTenantId,
        ResourceType: resourceType,
        SearchParameters: fhirParams
    );

    var result = await _mediator.SendAsync(query, cancellationToken);

    // Map to DTO (just resource + searchMode)
    return new SearchResultsDto
    {
        ResourceType = resourceType,
        Entries = result.Results.Select(r => new ResourceEntryDto
        {
            Resource = r.Resource,  // JsonDocument already filtered by _elements
            SearchMode = r.SearchMode
        }).ToList(),
        Total = result.Total,
        HasMore = result.HasMore,
        ContinuationToken = result.ContinuationToken
    };
}
```

---

## Response Size Optimization

### Example Scenarios

**Scenario 1**: Find patients named "Smith"

**Naive approach** (full resources, no limit):
```json
{
  "resourceType": "Patient",
  "searchParams": {"name": "Smith"}
}
```
Response: ~50 patients × ~2KB = **100KB** (25,000 tokens!)

**Optimized approach** (10 results, names only):
```json
{
  "resourceType": "Patient",
  "searchParams": {"name": "Smith"},
  "_count": 10,
  "_elements": "id,name"
}
```
Response: 10 patients × ~0.2KB = **2KB** (500 tokens)

**Savings: 98% fewer tokens!**

---

**Scenario 2**: Get patient demographics

**Naive** (full resource):
```json
{
  "resourceType": "Patient",
  "id": "123"
}
```
Response: ~2KB (500 tokens)

**Optimized** (demographics only):
```json
{
  "resourceType": "Patient",
  "id": "123",
  "_elements": "id,name,birthDate,gender,telecom,address"
}
```
Response: ~0.5KB (125 tokens)

**Savings: 75% fewer tokens!**

---

## Implementation Checklist

### For Each MCP Tool

- [ ] Default `_count` = 10 (hardcoded, not configurable in Phase 1)
- [ ] Support `_elements` parameter (comma-separated field names)
- [ ] Support `_summary` parameter (true, data, text, false)
- [ ] Enforce max `_count` = 50
- [ ] Return `JsonDocument` directly (don't serialize to string then parse)
- [ ] Remove redundant DTO fields (id, resourceType, etc. already in resource)
- [ ] Include `SearchMode` only for search results (omit for single resource GET)
- [ ] Document token savings in tool description

### Tool Description Template

```csharp
[Description(@"Search {ResourceType} resources.
Returns max 10 results by default (specify _count up to 50 for more).
Use _elements='id,field1,field2' to limit fields and reduce response size.
Use _summary='true' for core fields only.
Example: searchParams={'name': 'Smith'}, _elements='id,name,birthDate'")]
```

---

## Performance Targets

| Metric | Target | Rationale |
|--------|--------|-----------|
| Default response size | < 5KB | LLM context limits |
| Max response size | < 50KB | Prevent timeouts |
| Tool invocation latency (p95) | < 500ms | User experience |
| Token cost per search | < 1,000 tokens | Cost optimization |

---

## Future Enhancements (Phase 2+)

- [ ] `_include` support (with strict limits: max 3 includes)
- [ ] `_revinclude` support (with strict limits)
- [ ] GraphQL-style field selection (nested field support)
- [ ] Automatic field selection based on common LLM queries (ML-driven)
- [ ] Response caching (identical queries return cached results)

---

**Status**: Phase 1 Design
**Last Updated**: 2025-11-08
**Owner**: MCP Integration Team
