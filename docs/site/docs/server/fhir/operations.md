---
sidebar_position: 4
title: Operations
description: FHIR operations supported by Ignixa
---

# Operations

Ignixa supports standard FHIR operations for validation, bulk data, patient access, and terminology.

## Core Operations

### $de-identify

[DARTS Spec](http://hl7.org/fhir/us/darts/OperationDefinition/de-identify)

De-identify a FHIR resource using DARTS policy configurations. Supports HIPAA Safe Harbor and Expert Determination methods.

```bash
# Tenant-scoped de-identify (FHIR standard Parameters format)
POST /tenant/{tenantId}/$de-identify
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "resource",
      "resource": {
        "resourceType": "Patient",
        "id": "example",
        "name": [{ "family": "Smith", "given": ["John"] }],
        "birthDate": "1980-05-15",
        "address": [{ "city": "Boston", "state": "MA" }]
      }
    },
    {
      "name": "policy",
      "valueString": "HHS_SAFE_HARBOR_DETERMINISTIC_METHOD"
    }
  ]
}
```

#### Request Formats

The operation accepts two request formats:

**Parameters resource** (FHIR standard):
- `resource` (Resource, required) — The FHIR resource to de-identify
- `policy` (string, optional) — Built-in de-identification policy. Defaults to `HHS_SAFE_HARBOR_DETERMINISTIC_METHOD`
- `configuration` (Library, optional) — Custom de-identification configuration as a FHIR Library resource. When provided, `policy` is ignored.

**Direct resource body** (convenience):
```bash
POST /tenant/{tenantId}/$de-identify?policy=HHS_SAFE_HARBOR_DETERMINISTIC_METHOD
Content-Type: application/fhir+json

{
  "resourceType": "Patient",
  "id": "example",
  "name": [{ "family": "Smith", "given": ["John"] }]
}
```

#### Policies

| Policy | Description |
|--------|-------------|
| `HHS_SAFE_HARBOR_DETERMINISTIC_METHOD` | HIPAA Safe Harbor — redacts direct identifiers, date-shifts dates |
| `HHS_EXPERT_DETERMINATION_METHOD` | Expert Determination — more aggressive redaction with fail-fast error handling |

#### Custom Configuration

Pass a custom de-identification configuration as a FHIR Library resource:

```bash
POST /tenant/{tenantId}/$de-identify
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "resource",
      "resource": {
        "resourceType": "Patient",
        "id": "example",
        "name": [{ "family": "Smith", "given": ["John"] }]
      }
    },
    {
      "name": "configuration",
      "resource": {
        "resourceType": "Library",
        "id": "custom-deid-policy",
        "status": "active",
        "type": {
          "coding": [
            {
              "system": "http://ignixa.io/library-types",
              "code": "deid-configuration"
            }
          ]
        },
        "content": [
          {
            "contentType": "application/json",
            "data": "eyJmaGlyVmVyc2lvbiI6IlI0IiwiZmhpcmVQYXRoUnVsZXMiOlt7InBhdGgiOiJQYXRpZW50Lm5hbWUiLCJtZXRob2QiOiJyZWRhY3QifSx7InBhdGgiOiJQYXRpZW50LmFkZHJlc3MiLCJtZXRob2QiOiJyZWRhY3QifV19"
          }
        ]
      }
    }
  ]
}
```

The Library `content` data is a base64-encoded JSON configuration following the [DeId Core SDK](/docs/core-sdk/deid) format.

#### Response

Returns the de-identified resource with security labels added to `meta.security`:

```json
{
  "resourceType": "Patient",
  "id": "a3f7...",
  "meta": {
    "security": [
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "REDACTED",
        "display": "redacted"
      },
      {
        "system": "http://terminology.hl7.org/CodeSystem/v3-ObservationValue",
        "code": "CRYTOHASH",
        "display": "cryptographic hash function"
      }
    ]
  }
}
```

See [DeId Core SDK](/docs/core-sdk/deid) for configuration format and supported de-identification methods.

### $validate

[FHIR Spec](https://hl7.org/fhir/resource-operation-validate.html)

Validate a resource against FHIR specifications and profiles:

```bash
# Type-level validation
POST /Patient/$validate
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "resource",
    "resource": {
      "resourceType": "Patient",
      "name": [{ "family": "Smith" }]
    }
  }]
}
```

#### Validation Modes

```bash
# Validate against a profile
POST /Patient/$validate?profile=http://hl7.org/fhir/us/core/StructureDefinition/us-core-patient

# Validation mode (create, update, delete)
POST /Patient/$validate?mode=create
```

#### Validation Depth

Control validation depth via Prefer header:

```bash
POST /Patient/$validate
Prefer: mode=minimal   # Structure only
Prefer: mode=spec      # FHIR spec compliance (default)
Prefer: mode=full      # Full profile validation with terminology
```

### $everything

[FHIR Spec](https://hl7.org/fhir/patient-operation-everything.html)

Retrieve all data for a patient:

```bash
GET /Patient/{id}/$everything
GET /Patient/{id}/$everything?start=2024-01-01&end=2024-12-31
GET /Patient/{id}/$everything?_type=Observation,Condition
GET /Patient/{id}/$everything?_since=2024-01-01T00:00:00Z
GET /Patient/{id}/$everything?_count=100
```

### $member-match

[Da Vinci HRex Spec](https://hl7.org/fhir/us/davinci-hrex/OperationDefinition-member-match.html)

Match patients across different payer systems (HRex specification):

```bash
POST /Patient/$member-match
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "MemberPatient",
      "resource": {
        "resourceType": "Patient",
        "identifier": [{ "system": "http://example.org", "value": "12345" }],
        "name": [{ "family": "Smith", "given": ["John"] }]
      }
    },
    {
      "name": "CoverageToMatch",
      "resource": {
        "resourceType": "Coverage",
        "status": "active",
        "beneficiary": { "reference": "Patient/member" }
      }
    }
  ]
}
```

## Bulk Data Operations

| Operation | Spec | Description |
|-----------|------|-------------|
| `$export` | [Bulk Data](https://hl7.org/fhir/uv/bulkdata/export.html) | Async export to NDJSON or Parquet |
| `$import` | [Bulk Data](https://hl7.org/fhir/uv/bulkdata/import.html) | Async bulk import from NDJSON |

### $export

Start an asynchronous bulk export operation:

```bash
# System-level export (auto-detect tenant)
POST /$export

# Tenant-scoped export
POST /tenant/{tenantId}/$export

# Group-scoped export (members only)
POST /Group/{groupId}/$export

# Query parameters
POST /tenant/{tenantId}/$export?_type=Patient,Observation&_since=2024-01-01&_outputFormat=ndjson
```

Supported parameters:
- `_type` - Comma-separated list of resource types to export
- `_since` - Only include resources modified after this date
- `_typeFilter` - Advanced resource filtering
- `_outputFormat` - `ndjson` (default) or `parquet`
- `_viewDefinition` - SQL on FHIR ViewDefinition for Parquet output

Returns `202 Accepted` with `Content-Location` header pointing to the status endpoint.

### $import

Start an asynchronous bulk import operation:

```bash
# Tenant-scoped import
POST /tenant/{tenantId}/$import
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "inputFormat",
      "valueCode": "application/fhir+ndjson"
    },
    {
      "name": "inputSource",
      "valueUri": "https://example.org/export/patients.ndjson"
    },
    {
      "name": "mode",
      "valueCode": "IncrementalLoad"
    }
  ]
}
```

Returns `202 Accepted` with `Content-Location` header to poll job status.

See [Bulk Operations](/docs/server/features/bulk-operations) for detailed usage, parameters, and configuration.

## Search Operations

### $includes

Fetch additional included resources from a search with independent pagination. When using `_include` or `_revinclude`, large result sets can cause performance issues. The `$includes` operation allows clients to paginate through included resources separately from primary search matches.

```bash
# Initial search with limited includes
GET /Patient?_include=Patient:organization&_count=10&_includesCount=50

# Response includes:
# - Up to 10 patients (primary matches)
# - Up to 50 organizations (included resources)
# - "related" link if more organizations exist

# Follow "related" link for more includes
GET /Patient/$includes?_includesContinuationToken=xyz123&_include=Patient:organization
```

#### Parameters

| Parameter | Type | Description |
|-----------|------|-------------|
| `_includesContinuationToken` | string | **Required**. Continuation token from previous search "related" link |
| `_include` | string | Include parameters (inherited from original search) |
| `_revinclude` | string | Reverse include parameters (inherited from original search) |
| `_includesCount` | integer | Maximum number of included resources per page (optional) |

#### Behavior

The `$includes` operation:
- Returns a Bundle with **only** included resources (no primary matches)
- Uses standard "next" link for additional pages of includes
- Requires `_includesContinuationToken` parameter from initial search
- Re-executes the search but filters to include entries only

#### Use Cases

1. **Large include sets**: When a search has many primary matches, each with related resources
2. **Progressive loading**: Load primary results first, then fetch related resources on demand
3. **Performance optimization**: Prevent response size explosion from large include sets

#### Example Flow

```bash
# 1. Initial search with _includesCount
GET /Observation?subject=Patient/123&_include=Observation:performer&_includesCount=25
# Returns: 100 observations + 25 practitioners (if more practitioners exist, includes "related" link)

# 2. Fetch next page of practitioners
GET /Observation/$includes?_includesContinuationToken=abc...&_include=Observation:performer
# Returns: Next 25 practitioners (no observations)

# 3. Continue until no "next" link
GET /Observation/$includes?_includesContinuationToken=def...&_include=Observation:performer
# Returns: Remaining practitioners
```

## Experimental Operations

These operations are available when experimental features are enabled.

### $summary (IPS)

[IPS Spec](https://hl7.org/fhir/uv/ips/OperationDefinition-summary.html)

Generate an International Patient Summary:

```bash
# By patient ID (GET)
GET /Patient/{id}/$summary

# By patient ID (POST)
POST /Patient/{id}/$summary

# By patient identifier (GET)
GET /Patient/$summary?identifier=http://example.org|12345

# With specific profile
GET /Patient/{id}/$summary?profile=http://hl7.org/fhir/uv/ips/StructureDefinition/Bundle-uv-ips
```

### Package-backed terminology imports

Package administration uses tenant-explicit routes:

- `GET /tenant/{tenantId}/admin/packages` lists loaded packages.
- `POST /tenant/{tenantId}/admin/packages/load` loads a package.
- `DELETE /tenant/{tenantId}/admin/packages/{packageId}/{version}` unloads a package.

`tenantId` must be an integer identifying an active tenant. System partition `0` remains inaccessible
through these routes. Package administration paths take precedence over generic FHIR resource routes.

Package content in tenant 1 and terminology in system partition 0 must use the **same SQL database**.
The server checks the resolved SQL server/database destination before shared package writes and
terminology imports. Connection-string inheritance and aliases resolving to that same destination are
supported; separate package and terminology databases are rejected before mutation. Other tenants may
use their own SQL databases or FileSystem resource storage. The identity check uses a short-lived
database-local application lock, writes no content rows, and needs two concurrent SQL connections
(the default connection pool supports this).

CodeSystem, ValueSet and ConceptMap imports replace by **case-sensitive canonical URL and resource version**,
not by package-row identity. URL paths and version labels differing only by case identify different content.
An omitted resource version identifies the unversioned canonical. The last
successful import owns that terminology content, including when it arrives in a newer package version.
Earlier package rows retain their completed import history, so unchanged startup/load retries do not
replace newer content with an older package.

New terminology jobs persist an in-package dependency plan before they start. ValueSet compose clauses
that read CodeSystem concepts (whole-system includes and filtered includes/excludes) wait for matching
CodeSystems. Unfiltered whole-system exclusions do not require CodeSystem content and are not blocked
by that CodeSystem's import failure. ValueSet references in includes or excludes wait for their in-job
ValueSets; a `url|version` reference matches only that exact, case-sensitive URL and business version.
An unversioned reference retains the most-recently-imported expanded ValueSet policy.
Precomputed expansions bypass compose, and absent external dependencies
retain the existing partial-expansion behavior. At most five resource activities are scheduled at once.
Failed internal prerequisites and unresolved ValueSet cycles leave dependents failed and retryable instead
of recording an incomplete successful import. Package-load notifications and startup retry scans both use
this job-creation path.

Within one compose include or exclude clause, the system/version selection, explicit concepts or filters,
and all referenced ValueSets are intersected. Different include clauses are unioned, and each exclude
clause subtracts its selection. Specifying both `concept` and `filter` in one clause is invalid and fails
the import. Referenced partial expansions propagate their incomplete state and reasons through both
includes and excludes, including when no known codes remain. A required system version is not guessed
for referenced codes that omit it; such restrictions are reported as incomplete. Whole-system exclusions
still operate on included codes without requiring the CodeSystem's content.

Version restrictions carried by referenced expansions have the same uncertainty rules as explicit
restrictions: intersections do not invent a pinned version for unknown-version codes, and pinned
exclusions retain unknown-version codes while marking the result incomplete. Unqualified concept
selections and exclusions keep their all-version behavior. Set intersection, subtraction, and
deduplication honor the imported CodeSystem's `caseSensitive` policy for the applicable version,
without changing code spelling or the case-sensitive canonical/version identity rules. Comparison
metadata is read once per composition, not per code. If an unknown version could select conflicting
case policies, the result is incomplete rather than guessing which policy applies.

Persisted jobs without a dependency plan replay their original activity sequence. This does not rewrite
already completed legacy partial expansions, and dependency ordering is scoped to the resources in one
package job rather than coordinating independently running package jobs.

Replacement data, hierarchy, completion status and content hash commit together. Failed replacements
record a failed attempt and remain retryable; previously usable terminology stays available. Effective
terminology routing continues using the surviving SQL content rather than switching to fallback merely
because a replacement failed. Cancellation propagates separately from an import failure. A lost connection
at commit can still leave an uncertain outcome; inspect the recorded import status before retrying.

SQL-backed lookup, expansion, and validation results are not memoized in application memory. A request
started after replacement commits reads current SQL data, including previously missing concepts and
dependent binding/display decisions, without a TTL delay or process-local invalidation event. This also
applies when another server process imports the replacement. An operation overlapping a commit can observe
different committed states across its SQL statements; there is no operation-wide snapshot guarantee, but
an older in-flight result cannot repopulate a shared result cache.

The tradeoff is additional database I/O compared with a warm memory-cache hit. Within the SQL terminology
service, common exact-match paths perform two reads for lookup, three for a nonempty expansion, and three
for ValueSet validation with an explicit system. Hybrid routing adds its import-status query;
case-insensitive fallback and binding checks may require more. Reference-data and immutable specification
caches are unaffected.

ValueSet and ConceptMap `name` is optional and is stored as SQL `NULL` when absent. This requires schema
version 3; upgrading preserves existing resource identities, search-index references and terminology.
CodeSystems with `content=not-present` or `content=supplement` remain skipped with a recorded hash, rather
than being retried on every startup.

### $expand (ValueSet)

[FHIR Spec](https://hl7.org/fhir/valueset-operation-expand.html)

Expand a ValueSet to a list of codes:

```bash
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&filter=blood
GET /ValueSet/$expand?url=http://hl7.org/fhir/ValueSet/observation-codes&count=100&offset=0
```

An unknown or unavailable ValueSet returns HTTP 404 with a FHIR `OperationOutcome`.
Operation-level missing-parameter errors from `$expand`, `$translate`, and `$subsumes` return
HTTP 400 with a FHIR `OperationOutcome`. These error responses use `application/fhir+json`
on both tenant-explicit and tenant-agnostic routes.

### $translate (ConceptMap)

[FHIR Spec](https://hl7.org/fhir/conceptmap-operation-translate.html)

Translate codes between systems using ConceptMap:

```bash
POST /ConceptMap/$translate
Content-Type: application/fhir+json

{
  "code": "123",
  "system": "http://source.org",
  "url": "http://example.org/ConceptMap/my-map"
}
```

### $subsumes (CodeSystem)

[FHIR Spec](https://hl7.org/fhir/codesystem-operation-subsumes.html)

Test subsumption relationship between codes:

```bash
POST /CodeSystem/$subsumes
Content-Type: application/fhir+json

{
  "codeA": "parent-code",
  "codeB": "child-code",
  "system": "http://example.org/CodeSystem/my-codes"
}
```

### $transform (StructureMap)

[FHIR Spec](https://hl7.org/fhir/structuremap-operation-transform.html)

Transform data using a StructureMap:

```bash
# Using a stored StructureMap
POST /StructureMap/{id}/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [{
    "name": "content",
    "resource": { ... }
  }]
}

# Using an inline StructureMap
POST /StructureMap/$transform
Content-Type: application/fhir+json

{
  "resourceType": "Parameters",
  "parameter": [
    {
      "name": "sourceMap",
      "resource": { "resourceType": "StructureMap", ... }
    },
    {
      "name": "content",
      "resource": { ... }
    }
  ]
}
```

## Not Yet Implemented

The following operations are planned but not yet available:

- [`$document`](https://hl7.org/fhir/composition-operation-document.html) - Generate document from Composition
- [`$validate-code`](https://hl7.org/fhir/valueset-operation-validate-code.html) - Validate code in ValueSet
- [`$lookup`](https://hl7.org/fhir/codesystem-operation-lookup.html) - CodeSystem code lookup
- [`$snapshot`](https://hl7.org/fhir/structuredefinition-operation-snapshot.html) - Generate StructureDefinition snapshot

## Related Documentation

- [Bulk Operations](/docs/server/features/bulk-operations)
- [Validation](/docs/server/features/validation)
