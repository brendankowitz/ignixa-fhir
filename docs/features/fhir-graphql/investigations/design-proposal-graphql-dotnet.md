# Investigation: GraphQL.NET-Based FHIR $graphql Design Proposal

**Feature**: fhir-graphql
**Status**: In Progress
**Created**: 2026-03-25

## Approach

This approach implements FHIR `$graphql` using **GraphQL.NET (graphql-dotnet)** with dynamic schema generation from Ignixa’s existing FHIR metadata (`ISchema`, `ITypeExtended`).

The design keeps Ignixa’s layering intact:
- **API**: `$graphql` endpoints, HTTP parsing/response formatting
- **Application**: GraphQL execution service, schema builders, resolvers, CQRS handlers
- **Domain**: unchanged abstractions (`IFhirRepository`, `ISearchService`, `ISchema`)
- **DataLayer**: unchanged

### 1) Library Choice Justification (GraphQL.NET)

GraphQL.NET is selected because it provides the right control level for FHIR-specific mapping while remaining production-grade.

| Criterion | GraphQL.NET Assessment |
|---|---|
| Ecosystem maturity | Mature .NET GraphQL server, long-running OSS project, widely deployed |
| Schema control | Full programmatic schema APIs (`Schema`, `ObjectGraphType`, `UnionGraphType`, resolvers) |
| Abstraction level | Lower-level than code-first frameworks, which is useful for precise FHIR shape mapping |
| Spec support | Introspection (`__schema`, `__type`), directives (`@skip`, `@include`) built in |
| Extensibility | Validation rules, complexity analyzers, DataLoader integration |
| License | MIT, aligned with project constraints |

Why GraphQL.NET over higher-level alternatives for this proposal:
1. FHIR schema generation is highly dynamic and metadata-driven; GraphQL.NET’s explicit type APIs map cleanly to this requirement.
2. Lower abstraction avoids framework conventions leaking into FHIR semantics (`value[x]`, references, profile-driven variability).
3. Strong control over execution options (depth, complexity, introspection policy) matches experimental hardening needs.

### 2) Architecture Overview

```text
HTTP Request
  -> GraphQlEndpoints (API)
     -> GraphQlRequestParser (GET query / POST JSON)
     -> IGraphQlExecutionService (Application)
         -> IGraphQlSchemaRegistry (Application cache)
             -> GraphQL.NET ISchema (tenant+FHIR-version specific)
         -> IDocumentExecuter.ExecuteAsync(...)
             -> Query root resolvers
                 -> IMediator.SendAsync(...) (CQRS)
                     -> GetResourceHandler / SearchResourcesHandler
                         -> IFhirRepository / ISearchService (Domain)
```

Design rule: **resolvers do not directly bind to storage implementations**. They call application-level query services/handlers, preserving capability enforcement and existing partition resolution patterns.

### 3) Schema Generation Strategy (Programmatic)

`GraphQlSchemaFactory` builds `GraphQL.Types.Schema` instances programmatically from `ISchema`/`ITypeExtended`.

#### 3.1 Core builder components

- `FhirGraphTypeRegistry` (memoizes generated graph types)
- `FhirObjectGraphTypeBuilder` (resource + complex/backbone types)
- `FhirChoiceUnionBuilder` (`value[x]` unions)
- `FhirReferenceGraphTypeBuilder` (reference fields + inline resource)
- `FhirQueryRootBuilder` (read/list fields + arguments)
- `FhirScalarRegistry` (primitive/custom scalar mapping)

#### 3.2 Resource types -> `ObjectGraphType`

For each known concrete resource type (`Patient`, `Observation`, etc.):
- Create `ObjectGraphType<FhirResourceEnvelope>` named exactly by FHIR type.
- Add fields for each child element in `ITypeExtended.Elements`.
- Respect cardinality:
  - `IsCollection == true` -> `ListGraphType<TFieldType>`
  - `Min > 0 && !IsCollection` -> `NonNullGraphType<TFieldType>`

#### 3.3 BackboneElement children -> nested `ObjectGraphType`

Backbone/nested components are generated recursively.

Naming convention:
- `{ParentType}_{ElementName}` (e.g., `Patient_Contact`, `Observation_Component`)
- Prevents collisions and keeps stable GraphQL schema names.

#### 3.4 Choice elements (`value[x]`) -> `UnionGraphType`

When `Types.Count > 1`, generate union:
- Example: `Observation_value_Choice = Quantity | CodeableConcept | String | ...`
- Parent field name remains FHIR base name (`value`), with resolver selecting correct runtime graph object type.

This maps choice polymorphism without leaking `[x]` suffixes into client queries.

#### 3.5 Reference fields -> Graph type with inline resolution

FHIR reference shape is preserved with a dedicated graph type:
- `reference: String`
- `type: String`
- `identifier: Identifier`
- `display: String`
- `resource: ResourceInterface` (resolved inline)

`ResourceInterface` is an `InterfaceGraphType` implemented by all resource object types; concrete type resolution uses `resourceType` from resolved JSON.

#### 3.6 Primitive mapping

Base mapping:
- `boolean` -> `BooleanGraphType`
- `integer`/`unsignedInt`/`positiveInt` -> `IntGraphType`
- `decimal` -> `DecimalGraphType`
- `string`/`code`/`id`/`uri`/`url`/`canonical`/`oid`/`uuid` -> `StringGraphType`
- `base64Binary` -> `StringGraphType` (Base64 payload)

Custom scalars (GraphQL.NET `ScalarGraphType`):
- `FhirDateScalar`
- `FhirDateTimeScalar`
- `FhirInstantScalar`
- `FhirTimeScalar`

Each custom scalar validates FHIR lexical forms and serializes as JSON string.

#### 3.7 Search parameters -> `QueryArgument`

For each resource list field (`PatientList`, `ObservationList`):
- Generate arguments from search parameter definitions (via version context metadata).
- Add universal control arguments:
  - `_count: Int`
  - `_cursor: String`
  - `_sort: String`
  - `_total: String`

Argument names remain FHIR-compatible where possible (`name`, `family`, `birthdate`, etc.).

### 4) Query Root Design

#### 4.1 System-level root (`[base]/$graphql`)

`FhirQueryGraphType` includes for each resource type:
- `Patient(id: ID!): Patient`
- `PatientList(name: String, _count: Int, _cursor: String, ...): PatientConnection`

`PatientConnection` uses cursor pagination:
- `nodes: [Patient!]!`
- `pageInfo { endCursor hasNextPage }`
- `totalCount` (optional depending on `_total`)

#### 4.2 Instance-level root (`[base]/[Type]/[id]/$graphql`)

FHIR requires the queried instance as root object. Design:
- Build/use an **instance schema per resource type** where `schema.Query = {ResourceType}GraphType`.
- Set `ExecutionOptions.Root = loaded resource envelope`.
- Query example:
  ```graphql
  {
    id
    subject {
      reference
      resource {
        ... on Patient { name { text } }
      }
    }
  }
  ```

This gives true resource-root semantics instead of wrapping in a synthetic `resource` field.

### 5) Resolver Architecture

CQRS-first resolver pattern:

- `ReadResourceResolver`
  - Input: `(resourceType, id)`
  - Calls: `IMediator.SendAsync(new GetResourceQuery(resourceType, id), ct)`
  - Output: parsed `FhirResourceEnvelope`

- `SearchResourceResolver`
  - Input: resource type + GraphQL args
  - Converts args -> `SearchOptions`
  - Calls: `IMediator.SendAsync(new SearchResourcesQuery(resourceType, searchOptions), ct)`
  - Wraps stream into connection result (`nodes`, `endCursor`, `hasNextPage`)

- `FieldValueResolver`
  - Reads projected property from `FhirResourceEnvelope` JSON node/typed element.

- `ReferenceResolver`
  - Resolves `Reference.resource` via DataLoader (section 8).

Direct repository/search calls are intentionally avoided in the default path to preserve existing behaviors (capability checks, partition strategy, logging).

### 6) Multi-Tenancy Context Propagation

Use GraphQL.NET `ExecutionOptions.UserContext`:

```csharp
public sealed class GraphQlUserContext : Dictionary<string, object>
{
    public required int TenantId { get; init; }
    public required FhirVersion FhirVersion { get; init; }
    public required IFhirRequestContext RequestContext { get; init; }
}
```

Population source:
- `IFhirRequestContextAccessor.RequestContext` (already set by middleware)

Resolvers access tenant context through `IResolveFieldContext.UserContext` and pass cancellation token from `context.CancellationToken`.

### 7) Multi-Version Support and Schema Caching

Cache key:
- `(tenantId, fhirVersion, schemaMode, resourceType?)`
  - `schemaMode=System` for `/.../$graphql`
  - `schemaMode=Instance` for `/.../{type}/{id}/$graphql`

Cache implementation:
- `ConcurrentDictionary<GraphQlSchemaCacheKey, Lazy<Task<GraphQL.Types.ISchema>>>`

Behavior:
1. Resolve tenant + FHIR version from request context.
2. Obtain `ISchema` via `IFhirVersionContext.GetSchemaProvider(fhirVersion, tenantId)`.
3. Build schema once per key.
4. Invalidate when conformance/package state changes (hook to existing package loaded events).

This supports mixed-version tenants and package-driven schema variation.

### 8) Reference Resolution and N+1 Prevention

Reference resolution pipeline:
1. Parse `Reference.reference` -> `(targetType, targetId)`.
2. Queue key into request-scoped DataLoader.
3. Batch load grouped by `targetType`.
4. Execute grouped lookups through CQRS search/read handlers.

DataLoader strategy:
- `ReferenceResourceDataLoader : DataLoaderBase<ReferenceKey, FhirResourceEnvelope?>`
- Group by `ResourceType` to allow batched fetch.
- For same-type keys, prefer single search with `_id` list when supported; fallback to parallel `GetResourceQuery` calls.

Outcome:
- Eliminates per-field per-reference network/storage calls.
- Ensures duplicate references in query resolve once per request.

### 9) Endpoint Design (`$graphql`)

New endpoints in experimental area:

Tenant-explicit:
- `GET  /tenant/{tenantId:int}/$graphql`
- `POST /tenant/{tenantId:int}/$graphql`
- `GET  /tenant/{tenantId:int}/{resourceType}/{id}/$graphql`
- `POST /tenant/{tenantId:int}/{resourceType}/{id}/$graphql`

Tenant-agnostic (single-tenant mode; blocked by existing middleware in multi-tenant mode):
- `GET  /$graphql`
- `POST /$graphql`
- `GET  /{resourceType}/{id}/$graphql`
- `POST /{resourceType}/{id}/$graphql`

Request handling:
- GET: read `query`, optional `variables` JSON string, optional `operationName`
- POST: read JSON body `{ "query": "...", "variables": {...}, "operationName": "..." }`

Response:
- `Content-Type: application/json`
- GraphQL spec response shape `{ "data": ..., "errors": [...] }`
- Never `application/fhir+json` for `$graphql`

Directives/introspection:
- `@skip` and `@include` supported by GraphQL.NET execution engine.
- `__schema`/`__type` available unless disabled by options.

### 10) Registration & DI (Experimental Pattern Compliance)

#### 10.1 Configuration object

Add to `ExperimentalOptions.cs`:

```csharp
public class GraphQlExperimentalOptions
{
    public bool Enabled { get; set; } = true;
    public bool IntrospectionEnabled { get; set; } = true;
    public int MaxDepth { get; set; } = 15;
    public int MaxComplexity { get; set; } = 400;
    public int MaxResultsPerList { get; set; } = 100;
    public int DefaultResultsPerList { get; set; } = 20;
    public bool EnableGetRequests { get; set; } = true;
}
```

And in `ExperimentalFeaturesOptions`:

```csharp
public GraphQlExperimentalOptions GraphQl { get; set; } = new();
```

#### 10.2 Autofac registration

In `ExperimentalAutofacRegistration.cs`:

```csharp
if (options.Features.GraphQl.Enabled)
{
    builder.RegisterGraphQlHandlers();
}

private static void RegisterGraphQlHandlers(this ContainerBuilder builder)
{
    builder.RegisterType<GraphQlExecutionService>()
        .As<IGraphQlExecutionService>()
        .InstancePerLifetimeScope();

    builder.RegisterType<GraphQlSchemaRegistry>()
        .As<IGraphQlSchemaRegistry>()
        .SingleInstance();

    builder.RegisterType<FhirGraphQlSchemaFactory>()
        .As<IFhirGraphQlSchemaFactory>()
        .SingleInstance();

    builder.RegisterType<GraphQlArgumentToSearchOptionsMapper>()
        .As<IGraphQlArgumentToSearchOptionsMapper>()
        .SingleInstance();

    builder.RegisterType<ReferenceResourceDataLoader>()
        .AsSelf()
        .InstancePerLifetimeScope();
}
```

#### 10.3 IServiceCollection registration

In `ExperimentalServicesRegistration.cs`:

```csharp
if (options.Features.GraphQl.Enabled)
{
    services.AddGraphQlServices(configuration);
}

private static void AddGraphQlServices(this IServiceCollection services, IConfiguration configuration)
{
    services.AddSingleton<IDocumentExecuter, DocumentExecuter>();
    services.AddSingleton<IDocumentBuilder, GraphQLParser.DocumentBuilder>();
    services.AddSingleton<IErrorInfoProvider, ErrorInfoProvider>();

    services.AddSingleton<DataLoaderDocumentListener>();
    services.Configure<ExecutionOptionsBuilderOptions>(...); // depth/complexity settings
}
```

#### 10.4 Endpoint mapping

In `ExperimentalEndpointExtensions.cs`:

```csharp
if (options.Features.GraphQl.Enabled)
{
    app.MapGraphQlEndpoints(configureTenantGroup);
}
```

### 11) Configuration Options

`GraphQlExperimentalOptions` (proposed):

| Option | Default | Purpose |
|---|---:|---|
| `Enabled` | `true` | Feature switch |
| `IntrospectionEnabled` | `true` | Enable/disable `__schema` and `__type` |
| `MaxDepth` | `15` | Query depth guard |
| `MaxComplexity` | `400` | Complexity/cost guard |
| `MaxResultsPerList` | `100` | Hard ceiling for list resolver `_count` |
| `DefaultResultsPerList` | `20` | Default `_count` when absent |
| `EnableGetRequests` | `true` | Allow GET transport for query documents |

Validation rules:
- `MaxDepth >= 1`
- `MaxComplexity >= 10`
- `1 <= DefaultResultsPerList <= MaxResultsPerList`

### 12) File/Folder Structure (Exact Paths)

```text
src/Application/Ignixa.Application/Features/Experimental/Configuration/
  ExperimentalOptions.cs                                # MODIFIED (add GraphQlExperimentalOptions)

src/Application/Ignixa.Application/Features/Experimental/Infrastructure/
  ExperimentalAutofacRegistration.cs                    # MODIFIED
  ExperimentalServicesRegistration.cs                   # MODIFIED

src/Application/Ignixa.Api/Endpoints/Experimental/
  ExperimentalEndpointExtensions.cs                     # MODIFIED
  GraphQlEndpoints.cs                                   # NEW

src/Application/Ignixa.Application/Features/Experimental/GraphQl/
  Contracts/
    IGraphQlExecutionService.cs                         # NEW
    IGraphQlSchemaRegistry.cs                           # NEW
    IFhirGraphQlSchemaFactory.cs                        # NEW
    IGraphQlArgumentToSearchOptionsMapper.cs            # NEW
  Models/
    GraphQlRequest.cs                                   # NEW
    GraphQlRequestEnvelope.cs                           # NEW
    GraphQlExecutionResult.cs                           # NEW
    GraphQlSchemaCacheKey.cs                            # NEW
    GraphQlUserContext.cs                               # NEW
    FhirResourceEnvelope.cs                             # NEW
    ReferenceKey.cs                                     # NEW
  Endpoint/
    GraphQlRequestParser.cs                             # NEW
  Execution/
    GraphQlExecutionService.cs                          # NEW
    GraphQlValidationRuleFactory.cs                     # NEW
  Schema/
    FhirGraphQlSchemaFactory.cs                         # NEW
    FhirSystemQueryGraphType.cs                         # NEW
    FhirResourceGraphTypeBuilder.cs                     # NEW
    FhirBackboneGraphTypeBuilder.cs                     # NEW
    FhirChoiceUnionGraphTypeBuilder.cs                  # NEW
    FhirReferenceGraphType.cs                           # NEW
    FhirResourceInterfaceGraphType.cs                   # NEW
    Scalars/
      FhirDateScalar.cs                                 # NEW
      FhirDateTimeScalar.cs                             # NEW
      FhirInstantScalar.cs                              # NEW
      FhirTimeScalar.cs                                 # NEW
  Resolvers/
    ReadResourceResolver.cs                             # NEW
    SearchResourceResolver.cs                           # NEW
    ReferenceResolver.cs                                # NEW
  DataLoaders/
    ReferenceResourceDataLoader.cs                      # NEW
  Mapping/
    GraphQlArgumentToSearchOptionsMapper.cs             # NEW
  Caching/
    GraphQlSchemaRegistry.cs                            # NEW
```

### 13) Tradeoffs

| Pros | Cons |
|---|---|
| Precise control over FHIR-to-GraphQL shape mapping | More manual implementation than high-level GraphQL frameworks |
| Reuses existing tenant/version/search/repository infrastructure | Significant upfront schema-builder complexity |
| Strong alignment with clean architecture and CQRS | Need careful schema cache invalidation when packages change |
| Built-in introspection/directives/spec behavior | GraphQL.NET DataLoader wiring adds request pipeline complexity |
| Supports both system-level and instance-level `$graphql` semantics | Runtime schema generation cost must be amortized by caching |

### 14) Alignment Checklist

- [x] **Follows architectural layering rules**
  - API endpoints remain thin; logic is in Application services/resolvers.
  - Domain abstractions reused; no DataLayer coupling.
- [x] **Developer Experience (works with minimal setup)**
  - No external infra required beyond existing server dependencies.
  - Feature is gated behind experimental config toggle.
- [x] **Specification compliance**
  - Supports system and instance `$graphql` endpoints.
  - Supports GET and POST transport.
  - Returns `application/json`.
  - Supports introspection and standard directives.
  - Maps search params to list arguments and supports inline reference resource resolution.
- [x] **Consistent with existing patterns**
  - Uses `ExperimentalOptions` master/per-feature switches.
  - Uses Autofac + IServiceCollection split.
  - Uses `MapGraphQlEndpoints(configureTenantGroup)` in experimental endpoint extension.

## Tradeoffs

| Pros | Cons |
|------|------|
| Full control over GraphQL surface makes FHIR-specific behavior explicit and testable. | Larger implementation surface than convention-first GraphQL stacks. |
| Clean integration with existing CQRS/multi-tenant/version mechanisms minimizes architectural risk. | Requires robust cache invalidation and query guardrails to avoid performance regressions. |
| DataLoader + grouped fetch strategy can prevent reference-resolution N+1 issues. | Batch lookup behavior depends on search capabilities and fallback logic. |

## Alignment

- [x] Follows architectural layering rules
- [x] Developer Experience (works with minimal setup)
- [x] Specification compliance (if applicable)
- [x] Consistent with existing patterns

## Evidence

### Codebase pattern evidence

1. **Experimental feature registration pattern exists and is centralized**
   - `src/Application/Ignixa.Application/Features/Experimental/Infrastructure/ExperimentalAutofacRegistration.cs`
   - `src/Application/Ignixa.Application/Features/Experimental/Infrastructure/ExperimentalServicesRegistration.cs`
   - `src/Application/Ignixa.Api/Endpoints/Experimental/ExperimentalEndpointExtensions.cs`

2. **Endpoint mapping pattern for experimental features already uses tenant + agnostic routes**
   - `src/Application/Ignixa.Api/Endpoints/Experimental/TransformEndpoints.cs`
   - `src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs`
   - `src/Application/Ignixa.Api/Endpoints/Experimental/SummaryEndpoints.cs`

3. **Core CQRS read/search handlers already encapsulate partition and capability behaviors**
   - `src/Application/Ignixa.Application/Features/Resource/GetResourceHandler.cs`
   - `src/Application/Ignixa.Application/Features/Resource/SearchResourcesHandler.cs`

4. **Tenant/version context contracts are already available**
   - `src/Application/Ignixa.Application/Infrastructure/IFhirRequestContextAccessor.cs`
   - `src/Application/Ignixa.Application/Features/Search/IFhirVersionContext.cs`

5. **FHIR schema metadata contracts required for dynamic type generation are present**
   - `src/Core/Ignixa.Abstractions/Structure/ISchema.cs`
   - `src/Core/Ignixa.Abstractions/Structure/ITypeExtended.cs`

6. **Domain data access contracts align with resolver needs**
   - `src/Application/Ignixa.Domain/Abstractions/IFhirRepository.cs`
   - `src/Application/Ignixa.Domain/Abstractions/ISearchService.cs`

### ADR alignment evidence

- **Vertical slice + layer separation**: `docs/adr/adr-2509-vertical-slice-architecture.md`
- **Multi-tenancy route and partition strategy**: `docs/adr/adr-2510-multi-tenancy.md`

### Prior-art/spec evidence used for design decisions

- FHIR GraphQL operation requirements from HL7 spec: system and instance endpoints, introspection, GET/POST, JSON media type.
- GraphQL core behavior: introspection and directive execution (`@skip`, `@include`) supported by GraphQL.NET execution engine.

## Verdict

GraphQL.NET is a viable and strong fit for Ignixa’s `$graphql` implementation when the priority is precise FHIR mapping control, clean architecture alignment, and explicit operational guardrails.

Recommended next step: implement vertical slice in this order:
1. Endpoint + request parser + execution service shell
2. System query root (read + list for 1-2 resource types)
3. Dynamic schema generation from `ISchema`
4. Reference inline resolution with DataLoader
5. Guardrails (depth/complexity/introspection) + cache invalidation hooks
