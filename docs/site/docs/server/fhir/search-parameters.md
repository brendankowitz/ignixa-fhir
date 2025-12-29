---
sidebar_position: 3
title: Search Parameters
description: FHIR search capabilities and parameters
---

# Search Parameters

Ignixa implements comprehensive FHIR search with support for all standard search parameters plus extensible search via FHIR packages.

## Package-Based Search Parameters

Beyond the base FHIR specification parameters, Ignixa can load additional SearchParameter definitions from installed FHIR packages (Implementation Guides). Packages can be installed via:

- **MCP tools** - Use `install_fhir_package` tool via AI assistants
- **Configuration** - Pre-load packages at startup (see [MCP Server](/docs/server/features/mcp-server))

This enables:
- **Implementation Guide parameters** - US Core, AU Base, UK Core, etc.
- **Custom SearchParameters** - Define your own search parameters
- **Custom resource types** - Search parameters for StructureDefinitions with custom types

### Configuration

Control how package search parameters are loaded:

```json
{
  "SearchParameters": {
    "ConflictResolution": {
      "PackagePriorityOrder": ["hl7.fhir.us.core", "hl7.fhir.au.base"],
      "UseSemanticVersioning": true,
      "LogConflicts": true,
      "EagerLoadPackageSearchParameters": true
    }
  }
}
```

| Setting | Description |
|---------|-------------|
| `PackagePriorityOrder` | Priority when multiple packages define same parameter code |
| `UseSemanticVersioning` | Newer versions win when no explicit priority |
| `EagerLoadPackageSearchParameters` | Load all at startup vs. lazy per resource type |

## Basic Search

```bash
GET /Patient?family=Smith&given=John
```

## Search Parameter Types

### String

Case-insensitive, partial match:

```bash
GET /Patient?name=smi
GET /Patient?name:exact=Smith
GET /Patient?name:contains=mit
```

| Modifier | Description |
|----------|-------------|
| (none) | Starts with |
| `:exact` | Exact match (case-sensitive) |
| `:contains` | Contains substring |

### Token

Coded values with system/code:

```bash
# Code only
GET /Observation?code=29463-7

# System|code
GET /Observation?code=http://loinc.org|29463-7

# System only
GET /Observation?code=http://loinc.org|

# Text modifier
GET /Observation?code:text=body%20weight
```

### Reference

Resource references:

```bash
# Relative reference
GET /Observation?subject=Patient/123

# Type specified
GET /Observation?subject:Patient=123

# Chaining
GET /Observation?subject.name=Smith

# Identifier
GET /Observation?subject:identifier=http://hospital.org|MRN123
```

### Date/DateTime

Temporal comparisons:

```bash
GET /Observation?date=2024-01-15
GET /Observation?date=ge2024-01-01&date=lt2024-02-01
GET /Patient?birthdate=1990
```

| Prefix | Meaning |
|--------|---------|
| `eq` | Equals (default) |
| `ne` | Not equals |
| `lt` | Less than |
| `gt` | Greater than |
| `le` | Less than or equal |
| `ge` | Greater than or equal |
| `sa` | Starts after |
| `eb` | Ends before |

### Number/Quantity

Numeric searches:

```bash
GET /Observation?value-quantity=gt100
GET /Observation?value-quantity=100||mg
GET /Observation?value-quantity=ge5.4|http://unitsofmeasure.org|mg
```

### URI

URI matching:

```bash
GET /ValueSet?url=http://example.org/ValueSet/my-codes
GET /ValueSet?url:below=http://example.org/
```

## Search Modifiers

### Common Modifiers

| Modifier | Applicable Types | Example |
|----------|-----------------|---------|
| `:missing` | All | `?birthdate:missing=true` |
| `:exact` | String | `?name:exact=Smith` |
| `:contains` | String | `?name:contains=mit` |
| `:text` | Token | `?code:text=weight` |
| `:not` | Token | `?status:not=cancelled` |
| `:above` | Token, URI | `?code:above=http://snomed.info/sct|123` |
| `:below` | Token, URI | `?code:below=http://snomed.info/sct|123` |
| `:identifier` | Reference | `?subject:identifier=MRN123` |
| `:of-type` | Token | `?identifier:of-type=http://...|MRN|123` |

## Chained Parameters

Search on referenced resource properties:

```bash
# Single chain
GET /Observation?subject.name=Smith

# Multiple chains
GET /Observation?subject.organization.name=Hospital

# Reverse chain
GET /Patient?_has:Observation:subject:code=29463-7
```

## Include/Revinclude

Return related resources:

```bash
# Forward include
GET /Observation?_include=Observation:subject

# Recursive include
GET /Observation?_include:iterate=Observation:subject

# Reverse include
GET /Patient?_revinclude=Observation:subject
```

### Paginated Includes

Control the number of included resources returned separately from primary matches:

```bash
# Limit primary results to 10, includes to 50
GET /Patient?_include=Patient:organization&_count=10&_includesCount=50

# Response contains:
# - Up to 10 patients (primary matches)
# - Up to 50 organizations (included resources)
# - "related" link with _includesContinuationToken if more includes exist
```

| Parameter | Description |
|-----------|-------------|
| `_includesCount` | Maximum number of included resources per page (separate from `_count`) |
| `_includesContinuationToken` | Continuation token for fetching additional included resources |

When `_includesCount` is specified:
- The Bundle will contain up to `_count` primary matches
- The Bundle will contain up to `_includesCount` included resources
- If more includes exist, a "related" link is added with `_includesContinuationToken`
- Use the `$includes` operation to fetch additional included resources

See [$includes operation](/docs/server/fhir/operations#includes) for details on fetching additional included resources.

## Result Modifiers

### Sorting

```bash
GET /Patient?_sort=birthdate
GET /Patient?_sort=-birthdate          # Descending
GET /Patient?_sort=family,given        # Multiple
```

### Paging

```bash
GET /Patient?_count=50
GET /Patient?_count=50&_offset=100
```

### Total Count

```bash
GET /Patient?_total=accurate    # Exact count
GET /Patient?_total=estimate    # Estimated
GET /Patient?_total=none        # No count
```

### Summary

```bash
GET /Patient?_summary=true      # Summary only
GET /Patient?_summary=text      # Text narrative
GET /Patient?_summary=data      # Data only
GET /Patient?_summary=count     # Count only
```

### Elements

```bash
GET /Patient?_elements=name,birthDate,gender
```

## Composite Parameters

Multiple values in conjunction:

```bash
GET /Observation?component-code-value-quantity=http://loinc.org|8480-6$gt100
```

## Special Search Parameters

### `_not-referenced` - Finding Orphaned Resources

The `_not-referenced` parameter finds resources that are not referenced by any other resource. This is useful for:
- Data cleanup workflows
- Identifying orphaned resources after deletions
- Data quality audits
- Compliance reporting

**Syntax:**

```bash
# Find all patients not referenced by any resource
GET /Patient?_not-referenced=*:*

# Find patients not referenced by any Observation
GET /Patient?_not-referenced=Observation:*

# Find patients not referenced by Observation.subject specifically
GET /Patient?_not-referenced=Observation:subject
```

**Patterns:**

| Pattern | Meaning |
|---------|---------|
| `*:*` | Not referenced by any resource via any reference path |
| `{ResourceType}:*` | Not referenced by the specified resource type via any path |
| `{ResourceType}:{path}` | Not referenced via the specific reference path |

**Implementation Notes:**
- Uses LEFT ANTI-JOIN SQL pattern for optimal performance
- Only available with SQL storage provider (not in-memory)
- Compatible with Microsoft FHIR Server syntax

**Example Use Cases:**

```bash
# Find patients without any observations
GET /Patient?_not-referenced=Observation:subject

# Find practitioners not assigned to any organization
GET /Practitioner?_not-referenced=Organization:*

# Find all completely orphaned diagnostic reports
GET /DiagnosticReport?_not-referenced=*:*
```

## Batch Search

Search via POST with `_search`:

```bash
POST /Patient/_search
Content-Type: application/x-www-form-urlencoded

family=Smith&given=John
```

## Search in Bundles

```json
{
  "resourceType": "Bundle",
  "type": "batch",
  "entry": [{
    "request": {
      "method": "GET",
      "url": "Patient?family=Smith"
    }
  }]
}
```

## Performance Tips

1. **Use indexed parameters** - Search on indexed fields for best performance
2. **Limit results** - Use `_count` to limit page size
3. **Avoid wildcards** - Avoid leading wildcards in string searches
4. **Chain sparingly** - Deep chains impact performance

## Related Documentation

- [Supported Resources](/docs/server/fhir/supported-resources)
- [Operations](/docs/server/fhir/operations)
