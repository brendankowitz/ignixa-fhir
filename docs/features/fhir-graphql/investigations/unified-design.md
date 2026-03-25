# Investigation: Unified FHIR $graphql Implementation Design

**Feature**: fhir-graphql
**Status**: Complete
**Created**: 2026-03-25

## Executive Summary

This document is the **definitive implementation plan** for FHIR `$graphql` in Ignixa, reconciling two competing design proposals:

- **Proposal A** (HotChocolate v15): Superior dynamic schema generation via `ITypeModule`, compiled resolvers, built-in DataLoader, named schemas per FHIR version.
- **Proposal B** (GraphQL.NET): Cleaner CQRS-first resolver pattern via MediatR, explicit execution service abstraction, richer schema caching with package-event invalidation, true resource-root instance semantics.

### Reconciliation Decisions

| Decision Point | Winner | Rationale |
|----------------|--------|-----------|
| **GraphQL Library** | HotChocolate v15 | `ITypeModule` is purpose-built for dynamic schema generation from metadata — exactly our use case. Compiled resolvers, built-in DataLoader, native ASP.NET Core integration. |
| **Resolver Pattern** | Proposal B (CQRS-first) | Resolvers call `IMediator.SendAsync()` through existing `GetResourceQuery` / `SearchResourcesQuery` handlers. Preserves capability enforcement, partition strategy, audit logging. |
| **Instance-Level Semantics** | Proposal B | Load resource first, set as root value via HotChocolate global state. True resource-root per FHIR spec. |
| **Schema Caching** | Merged | HotChocolate's `IRequestExecutorResolver` for named schemas + explicit cache invalidation via package-loaded events. |
| **Configuration** | Merged | Proposal A's richer options + Proposal B's `EnableGetRequests`. |
| **File Organization** | Merged | Proposal B's granular directories + Proposal A's HotChocolate-specific types. |
| **Field Resolution** | Proposal A | `JsonElement` tree navigation for zero-copy field access from raw bytes. |

### Estimated Effort

| Phase | Scope | Estimate |
|-------|-------|----------|
| Phase 1 | Core schema generation + instance reads | 30-40 hours |
| Phase 2 | Search resolver + pagination + search params | 25-30 hours |
| Phase 3 | Reference resolution + multi-version + hardening | 25-30 hours |
| **Total** | | **80-100 hours** |

---

## 1. Library Selection Rationale

### Final Comparison

| Criterion | HotChocolate v15 ✅ | GraphQL.NET |
|-----------|---------------------|-------------|
| **Dynamic schema** | `ITypeModule` — first-class hook for runtime type generation from metadata | Manual `Schema.For()` with programmatic builders |
| **.NET 9 support** | First-class; targets `net9.0` | Supported but community-driven |
| **Compiled resolvers** | Yes — sub-microsecond dispatch after startup | No — reflection-based |
| **DataLoader** | Built-in `BatchDataLoader<TKey, TValue>` | Separate `GraphQL.DataLoader` package |
| **ASP.NET integration** | `MapGraphQL()` middleware, `IHttpRequestInterceptor` | `AddGraphQL()` but less pipeline control |
| **Named schemas** | `IRequestExecutorResolver` — one schema per FHIR version | Manual schema registry needed |
| **Complexity analysis** | Built-in query cost/depth limiting | Community package |
| **Directives** | `@skip`, `@include`, `@defer`, `@stream` | `@skip`, `@include` only |
| **Introspection** | Standard `__schema`/`__type` out of the box | Standard |
| **License** | MIT | MIT |
| **NuGet downloads** | ~85M total | ~45M total |

### Why HotChocolate Wins

1. **`ITypeModule`** is the decisive factor. FHIR has ~150 resource types × ~20 elements each = ~3000+ GraphQL types. Hand-coding these is infeasible. `ITypeModule.CreateTypesAsync()` walks `ISchema` metadata and emits all types programmatically at schema build time.

2. **Named schemas** via `IRequestExecutorResolver` cleanly handle multi-version FHIR (STU3, R4, R4B, R5, R6) without manual schema management.

3. **Built-in `BatchDataLoader`** solves the reference N+1 problem inherent in FHIR GraphQL queries without additional packages.

### What We Take from Proposal B (GraphQL.NET)

Even though we use HotChocolate as the library, we adopt these architectural patterns from Proposal B:

- **CQRS-first resolvers**: Resolvers call `IMediator.SendAsync(new GetResourceQuery(...))` instead of `IFhirRepository` directly. This preserves capability enforcement, partition resolution, and audit logging.
- **Explicit execution service abstraction**: `IGraphQlExecutionService` wraps HotChocolate execution for clean testability.
- **Schema cache invalidation**: Hook into `PackageLoadedEvent` to evict stale schemas when conformance resources change.
- **Instance-level root semantics**: Pre-load the resource and set it as the GraphQL execution root value.
- **`EnableGetRequests` config option**: Allow disabling GET transport in production.

### NuGet Packages

```xml
<PackageReference Include="HotChocolate.AspNetCore" Version="15.*" />
```

No dependency on `Hl7.Fhir.*` — all FHIR metadata comes from `Ignixa.Abstractions` (`ISchema`, `ITypeExtended`), respecting layer dependency rules.

---

## 2. Architecture Overview

### Layer Mapping

```
┌──────────────────────────────────────────────────────────────────────────┐
│  API Layer (Ignixa.Api)                                                  │
│  ┌────────────────────────────────────────────────────────────────────┐  │
│  │  GraphQlEndpoints.cs                                               │  │
│  │  - POST /tenant/{tenantId}/$graphql          (system, tenant)      │  │
│  │  - GET  /tenant/{tenantId}/$graphql           (system, tenant)     │  │
│  │  - POST /tenant/{tenantId}/{Type}/{id}/$graphql (instance, tenant) │  │
│  │  - GET  /tenant/{tenantId}/{Type}/{id}/$graphql (instance, tenant) │  │
│  │  + Tenant-agnostic variants of all four                            │  │
│  └───────────────────────────┬────────────────────────────────────────┘  │
│                              │ Delegates to IGraphQlExecutionService     │
├──────────────────────────────┼───────────────────────────────────────────┤
│  Application Layer (Ignixa.Application.Features.Experimental.GraphQl/)   │
│  ┌───────────────────────────▼────────────────────────────────────────┐  │
│  │  IGraphQlExecutionService                                          │  │
│  │  └→ HotChocolate IRequestExecutorResolver (named schemas)          │  │
│  │     └→ FhirHttpRequestInterceptor (tenant + version context)       │  │
│  │        └→ Query Execution Pipeline                                 │  │
│  ├────────────────────────────────────────────────────────────────────┤  │
│  │  FhirTypeModule : ITypeModule (schema generation from ISchema)     │  │
│  │  ┌──────────────────────────────────────────────────────────────┐  │  │
│  │  │  • Resource types      → ObjectType (Patient, Observation…)  │  │  │
│  │  │  • BackboneElements    → Nested ObjectType (Patient_Contact) │  │  │
│  │  │  • Choice elements     → UnionType (Observation_ValueUnion)  │  │  │
│  │  │  • Reference fields    → ResourceReference type + Resource   │  │  │
│  │  │  • Primitives          → Scalar types (String, Int, Date…)   │  │  │
│  │  │  • Search parameters   → Query arguments on List fields      │  │  │
│  │  └──────────────────────────────────────────────────────────────┘  │  │
│  ├────────────────────────────────────────────────────────────────────┤  │
│  │  Resolvers (CQRS-first)                                            │  │
│  │  • ResourceResolver  → IMediator.SendAsync(GetResourceQuery)       │  │
│  │  • SearchResolver    → IMediator.SendAsync(SearchResourcesQuery)   │  │
│  │  • FieldResolver     → JsonElement tree navigation (zero-copy)     │  │
│  │  • ReferenceResolver → ResourceDataLoader (batched)                │  │
│  ├────────────────────────────────────────────────────────────────────┤  │
│  │  ResourceDataLoader : BatchDataLoader<ResourceKey, JsonElement?>   │  │
│  │  • Batches reference resolutions per request                       │  │
│  │  • Deduplicates identical keys automatically                       │  │
│  └────────────────────────────────────────────────────────────────────┘  │
├──────────────────────────────────────────────────────────────────────────┤
│  Domain Layer (Ignixa.Domain) — UNCHANGED                                │
│  • IFhirRepository, ISearchService, ISchema                              │
├──────────────────────────────────────────────────────────────────────────┤
│  DataLayer — UNCHANGED                                                   │
└──────────────────────────────────────────────────────────────────────────┘
```

**Key principle**: No changes to Domain or DataLayer. All GraphQL logic lives in:
- `Ignixa.Api` — endpoint registration (thin HTTP glue)
- `Ignixa.Application` — schema generation, resolvers, DataLoader (business logic)

### Request Flow

```
1. HTTP POST /tenant/1/$graphql  {"query": "{ Patient(id: \"123\") { name { text } } }"}
2. → TenantResolutionMiddleware (existing) sets TenantId=1, FhirVersion=R4
3. → GraphQlEndpoints.HandleSystemGraphQl() parses request body
4. → IGraphQlExecutionService.ExecuteAsync(query, variables, mode=System)
5.   → IRequestExecutorResolver.GetRequestExecutorAsync("fhir-r4")
6.   → FhirHttpRequestInterceptor injects tenant context into global state
7.   → HotChocolate executes query against compiled schema
8.     → Patient(id: "123") field resolver:
9.       → IMediator.SendAsync(new GetResourceQuery("Patient", "123"))
10.        → GetResourceHandler: partition strategy → IFhirRepository.GetAsync()
11.        → Returns SearchEntryResult (raw JSON bytes)
12.      → Parse to JsonElement for field-level resolution
13.    → name field resolver: JsonElement.GetProperty("name") → JsonElement[]
14.    → text field resolver: JsonElement.GetProperty("text") → "John Smith"
15. → Serialize GraphQL response as application/json
16. → Return { "data": { "Patient": { "name": [{ "text": "John Smith" }] } } }
```

---

## 3. Schema Generation

### 3.1 FhirTypeModule — Heart of the Design

HotChocolate's `ITypeModule` is invoked once during schema creation. We implement it to walk the full `ISchema` type graph and emit GraphQL types.

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Schema/FhirTypeModule.cs

// FhirTypeModule is registered as a keyed singleton in IServiceCollection (see §11.3).
// This allows both HotChocolate and the Autofac-registered PackageLoadedSchemaInvalidationHandler
// to reference the exact same instance, ensuring TypesChanged fires on the live schema.
public sealed class FhirTypeModule(
    Ignixa.Abstractions.ISchema fhirSchema,
    ILogger<FhirTypeModule> logger) : ITypeModule
{
    public event EventHandler<EventArgs>? TypesChanged;

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        // nestedTypes collects BackboneElement + Union types generated during element traversal.
        // MUST be a local variable — FhirTypeModule is a singleton, and CreateTypesAsync is
        // called again whenever TypesChanged fires (e.g., after package load). A field would
        // accumulate duplicate type registrations across rebuilds, causing HC schema validation
        // failures on the second and subsequent schema builds.
        var nestedTypes = new List<ITypeSystemMember>();
        var types = new List<ITypeSystemMember>();

        // 1. Emit custom scalars for FHIR date/time types
        EmitFhirScalars(types);

        // 2. Emit ObjectType per complex/datatype (HumanName, CodeableConcept, etc.)
        foreach (var complexType in GetComplexDataTypes())
        {
            var fhirType = fhirSchema.GetTypeDefinition(complexType);
            if (fhirType is null) continue;
            types.Add(BuildObjectType(complexType, fhirType, nestedTypes));
        }

        // 3. Emit ObjectType per concrete resource (Patient, Observation, etc.)
        foreach (var resourceType in GetConcreteResourceTypes())
        {
            var fhirType = fhirSchema.GetTypeDefinition(resourceType);
            if (fhirType is null) continue;
            types.Add(BuildResourceObjectType(resourceType, fhirType, nestedTypes));
        }

        // 4. Emit ResourceReference type with inline resource resolution
        types.Add(BuildResourceReferenceType());

        // 5. Emit Resource union type for Reference.resource field
        types.Add(BuildResourceUnionType());

        // 6. Emit Connection types for pagination
        foreach (var resourceType in GetConcreteResourceTypes())
        {
            types.Add(BuildConnectionType(resourceType));
        }
        types.Add(BuildPaginationLinksType());

        // 7. Emit Query root type
        types.Add(BuildQueryType(nestedTypes));

        // 8. Add nested BackboneElement + union types collected during traversal
        types.AddRange(nestedTypes);

        logger.LogInformation(
            "FhirTypeModule generated {TypeCount} GraphQL types for FHIR {Version}",
            types.Count, fhirSchema.Version);

        return ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>(types);
    }

    public void NotifyTypesChanged() => TypesChanged?.Invoke(this, EventArgs.Empty);
}
```

### 3.2 Mapping Rules

#### Resource Types → GraphQL ObjectTypes

Each concrete FHIR resource type maps to a GraphQL `ObjectType`. Child elements become fields.

| FHIR Concept | GraphQL Concept | Example |
|--------------|-----------------|---------|
| Resource (Patient) | `ObjectType("Patient")` | `type Patient { id: ID, name: [HumanName] }` |
| BackboneElement | Nested `ObjectType` | `type Patient_Contact { name: HumanName }` |
| Primitive element | Scalar field | `birthDate: FhirDate` |
| Complex element (1..1) | Object field | `maritalStatus: CodeableConcept` |
| Collection element (0..*) | List field | `name: [HumanName]` |
| Required element (1..1) | Non-null field | `status: String!` |
| Reference element | `ResourceReference` type | See §3.6 |
| Choice element (value[x]) | Union type | See §3.5 |

```csharp
private ObjectType BuildResourceObjectType(string resourceTypeName, IType fhirType, List<ITypeSystemMember> nestedTypes)
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name(resourceTypeName);
        descriptor.Description($"FHIR {resourceTypeName} resource");

        // Always include "resourceType" meta-field for union type resolution.
        // Required so HC union discriminator (IsTypeOf) can identify the concrete type.
        descriptor.Field("resourceType")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => resourceTypeName);

        // For union membership: each ObjectType participating in the Resource union declares
        // IsTypeOf to check the ChoiceElementValue.TypeName wrapper (see §3.5).
        descriptor.IsTypeOf(obj =>
            obj is ChoiceElementValue cv && cv.TypeName == resourceTypeName
            || obj is JsonElement je && je.TryGetProperty("resourceType", out var rt)
                && rt.GetString() == resourceTypeName);

        // Walk child elements from ITypeExtended
        if (fhirType is ITypeExtended extended)
        {
            foreach (var child in extended.Elements)
            {
                AddFieldForElement(descriptor, child, resourceTypeName, nestedTypes);
            }
        }
    });
}
```

#### BackboneElement Children → Nested ObjectTypes

Naming: `{ParentType}_{ElementName}` — e.g., `Patient_Contact`, `Observation_Component`.

```csharp
private void AddFieldForElement(
    IObjectTypeDescriptor descriptor,
    ITypeExtended element,
    string parentPath,
    List<ITypeSystemMember> nestedTypes)
{
    var elementName = element.Name;

    // Choice element (value[x]) → union type
    if (element.Types.Count > 1)
    {
        AddChoiceElementField(descriptor, element, parentPath, nestedTypes);
        return;
    }

    // Reference element → ResourceReference type
    var typeName = element.Types.FirstOrDefault()?.Code;
    if (typeName == "Reference")
    {
        AddReferenceField(descriptor, element);
        return;
    }

    // Primitive → scalar
    if (IsFhirPrimitive(typeName))
    {
        var graphQlType = MapFhirPrimitiveToGraphQlType(typeName!);
        var field = descriptor.Field(CamelCase(elementName)).Type(graphQlType);
        ApplyCardinality(field, element);
        field.Resolve(ctx => ResolveJsonField(ctx, elementName));
        return;
    }

    // BackboneElement (has children, not a standalone type) → nested ObjectType
    if (element.Elements.Count > 0 && !fhirSchema.IsKnownType(elementName))
    {
        var nestedTypeName = $"{parentPath}_{PascalCase(elementName)}";
        var nestedType = BuildObjectType(nestedTypeName, element, nestedTypes);
        nestedTypes.Add(nestedType);

        var field = descriptor.Field(CamelCase(elementName))
            .Type(new NamedTypeNode(nestedTypeName));
        ApplyCardinality(field, element);
        field.Resolve(ctx => ResolveJsonField(ctx, elementName));
        return;
    }

    // Known complex type (HumanName, CodeableConcept, etc.) → reference existing type
    if (typeName is not null && fhirSchema.IsKnownType(typeName))
    {
        var field = descriptor.Field(CamelCase(elementName))
            .Type(new NamedTypeNode(typeName));
        ApplyCardinality(field, element);
        field.Resolve(ctx => ResolveJsonField(ctx, elementName));
    }
}
```

#### Primitive Types → GraphQL Scalars

| FHIR Primitive | GraphQL Type | Notes |
|----------------|-------------|-------|
| `boolean` | `Boolean` | Direct map |
| `integer`, `unsignedInt`, `positiveInt` | `Int` | Direct map |
| `integer64` | `Long` | HotChocolate built-in `LongType` |
| `decimal` | `Decimal` | HotChocolate built-in `DecimalType` |
| `string`, `code`, `markdown` | `String` | Direct map |
| `uri`, `url`, `canonical`, `oid` | `String` | URI as string |
| `uuid` | `String` | UUID as string |
| `base64Binary` | `String` | Base64-encoded |
| `id` | `ID` | GraphQL ID scalar |
| `date` | `FhirDate` | Custom scalar (FHIR date format: YYYY, YYYY-MM, YYYY-MM-DD) |
| `dateTime` | `FhirDateTime` | Custom scalar (FHIR dateTime format) |
| `instant` | `FhirInstant` | Custom scalar (FHIR instant format) |
| `time` | `FhirTime` | Custom scalar (HH:MM:SS) |

```csharp
private static ITypeNode MapFhirPrimitiveToGraphQlType(string fhirType) => fhirType switch
{
    "boolean" => new NamedTypeNode("Boolean"),
    "integer" or "unsignedInt" or "positiveInt" => new NamedTypeNode("Int"),
    "integer64" => new NamedTypeNode("Long"),
    "decimal" => new NamedTypeNode("Decimal"),
    "string" or "code" or "markdown" or "uri" or "url"
        or "canonical" or "oid" or "uuid" => new NamedTypeNode("String"),
    "base64Binary" => new NamedTypeNode("String"),
    "id" => new NamedTypeNode("ID"),
    "date" => new NamedTypeNode("FhirDate"),
    "dateTime" => new NamedTypeNode("FhirDateTime"),
    "instant" => new NamedTypeNode("FhirInstant"),
    "time" => new NamedTypeNode("FhirTime"),
    _ => new NamedTypeNode("String"),
};
```

#### Choice Elements (value[x]) → Union Types

Naming: `{ParentType}_{ElementName}Union` — e.g., `Observation_ValueUnion`.

```csharp
private void AddChoiceElementField(
    IObjectTypeDescriptor descriptor,
    ITypeExtended element,
    string parentPath,
    List<ITypeSystemMember> nestedTypes)
{
    var elementName = element.Name; // "value" (without [x])
    var unionName = $"{parentPath}_{PascalCase(elementName)}Union";

    var memberTypeCodes = element.Types
        .Select(t => t.Code)
        .Where(code => fhirSchema.IsKnownType(code))
        .Distinct()
        .ToList();

    if (memberTypeCodes.Count == 0) return;

    var unionType = new UnionType(ud =>
    {
        ud.Name(unionName);
        ud.Description($"Choice type for {parentPath}.{elementName}[x]");
        foreach (var memberTypeCode in memberTypeCodes)
        {
            ud.Type(new NamedTypeNode(memberTypeCode));
        }
    });
    nestedTypes.Add(unionType);

    var memberTypes = element.Types.ToList(); // capture for closure
    var field = descriptor.Field(CamelCase(elementName))
        .Type(new NamedTypeNode(unionName));
    ApplyCardinality(field, element);
    field.Resolve(ctx => ResolveChoiceElement(ctx, elementName, memberTypes));
}
```

**Choice element resolver**: FHIR serializes `value[x]` as a named property using the type suffix (e.g., `valueQuantity`, `valueString`). The resolver searches for the first matching variant, then returns a `ChoiceElementValue` wrapper that carries both the matched `JsonElement` and its FHIR type code. HotChocolate union resolution uses the `IsTypeOf` delegate registered on each `ObjectType` (see `BuildResourceObjectType`) to select the correct concrete GraphQL type from the wrapper.

```csharp
// Internal wrapper: carries the matched JsonElement + its FHIR type code for union resolution.
internal sealed record ChoiceElementValue(string TypeName, JsonElement Element);

private static ChoiceElementValue? ResolveChoiceElement(
    IResolverContext ctx,
    string elementName,          // e.g., "value"
    IReadOnlyList<ITypeElementType> memberTypes)
{
    var parent = ctx.Parent<JsonElement>();
    if (parent.ValueKind != JsonValueKind.Object)
        return null;

    // FHIR JSON: value[x] is stored as "{elementName}{TypeCode}" e.g., "valueQuantity".
    // The element name is camelCase and the type code starts with an uppercase letter.
    foreach (var memberType in memberTypes)
    {
        var propertyName = $"{CamelCase(elementName)}{char.ToUpperInvariant(memberType.Code[0])}{memberType.Code[1..]}";
        if (parent.TryGetProperty(propertyName, out var value) &&
            value.ValueKind != JsonValueKind.Null)
        {
            return new ChoiceElementValue(memberType.Code, value);
        }
    }
    return null;
}
```

Field resolvers on types that appear as union members receive the `ChoiceElementValue` wrapper as their parent context and unwrap the `JsonElement` before navigation:

```csharp
// In BuildObjectType — field resolver unwraps ChoiceElementValue when used as union member
field.Resolve(ctx =>
{
    var raw = ctx.Parent<object?>();
    var element = raw is ChoiceElementValue cv ? cv.Element : (JsonElement)raw!;
    return ResolveJsonElement(element, fieldName);
});
```

#### Reference Fields → ResourceReference Type

FHIR `Reference` elements map to a `ResourceReference` GraphQL type with an inline `resource` field:

```graphql
type ResourceReference {
  reference: String
  type: String
  display: String
  identifier: Identifier
  resource: Resource           # Inline resolution via DataLoader
}

union Resource = Patient | Observation | Encounter | Practitioner | Organization | ...
```

See §6 for the full reference resolution design with `BatchDataLoader`.

#### Search Parameters → Query Arguments

All search parameter arguments are typed as `String` in GraphQL. This matches the FHIR search specification where search parameters are transmitted as string-encoded values with prefix modifiers (e.g., `ge2024-01-01`, `exact|Smith`). The existing `Ignixa.Search` parsing infrastructure handles decoding.

```csharp
private void AddSearchArguments(IObjectFieldDescriptor fieldDescriptor, string resourceType)
{
    // Standard FHIR search control parameters
    fieldDescriptor.Argument("_count", a => a.Type<IntType>().Description("Page size (default: 10, max: 1000)"));
    // _cursor receives the opaque ContinuationToken string returned in PaginationLinks.next.
    // SearchOptions.ContinuationToken is a server-side cursor, NOT a numeric offset.
    fieldDescriptor.Argument("_cursor", a => a.Type<StringType>().Description("Continuation cursor from previous page's link.next"));
    fieldDescriptor.Argument("_sort", a => a.Type<StringType>().Description("Sort criteria (e.g., \"-date,name\")"));
    fieldDescriptor.Argument("_total", a => a.Type<StringType>().Description("Total count mode: none | estimate | accurate"));

    // Resource-specific search parameters (from ISearchParameterDefinitionManager)
    // Added dynamically based on CapabilityStatement search parameter definitions
}
```

---

## 4. Query Root Design

### 4.1 System-Level Query Root

The query root type provides two fields per resource type:

```graphql
type Query {
  # Instance-level read (by ID)
  Patient(id: ID!): Patient
  Observation(id: ID!): Observation
  Encounter(id: ID!): Encounter
  # ... one per concrete resource type

  # Type-level search (with search parameters as arguments)
  # _cursor is an opaque server token from PaginationLinks.next — not a numeric offset
  PatientList(name: String, family: String, birthdate: String,
              _count: Int, _cursor: String, _sort: String): PatientConnection
  ObservationList(code: String, patient: String, date: String,
                  _count: Int, _cursor: String, _sort: String): ObservationConnection
  # ... one per concrete resource type
}
```

```csharp
private ObjectType BuildQueryType()
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name("Query");

        foreach (var resourceType in GetConcreteResourceTypes())
        {
            // Instance read: Patient(id: "123")
            descriptor.Field(resourceType)
                .Argument("id", a => a.Type<NonNullType<IdType>>())
                .Type(new NamedTypeNode(resourceType))
                .Resolve(async ctx =>
                {
                    var id = ctx.ArgumentValue<string>("id");
                    return await ctx.Service<ResourceResolver>()
                        .ResolveByIdAsync(resourceType, id, ctx.RequestAborted);
                });

            // List/search: PatientList(name: "Smith", _count: 10)
            var listField = descriptor.Field($"{resourceType}List")
                .Type(new NamedTypeNode($"{resourceType}Connection"));
            AddSearchArguments(listField, resourceType);
            listField.Resolve(async ctx =>
            {
                return await ctx.Service<SearchResolver>()
                    .SearchAsync(resourceType, ctx, ctx.RequestAborted);
            });
        }
    });
}
```

### 4.2 Connection Types for Pagination

Each list query returns a FHIR-flavored connection type:

```graphql
type PatientConnection {
  entry: [Patient!]!
  total: Int
  link: PaginationLinks
}

type PaginationLinks {
  next: String
  previous: String
  self: String
}
```

### 4.3 Instance-Level Query Root

For instance-level `$graphql` (e.g., `POST /Patient/123/$graphql`), the FHIR spec requires the root object to be the specific resource. We achieve this by:

1. Pre-loading the resource in the endpoint handler
2. Setting it as the root value via HotChocolate global state
3. Using a resolver middleware that detects instance mode and returns the pre-loaded resource

```csharp
// In the endpoint handler:
var queryRequest = QueryRequestBuilder.New()
    .SetQuery(graphQlRequest.Query)
    .SetVariableValues(graphQlRequest.Variables)
    .AddGlobalState("InstanceResourceType", resourceType)
    .AddGlobalState("InstanceResourceId", resourceId)
    .Create();
```

The query root's resolver for the resource type checks for global state first — if `InstanceResourceType` matches, it loads and returns that specific resource as the root, allowing queries like:

```graphql
{
  id
  name { text given family }
  birthDate
  generalPractitioner {
    reference
    resource {
      ... on Practitioner { name { text } }
    }
  }
}
```

---

## 5. Resolver Architecture (CQRS-First)

### Design Decision: Why CQRS-First

Unlike Proposal A which calls `IFhirRepository`/`ISearchService` directly from resolvers, this design routes through the existing MediatR CQRS handlers. This preserves:

- **Capability enforcement**: `IRequireCapability` on queries validates that the operation is advertised in CapabilityStatement
- **Partition strategy**: `IPartitionStrategy.DetermineReadPartition()` resolves the correct tenant partition
- **Audit logging**: Existing logging in handlers captures all data access
- **Consistent behavior**: Same code path as REST API reads and searches

### 5.1 ResourceResolver — Instance Reads

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/ResourceResolver.cs

public sealed class ResourceResolver(
    IMediator mediator,
    ILogger<ResourceResolver> logger)
{
    public async Task<JsonElement?> ResolveByIdAsync(
        string resourceType,
        string id,
        CancellationToken cancellationToken)
    {
        logger.LogDebug("GraphQL resolving {ResourceType}/{Id}", resourceType, id);

        var query = new GetResourceQuery(resourceType, id);
        var result = await mediator.SendAsync(query, cancellationToken);

        if (result is null || result.IsDeleted)
            return null;

        // Parse raw bytes into JsonElement for field-level resolution
        return JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span);
    }
}
```

### 5.2 SearchResolver — List Queries

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/SearchResolver.cs

public sealed class SearchResolver(
    IMediator mediator,
    ILogger<SearchResolver> logger)
{
    public async Task<SearchConnectionResult> SearchAsync(
        string resourceType,
        IResolverContext graphQlContext,
        CancellationToken cancellationToken)
    {
        var searchOptions = BuildSearchOptionsFromArguments(resourceType, graphQlContext);

        logger.LogDebug("GraphQL searching {ResourceType} with {ParamCount} parameters",
            resourceType, searchOptions.Expression is not null ? 1 : 0);

        var query = new SearchResourcesQuery(resourceType, searchOptions);
        var result = await mediator.SendAsync(query, cancellationToken);

        // Materialize stream into JsonElement list for field resolution
        var entries = new List<JsonElement>();
        await foreach (var entry in result.Resources.WithCancellation(cancellationToken))
        {
            var json = JsonSerializer.Deserialize<JsonElement>(entry.ResourceBytes.Span);
            entries.Add(json);

            // Respect requested page size (stream includes +1 for pagination detection)
            if (entries.Count >= searchOptions.MaxItemCount)
                break;
        }

        return new SearchConnectionResult
        {
            Entries = entries,
            Total = result.Total,
            Links = BuildPaginationLinks(searchOptions, entries.Count, result.HasMore),
        };
    }

    private static SearchOptions BuildSearchOptionsFromArguments(
        string resourceType,
        IResolverContext context)
    {
        var options = new SearchOptions { ResourceType = resourceType };

        // Map _count argument
        if (context.ArgumentOptional<int?>("_count") is { } count)
            options.MaxItemCount = Math.Clamp(count, 1, 1000);

        // Map _cursor argument → SearchOptions.ContinuationToken.
        // ContinuationToken is an opaque server-side cursor string, not a numeric offset.
        // The client obtains this value from PaginationLinks.next in the previous response.
        if (context.ArgumentOptional<string?>("_cursor") is { } cursor)
            options.ContinuationToken = cursor;

        // Map _sort argument
        if (context.ArgumentOptional<string?>("_sort") is { } sort)
            options.Sort = sort;

        // Map search parameters to query string format for existing parser
        // Each non-underscore argument maps to a search parameter
        // Reuses existing Ignixa.Search parsing infrastructure
        return options;
    }
}
```

### 5.3 Field Resolution — JsonElement Tree Navigation

`SearchEntryResult.ResourceBytes` is `ReadOnlyMemory<byte>` — raw JSON. We parse it into `System.Text.Json.JsonElement` which provides zero-copy field access. Each child field resolver navigates the `JsonElement` tree:

```csharp
// Generic field resolver for all resource/complex type fields
private static object? ResolveJsonField(IResolverContext ctx, string fieldName)
{
    var parent = ctx.Parent<JsonElement>();

    if (parent.ValueKind != JsonValueKind.Object)
        return null;

    if (!parent.TryGetProperty(fieldName, out var value))
        return null;

    return value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => value.EnumerateArray().ToList(),
        JsonValueKind.Object => value, // Pass through for nested resolution
        _ => null,
    };
}
```

### 5.4 Resolver Lifecycle

| Resolver | Scope | Injected Via |
|----------|-------|-------------|
| `ResourceResolver` | Scoped (per-request) | `ctx.Service<ResourceResolver>()` |
| `SearchResolver` | Scoped (per-request) | `ctx.Service<SearchResolver>()` |
| `ResourceDataLoader` | Scoped (per-request) | HotChocolate DataLoader DI |
| Field resolvers | Compiled delegates | `FhirTypeModule` at schema build |

---

## 6. Reference Resolution (DataLoader Pattern)

### 6.1 The N+1 Problem

A query like:
```graphql
{
  PatientList(name: "Smith") {
    entry {
      generalPractitioner { resource { ... on Practitioner { name { text } } } }
      managingOrganization { resource { ... on Organization { name } } }
    }
  }
}
```

Without batching, each `resource` field triggers a separate read — potentially dozens per page.

### 6.2 ResourceDataLoader

HotChocolate's `BatchDataLoader` collects all keys accumulated within a single execution step (across all field resolvers in the response), then calls `LoadBatchAsync` once with the full set. This eliminates the sequential-per-field N+1 pattern.

**Important**: `LoadBatchAsync` fires one `GetResourceQuery` per unique key via `Task.WhenAll` — these are parallel individual reads, not a single batched SQL query. The primary benefit is **deduplication** (same `Practitioner/123` referenced by 10 patients loads once) and **parallelism** (all unique references load concurrently, not serially). A future optimisation could group keys by `ResourceType` and use a `_id=a,b,c` search query to reduce round-trips, but this is deferred because: (a) it requires knowing the batch IDs at search time, (b) fallback handling for partial results is complex, and (c) `GetResourceQuery` already leverages partition strategy and capability enforcement.

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/DataLoaders/ResourceDataLoader.cs

public sealed class ResourceDataLoader(
    IMediator mediator,
    IBatchScheduler batchScheduler)
    : BatchDataLoader<ResourceKey, JsonElement?>(batchScheduler)
{
    protected override async Task<IReadOnlyDictionary<ResourceKey, JsonElement?>> LoadBatchAsync(
        IReadOnlyList<ResourceKey> keys,
        CancellationToken cancellationToken)
    {
        var results = new Dictionary<ResourceKey, JsonElement?>(keys.Count);

        // Parallel individual reads — one GetResourceQuery per unique key.
        // The DataLoader guarantees each key appears in `keys` at most once per request
        // (deduplication happens before LoadBatchAsync is called).
        // This is parallel, not a batched DB query. See §6 note above for rationale.
        var tasks = keys.Select(async key =>
        {
            var query = new GetResourceQuery(key.ResourceType, key.ResourceId);
            var entry = await mediator.SendAsync(query, cancellationToken);

            if (entry is not null && !entry.IsDeleted)
            {
                var json = JsonSerializer.Deserialize<JsonElement>(entry.ResourceBytes.Span);
                return (key, json: (JsonElement?)json);
            }

            return (key, json: null);
        });

        foreach (var (key, json) in await Task.WhenAll(tasks))
        {
            results[key] = json;
        }

        return results;
    }
}
```

### 6.3 ResourceReference Type with Inline Resolution

```csharp
private ObjectType BuildResourceReferenceType()
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name("ResourceReference");

        descriptor.Field("reference").Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "reference"));

        descriptor.Field("type").Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "type"));

        descriptor.Field("display").Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "display"));

        descriptor.Field("resource")
            .Type(new NamedTypeNode("Resource"))
            .Resolve(async ctx =>
            {
                var refElement = ctx.Parent<JsonElement>();
                var reference = GetStringProperty(refElement, "reference");
                if (reference is null) return null;

                // Parse "ResourceType/id" from reference string
                var parsed = ParseFhirReference(reference);
                if (parsed is null) return null;

                var (resourceType, id) = parsed.Value;
                var key = new ResourceKey(resourceType, id);

                // Use DataLoader for batched resolution
                var loader = ctx.DataLoader<ResourceDataLoader>();
                return await loader.LoadAsync(key, ctx.RequestAborted);
            });
    });
}
```

### 6.4 Deduplication

The `BatchDataLoader` automatically deduplicates keys. If the same `Practitioner/123` is referenced by 10 patients in a list result, it's loaded exactly once.

---

## 7. Multi-Tenancy

### 7.1 Tenant Context Flow

```
HTTP Request
  │
  ▼
TenantResolutionMiddleware          ← Existing: extracts tenantId from route
  │
  ▼
FhirRequestContextMiddleware        ← Existing: sets IFhirRequestContext (TenantId, FhirVersion)
  │
  ▼
GraphQlEndpoints.HandleGraphQl()   ← Reads context, selects named schema
  │
  ▼
FhirHttpRequestInterceptor          ← Copies tenant context into HotChocolate global state
  │
  ▼
Resolvers                           ← IMediator.SendAsync() → handlers use IFhirRequestContextAccessor
  │                                   All repository/search calls are automatically tenant-scoped
  ▼
IFhirRepository / ISearchService    ← Tenant-filtered at the data layer
```

### 7.2 Request Interceptor

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Pipeline/FhirHttpRequestInterceptor.cs

public sealed class FhirHttpRequestInterceptor(
    IFhirRequestContextAccessor contextAccessor) : DefaultHttpRequestInterceptor
{
    public override ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        var fhirContext = contextAccessor.RequestContext;
        if (fhirContext is not null)
        {
            requestBuilder.AddGlobalState("TenantId", fhirContext.TenantId);
            requestBuilder.AddGlobalState("FhirVersion", fhirContext.FhirVersion);
        }

        return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
```

### 7.3 Tenant Route Groups

Following the established `TerminologyEndpoints.cs` pattern:

- **Tenant-explicit routes** (always supported): `/tenant/{tenantId:int}/$graphql`
- **Tenant-agnostic routes** (single-tenant mode; blocked in multi-tenant by `TenantResolutionMiddleware`): `/$graphql`

---

## 8. Multi-Version FHIR Support

### 8.1 Named Schemas per FHIR Version

Different tenants may use different FHIR versions. Each version has a different `ISchema` with different resource types and elements. HotChocolate supports named schemas via `IRequestExecutorResolver`:

```csharp
// During service registration (one per supported FHIR version):
services.AddGraphQLServer("fhir-r4")
    .AddTypeModule(sp =>
    {
        var versionContext = sp.GetRequiredService<IFhirVersionContext>();
        var schema = versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        return new FhirTypeModule(schema, sp.GetRequiredService<ILogger<FhirTypeModule>>());
    })
    .AddDataLoader<ResourceDataLoader>()
    .AddMaxExecutionDepthRule(graphQlOptions.MaxQueryDepth)
    .AddHttpRequestInterceptor<FhirHttpRequestInterceptor>();

services.AddGraphQLServer("fhir-r4b") // ... similar for R4B
services.AddGraphQLServer("fhir-r5")  // ... similar for R5
services.AddGraphQLServer("fhir-r6")  // ... similar for R6
services.AddGraphQLServer("fhir-stu3") // ... similar for STU3
```

### 8.2 Schema Selection at Request Time

The endpoint handler selects the correct schema based on the tenant's FHIR version:

```csharp
var schemaName = $"fhir-{fhirContext.FhirVersion.ToString().ToLowerInvariant()}";
var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);
```

### 8.3 Schema Warm-Up

Schemas are built lazily on first request but can be warmed during startup:

```csharp
public class GraphQlSchemaWarmupService(
    IRequestExecutorResolver resolver,
    IOptions<GraphQlExperimentalOptions> options) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var version in options.Value.WarmupVersions)
        {
            // FhirVersion.R4.ToString() = "R4" → schema name "fhir-r4"
            var schemaName = $"fhir-{version.ToString().ToLowerInvariant()}";
            await resolver.GetRequestExecutorAsync(schemaName, cancellationToken);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

### 8.4 Schema Cache Invalidation

When conformance resources change (e.g., new StructureDefinitions installed via package management), schemas need rebuilding. This integrates with the existing `PackageLoadedEvent`.

**DI lifetime note**: `FhirTypeModule` is owned by HotChocolate's service provider (registered via `AddTypeModule`). The `PackageLoadedSchemaInvalidationHandler` is registered in Autofac and must call `NotifyTypesChanged()` on the **exact same singleton instance** that HC owns. To bridge the two containers, `FhirTypeModule` is registered as a keyed singleton in `IServiceCollection` (see §11.3), and the Autofac handler factory resolves it via `IServiceProvider` (which wraps `IServiceCollection` when using `AutofacServiceProviderFactory`).

```csharp
public class PackageLoadedSchemaInvalidationHandler(
    IReadOnlyList<FhirTypeModule> typeModules) : INotificationHandler<PackageLoadedEvent>
{
    public Task HandleAsync(PackageLoadedEvent notification, CancellationToken cancellationToken)
    {
        // Trigger HotChocolate schema rebuild for all registered FHIR versions
        foreach (var module in typeModules)
            module.NotifyTypesChanged();
        return Task.CompletedTask;
    }
}
```

---

## 9. Endpoint Design

### 9.1 GraphQlEndpoints.cs

```csharp
// src/Application/Ignixa.Api/Endpoints/Experimental/GraphQlEndpoints.cs

public static class GraphQlEndpoints
{
    public static IEndpointRouteBuilder MapGraphQlEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        MapTenantEndpoints(endpoints, configureTenantGroup);
        MapAgnosticEndpoints(endpoints);
        return endpoints;
    }

    private static void MapTenantEndpoints(
        IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup)
    {
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");
        configureTenantGroup?.Invoke(tenantGroup);

        // System-level $graphql
        tenantGroup.MapPost("/$graphql", HandleSystemGraphQl)
            .WithName("GraphQlSystem").WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>(KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);

        tenantGroup.MapGet("/$graphql", HandleSystemGraphQlGet)
            .WithName("GraphQlSystemGet").WithTags("Experimental", "GraphQL");

        // Instance-level $graphql
        tenantGroup.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQl)
            .WithName("GraphQlInstance").WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>(KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);

        tenantGroup.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGet)
            .WithName("GraphQlInstanceGet").WithTags("Experimental", "GraphQL");
    }

    private static void MapAgnosticEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/$graphql", HandleSystemGraphQlAgnostic)
            .WithName("GraphQlSystemAgnostic").WithTags("Experimental", "GraphQL");
        endpoints.MapGet("/$graphql", HandleSystemGraphQlGetAgnostic)
            .WithName("GraphQlSystemGetAgnostic").WithTags("Experimental", "GraphQL");
        endpoints.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlAgnostic)
            .WithName("GraphQlInstanceAgnostic").WithTags("Experimental", "GraphQL");
        endpoints.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGetAgnostic)
            .WithName("GraphQlInstanceGetAgnostic").WithTags("Experimental", "GraphQL");
    }
}
```

### 9.2 Request Handling

- **POST**: Parse JSON body `{ "query": "...", "variables": {...}, "operationName": "..." }`
- **GET**: Read `query` param, optional `variables` JSON string, optional `operationName`

### 9.3 Response Format

Per FHIR spec, responses use `Content-Type: application/json` (not `application/fhir+json`):

```json
{
  "data": {
    "Patient": {
      "id": "123",
      "name": [{ "text": "John Smith" }],
      "birthDate": "1974-12-25"
    }
  }
}
```

Errors follow GraphQL spec format:

```json
{
  "data": null,
  "errors": [
    {
      "message": "Resource not found",
      "locations": [{ "line": 1, "column": 3 }],
      "path": ["Patient"]
    }
  ]
}
```

---

## 10. Security & Guardrails

| Protection | Mechanism | Default |
|-----------|-----------|---------|
| **Query depth** | `AddMaxExecutionDepthRule()` | 15 levels |
| **Query complexity** | HotChocolate complexity analysis | 500 cost units |
| **Execution timeout** | `RequestExecutorOptions.ExecutionTimeout` | 30 seconds |
| **Page size cap** | `MaxPageSize` in `GraphQlExperimentalOptions` | 1000 results |
| **Introspection** | `EnableIntrospection` toggle | Enabled (disable in production) |
| **GET requests** | `EnableGetRequests` toggle | Enabled (disable to prevent URL-logged queries) |
| **Error details** | `IncludeExceptionDetails = false` | No stack traces leaked |
| **Reference cycles** | Max depth limit prevents infinite recursion | DataLoader deduplicates |

---

## 11. Registration & Dependency Injection

### 11.1 Configuration Options

```csharp
// Added to ExperimentalOptions.cs

public class GraphQlExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public int MaxQueryDepth { get; set; } = 15;
    public bool EnableIntrospection { get; set; } = true;
    public int MaxQueryComplexity { get; set; } = 500;
    public int MaxPageSize { get; set; } = 1000;
    public int DefaultPageSize { get; set; } = 10;
    public bool EnableGetRequests { get; set; } = true;
    public int ExecutionTimeoutSeconds { get; set; } = 30;
    // FhirVersion enum (not string) for type safety. Configuration binding supports enum values.
    public ICollection<FhirVersion> WarmupVersions { get; } = [FhirVersion.R4];
}
```

Add to `ExperimentalFeaturesOptions`:
```csharp
public GraphQlExperimentalOptions GraphQl { get; set; } = new();
```

### 11.2 Autofac Registration

```csharp
// In ExperimentalAutofacRegistration.cs:

if (options.Features.GraphQl.Enabled)
{
    builder.RegisterGraphQlHandlers();
}

private static void RegisterGraphQlHandlers(this ContainerBuilder builder)
{
    builder.RegisterType<ResourceResolver>().AsSelf().InstancePerLifetimeScope();
    builder.RegisterType<SearchResolver>().AsSelf().InstancePerLifetimeScope();
    builder.RegisterType<GraphQlExecutionService>()
        .As<IGraphQlExecutionService>().InstancePerLifetimeScope();

    // Schema cache invalidation on package load.
    // Uses a factory registration (not RegisterType<>) to bridge the IServiceCollection
    // keyed singletons into Autofac. IServiceProvider in the Autofac container wraps
    // IServiceCollection registrations via AutofacServiceProviderFactory, so keyed services
    // registered in AddGraphQlServices are resolvable here.
    // Following the established pattern from RegisterIpsHandlers (uses builder.Register factory).
    builder.Register(c =>
    {
        var sp = c.Resolve<IServiceProvider>();
        var modules = new[] { FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6 }
            .Select(v => sp.GetKeyedService<FhirTypeModule>(v))
            .OfType<FhirTypeModule>()
            .ToList()
            .AsReadOnly();
        return new PackageLoadedSchemaInvalidationHandler(modules);
    })
    .As<INotificationHandler<PackageLoadedEvent>>()
    .InstancePerDependency();
}
```

### 11.3 IServiceCollection Registration

```csharp
// In ExperimentalServicesRegistration.cs:

if (options.Features.GraphQl.Enabled)
{
    services.AddGraphQlServices(configuration, options.Features.GraphQl);
}

private static void AddGraphQlServices(
    this IServiceCollection services,
    IConfiguration configuration,
    GraphQlExperimentalOptions graphQlOptions)
{
    // All supported FHIR versions. No semantic difference between versions for the GraphQL
    // layer — the named schema approach handles each identically via FhirTypeModule(ISchema).
    var versions = new[] { FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5, FhirVersion.R6 };

    foreach (var version in versions)
    {
        var schemaName = $"fhir-{version.ToString().ToLowerInvariant()}";

        // Register FhirTypeModule as a KEYED singleton per FHIR version.
        // This serves two purposes:
        // 1. HC resolves it via AddTypeModule(sp => sp.GetRequiredKeyedService<FhirTypeModule>(version))
        // 2. Autofac's PackageLoadedSchemaInvalidationHandler resolves ALL versions via IServiceProvider,
        //    ensuring it calls NotifyTypesChanged() on the exact same HC-owned instance.
        services.AddKeyedSingleton<FhirTypeModule>(version, (sp, _) =>
        {
            var versionContext = sp.GetRequiredService<IFhirVersionContext>();
            var schema = versionContext.GetBaseSchemaProvider(version);
            var logger = sp.GetRequiredService<ILogger<FhirTypeModule>>();
            return new FhirTypeModule(schema, logger);
        });

        services.AddGraphQLServer(schemaName)
            .AddTypeModule(sp => sp.GetRequiredKeyedService<FhirTypeModule>(version))
            .AddDataLoader<ResourceDataLoader>()
            .ModifyRequestOptions(opt => opt.IncludeExceptionDetails = false)
            .AddMaxExecutionDepthRule(graphQlOptions.MaxQueryDepth)
            .SetRequestOptions(_ => new RequestExecutorOptions
            {
                ExecutionTimeout = TimeSpan.FromSeconds(graphQlOptions.ExecutionTimeoutSeconds),
            })
            .AddHttpRequestInterceptor<FhirHttpRequestInterceptor>();
    }

    if (graphQlOptions.WarmupVersions.Count > 0)
    {
        services.AddHostedService<GraphQlSchemaWarmupService>();
    }
}
```

### 11.4 Endpoint Registration

```csharp
// In ExperimentalEndpointExtensions.cs:

if (options.Features.GraphQl.Enabled)
{
    app.MapGraphQlEndpoints(configureTenantGroup);
}
```

### 11.5 appsettings.json

```json
{
  "Experimental": {
    "Enabled": true,
    "Features": {
      "GraphQl": {
        "Enabled": true,
        "MaxQueryDepth": 15,
        "EnableIntrospection": true,
        "MaxQueryComplexity": 500,
        "MaxPageSize": 1000,
        "DefaultPageSize": 10,
        "EnableGetRequests": true,
        "ExecutionTimeoutSeconds": 30,
        "WarmupVersions": ["R4"]
      }
    }
  }
}
```

> **Note**: `WarmupVersions` binds to `ICollection<FhirVersion>`. ASP.NET Core configuration binding supports enum values by name (case-insensitive), so `"R4"` binds correctly to `FhirVersion.R4`.

---

## 12. File / Folder Structure

All new files and their exact paths:

```
src/
├── Application/
│   ├── Ignixa.Api/
│   │   └── Endpoints/
│   │       └── Experimental/
│   │           ├── ExperimentalEndpointExtensions.cs    # MODIFIED: add GraphQL toggle
│   │           └── GraphQlEndpoints.cs                  # NEW: Minimal API endpoints
│   │
│   └── Ignixa.Application/
│       └── Features/
│           └── Experimental/
│               ├── Configuration/
│               │   └── ExperimentalOptions.cs            # MODIFIED: add GraphQlExperimentalOptions
│               ├── Infrastructure/
│               │   ├── ExperimentalAutofacRegistration.cs    # MODIFIED: add GraphQL registrations
│               │   └── ExperimentalServicesRegistration.cs   # MODIFIED: add HotChocolate services
│               └── GraphQl/                              # NEW DIRECTORY
│                   ├── Contracts/
│                   │   └── IGraphQlExecutionService.cs   # Execution abstraction
│                   ├── Schema/
│                   │   ├── FhirTypeModule.cs              # ITypeModule: schema from ISchema metadata
│                   │   ├── FhirScalarMappings.cs          # FHIR primitive → GraphQL scalar mapping
│                   │   └── GraphQlNamingHelper.cs         # Naming: PascalCase, union names, backbone
│                   ├── Resolvers/
│                   │   ├── ResourceResolver.cs            # Instance read via IMediator → GetResourceQuery
│                   │   ├── SearchResolver.cs              # List search via IMediator → SearchResourcesQuery
│                   │   └── FieldResolver.cs               # JsonElement tree navigation helpers
│                   ├── DataLoaders/
│                   │   └── ResourceDataLoader.cs          # BatchDataLoader for reference resolution
│                   ├── Pipeline/
│                   │   ├── FhirHttpRequestInterceptor.cs  # Tenant context injection
│                   │   └── GraphQlSchemaWarmupService.cs   # IHostedService for schema pre-build
│                   ├── Events/
│                   │   └── PackageLoadedSchemaInvalidationHandler.cs  # Cache invalidation
│                   ├── Execution/
│                   │   └── GraphQlExecutionService.cs     # Wraps HotChocolate execution
│                   └── Models/
│                       ├── GraphQlRequestBody.cs          # Request DTO (query, variables, operationName)
│                       ├── SearchConnectionResult.cs      # Pagination result DTO
│                       └── GraphQlJsonOptions.cs          # STJ options for GraphQL I/O

test/
└── Ignixa.Application.Tests/
    └── Features/
        └── Experimental/
            └── GraphQl/                                   # NEW DIRECTORY
                ├── FhirTypeModuleTests.cs                 # Schema generation: types, unions, references
                ├── ResourceResolverTests.cs               # Instance read resolution
                ├── SearchResolverTests.cs                 # List search resolution + pagination
                ├── ResourceDataLoaderTests.cs             # DataLoader batching + deduplication
                ├── GraphQlEndpointIntegrationTests.cs     # Full endpoint integration tests
                └── FieldResolverTests.cs                  # JsonElement field navigation tests
```

**New files**: 17 (13 source + 4 test minimum)
**Modified files**: 4 (ExperimentalOptions.cs, ExperimentalAutofacRegistration.cs, ExperimentalServicesRegistration.cs, ExperimentalEndpointExtensions.cs)

---

## 13. Testing Strategy

### Unit Tests

| Test Class | What It Validates |
|------------|-------------------|
| `FhirTypeModuleTests` | Schema generation produces correct ObjectTypes for Patient, Observation; correct UnionTypes for choice elements; correct ResourceReference type; correct Connection types |
| `ResourceResolverTests` | Calls `IMediator.SendAsync(GetResourceQuery)` with correct args; returns `JsonElement` from raw bytes; returns null for missing/deleted resources |
| `SearchResolverTests` | Builds `SearchOptions` from GraphQL arguments; calls `IMediator.SendAsync(SearchResourcesQuery)`; materializes stream into connection result; respects page size |
| `ResourceDataLoaderTests` | Batches multiple keys; deduplicates identical keys; handles null results |
| `FieldResolverTests` | Navigates `JsonElement` for primitives, arrays, nested objects; handles missing properties gracefully |

### Integration Tests

| Test Class | What It Validates |
|------------|-------------------|
| `GraphQlEndpointIntegrationTests` | Full request/response cycle: POST body parsing, GET query param, system-level query, instance-level query, error responses, content-type header, multi-tenant routing |

### Test Data Pattern

Follow existing AAA + BDD naming:
```csharp
[Fact]
public async Task GivenPatientExists_WhenQueryById_ThenReturnsPatientFields()
{
    // Arrange: mock IMediator to return SearchEntryResult with Patient JSON bytes
    // Act: execute GraphQL query { Patient(id: "123") { id name { text } } }
    // Assert: response.Data.Patient.id == "123", name[0].text == expected
}
```

---

## 14. Implementation Phases

### Phase 1: Core Schema Generation + Instance Reads (30-40h)

**Deliverables**:
- `FhirTypeModule` generating ObjectTypes for all resource types from `ISchema`
- `ResourceResolver` resolving instance reads via `GetResourceQuery`
- `FieldResolver` for `JsonElement` tree navigation
- `GraphQlEndpoints` with system-level POST endpoint
- `GraphQlExperimentalOptions` configuration
- Registration in all three experimental extension points
- Unit tests for schema generation and resolution

**Dependencies**: None beyond existing codebase.

### Phase 2: Search Resolver + Pagination + Search Parameters (25-30h)

**Deliverables**:
- `SearchResolver` with `SearchResourcesQuery` integration
- Connection types with pagination (`entry`, `total`, `link`)
- Search parameter mapping from GraphQL arguments to `SearchOptions`
- GET endpoint support
- Instance-level `$graphql` with resource-root semantics
- Unit + integration tests for search

**Dependencies**: Phase 1 complete.

### Phase 3: Reference Resolution + Multi-Version + Hardening (25-30h)

**Deliverables**:
- `ResourceDataLoader` for batched reference resolution
- `ResourceReference` type with inline `resource` field
- Named schemas for STU3, R4, R4B, R5, R6
- `GraphQlSchemaWarmupService`
- `PackageLoadedSchemaInvalidationHandler`
- Query depth/complexity/timeout enforcement
- Introspection toggle
- Full integration test suite

**Dependencies**: Phases 1 + 2 complete.

---

## 15. FHIR GraphQL Spec Compliance Matrix

| Spec Requirement | Design Element | Status |
|-----------------|----------------|--------|
| System-level `[base]/$graphql` | `GraphQlEndpoints.HandleSystemGraphQl()` | ✅ Designed |
| Instance-level `[base]/[Type]/[id]/$graphql` | `GraphQlEndpoints.HandleInstanceGraphQl()` + root value | ✅ Designed |
| GET with `query` parameter | `HandleSystemGraphQlGet()` + `EnableGetRequests` toggle | ✅ Designed |
| POST with JSON body | `HandleSystemGraphQl()` + `GraphQlRequestBody` DTO | ✅ Designed |
| Response `application/json` | `Results.Bytes(bytes, KnownContentTypes.ApplicationJson)` | ✅ Designed |
| Schema introspection (`__schema`, `__type`) | HotChocolate built-in + `EnableIntrospection` toggle | ✅ Designed |
| `@skip` directive | HotChocolate built-in | ✅ Free |
| `@include` directive | HotChocolate built-in | ✅ Free |
| Resource type queries | `Patient(id: ID!): Patient` in Query root | ✅ Designed |
| Search with parameters | `PatientList(name: String, ...): PatientConnection` | ✅ Designed |
| Reference inline resolution | `ResourceReference.resource` via DataLoader | ✅ Designed |
| GraphQL error format | HotChocolate standard error serialization | ✅ Free |

---

## 16. Open Questions & Decisions

### Resolved

1. **Library choice**: HotChocolate v15. Dynamic schema via `ITypeModule` is the decisive factor.
2. **Resolver pattern**: CQRS-first via `IMediator.SendAsync()`. Preserves existing capability/partition/audit behavior.
3. **Field resolution**: `JsonElement` tree navigation from raw bytes. Zero intermediate model materialization.
4. **Instance semantics**: Pre-load resource, set as root value via HotChocolate global state.

### Deferred

1. **Abstract types in schema**: FHIR spec says only concrete types. We emit `Resource` as a GraphQL `UnionType` (not object type) for reference resolution discriminator. `DomainResource` is omitted.

2. **`_include`/`_revinclude` arguments**: Omitted. GraphQL's inline reference resolution (`subject { resource { ... } }`) supersedes `_include`. If needed, add as a future enhancement.

3. **FHIR extensions**: Exposed as `extension: [Extension]` generic list type. Profile-aware typed extensions deferred to future iteration. The `Extension` type has `url: String`, `value: JSON` (opaque).

4. **GraphQL subscriptions**: HotChocolate supports WebSocket subscriptions. Could bridge to FHIR Subscriptions in the future. Design the schema to be subscription-compatible but don't implement now.

5. **Mutations (write operations)**: FHIR `$graphql` spec is read-only. No mutations planned. If future spec allows mutations, HotChocolate supports them natively.

6. **ResourceDataLoader: parallel reads vs. batched search by `_id`**: The current design fires one `GetResourceQuery` per unique referenced key in parallel. A more efficient approach would group keys by `ResourceType` and issue a single `SearchResourcesQuery` with `_id=a,b,c` per group. This is deferred because it requires building a batch-read CQRS query, handling partial failures, and verifying that `_id` multi-value search is supported across all storage backends. The current parallel approach is correct and benefits from DataLoader deduplication; the optimisation is a Phase 3+ enhancement.

---

## 17. Tradeoffs

| Pros | Cons |
|------|------|
| **Dynamic schema from `ISchema` metadata** — auto-sync with FHIR StructureDefinitions | **Startup cost** — schema build walks hundreds of types (~2-5s per version). Mitigated by warmup. |
| **CQRS-first resolvers** — preserve capability enforcement, partition strategy, audit logging | **Slight overhead** — MediatR dispatch per read/search vs direct repo calls. Acceptable for GraphQL's expected query patterns. |
| **HotChocolate compiled resolvers** — sub-microsecond dispatch after compilation | **Large dependency** — ~15 NuGet packages, ~5-10 MB binary size increase. |
| **Built-in `BatchDataLoader`** — deduplication + parallelism for reference resolution | **DataLoader parallel reads** — `LoadBatchAsync` fires one `GetResourceQuery` per key concurrently, not a single batched SQL query. Future: group by type + `_id=a,b,c` search. |
| **Standard introspection** — clients auto-discover schema | **Schema size** — ~3000+ types may overwhelm naive clients. |
| **Query depth/complexity limiting** — built-in DoS protection | **Memory** — each compiled schema holds ~10-50 MB. Multiply by FHIR version count. |
| **`JsonElement` field resolution** — zero-copy from raw bytes | **String-typed** — field name mismatches are runtime errors, not compile errors. |
| **Follows experimental pattern** — zero deviation from established registration approach | **No mutations** — HotChocolate mutation infrastructure is unused weight. |
| **`application/json` response** — correct per FHIR GraphQL spec | **No OperationOutcome** — GraphQL errors differ from FHIR error conventions. |
| **Multi-version named schemas** — clean per-version separation | **Version proliferation** — N versions × schema builds = N compiled schemas in memory. |

### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| HotChocolate breaking changes in v16+ | Medium | Medium | Pin to `15.x.*`; integration tests catch regressions |
| Schema generation performance at scale | Low | Medium | Lazy build + warmup service; benchmark with R5 (~180 resources) |
| Memory pressure from multiple schemas | Low | Low | Monitor via health endpoint; option to disable unused versions |
| Overly complex queries (DoS) | Medium | High | Max depth (15), complexity (500), timeout (30s) |
| Reference resolution cycles | Low | High | Max depth prevents infinite recursion; DataLoader deduplicates |

---

## 18. Alignment Checklist

- [x] **Follows layer rules (API → App → Domain → Data)** — Endpoints in `Ignixa.Api`, all logic in `Ignixa.Application`, no Domain/DataLayer changes
- [x] **No `Hl7.Fhir.*` dependency** — Uses `Ignixa.Abstractions` exclusively (`ISchema`, `ITypeExtended`)
- [x] **Minimal API endpoints** — `GraphQlEndpoints.cs` with `MapPost`/`MapGet`, no MVC controllers
- [x] **Experimental feature pattern** — Master switch + per-feature toggle via `GraphQlExperimentalOptions`
- [x] **Multi-tenancy** — Tenant-explicit + agnostic routes; tenant context via `IFhirRequestContextAccessor`
- [x] **Multi-version FHIR** — Named schemas per version; schema selected at request time
- [x] **CancellationToken** — All async methods accept `CancellationToken cancellationToken`
- [x] **CQRS-first** — Resolvers call `IMediator.SendAsync()` preserving capability/partition/audit
- [x] **F5 Developer Experience** — Works with `dotnet run`; no external services; schema auto-generates
- [x] **FHIR spec compliance** — System + instance `$graphql`; GET + POST; `application/json`; introspection; directives
- [x] **One type per file** — Each class/record/interface in its own file
- [x] **File-scoped namespaces** — All new files use `namespace Foo;` syntax
- [x] **Primary constructors** — Used where appropriate for DI
- [x] **Nullable enabled** — All reference types nullable-annotated
- [x] **No secrets committed** — Configuration via `appsettings.json` overrides
- [x] **Existing data access reuse** — Via MediatR → existing handlers → repository/search

---

## 19. Evidence

### Specification References
- [FHIR GraphQL Specification](https://build.fhir.org/graphql.html)
- [FHIR R4 $graphql Operation Definition](https://www.hl7.org/fhir/R4/operation-resource-graphql.xml.html)
- [GraphQL Specification (June 2018)](https://spec.graphql.org/June2018/)

### Library References
- [HotChocolate v15 Documentation](https://chillicream.com/docs/hotchocolate/v15)
- [HotChocolate ITypeModule (Dynamic Schemas)](https://chillicream.com/docs/hotchocolate/v15/defining-a-schema/dynamic-schemas)
- [HotChocolate DataLoader](https://chillicream.com/docs/hotchocolate/v15/fetching-data/dataloader)
- [HotChocolate Named Schemas](https://chillicream.com/docs/hotchocolate/v15/distributed-schema/schema-stitching)

### Prior Art
- [GraphIR (Microsoft)](https://github.com/microsoft/Graphir) — .NET + HotChocolate proxy for FHIR
- [Medplum](https://www.medplum.com/docs/graphql) — TypeScript FHIR GraphQL
- [HAPI FHIR](https://github.com/hapifhir/hapi-fhir) — Java FHIR GraphQL
- [GraphQL-FHIR (Bluehalo)](https://github.com/bluehalo/graphql-fhir) — Node.js auto-generated from StructureDefinitions

### Codebase References
- `src/Application/Ignixa.Application/Features/Experimental/Infrastructure/ExperimentalAutofacRegistration.cs`
- `src/Application/Ignixa.Application/Features/Experimental/Infrastructure/ExperimentalServicesRegistration.cs`
- `src/Application/Ignixa.Api/Endpoints/Experimental/ExperimentalEndpointExtensions.cs`
- `src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs`
- `src/Application/Ignixa.Application/Features/Resource/GetResourceHandler.cs`
- `src/Application/Ignixa.Application/Features/Resource/SearchResourcesHandler.cs`
- `src/Core/Ignixa.Abstractions/Structure/ISchema.cs`
- `src/Core/Ignixa.Abstractions/Structure/ITypeExtended.cs`
- `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs`
- `src/Application/Ignixa.Domain/Abstractions/ISearchService.cs`
- `docs/adr/adr-2509-vertical-slice-architecture.md`
- `docs/adr/adr-2510-multi-tenancy.md`

---

## Verdict

**Viable and recommended for implementation.** HotChocolate v15 with `ITypeModule` dynamic schema generation, CQRS-first resolvers via MediatR, and `BatchDataLoader` for reference resolution is the strongest design for FHIR `$graphql` in the .NET ecosystem. The design:

- Integrates cleanly with the existing clean architecture
- Requires no changes to Domain or DataLayer
- Follows all established experimental feature patterns
- Provides a clear 3-phase implementation path (80-100 hours)
- Achieves full FHIR $graphql spec compliance

**Next steps**:
1. `/fn-adr fhir-graphql` — Synthesize this investigation into a proposed ADR
2. Phase 1 implementation: `FhirTypeModule` + `ResourceResolver` + `GraphQlEndpoints`
