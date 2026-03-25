# Investigation: HotChocolate-Based FHIR $graphql Design Proposal

**Feature**: fhir-graphql
**Status**: In Progress
**Created**: 2026-03-25

## Executive Summary

This document proposes implementing the FHIR `$graphql` operation using **HotChocolate v15** — the most mature GraphQL server library for .NET. The design dynamically generates a GraphQL schema from existing `ISchema`/`ITypeExtended` FHIR metadata at startup, routes queries through the existing CQRS layer (`IFhirRepository`, `ISearchService`), and follows the established experimental feature registration pattern.

**Key Decision**: Use HotChocolate's `ITypeModule` interface to build GraphQL types at schema creation time from the FHIR StructureDefinition metadata already available via `ISchema`. This avoids maintaining a parallel hand-coded GraphQL schema and automatically adapts when FHIR versions or custom StructureDefinitions change.

---

## 1. Library Choice Justification

### Why HotChocolate v15?

| Criterion | HotChocolate v15 | GraphQL.NET | Raw Execution |
|-----------|------------------|-------------|---------------|
| **.NET 9 support** | First-class; targets `net9.0` | Supported but community-driven | N/A |
| **Dynamic schema** | `ITypeModule` — purpose-built hook for runtime type generation | `Schema.For()` with manual builders | Full DIY |
| **Performance** | Compiled resolvers, query plan caching, operation pipeline | Good but no compiled resolvers | Depends on impl |
| **DataLoader** | Built-in `BatchDataLoader<TKey, TValue>`, `GroupedDataLoader` | Separate `GraphQL.DataLoader` pkg | DIY |
| **ASP.NET integration** | `MapGraphQL()` middleware, `IHttpRequestInterceptor` | `AddGraphQL()` but less pipeline control | DIY |
| **Subscriptions** | Built-in (WebSocket, SSE) — future FHIR Subscription bridge | Requires extra wiring | DIY |
| **Introspection** | Standard `__schema`/`__type` out of the box | Standard | DIY |
| **Directives** | `@skip`, `@include`, `@defer`, `@stream` built-in | `@skip`, `@include` built-in | DIY |
| **Complexity analysis** | Built-in query cost/depth limiting | Community package | DIY |
| **Community** | 5.5k+ GitHub stars, active maintenance, corporate backing (ChilliCream) | 5.8k stars, slower release cadence | N/A |
| **NuGet downloads** | ~85M total (HotChocolate.AspNetCore) | ~45M total | N/A |
| **License** | MIT | MIT | N/A |

**Decisive factors:**

1. **`ITypeModule`** — HotChocolate is the only .NET GraphQL library with a first-class extension point for injecting types at schema build time. This is exactly what we need: read `ISchema` metadata → emit GraphQL `ObjectType`, `UnionType`, `InterfaceType`, and `InputType` definitions without hand-coding each FHIR resource.

2. **Compiled resolvers** — HotChocolate compiles resolver delegates at startup, avoiding reflection overhead on every query. For a FHIR server handling complex nested queries (`Patient → Encounter → Observation`), this is a meaningful performance advantage.

3. **DataLoader integration** — Built-in `BatchDataLoader` solves the N+1 reference resolution problem inherent in FHIR GraphQL (a Patient query resolving `generalPractitioner`, `managingOrganization`, etc. in a single batch).

4. **Request interceptor pipeline** — `IHttpRequestInterceptor` lets us inject tenant context into the GraphQL execution context before resolvers run, aligning with the existing `TenantResolutionMiddleware` pattern.

### Version Pinning

Target **HotChocolate 15.x** (latest stable as of 2026). Key packages:

```xml
<PackageReference Include="HotChocolate.AspNetCore" Version="15.*" />
<PackageReference Include="HotChocolate.Data" Version="15.*" />
```

No dependency on `Hl7.Fhir.*` — all FHIR metadata comes from `Ignixa.Abstractions` (`ISchema`, `ITypeExtended`), respecting the layer dependency rules.

---

## 2. Architecture Overview

### Layer Mapping

```
┌─────────────────────────────────────────────────────────────────────┐
│  API Layer (Ignixa.Api)                                             │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │  GraphQlEndpoints.cs                                          │  │
│  │  - POST /tenant/{tenantId}/$graphql                           │  │
│  │  - GET  /tenant/{tenantId}/$graphql?query=...                 │  │
│  │  - POST /tenant/{tenantId}/{Type}/{id}/$graphql               │  │
│  │  - GET  /tenant/{tenantId}/{Type}/{id}/$graphql?query=...     │  │
│  │  + Tenant-agnostic variants of all four                       │  │
│  └──────────────────────────┬────────────────────────────────────┘  │
│                             │ Delegates to HotChocolate middleware   │
│  ┌──────────────────────────▼────────────────────────────────────┐  │
│  │  HotChocolate Pipeline                                        │  │
│  │  - FhirHttpRequestInterceptor (injects tenant + version ctx)  │  │
│  │  - Schema selected per-tenant via IRequestExecutorResolver    │  │
│  └──────────────────────────┬────────────────────────────────────┘  │
├─────────────────────────────┼───────────────────────────────────────┤
│  Application Layer (Ignixa.Application)                             │
│  ┌──────────────────────────▼────────────────────────────────────┐  │
│  │  GraphQL Module (Ignixa.Application.Features.Experimental.    │  │
│  │                  GraphQl/)                                    │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │  FhirTypeModule : ITypeModule                            │  │  │
│  │  │  - Reads ISchema at schema creation time                 │  │  │
│  │  │  - Emits ObjectType per resource (Patient, Observation)  │  │  │
│  │  │  - Emits nested ObjectType per BackboneElement           │  │  │
│  │  │  - Emits UnionType per choice element (value[x])         │  │  │
│  │  │  - Emits ResourceInterfaceType for Reference resolution  │  │  │
│  │  │  - Emits QueryType with read + list fields               │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │  Resolvers                                               │  │  │
│  │  │  - ResourceResolver  → IFhirRepository.GetAsync()        │  │  │
│  │  │  - SearchResolver    → ISearchService.SearchStreamAsync() │  │  │
│  │  │  - ReferenceResolver → ResourceDataLoader (batched)      │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  │  ┌─────────────────────────────────────────────────────────┐  │  │
│  │  │  ResourceDataLoader : BatchDataLoader<ResourceKey, …>    │  │  │
│  │  │  - Batches reference resolutions per request             │  │  │
│  │  └─────────────────────────────────────────────────────────┘  │  │
│  └───────────────────────────────────────────────────────────────┘  │
├─────────────────────────────────────────────────────────────────────┤
│  Domain Layer (Ignixa.Domain)                                       │
│  - IFhirRepository, ISearchService, ISchema — unchanged             │
├─────────────────────────────────────────────────────────────────────┤
│  DataLayer — unchanged                                              │
└─────────────────────────────────────────────────────────────────────┘
```

**Key principle**: No changes to Domain or DataLayer. All GraphQL logic lives in:
- `Ignixa.Api` — endpoint registration (thin HTTP glue)
- `Ignixa.Application` — schema generation, resolvers, DataLoader (business logic)

---

## 3. Schema Generation Strategy

### 3.1 The `FhirTypeModule` — Heart of the Design

HotChocolate's `ITypeModule` is invoked once during schema creation. We implement it to walk the full `ISchema` type graph and emit GraphQL types.

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Schema/FhirTypeModule.cs

public sealed class FhirTypeModule(
    ISchema fhirSchema,
    ISearchParameterDefinitionManager searchParamManager,
    ILogger<FhirTypeModule> logger) : ITypeModule
{
    public event EventHandler<EventArgs>? TypesChanged;

    public ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken)
    {
        var types = new List<ITypeSystemMember>();

        // 1. Emit scalar mappings for FHIR primitives
        EmitPrimitiveScalars(types);

        // 2. Emit ObjectType for each concrete resource type
        foreach (var resourceType in GetConcreteResourceTypes())
        {
            var fhirType = fhirSchema.GetTypeDefinition(resourceType);
            if (fhirType is null) continue;

            var objectType = BuildResourceObjectType(resourceType, fhirType);
            types.Add(objectType);
        }

        // 3. Emit the ResourceInterface union for Reference resolution
        types.Add(BuildResourceUnionType());

        // 4. Emit the Query root type
        types.Add(BuildQueryType());

        logger.LogInformation(
            "FhirTypeModule generated {TypeCount} GraphQL types for FHIR {Version}",
            types.Count, fhirSchema.Version);

        return ValueTask.FromResult<IReadOnlyCollection<ITypeSystemMember>>(types);
    }
}
```

### 3.2 Resource Types → GraphQL ObjectTypes

Each concrete FHIR resource type (Patient, Observation, etc.) maps to a GraphQL `ObjectType`. Child elements become fields on that type.

**Mapping Rules:**

| FHIR Concept | GraphQL Concept | Example |
|--------------|-----------------|---------|
| Resource (Patient) | `ObjectType("Patient")` | `type Patient { id: ID, name: [HumanName] }` |
| BackboneElement | Nested `ObjectType` | `type PatientContact { name: HumanName }` |
| Primitive element | Scalar field | `birthDate: FhirDate` |
| Complex element (1..1) | Object field | `maritalStatus: CodeableConcept` |
| Collection element (0..*) | List field | `name: [HumanName]` |
| Required element (1..1) | Non-null field | `status: String!` |
| Reference element | `ResourceReference` type with `resource` resolver | See §8 |
| Choice element (value[x]) | Union type | See §3.5 |

**ObjectType Generation:**

```csharp
private ObjectType BuildResourceObjectType(string resourceTypeName, IType fhirType)
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name(resourceTypeName);
        descriptor.Description($"FHIR {resourceTypeName} resource");

        // Always include "resourceType" meta-field
        descriptor.Field("resourceType")
            .Type<NonNullType<StringType>>()
            .Resolve(ctx => resourceTypeName);

        foreach (var child in fhirType.Children)
        {
            AddFieldForElement(descriptor, child, resourceTypeName);
        }
    });
}
```

### 3.3 BackboneElement Children → Nested ObjectTypes

FHIR BackboneElements (e.g., `Patient.contact`, `Observation.component`) have their own child elements. These become separate GraphQL ObjectTypes with a naming convention that avoids collisions.

**Naming**: `{ParentType}_{ElementName}` — e.g., `Patient_Contact`, `Observation_Component`, `Observation_Component_ReferenceRange`.

```csharp
private void AddFieldForElement(
    IObjectTypeDescriptor descriptor,
    IType element,
    string parentPath)
{
    var elementName = element.Info.Name;
    var info = element.Info;

    if (info.IsChoiceElement)
    {
        AddChoiceElementField(descriptor, element, parentPath);
        return;
    }

    if (info.IsPrimitive)
    {
        var graphQlType = MapFhirPrimitiveToGraphQlType(info.Primitive);
        var fieldDescriptor = descriptor.Field(ToCamelCase(elementName)).Type(graphQlType);
        ConfigureFieldCardinality(fieldDescriptor, element);
        fieldDescriptor.Resolve(ctx => ResolvePrimitiveValue(ctx, elementName));
        return;
    }

    // Complex type — check if it's a BackboneElement (has children but no standalone type)
    if (element.Children.Count > 0 && !fhirSchema.IsKnownType(info.Name))
    {
        // BackboneElement: generate nested ObjectType
        var nestedTypeName = $"{parentPath}_{ToPascalCase(elementName)}";
        var nestedType = BuildBackboneObjectType(nestedTypeName, element);
        _additionalTypes.Add(nestedType);

        var fieldDescriptor = descriptor.Field(ToCamelCase(elementName))
            .Type(new NamedTypeNode(nestedTypeName));
        ConfigureFieldCardinality(fieldDescriptor, element);
        fieldDescriptor.Resolve(ctx => ResolveComplexElement(ctx, elementName));
    }
    else
    {
        // Known complex type (HumanName, CodeableConcept, etc.)
        var fieldDescriptor = descriptor.Field(ToCamelCase(elementName))
            .Type(new NamedTypeNode(info.Name));
        ConfigureFieldCardinality(fieldDescriptor, element);
        fieldDescriptor.Resolve(ctx => ResolveComplexElement(ctx, elementName));
    }
}
```

### 3.4 Primitive Types → GraphQL Scalars

FHIR primitives map to either built-in GraphQL scalars or custom scalars:

| FhirPrimitive | GraphQL Type | Notes |
|---------------|-------------|-------|
| `Boolean` | `Boolean` | Direct map |
| `Integer` | `Int` | Direct map |
| `Integer64` | `Long` (custom) | 64-bit, HotChocolate has `LongType` |
| `String` | `String` | Direct map |
| `Decimal` | `Decimal` (custom) | HotChocolate has `DecimalType` |
| `Uri` | `String` | URI as string |
| `Url` | `UrlType` | HotChocolate built-in |
| `Canonical` | `String` | Canonical URL as string |
| `Uuid` | `UuidType` | HotChocolate built-in |
| `Base64Binary` | `String` | Base64-encoded |
| `Instant` | `DateTime` | HotChocolate `DateTimeType` |
| `Date` | `Date` | HotChocolate `DateType` |
| `DateTime` | `DateTime` | HotChocolate `DateTimeType` |
| `Time` | `String` | HH:MM:SS as string |
| `Code` | `String` | Code value |
| `Oid` | `String` | OID as string |
| `Id` | `ID` | GraphQL ID scalar |
| `Markdown` | `String` | Markdown text |
| `UnsignedInt` | `Int` | Non-negative integer |
| `PositiveInt` | `Int` | Positive integer |

```csharp
private static ITypeNode MapFhirPrimitiveToGraphQlType(FhirPrimitive primitive) => primitive switch
{
    FhirPrimitive.Boolean => new NamedTypeNode("Boolean"),
    FhirPrimitive.Integer => new NamedTypeNode("Int"),
    FhirPrimitive.Integer64 => new NamedTypeNode("Long"),
    FhirPrimitive.String => new NamedTypeNode("String"),
    FhirPrimitive.Decimal => new NamedTypeNode("Decimal"),
    FhirPrimitive.Uri => new NamedTypeNode("String"),
    FhirPrimitive.Url => new NamedTypeNode("Url"),
    FhirPrimitive.Canonical => new NamedTypeNode("String"),
    FhirPrimitive.Uuid => new NamedTypeNode("Uuid"),
    FhirPrimitive.Base64Binary => new NamedTypeNode("String"),
    FhirPrimitive.Instant => new NamedTypeNode("DateTime"),
    FhirPrimitive.Date => new NamedTypeNode("Date"),
    FhirPrimitive.DateTime => new NamedTypeNode("DateTime"),
    FhirPrimitive.Time => new NamedTypeNode("String"),
    FhirPrimitive.Code => new NamedTypeNode("String"),
    FhirPrimitive.Oid => new NamedTypeNode("String"),
    FhirPrimitive.Id => new NamedTypeNode("ID"),
    FhirPrimitive.Markdown => new NamedTypeNode("String"),
    FhirPrimitive.UnsignedInt => new NamedTypeNode("Int"),
    FhirPrimitive.PositiveInt => new NamedTypeNode("Int"),
    _ => new NamedTypeNode("String"),
};
```

### 3.5 Choice Elements (value[x]) → Union Types

FHIR choice elements like `Observation.value[x]` can hold different types (Quantity, CodeableConcept, String, etc.). These map to GraphQL union types.

**Naming**: `{ParentType}_{ElementName}Union` — e.g., `Observation_ValueUnion`.

```csharp
private void AddChoiceElementField(
    IObjectTypeDescriptor descriptor,
    IType element,
    string parentPath)
{
    var elementName = element.Info.Name; // "value" (without [x])
    var extendedElement = element as ITypeExtended;
    if (extendedElement is null) return;

    var unionName = $"{parentPath}_{ToPascalCase(elementName)}Union";
    var memberTypeNames = extendedElement.Types
        .Select(t => t.Code)
        .Where(code => fhirSchema.IsKnownType(code))
        .Distinct()
        .ToList();

    if (memberTypeNames.Count == 0) return;

    // Create union type
    var unionType = new UnionType(unionDescriptor =>
    {
        unionDescriptor.Name(unionName);
        unionDescriptor.Description($"Choice type for {parentPath}.{elementName}[x]");
        foreach (var memberTypeName in memberTypeNames)
        {
            unionDescriptor.Type(new NamedTypeNode(memberTypeName));
        }
    });
    _additionalTypes.Add(unionType);

    // Add field to parent
    var fieldDescriptor = descriptor.Field(ToCamelCase(elementName))
        .Type(new NamedTypeNode(unionName));
    ConfigureFieldCardinality(fieldDescriptor, element);
    fieldDescriptor.Resolve(ctx => ResolveChoiceElement(ctx, elementName, extendedElement.Types));
}
```

The resolver inspects which typed variant is present (e.g., `valueQuantity`, `valueString`) and returns the appropriate object with `__typename` set for the union discriminator.

### 3.6 Reference Fields → ResourceReference Type with Inline Resolution

FHIR `Reference` elements (e.g., `Patient.generalPractitioner`) map to a `ResourceReference` GraphQL type with a special `resource` field that resolves the referenced resource inline.

```graphql
type ResourceReference {
  reference: String
  type: String
  display: String
  identifier: Identifier
  resource: Resource           # <-- Inline resolution via DataLoader
}

union Resource = Patient | Observation | Encounter | Practitioner | Organization | ...
```

See §8 for the full reference resolution design.

### 3.7 Search Parameters → Query Arguments

Search parameters for each resource type (from `ISearchParameterDefinitionManager`) become GraphQL arguments on the list query fields.

```csharp
private void AddSearchArguments(
    IObjectFieldDescriptor fieldDescriptor,
    string resourceType)
{
    if (!searchParamManager.TryGetSearchParameters(resourceType, out var searchParams))
        return;

    foreach (var param in searchParams.Where(p => p.IsSearchable && p.IsSupported))
    {
        var argName = SanitizeSearchParamName(param.Code); // e.g., "family" stays "family"
        var argType = MapSearchParamTypeToGraphQl(param.Type);

        fieldDescriptor.Argument(argName, arg =>
        {
            arg.Type(argType);
            arg.Description(param.Description);
        });
    }

    // Standard FHIR search result parameters
    fieldDescriptor.Argument("_count", a => a.Type<IntType>().Description("Page size"));
    fieldDescriptor.Argument("_offset", a => a.Type<IntType>().Description("Page offset"));
    fieldDescriptor.Argument("_sort", a => a.Type<StringType>().Description("Sort criteria"));
    fieldDescriptor.Argument("_total", a => a.Type<StringType>().Description("Total count mode"));
}

private static ITypeNode MapSearchParamTypeToGraphQl(SearchParamType type) => type switch
{
    SearchParamType.String => new NamedTypeNode("String"),
    SearchParamType.Token => new NamedTypeNode("String"),
    SearchParamType.Reference => new NamedTypeNode("String"),
    SearchParamType.Date => new NamedTypeNode("String"),     // FHIR date prefix (e.g., "ge2024-01-01")
    SearchParamType.Number => new NamedTypeNode("String"),   // FHIR number prefix (e.g., "gt5")
    SearchParamType.Quantity => new NamedTypeNode("String"), // system|code|value
    SearchParamType.Uri => new NamedTypeNode("String"),
    SearchParamType.Composite => new NamedTypeNode("String"),
    SearchParamType.Special => new NamedTypeNode("String"),
    _ => new NamedTypeNode("String"),
};
```

**Design note**: All search parameter arguments are typed as `String` in GraphQL. This matches the FHIR search specification where all search parameters are transmitted as string-encoded values with prefix modifiers (e.g., `ge2024-01-01`, `exact|Smith`). The existing search parameter parsing infrastructure in `Ignixa.Search` handles decoding.

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
  # ... one per resource type

  # Type-level search (with search parameters as arguments)
  PatientList(name: String, family: String, birthdate: String, _count: Int, ...): PatientConnection
  ObservationList(code: String, patient: String, date: String, _count: Int, ...): ObservationConnection
  # ... one per resource type
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
            var listFieldName = $"{resourceType}List";
            var listField = descriptor.Field(listFieldName)
                .Type(new NamedTypeNode($"{resourceType}Connection"));

            AddSearchArguments(listField, resourceType);

            listField.Resolve(async ctx =>
            {
                var arguments = ctx.Selection.Arguments;
                return await ctx.Service<SearchResolver>()
                    .SearchAsync(resourceType, arguments, ctx.RequestAborted);
            });
        }
    });
}
```

### 4.2 Instance-Level Override

For instance-level `$graphql` (e.g., `POST /Patient/123/$graphql`), the endpoint injects the resource type and ID into the GraphQL execution context. The query root is rewritten so the root object is the specific resource:

```csharp
// In the endpoint handler:
var request = QueryRequestBuilder.New()
    .SetQuery(graphQlQuery)
    .SetVariableValues(variables)
    .AddGlobalState("InstanceResourceType", resourceType)
    .AddGlobalState("InstanceResourceId", resourceId)
    .Create();
```

The `FhirTypeModule` emits an alternative query root for instance-level queries where the root fields are the resource's own fields (not the `Patient(id)` / `PatientList(...)` pattern).

### 4.3 Connection Type for Pagination

Each list query returns a connection type following the Relay pagination convention (simplified for FHIR):

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

```csharp
private ObjectType BuildConnectionType(string resourceType)
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name($"{resourceType}Connection");

        descriptor.Field("entry")
            .Type<NonNullType<ListType<NonNullType<ObjectType>>>>()
            .Resolve(ctx => ctx.Parent<SearchResult>().Entries);

        descriptor.Field("total")
            .Type<IntType>()
            .Resolve(ctx => ctx.Parent<SearchResult>().Total);

        descriptor.Field("link")
            .Type(new NamedTypeNode("PaginationLinks"))
            .Resolve(ctx => ctx.Parent<SearchResult>().Links);
    });
}
```

---

## 5. Resolver Architecture

Resolvers bridge GraphQL field execution to the existing Ignixa data access layer. They do **not** go through MediatR/CQRS — they call `IFhirRepository` and `ISearchService` directly. This is intentional: the GraphQL operation itself is the "command/query" that the MediatR handler dispatches, and resolvers are internal implementation details of that handler's execution.

### 5.1 ResourceResolver — Instance Reads

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/ResourceResolver.cs

public sealed class ResourceResolver(
    IFhirRepository repository,
    IFhirRequestContextAccessor contextAccessor,
    IFhirVersionContext versionContext)
{
    public async Task<JsonElement?> ResolveByIdAsync(
        string resourceType,
        string id,
        CancellationToken cancellationToken)
    {
        var requestContext = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        var key = new ResourceKey(resourceType, id, TenantId: requestContext.TenantId);
        var result = await repository.GetAsync(key, cancellationToken);

        if (result is null || result.IsDeleted)
            return null;

        // Parse raw bytes into JsonElement for field-level resolution
        return JsonSerializer.Deserialize<JsonElement>(result.ResourceBytes.Span);
    }
}
```

**Why `JsonElement` as the resolver return type?**

`SearchEntryResult.ResourceBytes` is `ReadOnlyMemory<byte>` — raw JSON. We parse it into `System.Text.Json.JsonElement` which provides zero-copy field access. Each child field resolver then navigates the `JsonElement` tree:

```csharp
// Generic field resolver for all resource fields
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

### 5.2 SearchResolver — List Queries

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/Resolvers/SearchResolver.cs

public sealed class SearchResolver(
    ISearchService searchService,
    IFhirRequestContextAccessor contextAccessor,
    ISearchParameterDefinitionManager searchParamManager)
{
    public async Task<SearchResult> SearchAsync(
        string resourceType,
        IReadOnlyDictionary<string, object?> arguments,
        CancellationToken cancellationToken)
    {
        var requestContext = contextAccessor.RequestContext
            ?? throw new InvalidOperationException("FHIR request context not available");

        // Build SearchOptions from GraphQL arguments
        var searchOptions = BuildSearchOptions(resourceType, arguments);

        // Stream results and materialize
        var entries = new List<JsonElement>();
        await foreach (var entry in searchService.SearchStreamAsync<SearchOptions>(
            searchOptions, cancellationToken))
        {
            var json = JsonSerializer.Deserialize<JsonElement>(entry.ResourceBytes.Span);
            entries.Add(json);
        }

        return new SearchResult
        {
            Entries = entries,
            Total = entries.Count, // Use TotalType.Accurate if requested
            Links = BuildPaginationLinks(searchOptions, entries.Count),
        };
    }

    private SearchOptions BuildSearchOptions(
        string resourceType,
        IReadOnlyDictionary<string, object?> arguments)
    {
        var options = new SearchOptions { ResourceType = resourceType };

        // Map _count argument
        if (arguments.TryGetValue("_count", out var countVal) && countVal is int count)
            options.MaxItemCount = Math.Min(count, 1000); // Cap at 1000

        // Map _sort argument
        if (arguments.TryGetValue("_sort", out var sortVal) && sortVal is string sort)
            options.Sort = ParseSortExpression(sort);

        // Map search parameters to Expression tree
        // Reuse existing search parameter parsing infrastructure
        var expressionParts = new List<Expression>();
        foreach (var (key, value) in arguments)
        {
            if (key.StartsWith('_') || value is null) continue;

            if (searchParamManager.TryGetSearchParameter(resourceType, key, out var paramInfo))
            {
                var expression = BuildSearchExpression(paramInfo, value.ToString()!);
                if (expression is not null)
                    expressionParts.Add(expression);
            }
        }

        if (expressionParts.Count > 0)
            options.Expression = Expression.And(expressionParts);

        return options;
    }
}
```

### 5.3 Resolver Lifecycle

| Resolver | Scope | Injected Via |
|----------|-------|-------------|
| `ResourceResolver` | Scoped (per-request) | `ctx.Service<ResourceResolver>()` |
| `SearchResolver` | Scoped (per-request) | `ctx.Service<SearchResolver>()` |
| `ResourceDataLoader` | Scoped (per-request) | HotChocolate DataLoader DI |
| Field resolvers | Compiled delegates | `FhirTypeModule` at schema build |

---

## 6. Multi-Tenancy

### 6.1 Tenant Context Flow

```
HTTP Request
  │
  ▼
TenantResolutionMiddleware          ← Existing: extracts tenantId, stores in HttpContext.Items
  │
  ▼
GraphQlEndpoints.HandleGraphQl()   ← Reads tenantId from HttpContext.Items
  │                                   Sets IFhirRequestContext with TenantId + FhirVersion
  │
  ▼
FhirHttpRequestInterceptor          ← Copies tenant context into HotChocolate's
  │                                   IRequestContext.ContextData for resolver access
  │
  ▼
Resolvers                           ← Read IFhirRequestContextAccessor (scoped, tenant-aware)
  │                                   All IFhirRepository/ISearchService calls are tenant-scoped
  ▼
IFhirRepository / ISearchService    ← Tenant-filtered at the data layer
```

### 6.2 Request Interceptor

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
        // Tenant context is already set by TenantResolutionMiddleware
        // and FhirRequestContextMiddleware. Propagate to HotChocolate context.
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

### 6.3 Tenant Route Groups

Following the `TransformEndpoints.cs` pattern:

```csharp
// Tenant-explicit routes
var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");
configureTenantGroup?.Invoke(tenantGroup);
tenantGroup.MapPost("/$graphql", HandleSystemGraphQl);
tenantGroup.MapGet("/$graphql", HandleSystemGraphQlGet);
tenantGroup.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQl);
tenantGroup.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGet);

// Tenant-agnostic routes (single-tenant auto-detection)
endpoints.MapPost("/$graphql", HandleSystemGraphQlAgnostic);
endpoints.MapGet("/$graphql", HandleSystemGraphQlGetAgnostic);
endpoints.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlAgnostic);
endpoints.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGetAgnostic);
```

---

## 7. Multi-Version Support (Per-Tenant Schema Selection)

Different tenants may use different FHIR versions (R4, R4B, R5). Each version has a different `ISchema` with different resource types, elements, and search parameters. HotChocolate needs a separate compiled schema per FHIR version.

### 7.1 Schema-Per-Version with `IRequestExecutorResolver`

HotChocolate supports named schemas via `IRequestExecutorResolver`. We create one schema per FHIR version:

```csharp
// During service registration:
services.AddGraphQLServer("fhir-r4")
    .AddTypeModule<FhirTypeModule>(sp =>
    {
        var versionContext = sp.GetRequiredService<IFhirVersionContext>();
        var schema = versionContext.GetBaseSchemaProvider(FhirVersion.R4);
        var searchParams = versionContext.GetSearchParameterDefinitionManager(FhirVersion.R4);
        return new FhirTypeModule(schema, searchParams, sp.GetRequiredService<ILogger<FhirTypeModule>>());
    });

services.AddGraphQLServer("fhir-r4b")
    .AddTypeModule<FhirTypeModule>(sp => /* R4B schema */);

services.AddGraphQLServer("fhir-r5")
    .AddTypeModule<FhirTypeModule>(sp => /* R5 schema */);
```

### 7.2 Schema Selection at Request Time

The endpoint handler selects the correct schema name based on the tenant's FHIR version:

```csharp
private static async Task<IResult> HandleSystemGraphQl(
    HttpContext context,
    int tenantId,
    [FromServices] IFhirRequestContextAccessor contextAccessor,
    [FromServices] IRequestExecutorResolver executorResolver,
    CancellationToken cancellationToken)
{
    var fhirContext = contextAccessor.RequestContext!;
    var schemaName = $"fhir-{fhirContext.FhirVersion.ToString().ToLowerInvariant()}";

    var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);

    // Parse GraphQL request from body
    var graphQlRequest = await ParseGraphQlRequestAsync(context.Request, cancellationToken);

    var result = await executor.ExecuteAsync(
        QueryRequestBuilder.New()
            .SetQuery(graphQlRequest.Query)
            .SetVariableValues(graphQlRequest.Variables)
            .AddGlobalState("TenantId", tenantId)
            .Create(),
        cancellationToken);

    return await WriteGraphQlResponseAsync(context.Response, result, cancellationToken);
}
```

### 7.3 Schema Warm-Up

Schemas are built lazily on first request but can be warmed during startup:

```csharp
// In ExperimentalServicesRegistration or a hosted service:
public class GraphQlSchemaWarmupService(
    IRequestExecutorResolver resolver) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Pre-build schemas for configured versions
        await resolver.GetRequestExecutorAsync("fhir-r4", cancellationToken);
        await resolver.GetRequestExecutorAsync("fhir-r5", cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

---

## 8. Reference Resolution (DataLoader Pattern)

### 8.1 The N+1 Problem

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

Without batching, each `resource` field triggers a separate `IFhirRepository.GetAsync()` call — potentially dozens per page.

### 8.2 ResourceDataLoader

HotChocolate's `BatchDataLoader` collects all keys within a single execution step, then resolves them in one batch:

```csharp
// src/Application/Ignixa.Application/Features/Experimental/GraphQl/DataLoaders/ResourceDataLoader.cs

public sealed class ResourceDataLoader(
    IFhirRepository repository,
    IFhirRequestContextAccessor contextAccessor,
    IBatchScheduler batchScheduler)
    : BatchDataLoader<ResourceKey, JsonElement?>(batchScheduler)
{
    protected override async Task<IReadOnlyDictionary<ResourceKey, JsonElement?>> LoadBatchAsync(
        IReadOnlyList<ResourceKey> keys,
        CancellationToken cancellationToken)
    {
        var tenantId = contextAccessor.RequestContext?.TenantId;
        var results = new Dictionary<ResourceKey, JsonElement?>(keys.Count);

        // Batch all lookups — IFhirRepository.GetAsync is called per-key
        // but the DataLoader ensures this happens once per unique key per request
        var tasks = keys.Select(async key =>
        {
            var tenantKey = key with { TenantId = tenantId };
            var entry = await repository.GetAsync(tenantKey, cancellationToken);

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

### 8.3 Reference Field Resolution

```csharp
private static void AddReferenceField(
    IObjectTypeDescriptor descriptor,
    IType element)
{
    descriptor.Field(ToCamelCase(element.Info.Name))
        .Type(new NamedTypeNode("ResourceReference"))
        .Resolve(ctx =>
        {
            var parent = ctx.Parent<JsonElement>();
            if (!parent.TryGetProperty(element.Info.Name, out var refElement))
                return null;
            return refElement;
        });
}

// ResourceReference type has a "resource" field that uses the DataLoader
private ObjectType BuildResourceReferenceType()
{
    return new ObjectType(descriptor =>
    {
        descriptor.Name("ResourceReference");

        descriptor.Field("reference")
            .Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "reference"));

        descriptor.Field("type")
            .Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "type"));

        descriptor.Field("display")
            .Type<StringType>()
            .Resolve(ctx => GetStringProperty(ctx.Parent<JsonElement>(), "display"));

        descriptor.Field("resource")
            .Type(new NamedTypeNode("Resource")) // The Resource union type
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

### 8.4 Deduplication

The `BatchDataLoader` automatically deduplicates keys. If the same `Practitioner/123` is referenced by 10 patients in a list result, it's loaded exactly once.

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
        endpoints.MapGraphQlTenantEndpoints(configureTenantGroup);
        endpoints.MapGraphQlAgnosticEndpoints();
        return endpoints;
    }

    private static void MapGraphQlTenantEndpoints(
        this IEndpointRouteBuilder endpoints,
        Action<RouteGroupBuilder>? configureTenantGroup = null)
    {
        var tenantGroup = endpoints.MapGroup("/tenant/{tenantId:int}");
        configureTenantGroup?.Invoke(tenantGroup);

        // System-level $graphql
        tenantGroup.MapPost("/$graphql", HandleSystemGraphQl)
            .WithName("GraphQlSystem")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>(KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);

        tenantGroup.MapGet("/$graphql", HandleSystemGraphQlGet)
            .WithName("GraphQlSystemGet")
            .WithTags("Experimental", "GraphQL")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);

        // Instance-level $graphql
        tenantGroup.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQl)
            .WithName("GraphQlInstance")
            .WithTags("Experimental", "GraphQL")
            .Accepts<GraphQlRequestBody>(KnownContentTypes.ApplicationJson)
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);

        tenantGroup.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGet)
            .WithName("GraphQlInstanceGet")
            .WithTags("Experimental", "GraphQL")
            .Produces<object>(StatusCodes.Status200OK, KnownContentTypes.ApplicationJson);
    }

    private static void MapGraphQlAgnosticEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/$graphql", HandleSystemGraphQlAgnostic)
            .WithName("GraphQlSystemAgnostic")
            .WithTags("Experimental", "GraphQL");

        endpoints.MapGet("/$graphql", HandleSystemGraphQlGetAgnostic)
            .WithName("GraphQlSystemGetAgnostic")
            .WithTags("Experimental", "GraphQL");

        endpoints.MapPost("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlAgnostic)
            .WithName("GraphQlInstanceAgnostic")
            .WithTags("Experimental", "GraphQL");

        endpoints.MapGet("/{resourceType}/{resourceId}/$graphql", HandleInstanceGraphQlGetAgnostic)
            .WithName("GraphQlInstanceGetAgnostic")
            .WithTags("Experimental", "GraphQL");
    }

    // --- Handler Methods ---

    private static async Task<IResult> HandleSystemGraphQl(
        HttpContext context,
        int tenantId,
        [FromServices] IRequestExecutorResolver executorResolver,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var fhirContext = contextAccessor.RequestContext!;
        var schemaName = GetSchemaName(fhirContext.FhirVersion);

        var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);
        var graphQlRequest = await ParseGraphQlRequestFromBodyAsync(context.Request, cancellationToken);

        var queryRequest = QueryRequestBuilder.New()
            .SetQuery(graphQlRequest.Query)
            .SetVariableValues(graphQlRequest.Variables)
            .SetOperationName(graphQlRequest.OperationName)
            .Create();

        var result = await executor.ExecuteAsync(queryRequest, cancellationToken);
        return await WriteGraphQlResultAsync(result);
    }

    private static async Task<IResult> HandleSystemGraphQlGet(
        HttpContext context,
        int tenantId,
        [FromQuery] string query,
        [FromServices] IRequestExecutorResolver executorResolver,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var fhirContext = contextAccessor.RequestContext!;
        var schemaName = GetSchemaName(fhirContext.FhirVersion);

        var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);

        var queryRequest = QueryRequestBuilder.New()
            .SetQuery(query)
            .Create();

        var result = await executor.ExecuteAsync(queryRequest, cancellationToken);
        return await WriteGraphQlResultAsync(result);
    }

    private static async Task<IResult> HandleInstanceGraphQl(
        HttpContext context,
        int tenantId,
        string resourceType,
        string resourceId,
        [FromServices] IRequestExecutorResolver executorResolver,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        var fhirContext = contextAccessor.RequestContext!;
        var schemaName = GetSchemaName(fhirContext.FhirVersion);

        var executor = await executorResolver.GetRequestExecutorAsync(schemaName, cancellationToken);
        var graphQlRequest = await ParseGraphQlRequestFromBodyAsync(context.Request, cancellationToken);

        var queryRequest = QueryRequestBuilder.New()
            .SetQuery(graphQlRequest.Query)
            .SetVariableValues(graphQlRequest.Variables)
            .SetOperationName(graphQlRequest.OperationName)
            .AddGlobalState("InstanceResourceType", resourceType)
            .AddGlobalState("InstanceResourceId", resourceId)
            .Create();

        var result = await executor.ExecuteAsync(queryRequest, cancellationToken);
        return await WriteGraphQlResultAsync(result);
    }

    // --- Helpers ---

    private static string GetSchemaName(FhirVersion version) =>
        $"fhir-{version.ToString().ToLowerInvariant()}";

    private static async Task<GraphQlRequestBody> ParseGraphQlRequestFromBodyAsync(
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        return await JsonSerializer.DeserializeAsync<GraphQlRequestBody>(
            request.Body,
            GraphQlJsonOptions.Default,
            cancellationToken)
            ?? throw new InvalidOperationException("Failed to parse GraphQL request body");
    }

    private static async Task<IResult> WriteGraphQlResultAsync(IExecutionResult result)
    {
        // HotChocolate IExecutionResult serializes to standard GraphQL JSON response
        // { "data": {...}, "errors": [...], "extensions": {...} }
        await using var stream = new MemoryStream();
        await result.WriteTo(stream);
        stream.Position = 0;
        var bytes = stream.ToArray();
        return Results.Bytes(bytes, KnownContentTypes.ApplicationJson);
    }

    // Agnostic handlers delegate to tenant-explicit after extracting tenantId
    private static async Task<IResult> HandleSystemGraphQlAgnostic(
        HttpContext context,
        [FromServices] IRequestExecutorResolver executorResolver,
        [FromServices] IFhirRequestContextAccessor contextAccessor,
        CancellationToken cancellationToken)
    {
        if (!context.Items.TryGetValue("TenantId", out var tenantIdObj) || tenantIdObj is not int tenantId)
            return Results.BadRequest(new { error = "Tenant ID is required for multi-tenant deployments" });

        return await HandleSystemGraphQl(context, tenantId, executorResolver, contextAccessor, cancellationToken);
    }

    // ... similar agnostic variants for GET, instance POST, instance GET
}

/// <summary>
/// GraphQL request body DTO.
/// </summary>
public sealed record GraphQlRequestBody
{
    [JsonPropertyName("query")]
    public string Query { get; init; } = string.Empty;

    [JsonPropertyName("operationName")]
    public string? OperationName { get; init; }

    [JsonPropertyName("variables")]
    public Dictionary<string, object?>? Variables { get; init; }
}
```

### 9.2 Response Format

Per the FHIR GraphQL spec, responses use `application/json` (not `application/fhir+json`) and follow the standard GraphQL response format:

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

Errors follow the GraphQL error format:

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

## 10. Registration & Dependency Injection

### 10.1 Configuration Options

```csharp
// Added to ExperimentalOptions.cs

/// <summary>
/// Configuration options for $graphql operation.
/// </summary>
public class GraphQlExperimentalOptions
{
    /// <summary>
    /// Whether $graphql operation is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum query depth allowed (prevents deeply nested attacks).
    /// Default: 15 (sufficient for most FHIR queries with reference resolution).
    /// </summary>
    public int MaxQueryDepth { get; set; } = 15;

    /// <summary>
    /// Whether to enable GraphQL introspection queries (__schema, __type).
    /// Should be disabled in production for security.
    /// Default: true (enabled in experimental mode).
    /// </summary>
    public bool EnableIntrospection { get; set; } = true;

    /// <summary>
    /// Maximum query complexity score allowed.
    /// Each field has a cost of 1; list fields have a cost multiplied by the page size.
    /// Default: 500.
    /// </summary>
    public int MaxQueryComplexity { get; set; } = 500;

    /// <summary>
    /// Maximum number of results per list query (_count cap).
    /// Default: 1000.
    /// </summary>
    public int MaxPageSize { get; set; } = 1000;

    /// <summary>
    /// Default page size when _count is not specified.
    /// Default: 10 (FHIR default).
    /// </summary>
    public int DefaultPageSize { get; set; } = 10;

    /// <summary>
    /// FHIR versions to pre-build schemas for at startup.
    /// Empty = lazy build on first request.
    /// Default: ["R4"] (most common version).
    /// </summary>
    public ICollection<string> WarmupVersions { get; } = ["R4"];
}
```

### 10.2 ExperimentalFeaturesOptions Addition

```csharp
// Add to ExperimentalFeaturesOptions class in ExperimentalOptions.cs:

/// <summary>
/// $graphql operation configuration.
/// </summary>
public GraphQlExperimentalOptions GraphQl { get; set; } = new();
```

### 10.3 Autofac Registration

```csharp
// Add to ExperimentalAutofacRegistration.cs:

// Feature: GraphQL - $graphql operation
if (options.Features.GraphQl.Enabled)
{
    builder.RegisterGraphQlHandlers();
}

// ...

private static void RegisterGraphQlHandlers(this ContainerBuilder builder)
{
    // Resolvers (scoped per-request)
    builder.RegisterType<ResourceResolver>()
        .AsSelf()
        .InstancePerLifetimeScope();

    builder.RegisterType<SearchResolver>()
        .AsSelf()
        .InstancePerLifetimeScope();

    // DataLoader is registered via HotChocolate's AddDataLoader<T>() in IServiceCollection
    // (HotChocolate manages DataLoader lifecycle per-request)

    // ISchema for GraphQL schema generation (request-scoped, tenant-aware)
    // NOTE: Reuses the same registration pattern as IPS
    builder.Register(c =>
    {
        var versionContext = c.Resolve<IFhirVersionContext>();
        var requestContextAccessor = c.Resolve<IFhirRequestContextAccessor>();
        var requestContext = requestContextAccessor.RequestContext;
        return requestContext is not null
            ? versionContext.GetSchemaProvider(requestContext.FhirVersion, requestContext.TenantId)
            : versionContext.GetBaseSchemaProvider(FhirVersion.R4);
    })
    .Named<ISchema>("graphql")
    .InstancePerLifetimeScope();
}
```

### 10.4 IServiceCollection Registration

```csharp
// Add to ExperimentalServicesRegistration.cs:

// Feature: GraphQL - $graphql operation
if (options.Features.GraphQl.Enabled)
{
    services.AddGraphQlServices(configuration, options.Features.GraphQl);
}

// ...

private static void AddGraphQlServices(
    this IServiceCollection services,
    IConfiguration configuration,
    GraphQlExperimentalOptions graphQlOptions)
{
    // Register one named schema per supported FHIR version
    var versions = new[] { FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5 };

    foreach (var version in versions)
    {
        var schemaName = $"fhir-{version.ToString().ToLowerInvariant()}";

        services.AddGraphQLServer(schemaName)
            .AddTypeModule(sp =>
            {
                var versionContext = sp.GetRequiredService<IFhirVersionContext>();
                var schema = versionContext.GetBaseSchemaProvider(version);
                var searchParams = versionContext.GetSearchParameterDefinitionManager(version);
                var logger = sp.GetRequiredService<ILogger<FhirTypeModule>>();
                return new FhirTypeModule(schema, searchParams, logger);
            })
            .AddDataLoader<ResourceDataLoader>()
            .ModifyRequestOptions(opt =>
            {
                opt.IncludeExceptionDetails = false; // Don't leak stack traces
            })
            .AddMaxExecutionDepthRule(graphQlOptions.MaxQueryDepth)
            .AddIntrospectionAllowedRule()  // Controlled per-query via interceptor
            .SetRequestOptions(_ => new RequestExecutorOptions
            {
                ExecutionTimeout = TimeSpan.FromSeconds(30),
            })
            .AddHttpRequestInterceptor<FhirHttpRequestInterceptor>();
    }

    // Schema warmup hosted service
    if (graphQlOptions.WarmupVersions.Count > 0)
    {
        services.AddHostedService<GraphQlSchemaWarmupService>();
    }
}
```

### 10.5 Endpoint Registration

```csharp
// Add to ExperimentalEndpointExtensions.cs:

// Feature: GraphQL - $graphql operation
if (options.Features.GraphQl.Enabled)
{
    app.MapGraphQlEndpoints(configureTenantGroup);
}
```

### 10.6 appsettings.json Configuration

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
        "WarmupVersions": ["R4"]
      }
    }
  }
}
```

---

## 11. File / Folder Structure

All new files and their exact paths:

```
src/
├── Application/
│   ├── Ignixa.Api/
│   │   └── Endpoints/
│   │       └── Experimental/
│   │           └── GraphQlEndpoints.cs                          # Minimal API endpoints
│   │
│   └── Ignixa.Application/
│       └── Features/
│           └── Experimental/
│               ├── Configuration/
│               │   └── ExperimentalOptions.cs                   # MODIFIED: Add GraphQlExperimentalOptions
│               ├── Infrastructure/
│               │   ├── ExperimentalAutofacRegistration.cs       # MODIFIED: Add GraphQL registrations
│               │   └── ExperimentalServicesRegistration.cs      # MODIFIED: Add HotChocolate services
│               └── GraphQl/                                     # NEW DIRECTORY
│                   ├── Schema/
│                   │   ├── FhirTypeModule.cs                    # ITypeModule implementation
│                   │   ├── FhirScalarMappings.cs                # FhirPrimitive → GraphQL scalar mapping
│                   │   └── GraphQlTypeNameHelper.cs             # Naming conventions (PascalCase, union names)
│                   ├── Resolvers/
│                   │   ├── ResourceResolver.cs                  # Instance-level read via IFhirRepository
│                   │   ├── SearchResolver.cs                    # List queries via ISearchService
│                   │   └── FieldResolver.cs                     # Generic JsonElement field navigation
│                   ├── DataLoaders/
│                   │   └── ResourceDataLoader.cs                # BatchDataLoader for reference resolution
│                   ├── Pipeline/
│                   │   ├── FhirHttpRequestInterceptor.cs        # Tenant context injection
│                   │   └── GraphQlSchemaWarmupService.cs        # IHostedService for schema pre-build
│                   └── Models/
│                       ├── GraphQlRequestBody.cs                # Request DTO
│                       ├── SearchResult.cs                      # Search result DTO for connection types
│                       └── GraphQlJsonOptions.cs                # System.Text.Json options for GraphQL I/O
│
test/
└── Ignixa.Application.Tests/
    └── Features/
        └── Experimental/
            └── GraphQl/                                         # NEW DIRECTORY
                ├── FhirTypeModuleTests.cs                       # Schema generation tests
                ├── ResourceResolverTests.cs                     # Instance read resolver tests
                ├── SearchResolverTests.cs                       # Search resolver tests
                ├── ResourceDataLoaderTests.cs                   # DataLoader batching tests
                ├── GraphQlEndpointTests.cs                      # Integration tests for endpoints
                └── GraphQlRequestParsingTests.cs                # Request body parsing tests
```

**Total new files**: ~18 (12 source + 6 test)
**Modified files**: 3 (ExperimentalOptions.cs, ExperimentalAutofacRegistration.cs, ExperimentalServicesRegistration.cs)
**Modified endpoint file**: 1 (ExperimentalEndpointExtensions.cs)

---

## 12. Tradeoffs

| Pros | Cons |
|------|------|
| **Dynamic schema from ISchema metadata** — automatically stays in sync with FHIR StructureDefinitions; no manual GraphQL schema maintenance | **Startup cost** — building the GraphQL schema walks all FHIR types (hundreds of types); ~2-5 seconds per version. Mitigated by warmup service |
| **HotChocolate's compiled resolvers** — sub-microsecond resolver dispatch after initial compilation | **Large dependency** — HotChocolate brings ~15 NuGet packages; increases binary size by ~5-10 MB |
| **Built-in DataLoader** — solves reference N+1 without custom batching infrastructure | **Learning curve** — team must understand HotChocolate's `ITypeModule`, resolver context, and execution pipeline |
| **Standard GraphQL introspection** — clients can auto-discover the schema without FHIR-specific tooling | **Schema complexity** — FHIR R4 has ~150 resource types × ~20 elements each = ~3000+ GraphQL types. May overwhelm naive introspection clients |
| **Query depth/complexity limiting** — built-in protection against DoS queries | **Memory** — each compiled schema holds ~10-50 MB of type metadata in memory; multiply by number of FHIR versions |
| **`@skip`/`@include`/`@defer`/`@stream` directives** — free from HotChocolate | **No mutations** — FHIR $graphql spec is read-only; HotChocolate's mutation infrastructure is unused weight |
| **Multi-version schemas** via named executors — clean separation per FHIR version | **Version proliferation** — N FHIR versions × M tenant configs = N compiled schemas in memory |
| **Follows existing experimental pattern** — zero deviation from established registration/configuration approach | **Not CQRS-pure** — resolvers call `IFhirRepository`/`ISearchService` directly rather than through MediatR handlers. This is a pragmatic choice; wrapping each GraphQL field in a MediatR command would add needless overhead |
| **Response format compliance** — `application/json` (not FHIR+json) matches the FHIR $graphql spec | **No FHIR OperationOutcome in errors** — GraphQL error format differs from FHIR error conventions; clients must handle both |
| **JsonElement-based resolution** — zero-copy from raw bytes to field values; no intermediate FHIR model materialization | **Limited type safety** — `JsonElement` navigation is stringly-typed; field name mismatches are runtime errors |

### Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|-----------|--------|------------|
| HotChocolate breaking changes in v16+ | Medium | Medium | Pin to `15.x.*`; integration tests catch regressions |
| Schema generation performance at scale | Low | Medium | Lazy schema build + warmup service; benchmark with R5 (~180 resources) |
| Memory pressure from multiple schemas | Low | Low | Monitor with `/health` endpoint; option to disable unused versions |
| Security: overly complex queries | Medium | High | Max depth (15), max complexity (500), execution timeout (30s) |
| Reference resolution cycles | Low | High | Max depth limit prevents infinite recursion; DataLoader deduplicates |

---

## 13. Alignment Checklist

- [x] **Follows layer rules (API → App → Domain → Data)** — Endpoints in `Ignixa.Api`, schema generation + resolvers in `Ignixa.Application`, no Domain/DataLayer changes
- [x] **No `Hl7.Fhir.*` dependency** — Uses `Ignixa.Abstractions` (`ISchema`, `ITypeExtended`, `FhirPrimitive`) exclusively
- [x] **Minimal API endpoints** — `GraphQlEndpoints.cs` with `MapPost`/`MapGet`, no MVC controllers
- [x] **Experimental feature pattern** — Master switch + per-feature toggle via `GraphQlExperimentalOptions`; registration in Autofac, IServiceCollection, and endpoint extensions
- [x] **Multi-tenancy** — Tenant-explicit (`/tenant/{tenantId}/...`) and agnostic routes; tenant context via `IFhirRequestContextAccessor`
- [x] **Multi-version FHIR** — Named schemas per FHIR version (`fhir-r4`, `fhir-r5`); schema selected at request time based on tenant's FHIR version
- [x] **CancellationToken everywhere** — All async methods accept `CancellationToken cancellationToken`
- [x] **F5 Developer Experience** — Works with `dotnet run`; no external services required; schema auto-generates from existing metadata
- [x] **FHIR spec compliance** — System-level and instance-level `$graphql`; GET and POST; `application/json` response; introspection; `@skip`/`@include` directives
- [x] **One type per file** — Each class/record/interface in its own file
- [x] **File-scoped namespaces** — All new files use `namespace Foo;` syntax
- [x] **Primary constructors** — Used for DI (resolvers, DataLoader, endpoints)
- [x] **Nullable enabled** — All reference types nullable-annotated
- [x] **No secrets committed** — Configuration via `appsettings.json` overrides
- [x] **Existing data access reuse** — Resolvers call `IFhirRepository.GetAsync()` and `ISearchService.SearchStreamAsync()` directly; no new data layer abstractions

---

## 14. Open Questions for ADR Discussion

1. **Should the GraphQL schema include abstract types (Resource, DomainResource)?** The FHIR GraphQL spec says no — only concrete resource types appear in the schema. But abstract types are useful as interface types for the `resource` union. Recommendation: emit them as GraphQL interfaces, not object types.

2. **Should we support `_include`/`_revinclude` as GraphQL arguments, or rely solely on reference inline resolution?** The GraphQL model encourages inline resolution (`subject { resource { ... } }`), making `_include` redundant. Recommendation: omit `_include`/`_revinclude` from arguments; use reference resolution instead.

3. **Should we expose extension elements in the GraphQL schema?** FHIR extensions are open-ended (`extension: [Extension]`), which maps poorly to GraphQL's static type system. Recommendation: expose `extension` as a generic list type initially; consider profile-aware typed extensions in a future iteration.

4. **Should instance-level queries (`/Patient/123/$graphql`) use a different query root or rewrite the query?** The FHIR spec says the root object should be the resource itself. Two options: (a) rewrite the query to inject the resource read, or (b) use a separate query root type. Recommendation: use global state injection and a middleware that intercepts the query execution to set the root value.

5. **Should we support GraphQL subscriptions for FHIR Subscriptions?** HotChocolate supports WebSocket subscriptions natively. This could bridge to FHIR Subscription resources. Recommendation: defer to a future investigation; design the schema to be subscription-compatible.

---

## Evidence

- [FHIR GraphQL Specification](https://build.fhir.org/graphql.html)
- [HotChocolate v15 Documentation](https://chillicream.com/docs/hotchocolate/v15)
- [HotChocolate ITypeModule API](https://chillicream.com/docs/hotchocolate/v15/defining-a-schema/dynamic-schemas)
- [HotChocolate DataLoader](https://chillicream.com/docs/hotchocolate/v15/fetching-data/dataloader)
- [HotChocolate Named Schemas](https://chillicream.com/docs/hotchocolate/v15/distributed-schema/schema-stitching)
- [GraphQL Specification (June 2018)](https://spec.graphql.org/June2018/)
- Existing Ignixa experimental features: `TransformEndpoints.cs`, `TerminologyEndpoints.cs`, `SummaryEndpoints.cs`
- Existing `ISchema` / `ITypeExtended` metadata model in `Ignixa.Abstractions`

---

## Verdict

**Viable**. HotChocolate v15 with `ITypeModule` is the strongest fit for dynamic FHIR schema generation in the .NET ecosystem. The design integrates cleanly with the existing clean architecture, requires no changes to Domain or DataLayer, follows all established patterns, and provides a clear path to full FHIR $graphql spec compliance.

**Estimated implementation effort**: ~80-100 hours across 3 phases:
- Phase 1: Core schema generation + instance-level reads (30-40h)
- Phase 2: Search resolver + pagination + search parameters (25-30h)
- Phase 3: Reference resolution with DataLoader + multi-version + configuration (25-30h)
