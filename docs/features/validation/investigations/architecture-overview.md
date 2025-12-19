# Investigation: FastPathValidator Architecture Overview

**Feature**: validation
**Status**: Complete
**Created**: 2025-10-12
**Original ADR**: N/A

## Request Flow: Standalone Operation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CLIENT REQUEST                                     │
│                     PUT /Patient/123 (JSON body)                            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         API LAYER (Sparky.Api)                              │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  FhirEndpoints.HandlePutResource                                   │    │
│  │  1. Parse JSON to ResourceJsonNode                                 │    │
│  │  2. Cache ToSourceNode() conversion (ResourceJsonNode.ToSourceNode)│    │
│  │  3. Validate resource type matches                                 │    │
│  │  4. Create CreateOrUpdateResourceCommand(sourceNode, rawJson)      │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
                    IMediator.SendAsync(command)
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                     APPLICATION LAYER (Sparky.Application)                  │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  CreateOrUpdateResourceHandler.HandleAsync                         │    │
│  │                                                                     │    │
│  │  ┌──────────────────────────────────────────────────────────────┐ │    │
│  │  │  VALIDATION POINT (INPUT ONLY)                               │ │    │
│  │  │  1. var result = _validator.Validate(command.Resource)       │ │    │
│  │  │  2. if (!result.IsValid)                                     │ │    │
│  │  │     throw new ValidationException(result)                    │ │    │
│  │  └──────────────────────────────────────────────────────────────┘ │    │
│  │           ↓ (if valid)                                              │    │
│  │  3. CreateResourceWrapper (extract search indices)                 │    │
│  │  4. _repository.CreateOrUpdateAsync(wrapper)                       │    │
│  └────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  FastPathValidator (Singleton)                                     │    │
│  │  - IStructureDefinitionSummaryProvider (R4/R4B/R5/STU3)           │    │
│  │  - ConcurrentDictionary<string, ValidationRuleSet> (rule cache)    │    │
│  │  - Validate(ISourceNode) → ValidationResult                        │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
              SUCCESS: Return ResourceKey (201 Created)
                        OR
              FAILURE: throw ValidationException → catch by middleware
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                    MIDDLEWARE (Sparky.Api.Middleware)                       │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  FhirExceptionMiddleware                                           │    │
│  │  1. Catch ValidationException                                      │    │
│  │  2. Extract OperationOutcome from exception                        │    │
│  │  3. Serialize to JSON (FhirJsonSerializer)                         │    │
│  │  4. Return HTTP 400 Bad Request + OperationOutcome                 │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CLIENT RESPONSE                                      │
│  SUCCESS: 201 Created + ETag: "1"                                           │
│  FAILURE: 400 Bad Request + OperationOutcome (validation issues)            │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Request Flow: Bundle Operation

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CLIENT REQUEST                                     │
│                     POST / (Bundle with entries)                            │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         API LAYER (Sparky.Api)                              │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  FhirEndpoints.HandleBundle                                        │    │
│  │  1. StreamingBundleParser.ParseStreamAsync(body)                   │    │
│  │  2. Determine bundle type (Transaction vs Batch)                   │    │
│  │  3. Create BundleProcessingOptions                                 │    │
│  │  4. Send to BundleProcessor                                        │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                     APPLICATION LAYER (Sparky.Application)                  │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  BundleProcessor.ProcessAsync                                      │    │
│  │  1. Create DeferredWriteCoordinator (for Transaction bundles)      │    │
│  │  2. BundleChannelExecutor.ExecuteEntriesAsync                      │    │
│  │     ├─ Parallel execution (10 workers)                             │    │
│  │     └─ For each entry → BundleEntryExecutor                        │    │
│  └────────────────────────────────────────────────────────────────────┘    │
│                                                                             │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  BundleEntryExecutor.ExecuteAsync                                  │    │
│  │  1. Create mini HttpContext                                        │    │
│  │  2. Set DeferredWriteCoordinator in HttpContext.Items              │    │
│  │  3. Route through AspNetCorePipelineExecutor                       │    │
│  │     └─ Simulates ASP.NET Core endpoint routing                     │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
            Routes back to FhirEndpoints.HandlePutResource
                 (same as standalone operation)
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  CreateOrUpdateResourceHandler.HandleAsync                                  │
│  ┌──────────────────────────────────────────────────────────────────┐      │
│  │  VALIDATION POINT (INPUT ONLY)                                   │      │
│  │  1. var result = _validator.Validate(command.Resource)           │      │
│  │  2. if (!result.IsValid)                                         │      │
│  │     throw ValidationException → caught by BundleEntryExecutor    │      │
│  └──────────────────────────────────────────────────────────────────┘      │
│           ↓ (if valid)                                                      │
│  3. Detect DeferredWriteCoordinator from HttpContext.Items                  │
│  4. coordinator.QueueWriteAsync(wrapper) (deferred batch write)             │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│  BundleProcessor.ProcessAsync (continued)                                   │
│  1. Await all entry executions                                              │
│  2. DeferredWriteCoordinator.FlushAsync() (batch write to repository)       │
│  3. Build Bundle response with individual entry statuses                    │
│     ├─ Entry[0]: 201 Created                                                │
│     ├─ Entry[1]: 400 Bad Request (validation failed)                        │
│     └─ Entry[2]: 201 Created                                                │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CLIENT RESPONSE                                      │
│  200 OK + Bundle (type: transaction-response or batch-response)             │
│  - Each entry.response includes status and OperationOutcome if failed       │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Request Flow: GET Operation (NO Validation)

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           CLIENT REQUEST                                     │
│                     GET /Patient/123                                        │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         API LAYER (Sparky.Api)                              │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  FhirEndpoints.HandleGetResource                                   │    │
│  │  1. Create GetResourceQuery(resourceType, id)                      │    │
│  │  2. Send to GetResourceHandler                                     │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                     APPLICATION LAYER (Sparky.Application)                  │
│  ┌────────────────────────────────────────────────────────────────────┐    │
│  │  GetResourceHandler.HandleAsync                                    │    │
│  │  1. _repository.GetAsync(resourceType, id)                         │    │
│  │  2. Return ResourceWrapper with RawJson                            │    │
│  │                                                                     │    │
│  │  ⚠️  NO VALIDATION - Resources from storage are trusted            │    │
│  └────────────────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────────────────┘
                                    ↓
┌─────────────────────────────────────────────────────────────────────────────┐
│                         CLIENT RESPONSE                                      │
│  200 OK + Resource JSON (from ResourceWrapper.RawJson)                      │
│  + ETag: "W/1"                                                              │
│  + Last-Modified: 2025-10-12T...                                            │
└─────────────────────────────────────────────────────────────────────────────┘
```

## Component Relationships

```
┌──────────────────────────────────────────────────────────────────────────┐
│                        Dependency Graph                                   │
└──────────────────────────────────────────────────────────────────────────┘

┌─────────────────────┐
│  Sparky.Api         │
│  - Program.cs       │ ──┐
│  - FhirEndpoints    │   │
│  - Middleware       │   │
└─────────────────────┘   │
           │              │
           │ references   │
           ↓              │
┌─────────────────────┐   │
│ Sparky.Application  │   │
│ - Handlers          │   │
│ - ValidationEx...   │   │
└─────────────────────┘   │
           │              │
           │ references   │
           ↓              │
┌─────────────────────┐   │
│ Sparky.Validation   │ ←─┘ (also referenced by Sparky.Api for middleware)
│ - FastPathValidator │
│ - ValidationResult  │
│ - ValidationIssue   │
└─────────────────────┘
           │
           │ references
           ↓
┌─────────────────────┐
│ Sparky.Specification│
│ - R4StructureDefini-│
│   tionSummaryProvider│
└─────────────────────┘
```

## Validation Decision Tree

```
                        Incoming Request
                              |
                    ┌─────────┴─────────┐
                    │                   │
               GET Request         PUT/POST Request
                    │                   │
                    │                   │
            ┌───────┴───────┐   ┌──────┴──────┐
            │               │   │             │
        Retrieve from   Return JSON   Parse to ISourceNode
         Repository         │              │
            │               │              │
            │               │         Validate with
            │               │       FastPathValidator
            │               │              │
            │               │      ┌───────┴───────┐
            │               │      │               │
            │               │   Valid         Invalid
            │               │      │               │
            │               │   Process        throw
            │               │   Resource    ValidationException
            │               │      │               │
            └───────────────┴──────┴───────────────┘
                              |
                         Return to Client
```

## Validation Scope

```
┌─────────────────────────────────────────────────────────────────┐
│                    Validation Coverage                           │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ✅ VALIDATED (Input Only)                                       │
│  ├─ PUT /{resourceType}/{id}                                    │
│  ├─ POST /{resourceType}                                        │
│  ├─ POST / (Bundle entries)                                     │
│  └─ Bundle transaction/batch entries                            │
│                                                                  │
│  ❌ NOT VALIDATED (Output/Trusted)                               │
│  ├─ GET /{resourceType}/{id}                                    │
│  ├─ GET /{resourceType} (search results)                        │
│  ├─ Repository.GetAsync() results                               │
│  └─ Internal resource operations                                │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Performance Characteristics

```
┌─────────────────────────────────────────────────────────────────┐
│           FastPathValidator Performance Profile                  │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  First Validation (Resource Type):                              │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ Parse Schema → Build Rules → Cache → Validate          │    │
│  │ [----15ms----][----30ms-----][--5ms--][----10ms---]    │    │
│  │ Total: ~60ms (one-time cost per resource type)         │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                  │
│  Subsequent Validations (Same Resource Type):                   │
│  ┌────────────────────────────────────────────────────────┐    │
│  │ Lookup Rules → Validate                                 │    │
│  │ [---<1ms----][----15ms---]                              │    │
│  │ Total: ~15-20ms (rule cache hit)                        │    │
│  └────────────────────────────────────────────────────────┘    │
│                                                                  │
│  Memory Overhead:                                                │
│  ├─ FastPathValidator singleton: ~100 KB                        │
│  ├─ Rule cache per resource type: ~5-10 KB                      │
│  └─ Total (100 resource types): ~1 MB                           │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

## Error Response Structure

```json
// Validation Failure Response (HTTP 400 Bad Request)
{
  "resourceType": "OperationOutcome",
  "issue": [
    {
      "severity": "error",
      "code": "invalid",
      "diagnostics": "Required element 'status' is missing",
      "expression": ["Observation.status"]
    },
    {
      "severity": "error",
      "code": "invalid",
      "diagnostics": "Invalid date format: 'not-a-date'. Expected YYYY-MM-DD",
      "expression": ["Patient.birthDate"]
    },
    {
      "severity": "warning",
      "code": "invalid",
      "diagnostics": "Coding should have at least a system or code",
      "expression": ["Patient.maritalStatus.coding"]
    }
  ]
}
```

## Key Design Principles

1. **Single Point of Validation**: Application layer handler (not API layer)
2. **Input Validation Only**: Trust stored resources, validate incoming data
3. **Performance First**: Singleton validator, rule caching, cached ISourceNode
4. **Consistent Error Handling**: ValidationException → FhirExceptionMiddleware → OperationOutcome
5. **Bundle Support Automatic**: Same handler for standalone and bundle operations
6. **Version Agnostic**: Works with R4, R4B, R5, STU3 via IStructureDefinitionSummaryProvider

## Related Documentation

- **Implementation Summary**: `VALIDATION_INTEGRATION_SUMMARY.md`
- **FastPathValidator Investigation**: `docs/investigations/fast-path-validation.md`
- **Test Suite**: `test/Sparky.Validation.Tests/`
- **Manual Tests**: `test-validation.http`
