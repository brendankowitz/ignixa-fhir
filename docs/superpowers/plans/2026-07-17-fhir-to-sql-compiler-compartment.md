# Compartment Search (Phase 8, part 1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Compile `CompartmentSearchExpression` (`GET /Patient/123/Observation`, `GET /Patient/123/*`) into the CTE-graph IR in `Ignixa.Search.Sql`, matching the grouped-by-`SearchParamId` shape production's `CompartmentSearchQueryGenerator` already uses (and fhir-server independently converges on), while fixing one real, documented production gap (missing `ReferenceResourceTypeId` filter) along the way.

**Architecture:** One new `CteDefinition` node, `CompartmentSource` (a dedicated node for a dedicated SQL shape, matching `ChainJoin`/`IncludeStage`'s precedent — NOT `ParamSource` widened, since `ParamSource.ResourceTypeId` being single-valued is a deliberate Phase 6 invariant). `Resolve`/`SymbolCollectingVisitor` widen to expand a compartment's membership (which resource types, which Reference-type search parameters establish membership) via `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` — both already Core-tier, so no new `ISymbolResolver`-style abstraction is needed, unlike `SearchParamId`/`ResourceTypeId` resolution. A new tier-1 `CompartmentLoweringRule` builds each grouped CTE's predicate reusing `ReferenceLoweringRule`'s exact `ReferenceResourceTypeId = @p AND ReferenceResourceId = @p` construction; a new tier-2 `StructuralContext.LowerCompartment` builds the group map (mirroring `CompartmentSearchQueryGenerator`'s own grouping loop) and `Union`s the results. `Lower.Run`'s `targetResourceType` becomes nullable for the one case that genuinely has no single scope — a wildcard compartment search — with an explicit throw for the one combination this phase does not support (wildcard compartment + an ordinary typed search parameter).

**Tech Stack:** C# / .NET 9+, xUnit + Shouldly, `Ignixa.Search.Sql` (Core-tier, no EF/ASP.NET references).

**Full design:** `docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-compartment-design.md` — read this first for the *why* behind every task below; this plan only covers the *what* and *how*, task by task. Section references (§N) below refer to that document. It was produced by a Fable adversarial design-research pass comparing Ignixa's production code, an orphaned dead-code rewriter, and `microsoft/fhir-server`'s own two generations of compartment SQL, and approved by the user without requesting section-by-section revision.

## Global Constraints

- `dotnet build All.sln` → 0 warnings, 0 errors. `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"` → all passing; the 2 `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework) are pre-existing and out of scope, per every prior increment on this branch.
- **`CompartmentSource`**, exact field shape (design §2) — no `TableDescriptor` field (compartment membership is always `dbo.ReferenceSearchParam`; `Emit` hardcodes the table name, matching `EmitChainJoin`'s own hardcoded-table-name precedent, not threaded via `TableDescriptor`):
  ```csharp
  public sealed record CompartmentSource(
      IReadOnlyList<short> ResourceTypeIds,
      short SearchParamId,
      Predicate Predicate)
      : CteDefinition;
  ```
  `ResourceTypeIds` and `SearchParamId` render as literals in `Emit` (never bound `@pN` parameters) — matching `ParamSource`/`ChainJoin`'s established precedent, and Step 0's own finding that `SearchParamId` literalization is this feature's load-bearing performance fix. `Predicate` (the `ReferenceResourceTypeId = @p AND ReferenceResourceId = @p` pair) uses real bound parameters via the existing `Predicate`/`EmitPredicate` machinery, exactly like every other leaf rule's predicate.
- **`ResourceTypeIds` renders as a literal `Or`-chain**, reusing the exact `EmitTypeInFilter` helper `Emit.cs` already has (added by the Phase 7 include plan, used today by `EmitIncludeStage`) — `EmitTypeInFilter("ResourceTypeId", cs.ResourceTypeIds)`. Do not write a new type-list-rendering helper; call the existing one.
- **`Resolve.RunAsync`'s widened signature** — two new parameters, both nullable, both APPENDED AFTER `cancellationToken` (a deliberate deviation from the ".NET convention: `CancellationToken` last" this project's own prior plans established, justified here because it is the only way to add these two parameters with ZERO changes to any of this project's 44 existing `Resolve.RunAsync` call sites — every one of them already ends with a bare positional `CancellationToken.None` as its final argument; inserting anything before it, even an optional parameter, breaks every one of those calls, since C#'s "positional named arguments must appear in declared order" rule means once a later parameter is supplied positionally, nothing new can be inserted ahead of it without a full sweep. Appending after `cancellationToken` avoids that sweep entirely — none of the 44 existing calls need to change, since C# lets trailing optional parameters be omitted freely):
  ```csharp
  public static async Task<SymbolTable> RunAsync(
      Expression? expression,
      IReadOnlyList<IncludeExpression> includes,
      IReadOnlyList<IncludeExpression> revIncludes,
      ISymbolResolver resolver,
      string targetResourceType,
      CancellationToken cancellationToken,
      ICompartmentDefinitionManager? compartmentDefinitionManager = null,
      ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
  ```
- **`SymbolTable`'s widened constructor** — one new trailing optional parameter, matching `QueryPlan.Includes`/`ResourceSource.Predicate`'s established "purely additive trailing field, zero call-site impact" precedent:
  ```csharp
  public SymbolTable(
      IReadOnlyDictionary<string, short> searchParamIds,
      IReadOnlyDictionary<string, short> resourceTypeIds,
      IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? compartmentMembership = null)
  ```
  New accessor: `CompartmentMembership(string compartmentType)` throws `KeyNotFoundException` on miss, matching `SearchParamId`/`ResourceTypeId`'s established "Resolve should have resolved every X Lower will need" throw message convention exactly.
- **`Lower.Run`'s `targetResourceType` becomes nullable** (`string?`, no other signature change) — this is a pure TYPE change, not an arity change: every one of this project's 47 existing `Lower.Run` call sites already passes a non-null `string` literal, which remains perfectly valid for a `string?` parameter with zero code changes required at any of those call sites. Do not attempt a call-site sweep for this change — there is nothing to sweep. `targetResourceType` is `null` only for a wildcard compartment search (`FilteredResourceTypes` empty, `SearchOptions.ResourceType` cleared to `null` by `SearchCompartmentHandler.cs:115-117` for the real `"*"` case) — every other caller continues supplying a real resource type exactly as today.
- **A wildcard compartment search (`targetResourceType == null`) combined with an ordinary typed search parameter throws `NotSupportedException`** (design §4) — a typed leaf rule fundamentally needs a single resource type to scope its `ParamSource` against, and this phase does not build cross-type common-parameter support. A wildcard compartment search combined with resource-column predicates (`_id`/`_type`/`_lastUpdated`) works unchanged, since `OuterPredicate`'s join is already type-agnostic. A wildcard compartment search combined with `_include`/`_revinclude` also throws `NotSupportedException` this phase — `BuildIncludeStages` needs a concrete match resource type to compute `SeedFromMatch`, which a wildcard compartment search does not have; this specific combination was not named in the design doc and is a plan-level scope decision, not a design reversal — record it in the roadmap update (Task 5) as a named Phase 9 follow-up.
- **The `CompartmentSource` degenerate case (zero grouped `SearchParamId`s after `FilteredResourceTypes` intersection) throws a named `NotSupportedException`** (design §2) — matching this project's "fail at Lower time, not silently query a table that can never match" principle, and deliberately not reproducing fhir-server's own documented trap (§1.3 of the design doc: an empty-membership compartment search there silently routes to a never-populated table). Phase 9's wiring layer owns short-circuiting this case before calling `Lower` at all, matching what `CompartmentSearchQueryGenerator.cs:85-89` already does today in production.
- **The `ReferenceResourceTypeId` filter is always present** in `CompartmentSource.Predicate` (design §1.4) — a deliberate, documented improvement over production's current `ReferenceResourceId`-only filter (`CompartmentSearchQueryGenerator.cs:181-185` never filters `ReferenceResourceTypeId`), named for Phase 9's differential-test suite, not silently reproduced as a bug.
- `_sort`, continuation tokens, and instance-level SMART/compartment-boundary enforcement (reading `HttpContext.Items["FhirAuthorizationFilter"]`, wiring it into a synthesized `CompartmentSearchExpression`) are explicitly out of scope for this plan (design §5/§6) — nothing in this plan should throw a DIFFERENT exception for these than whatever the existing code already produces for unhandled shapes.

---

### Task 1: Widen `Resolve`/`SymbolCollectingVisitor`/`SymbolTable` to resolve compartment membership

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Symbols/SymbolTableTests.cs`

**Interfaces:**
- Consumes: nothing new from earlier tasks (this is the foundational task, matching the "resolve first" sequencing precedent both the chain and include plans used).
- Produces: `SymbolCollectingVisitor.Compartments: List<(string CompartmentType, ISet<string> FilteredResourceTypes)>` (public property). `SymbolTable.CompartmentMembership(string compartmentType) → IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>`. `LeafContext.CompartmentMembership(string compartmentType)` (pass-through). Task 3 (`StructuralContext.LowerCompartment`) is the primary consumer of the last one.

**Unlike `ChainedExpression`/`IncludeExpression`, `CompartmentSearchExpression` DOES appear directly on the ordinary `Expression` tree `SymbolCollectingVisitor` already walks** — `SearchCompartmentHandler.cs:83-85` either sets `SearchOptions.Expression` to the bare `CompartmentSearchExpression` (no other query-string predicates) or `Expression.And(compartmentExpression, otherPredicates)` (predicates present) — confirmed by reading that file directly. So this task adds a genuine `VisitCompartment` **visitor override** (matching `VisitChained`'s pattern exactly), not a `CollectInclude`-style direct-call method.

- [ ] **Step 1: Add `SymbolCollectingVisitor.VisitCompartment`**

In `src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs`, add a new public property and override (after the existing `VisitChained` override, before `CollectInclude`):

```csharp
    public List<(string CompartmentType, ISet<string> FilteredResourceTypes)> Compartments { get; } = [];

    /// <summary>
    /// Records a CompartmentSearchExpression's own type/filter for Resolve to expand -- unlike
    /// VisitChained, this override does no further recursion (CompartmentSearchExpression has no
    /// child Expression field to walk into). Resolve, not this visitor, does the actual
    /// ICompartmentDefinitionManager/ISearchParameterDefinitionManager expansion -- this class's own
    /// contract is tree traversal without I/O; recording the raw (type, filter) pair here and
    /// resolving it in Resolve keeps that contract intact for compartment search the same way it
    /// already does for every other collected symbol.
    /// </summary>
    public override Expression VisitCompartment(CompartmentSearchExpression expression, object? context)
    {
        AddResourceType(expression.CompartmentType);
        Compartments.Add((expression.CompartmentType, expression.FilteredResourceTypes));
        return expression;
    }
```

`AddResourceType` is the existing private helper `CollectInclude` already uses (skips `null`/empty/`"*"`) — reuse it unchanged, do not duplicate its logic. Add `using Ignixa.Specification.ValueSets.Normative;` if the build reports `CompartmentType`-adjacent errors — it should not be needed here (the enum parse happens in `Resolve`, not this file), but `CompartmentSearchExpression` itself needs `using Ignixa.Search.Expressions;`, already present.

Update the class's `<remarks>` block: replace the final sentence `"Compartment target-type resolution remains Phase 8's job. See Resolve's remarks for the full argument."` with `"As of Phase 8, VisitCompartment collects a CompartmentSearchExpression's own CompartmentType (added to ResourceTypes, since the compiled predicate needs it to filter ReferenceResourceTypeId) and records the full (CompartmentType, FilteredResourceTypes) pair into Compartments for Resolve to expand via ICompartmentDefinitionManager/ISearchParameterDefinitionManager -- see Resolve's remarks for the full argument."`

- [ ] **Step 2: Widen `SymbolTable`**

In `src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs`, change the constructor:

```csharp
    public SymbolTable(
        IReadOnlyDictionary<string, short> searchParamIds,
        IReadOnlyDictionary<string, short> resourceTypeIds)
    {
        _searchParamIds = searchParamIds;
        _resourceTypeIds = resourceTypeIds;
    }
```

to:

```csharp
    public SymbolTable(
        IReadOnlyDictionary<string, short> searchParamIds,
        IReadOnlyDictionary<string, short> resourceTypeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? compartmentMembership = null)
    {
        _searchParamIds = searchParamIds;
        _resourceTypeIds = resourceTypeIds;
        _compartmentMembership = compartmentMembership ?? new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>();
    }
```

Add the backing field (next to `_searchParamIds`/`_resourceTypeIds`):

```csharp
    private readonly IReadOnlyDictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>> _compartmentMembership;
```

Add the new accessor (after `ResourceTypeId`):

```csharp
    /// <summary>
    /// Looks up a compartment type's full membership map -- every Reference-type search parameter
    /// that establishes membership in this compartment, grouped by parameter, each with the full set
    /// of resource types that use it. Names, not resolved ids (Lower resolves SearchParamId/
    /// ResourceTypeId through the existing methods above) -- see Resolve's remarks for why this
    /// stores the compartment's FULL map rather than pre-filtered to any one request's
    /// FilteredResourceTypes. Throws if Resolve did not resolve this compartment type -- the same
    /// "Resolve should have resolved every X Lower will need" contract SearchParamId/ResourceTypeId
    /// already establish.
    /// </summary>
    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType)
        => _compartmentMembership.TryGetValue(compartmentType, out var membership)
           ? membership
           : throw new KeyNotFoundException($"SymbolTable has no compartment membership map for '{compartmentType}' -- Resolve should have resolved every compartment type Lower will need.");
```

Add `using Ignixa.Search.Models;` if not already present (it is, for `SearchParameterInfo`).

- [ ] **Step 3: `LeafContext` pass-through**

In `src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs`, add (after `ResourceTypeId`):

```csharp
    public IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)> CompartmentMembership(string compartmentType) => _symbols.CompartmentMembership(compartmentType);
```

- [ ] **Step 4: Widen `Resolve.RunAsync`**

In `src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs`, change the method signature and body:

```csharp
    public static async Task<SymbolTable> RunAsync(
        Expression? expression,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        ISymbolResolver resolver,
        string targetResourceType,
        CancellationToken cancellationToken,
        ICompartmentDefinitionManager? compartmentDefinitionManager = null,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager = null)
    {
        ArgumentNullException.ThrowIfNull(includes);
        ArgumentNullException.ThrowIfNull(revIncludes);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(targetResourceType);

        var collector = new SymbolCollectingVisitor();
        if (expression is not null)
        {
            expression.AcceptVisitor(collector, context: null);
        }

        foreach (var include in includes)
        {
            collector.CollectInclude(include);
        }

        foreach (var revInclude in revIncludes)
        {
            collector.CollectInclude(revInclude);
        }

        var compartmentMembership = ResolveCompartmentMembership(collector, compartmentDefinitionManager, searchParameterDefinitionManager);

        var searchParamIds = new Dictionary<string, short>();
        foreach (var parameter in collector.Parameters)
        {
            var id = await resolver.GetSearchParamIdAsync(parameter, cancellationToken);
            if (id.HasValue)
            {
                searchParamIds[parameter.Url.ToString()] = id.Value;
            }
        }

        var resourceTypes = new HashSet<string>(collector.ResourceTypes);
        resourceTypes.Add(targetResourceType);

        var resourceTypeIds = new Dictionary<string, short>();
        foreach (var resourceType in resourceTypes)
        {
            var id = await resolver.GetResourceTypeIdAsync(resourceType, cancellationToken);
            if (id.HasValue)
            {
                resourceTypeIds[resourceType] = id.Value;
            }
        }

        return new SymbolTable(searchParamIds, resourceTypeIds, compartmentMembership);
    }

    private static Dictionary<string, IReadOnlyList<(SearchParameterInfo Parameter, IReadOnlyList<string> ResourceTypes)>>? ResolveCompartmentMembership(
        SymbolCollectingVisitor collector,
        ICompartmentDefinitionManager? compartmentDefinitionManager,
        ISearchParameterDefinitionManager? searchParameterDefinitionManager)
    {
        if (collector.Compartments.Count == 0)
        {
            return null;
        }

        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>();
        foreach (var (compartmentType, _) in collector.Compartments)
        {
            if (membership.ContainsKey(compartmentType))
            {
                continue;
            }

            if (compartmentDefinitionManager is null || searchParameterDefinitionManager is null)
            {
                throw new InvalidOperationException(
                    $"Resolve encountered a compartment search for '{compartmentType}' but no " +
                    "ICompartmentDefinitionManager/ISearchParameterDefinitionManager was supplied -- both are " +
                    "required to resolve compartment membership.");
            }

            if (!Enum.TryParse<CompartmentType>(compartmentType, out var compartmentTypeEnum))
            {
                throw new InvalidOperationException($"Invalid compartment type: {compartmentType}");
            }

            var groups = new Dictionary<string, (SearchParameterInfo Parameter, List<string> ResourceTypes)>();
            if (compartmentDefinitionManager.TryGetResourceTypes(compartmentTypeEnum, out var allResourceTypes))
            {
                foreach (var resourceType in allResourceTypes)
                {
                    if (!compartmentDefinitionManager.TryGetSearchParams(resourceType, compartmentTypeEnum, out var searchParamCodes))
                    {
                        continue;
                    }

                    foreach (var code in searchParamCodes)
                    {
                        if (!searchParameterDefinitionManager.TryGetSearchParameter(resourceType, code, out var searchParam)
                            || searchParam.Type != SearchParamType.Reference)
                        {
                            continue;
                        }

                        var key = searchParam.Url.ToString();
                        if (!groups.TryGetValue(key, out var group))
                        {
                            group = (searchParam, []);
                            groups[key] = group;
                        }

                        group.ResourceTypes.Add(resourceType);
                    }
                }
            }

            var groupList = groups.Values
                .Select(g => (g.Parameter, (IReadOnlyList<string>)g.ResourceTypes))
                .ToList();
            membership[compartmentType] = groupList;

            foreach (var (parameter, resourceTypes) in groupList)
            {
                collector.Parameters.Add(parameter);
                foreach (var resourceType in resourceTypes)
                {
                    collector.ResourceTypes.Add(resourceType);
                }
            }
        }

        return membership;
    }
```

Add `using Ignixa.Search.Definition;` (for `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager`), `using Ignixa.Search.Models;` (for `SearchParamType`, likely already present), and `using Ignixa.Specification.ValueSets.Normative;` (for the `CompartmentType` enum) to the top of `Resolve.cs`.

This mirrors `CompartmentSearchQueryGenerator.cs:93-157`'s own grouping loop exactly (resource types → membership codes → resolved `SearchParameterInfo`, skip non-Reference, group by parameter URL) — re-read that file's loop if anything here is unclear before writing code, don't paraphrase from this brief alone.

Update the class's `<remarks>` block: replace the final sentence `"Resolve still does not resolve resource types touched only by compartment context that does not exist anywhere on this Expression tree -- that generalization is Phase 8's job."` with `"As of Phase 8, Resolve also expands every SymbolCollectingVisitor.Compartments entry via ICompartmentDefinitionManager/ISearchParameterDefinitionManager (both optional, required only when a compartment search is actually present) into SymbolTable.CompartmentMembership -- see that method's remarks for the exact shape."`

- [ ] **Step 5: Write the tests**

Add to `test/Ignixa.Search.Sql.Tests/Symbols/SymbolTableTests.cs` (read the existing file first to match its test-double/fixture conventions):

```csharp
    [Fact]
    public void GivenACompartmentMembershipMap_WhenLookedUp_ThenReturnsTheStoredGroups()
    {
        // Arrange
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var membership = new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
        {
            ["Patient"] = [(subjectParam, ["Observation", "Condition"])],
        };
        var symbolTable = new SymbolTable(
            new Dictionary<string, short>(),
            new Dictionary<string, short>(),
            membership);

        // Act
        var result = symbolTable.CompartmentMembership("Patient");

        // Assert
        result.Count.ShouldBe(1);
        result[0].Parameter.ShouldBe(subjectParam);
        result[0].ResourceTypes.ShouldBe(["Observation", "Condition"]);
    }

    [Fact]
    public void GivenNoCompartmentMembershipWasResolved_WhenLookedUp_ThenThrowsKeyNotFoundException()
    {
        // Arrange
        var symbolTable = new SymbolTable(new Dictionary<string, short>(), new Dictionary<string, short>());

        // Act & Assert
        Should.Throw<KeyNotFoundException>(() => symbolTable.CompartmentMembership("Patient"));
    }
```

Add to `test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs` (a real, in-memory `ICompartmentDefinitionManager`/`ISearchParameterDefinitionManager` test double — not a mock, matching this file's existing `FakeSymbolResolver` philosophy):

```csharp
    [Fact]
    public async Task GivenACompartmentSearchExpression_WhenResolved_ThenSymbolTableHasItsCompartmentMembership()
    {
        // Arrange -- Patient/123/Observation-shaped: Patient compartment, Observation membership via "subject".
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", Ignixa.Specification.ValueSets.Normative.CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 77;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        // Act
        var symbolTable = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager);

        // Assert
        var membership = symbolTable.CompartmentMembership("Patient");
        membership.Count.ShouldBe(1);
        membership[0].Parameter.ShouldBe(subjectParam);
        membership[0].ResourceTypes.ShouldBe(["Observation"]);
        symbolTable.SearchParamId(subjectParam).ShouldBe((short)77);
        symbolTable.ResourceTypeId("Patient").ShouldBe((short)103);
        symbolTable.ResourceTypeId("Observation").ShouldBe((short)104);
    }

    [Fact]
    public async Task GivenACompartmentSearchExpressionWithNoManagersSupplied_WhenResolved_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var resolver = new FakeSymbolResolver();

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(() =>
            Resolve.RunAsync(compartment, includes: [], revIncludes: [], resolver, targetResourceType: "Observation", CancellationToken.None));
    }

    /// <summary>
    /// An in-memory, dictionary-backed ICompartmentDefinitionManager test double -- not a mock,
    /// matching this file's existing FakeSymbolResolver philosophy.
    /// </summary>
    private sealed class FakeCompartmentDefinitionManager : ICompartmentDefinitionManager
    {
        public Dictionary<Ignixa.Specification.ValueSets.Normative.CompartmentType, HashSet<string>> ResourceTypes { get; } = [];

        public Dictionary<(string ResourceType, Ignixa.Specification.ValueSets.Normative.CompartmentType CompartmentType), HashSet<string>> SearchParams { get; } = [];

        public bool TryGetResourceTypes(Ignixa.Specification.ValueSets.Normative.CompartmentType compartmentType, out HashSet<string> resourceTypes)
            => ResourceTypes.TryGetValue(compartmentType, out resourceTypes!);

        public bool TryGetSearchParams(string resourceType, Ignixa.Specification.ValueSets.Normative.CompartmentType compartmentType, out HashSet<string> searchParams)
            => SearchParams.TryGetValue((resourceType, compartmentType), out searchParams!);
    }

    /// <summary>
    /// A minimal ISearchParameterDefinitionManager test double implementing only what Resolve calls
    /// (TryGetSearchParameter) -- every other member throws NotImplementedException deliberately,
    /// surfacing loudly if a future change makes Resolve call something this test double doesn't expect.
    /// </summary>
    private sealed class FakeSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        public Dictionary<(string ResourceType, string Code), SearchParameterInfo> Parameters { get; } = [];

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
            => Parameters.TryGetValue((resourceType, code), out searchParameter!);

        public IEnumerable<SearchParameterInfo> AllSearchParameters => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> SearchParameterHashMap => throw new NotImplementedException();
        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType) => throw new NotImplementedException();
        public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(string resourceType, string code) => throw new NotImplementedException();
        public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(Uri definitionUri) => throw new NotImplementedException();
        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap) => throw new NotImplementedException();
        public string GetSearchParameterHashForResourceType(string resourceType) => throw new NotImplementedException();
        public void AddNewSearchParameters(IReadOnlyCollection<Ignixa.Abstractions.IElement> searchParameters, bool calculateHash = true) => throw new NotImplementedException();
        public void DeleteSearchParameter(string url, bool calculateHash = true) => throw new NotImplementedException();
    }
```

Add `using Ignixa.Search.Definition;` to `ResolveTests.cs`'s usings if not already present.

- [ ] **Step 6: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all new tests, plus every existing test in this project unmodified (this task's signature changes are purely additive-trailing per the Global Constraints — no existing call site should need editing).

- [ ] **Step 7: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Symbols/SymbolCollectingVisitor.cs src/Core/Ignixa.Search.Sql/Symbols/Resolve.cs src/Core/Ignixa.Search.Sql/Symbols/SymbolTable.cs src/Core/Ignixa.Search.Sql/Lowering/LeafContext.cs test/Ignixa.Search.Sql.Tests/Symbols/ResolveTests.cs test/Ignixa.Search.Sql.Tests/Symbols/SymbolTableTests.cs
git commit -m "feat(search-sql): resolve compartment membership via ICompartmentDefinitionManager"
```

---

### Task 2: `CompartmentSource` AST + `Emit` + `PlanExplainer` — AST-only, no lowering rule yet

Mirrors the include plan's Task 2 pattern exactly: AST + `Emit` land together (this codebase's established convention is that `Emit`'s private per-node renderers are only ever tested through the public `Emit.Run(plan)` entry point, so a node's AST definition and its `Emit` rendering cannot be usefully split into separate reviewable tasks). Hand-constructed `CompartmentSource` instances prove the rendering; no lowering rule exists yet.

**Files:**
- Modify: `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: `CteDefinition.CompartmentSource(IReadOnlyList<short> ResourceTypeIds, short SearchParamId, Predicate Predicate)`. Task 3 constructs `CompartmentSource` instances from real compartment membership data and hands them to this same `Emit.Run`/`PlanExplainer.Print` machinery unmodified.

- [ ] **Step 1: `CompartmentSource` on `CteDefinition`**

In `src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs`, add (after `ChainJoin`):

```csharp
    public sealed record CompartmentSource(IReadOnlyList<short> ResourceTypeIds, short SearchParamId, Predicate Predicate) : CteDefinition;
```

Update the class's XML doc comment: append, after the existing sentence ending "...see the chain design doc for the full derivation.": ` CompartmentSource represents a compartment-search grouped predicate -- all rows in dbo.ReferenceSearchParam matching one SearchParamId, any of a list of ResourceTypeIds (the resource types that share this particular membership parameter), and a fixed compartment reference -- one CTE per distinct membership SearchParamId (matching CompartmentSearchQueryGenerator's own grouping), Unioned by StructuralContext.LowerCompartment. See the compartment design doc §2 for the full derivation.`

- [ ] **Step 2: `Emit.EmitCompartmentSource`**

In `src/Core/Ignixa.Search.Sql/Ast/Emit.cs`, add this case to the `EmitCte` switch (after the `ChainJoin` arm):

```csharp
        CteDefinition.CompartmentSource cs => EmitCompartmentSource(cs, parameters),
```

Add the method (after `EmitChainJoin`/`OutputTypeColumn`, before `EmitResourceSource`):

```csharp
    private static string EmitCompartmentSource(CteDefinition.CompartmentSource cs, List<EmittedSqlParameter> parameters)
        => $"    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
           $"    FROM dbo.ReferenceSearchParam\n" +
           $"    WHERE SearchParamId = {cs.SearchParamId}\n" +
           $"      AND {EmitTypeInFilter("ResourceTypeId", cs.ResourceTypeIds)}\n" +
           $"      AND {EmitPredicate(cs.Predicate, parameters)}";
```

`EmitTypeInFilter` already exists in this file (added by the Phase 7 include plan, used by `EmitIncludeStage`) — reuse it directly, do not write a new type-list-rendering helper. No `dbo.ReferenceSearchParam`/`dbo.Resource` join is emitted — matching `CompartmentSearchQueryGenerator.cs:187-191`'s own documented reasoning (the `ReferenceSearchParam` index is covering for active resources; `SearchIndexWriter` only writes indices for `IsHistory=0`/`IsDeleted=0` rows), which `ParamSource`'s own un-joined shape already relies on identically.

- [ ] **Step 3: `PlanExplainer` rendering**

In `src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs`, add this case to the `PrintCte` switch (after the `ChainJoin` arm):

```csharp
        CteDefinition.CompartmentSource cs =>
            $"CompartmentSource[{string.Join(",", cs.ResourceTypeIds)},{cs.SearchParamId}]  {PrintPredicate(cs.Predicate, ref parameterOrdinal)}{PrintTop(top)}",
```

- [ ] **Step 4: Write the tests**

Add to `test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs`:

```csharp
    [Fact]
    public void GivenACompartmentSourcePlan_WhenEmitted_ThenProducesAGroupedSelectWithTheTypeOrChainAndTheReferencePredicate()
    {
        // Arrange -- Patient/123 compartment, "subject" SearchParamId 77, spanning Observation(104)/Condition(106).
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104, 106], 77, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldBe(
            ";WITH cte0 AS (\n" +
            "    SELECT DISTINCT ResourceTypeId AS T1, ResourceSurrogateId AS Sid1\n" +
            "    FROM dbo.ReferenceSearchParam\n" +
            "    WHERE SearchParamId = 77\n" +
            "      AND (ResourceTypeId = 104 OR ResourceTypeId = 106)\n" +
            "      AND ReferenceResourceTypeId = @p0 AND ReferenceResourceId = @p1\n" +
            ")\n" +
            "SELECT T1, Sid1 FROM cte0");
        emitted.Parameters.Count.ShouldBe(2);
        emitted.Parameters[0].ShouldBe(new EmittedSqlParameter("@p0", (short)103));
        emitted.Parameters[1].ShouldBe(new EmittedSqlParameter("@p1", "123"));
    }

    [Fact]
    public void GivenACompartmentSourceWithASingleResourceType_WhenEmitted_ThenTheTypeFilterIsABareEqualNotAnOrChain()
    {
        // Arrange -- the non-wildcard case (design §4): one grouped SearchParamId, one resource type.
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104], 77, predicate)], new CteRef(0));

        // Act
        var emitted = Emit.Run(plan);

        // Assert
        emitted.Sql.ShouldContain("      AND ResourceTypeId = 104\n");
        emitted.Sql.ShouldNotContain("(ResourceTypeId = 104)");
    }
```

Add to `test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs`:

```csharp
    [Fact]
    public void GivenACompartmentSourcePlan_WhenExplained_ThenPrintsTheGroupedTypeListAndSearchParamId()
    {
        // Arrange
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"), new SqlParameterRef((short)103)),
            new Predicate.Equal(new SqlColumnRef(table.TableName, "ReferenceResourceId"), new SqlParameterRef("123")));
        var plan = new QueryPlan([new CteDefinition.CompartmentSource([104, 106], 77, predicate)], new CteRef(0));

        // Act
        var explained = plan.Explain();

        // Assert
        explained.ShouldBe("root = CompartmentSource[104,106,77]  ReferenceResourceTypeId = @p0 AND ReferenceResourceId = @p1");
    }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet build src/Core/Ignixa.Search.Sql/Ignixa.Search.Sql.csproj`
Expected: build errors until Steps 1-3 are applied in order, then 0 warnings, 0 errors.

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all new tests, zero changes to any prior golden string's expected value (this task adds a new `CteDefinition` case; it does not touch any existing `Emit.Run`/`PlanExplainer.Print` code path).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Ast/CteDefinition.cs src/Core/Ignixa.Search.Sql/Ast/Emit.cs src/Core/Ignixa.Search.Sql/Ast/PlanExplainer.cs test/Ignixa.Search.Sql.Tests/Ast/EmitTests.cs test/Ignixa.Search.Sql.Tests/Ast/PlanExplainerTests.cs
git commit -m "feat(search-sql): add CompartmentSource AST, Emit/PlanExplainer rendering"
```

---

### Task 3: `CompartmentLoweringRule` + `StructuralContext.LowerCompartment` + `Lower.Run`'s nullable `targetResourceType`

**Files:**
- Create: `src/Core/Ignixa.Search.Sql/Lowering/CompartmentLoweringRule.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`
- Modify: `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/CompartmentLoweringRuleTests.cs`
- Test: `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`

**Interfaces:**
- Consumes: `SymbolTable.CompartmentMembership`, `LeafContext.CompartmentMembership` (Task 1). `CteDefinition.CompartmentSource`, `EmitTypeInFilter` (Task 2, indirectly — this task only constructs `CompartmentSource` instances, `Emit` already knows how to render them).
- Produces: `CompartmentLoweringRule.Lower(SearchParameterInfo parameter, IReadOnlyList<string> resourceTypes, string compartmentType, string compartmentId, LeafContext context) → CteDefinition.CompartmentSource`. `StructuralContext.LowerCompartment(CompartmentSearchExpression expression) → CteRef`. `Lower.Run(Expression? expression, SymbolTable symbols, string? targetResourceType, IReadOnlyList<IncludeExpression> includes, IReadOnlyList<IncludeExpression> revIncludes, int includeLimit, int? top = null)`. Task 4's end-to-end tests are the primary consumer of the full pipeline.

- [ ] **Step 1: `CompartmentLoweringRule`**

Read `src/Core/Ignixa.Search.Sql/Lowering/ReferenceLoweringRule.cs` in full first — this rule's predicate construction (`ReferenceResourceTypeId = @p AND ReferenceResourceId = @p`, using `SqlCatalog.Default.Table("ReferenceSearchParam")` and `context.Parameter(...)`) is what this new rule reuses, transcribed exactly, not paraphrased.

Create `src/Core/Ignixa.Search.Sql/Lowering/CompartmentLoweringRule.cs`:

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Catalog;

namespace Ignixa.Search.Sql.Lowering;

/// <summary>
/// Lowers one grouped compartment-membership entry (a single Reference-type search parameter, and
/// every resource type that shares it) to a CompartmentSource. Reuses ReferenceLoweringRule's exact
/// ReferenceResourceTypeId/ReferenceResourceId predicate construction -- compartment membership is,
/// structurally, an ordinary reference-equality predicate against a fixed (compartment type,
/// compartment id) pair; the only difference from an ordinary Observation?subject=Patient/123 search
/// is that CompartmentSource covers many resource types in one CTE instead of one.
/// </summary>
public static class CompartmentLoweringRule
{
    public static CteDefinition.CompartmentSource Lower(
        SearchParameterInfo parameter,
        IReadOnlyList<string> resourceTypes,
        string compartmentType,
        string compartmentId,
        LeafContext context)
    {
        var table = SqlCatalog.Default.Table("ReferenceSearchParam");
        var predicate = new Predicate.And(
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceTypeId"),
                context.Parameter(context.ResourceTypeId(compartmentType))),
            new Predicate.Equal(
                new SqlColumnRef(table.TableName, "ReferenceResourceId"),
                context.Parameter(compartmentId)));

        var resourceTypeIds = resourceTypes.Select(context.ResourceTypeId).ToList();
        return new CteDefinition.CompartmentSource(resourceTypeIds, context.SearchParamId(parameter), predicate);
    }
}
```

- [ ] **Step 2: `StructuralContext.LowerCompartment`**

In `src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs`, add `using Ignixa.Search.Expressions;` if not already present (it is), and add this method (after `LowerChain`):

```csharp
    public CteRef LowerCompartment(CompartmentSearchExpression expression)
    {
        var membership = _leafContext.CompartmentMembership(expression.CompartmentType);
        var groups = expression.FilteredResourceTypes.Count == 0
            ? membership
            : membership
                .Select(m => (m.Parameter, ResourceTypes: (IReadOnlyList<string>)m.ResourceTypes.Where(expression.FilteredResourceTypes.Contains).ToList()))
                .Where(m => m.ResourceTypes.Count > 0)
                .ToList();

        if (groups.Count == 0)
        {
            throw new NotSupportedException(
                $"Compartment search for '{expression.CompartmentType}/{expression.CompartmentId}' resolved to " +
                "zero membership search parameters for the requested resource type(s) -- this compartment/filter " +
                "combination can never match any row. Callers should short-circuit this case before calling " +
                "Lower (matching CompartmentSearchQueryGenerator's own empty-result short-circuit today), not " +
                "rely on this throw.");
        }

        var refs = groups.Select(g =>
        {
            var cte = CompartmentLoweringRule.Lower(g.Parameter, g.ResourceTypes, expression.CompartmentType, expression.CompartmentId, _leafContext);
            _ctes.Add(cte);
            return new CteRef(_ctes.Count - 1);
        }).ToList();

        return Union(refs);
    }
```

- [ ] **Step 3: `Lower.Run`'s nullable `targetResourceType` and `LowerNode`'s new `CompartmentSearchExpression` arm**

In `src/Core/Ignixa.Search.Sql/Lowering/Lower.cs`, change `Run`:

```csharp
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        int? top = null)
    {
        var context = new StructuralContext(symbols);
        CteRef match;
        Predicate? outerPredicate = null;

        if (expression is null)
        {
            match = context.LowerResourceSource(targetResourceType);
        }
        else
        {
            var leafContext = new LeafContext(symbols);
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
            outerPredicate = extractedPredicate;
            match = remaining is null
                ? context.LowerResourceSource(targetResourceType)
                : LowerNode(remaining, context, targetResourceType);
        }

        var includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        return new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages);
    }
```

to:

```csharp
    public static QueryPlan Run(
        Expression? expression,
        SymbolTable symbols,
        string? targetResourceType,
        IReadOnlyList<IncludeExpression> includes,
        IReadOnlyList<IncludeExpression> revIncludes,
        int includeLimit,
        int? top = null)
    {
        var context = new StructuralContext(symbols);
        CteRef match;
        Predicate? outerPredicate = null;

        if (expression is null)
        {
            match = context.LowerResourceSource(RequireResourceType(targetResourceType));
        }
        else
        {
            var leafContext = new LeafContext(symbols);
            var (remaining, extractedPredicate) = ExtractResourceColumnPredicates(expression, leafContext);
            outerPredicate = extractedPredicate;
            match = remaining switch
            {
                null => context.LowerResourceSource(RequireResourceType(targetResourceType)),
                CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
                _ when targetResourceType is null => throw new NotSupportedException(
                    "A search with no single target resource type (a wildcard compartment search) can only " +
                    "combine with a CompartmentSearchExpression and resource-column predicates -- an ordinary " +
                    "typed search parameter alongside it has no single resource type to scope it against, " +
                    "which this phase does not support."),
                _ => LowerNode(remaining, context, targetResourceType!), // non-null: the prior arm already threw otherwise.
            };
        }

        IReadOnlyList<IncludeStage>? includeStages;
        if (targetResourceType is null)
        {
            if (includes.Count > 0 || revIncludes.Count > 0)
            {
                throw new NotSupportedException(
                    "_include/_revinclude combined with a wildcard compartment search (no single target resource " +
                    "type) is not supported -- BuildIncludeStages needs a concrete match resource type to compute " +
                    "SeedFromMatch.");
            }

            includeStages = null;
        }
        else
        {
            includeStages = BuildIncludeStages(includes, revIncludes, symbols, targetResourceType, includeLimit);
        }

        return new QueryPlan(context.Ctes, match, top, outerPredicate, includeStages);
    }

    private static string RequireResourceType(string? targetResourceType)
        => targetResourceType ?? throw new NotSupportedException(
            "targetResourceType is required unless the top-level expression is a compartment search with no single target resource type.");
```

Change `LowerNode`'s switch to add a new arm (after the `ChainedExpression` arm, before the fallback):

```csharp
    private static CteRef LowerNode(Expression expression, StructuralContext context, string resourceType) => expression switch
    {
        SearchParameterPredicateExpression { Modifier.SearchModifierCode: SearchModifierCode.Not } => throw new NotSupportedException(
            "A :not-modified predicate reached leaf dispatch directly, outside a SearchParameterExpression wrapper -- " +
            "the real binder never produces this shape (LowerSearchParameter handles :not for both the single-value " +
            "and comma-separated cases), so this is unexpected input. Throwing rather than silently lowering it as a " +
            "positive match, which is exactly the bug this guard exists to prevent."),
        SearchParameterPredicateExpression leaf => context.Lower(leaf, resourceType),
        SearchParameterExpression sp => LowerSearchParameter(sp, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.And } and => LowerAnd(and, context, resourceType),
        MultiaryExpression { MultiaryOperation: MultiaryOperator.Or } or => context.Union(
            or.Expressions.Select(e => LowerNode(e, context, resourceType)).ToList()),
        ChainedExpression chain => context.LowerChain(chain, LowerScopedExpression),
        CompartmentSearchExpression compartment => context.LowerCompartment(compartment),
        _ => throw new NotSupportedException(
            $"Lower does not support {expression.GetType().Name} yet -- see this plan's scope notes."),
    };
```

This arm is reached when a compartment search is combined with an ordinary predicate under a NON-null `targetResourceType` (e.g. `GET /Patient/123/Observation?category=laboratory`, `remaining = And(CompartmentSearchExpression, category)`, `LowerAnd` recurses into both children via `LowerNode`) — `context.LowerCompartment` ignores the `resourceType` parameter entirely (it derives its own scope from `expression.CompartmentType`), so this arm is correct regardless of what `resourceType` happens to be, as long as it's non-null (guaranteed here, since `LowerNode` is never called with a null `resourceType` per the `Run` method's own guard above).

Update the class's XML doc comment: append, after "...and includes/revIncludes (via BuildIncludeStages, Phase 7) into QueryPlan.Includes.": ` As of Phase 8, CompartmentSearchExpression is also handled, via StructuralContext.LowerCompartment, dispatched both from Run's top-level switch (the wildcard, no-single-scope case) and from LowerNode's ordinary switch (the non-wildcard case, reachable standalone or nested inside an And alongside ordinary predicates).`

- [ ] **Step 4: Write the tests**

Add to `test/Ignixa.Search.Sql.Tests/Lowering/CompartmentLoweringRuleTests.cs` (new file, mirroring `ReferenceLoweringRuleTests.cs`'s existing conventions — read that file first to match its exact style):

```csharp
using Ignixa.Search.Models;
using Ignixa.Search.Sql.Ast;
using Ignixa.Search.Sql.Lowering;
using Ignixa.Search.Sql.Symbols;
using Ignixa.Specification.ValueSets.Normative;
using Shouldly;
using Xunit;

namespace Ignixa.Search.Sql.Tests.Lowering;

public class CompartmentLoweringRuleTests
{
    [Fact]
    public void GivenAGroupedMembershipParameter_WhenLowered_ThenProducesACompartmentSourceWithTheReferencePredicate()
    {
        // Arrange
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104, ["Condition"] = 106 });
        var context = new LeafContext(symbols);

        // Act
        var cte = CompartmentLoweringRule.Lower(subjectParam, ["Observation", "Condition"], "Patient", "123", context);

        // Assert
        cte.SearchParamId.ShouldBe((short)77);
        cte.ResourceTypeIds.ShouldBe([(short)104, (short)106]);
        cte.Predicate.ShouldBeOfType<Predicate.And>();
    }
}
```

Add to `test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs`:

```csharp
    [Fact]
    public void GivenAWildcardCompartmentSearchWithAnOrdinaryTypedPredicate_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?name=Smith -- no single resource type to scope "name" against.
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var nameParam = new SearchParameterInfo("name", "name", SearchParamType.String, new Uri("http://hl7.org/fhir/SearchParameter/Patient-name"));
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var namePredicate = new SearchParameterPredicateExpression(nameParam, SearchComparator.Eq, modifier: null, new StringSearchValue("Smith"));
        var tree = new MultiaryExpression(MultiaryOperator.And, [compartment, namePredicate]);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [nameParam.Url.ToString()] = 202 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(tree, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0))
            .Message.ShouldContain("no single resource type");
    }

    [Fact]
    public void GivenAWildcardCompartmentSearchWithIncludes_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/*?_include=Observation:encounter
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Observation-subject"));
        var encounterParam = new SearchParameterInfo(
            "encounter", "encounter", SearchParamType.Reference,
            new Uri("http://hl7.org/fhir/SearchParameter/Observation-encounter"), targetResourceTypes: ["Encounter"]);
        var compartment = new CompartmentSearchExpression("Patient", "123");
        var include = new IncludeExpression(["Observation"], encounterParam, "Observation", "Encounter", null, wildCard: false, reversed: false, iterate: false);
        var symbols = new SymbolTable(
            new Dictionary<string, short> { [subjectParam.Url.ToString()] = 77, [encounterParam.Url.ToString()] = 88 },
            new Dictionary<string, short> { ["Patient"] = 103, ["Observation"] = 104, ["Encounter"] = 105 },
            new Dictionary<string, IReadOnlyList<(SearchParameterInfo, IReadOnlyList<string>)>>
            {
                ["Patient"] = [(subjectParam, ["Observation"])],
            });

        // Act & Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(compartment, symbols, targetResourceType: null, includes: [include], revIncludes: [], includeLimit: 1000))
            .Message.ShouldContain("SeedFromMatch");
    }
```

- [ ] **Step 5: Run the tests**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all new tests, and every existing `LowerTests`/`EndToEndCompilationTests` case unmodified (per the Global Constraints, `targetResourceType`'s type change from `string` to `string?` needs zero call-site edits).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Search.Sql/Lowering/CompartmentLoweringRule.cs src/Core/Ignixa.Search.Sql/Lowering/StructuralContext.cs src/Core/Ignixa.Search.Sql/Lowering/Lower.cs test/Ignixa.Search.Sql.Tests/Lowering/CompartmentLoweringRuleTests.cs test/Ignixa.Search.Sql.Tests/Lowering/LowerTests.cs
git commit -m "feat(search-sql): lower CompartmentSearchExpression to grouped CompartmentSource CTEs"
```

---

### Task 4: End-to-end compilation tests (Resolve → Lower → Emit, full pipeline)

**Files:**
- Test: `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: nothing new — pure proof, composing `Resolve.RunAsync`, `Lower.Run`, and `Emit.Run`/`Explain()` together the way Phase 9's DataLayer wiring eventually will.

- [ ] **Step 1: Read the file's existing test pattern and its `FakeSymbolResolver`**

Open `test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs`, confirm its `FakeSymbolResolver` private nested class (reuse it — do not create a new one), and find its most recent Phase-7 test for structural reference.

- [ ] **Step 2: Write the wildcard compartment search end-to-end test**

```csharp
    [Fact]
    public async Task GivenAWildcardPatientCompartmentSearch_WhenCompiledEndToEnd_ThenTheCteIsAUnionOfGroupedCompartmentSources()
    {
        // Arrange -- GET /Patient/123/* -- Patient compartment covers Observation (via "subject") and
        // Condition (via "subject" AND "asserter", two distinct membership parameters).
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var asserterParam = new SearchParameterInfo("asserter", "asserter", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/Condition-asserter"));
        var compartment = new CompartmentSearchExpression("Patient", "123");

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[asserterParam.Url!.ToString()] = 66;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;
        resolver.ResourceTypeIds["Condition"] = 106;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation", "Condition"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];
        compartmentManager.SearchParams[("Condition", CompartmentType.Patient)] = ["subject", "asserter"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;
        searchParamManager.Parameters[("Condition", "subject")] = subjectParam;
        searchParamManager.Parameters[("Condition", "asserter")] = asserterParam;

        // Act
        var symbols = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None,
            compartmentManager, searchParamManager);
        var plan = Lower.Run(compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0);

        // Assert -- inc-free, two grouped CompartmentSource CTEs (one per distinct SearchParamId), Unioned.
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[1].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Union>();
        plan.Match.ShouldBe(new CteRef(2));

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("SearchParamId = 55");
        emitted.Sql.ShouldContain("SearchParamId = 66");
        emitted.Sql.ShouldContain("(ResourceTypeId = 104 OR ResourceTypeId = 106)"); // subject: Observation + Condition
        emitted.Sql.ShouldContain("ResourceTypeId = 106\n"); // asserter: Condition only, bare Equal
    }
```

- [ ] **Step 3: Write the non-wildcard compartment search end-to-end test**

```csharp
    [Fact]
    public async Task GivenANonWildcardPatientCompartmentSearchForObservation_WhenCompiledEndToEnd_ThenTargetResourceTypeIsUsedNormally()
    {
        // Arrange -- GET /Patient/123/Observation -- FilteredResourceTypes = {"Observation"}, a real
        // targetResourceType ("Observation") is supplied (matching SearchCompartmentHandler's own
        // non-wildcard behavior -- SearchOptions.ResourceType is only ever nulled for "*").
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager);
        var plan = Lower.Run(compartment, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);

        // Assert
        plan.Ctes.Count.ShouldBe(1);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();

        var emitted = Emit.Run(plan);
        emitted.Sql.ShouldContain("ResourceTypeId = 104\n");
        emitted.Sql.ShouldNotContain("(ResourceTypeId = 104)");
    }
```

- [ ] **Step 4: Write the compartment + ordinary-predicate combination end-to-end test**

```csharp
    [Fact]
    public async Task GivenANonWildcardCompartmentSearchCombinedWithAnOrdinaryPredicate_WhenCompiledEndToEnd_ThenAnIntersectComposesThem()
    {
        // Arrange -- GET /Patient/123/Observation?category=laboratory -- zero new mechanism (design §4):
        // LowerAnd's existing recursion produces Intersect(compartmentUnion, categoryCte).
        var subjectParam = new SearchParameterInfo("subject", "subject", SearchParamType.Reference, new Uri("http://hl7.org/fhir/SearchParameter/clinical-subject"));
        var categoryParam = new SearchParameterInfo("category", "category", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-category"));
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "Observation" });
        var categoryPredicate = new SearchParameterPredicateExpression(categoryParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "laboratory", text: null));
        var tree = new MultiaryExpression(MultiaryOperator.And, [compartment, categoryPredicate]);

        var resolver = new FakeSymbolResolver();
        resolver.SearchParamIds[subjectParam.Url!.ToString()] = 55;
        resolver.SearchParamIds[categoryParam.Url!.ToString()] = 22;
        resolver.ResourceTypeIds["Patient"] = 103;
        resolver.ResourceTypeIds["Observation"] = 104;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"];
        compartmentManager.SearchParams[("Observation", CompartmentType.Patient)] = ["subject"];

        var searchParamManager = new FakeSearchParameterDefinitionManager();
        searchParamManager.Parameters[("Observation", "subject")] = subjectParam;

        // Act
        var symbols = await Resolve.RunAsync(
            tree, includes: [], revIncludes: [], resolver, targetResourceType: "Observation", CancellationToken.None,
            compartmentManager, searchParamManager);
        var plan = Lower.Run(tree, symbols, targetResourceType: "Observation", includes: [], revIncludes: [], includeLimit: 0);

        // Assert
        plan.Ctes.Count.ShouldBe(3);
        plan.Ctes[0].ShouldBeOfType<CteDefinition.CompartmentSource>();
        plan.Ctes[1].ShouldBeOfType<CteDefinition.ParamSource>();
        plan.Ctes[2].ShouldBeOfType<CteDefinition.Intersect>();
        plan.Match.ShouldBe(new CteRef(2));
    }
```

- [ ] **Step 5: Write the degenerate (empty-membership) end-to-end test**

```csharp
    [Fact]
    public async Task GivenACompartmentSearchThatResolvesToZeroMembershipParameters_WhenLowered_ThenThrowsNotSupportedException()
    {
        // Arrange -- GET /Patient/123/NotInCompartment (design §2's degenerate case).
        var compartment = new CompartmentSearchExpression("Patient", "123", new HashSet<string> { "NotInCompartment" });

        var resolver = new FakeSymbolResolver();
        resolver.ResourceTypeIds["Patient"] = 103;

        var compartmentManager = new FakeCompartmentDefinitionManager();
        compartmentManager.ResourceTypes[CompartmentType.Patient] = ["Observation"]; // "NotInCompartment" isn't listed

        var searchParamManager = new FakeSearchParameterDefinitionManager();

        // Act
        var symbols = await Resolve.RunAsync(
            compartment, includes: [], revIncludes: [], resolver, targetResourceType: "Patient", CancellationToken.None,
            compartmentManager, searchParamManager);

        // Assert
        Should.Throw<NotSupportedException>(() =>
            Lower.Run(compartment, symbols, targetResourceType: null, includes: [], revIncludes: [], includeLimit: 0))
            .Message.ShouldContain("zero membership");
    }
```

- [ ] **Step 6: Add the shared test doubles**

Add `using Ignixa.Search.Definition;` and `using Ignixa.Specification.ValueSets.Normative;` to the top of `EndToEndCompilationTests.cs` if not already present, and add these two private nested classes (copy verbatim from Task 1's `ResolveTests.cs` — same shape, same reasoning, this file needs its own copies since C# nested test-double classes are not shared across test classes in this codebase's convention):

```csharp
    private sealed class FakeCompartmentDefinitionManager : ICompartmentDefinitionManager
    {
        public Dictionary<CompartmentType, HashSet<string>> ResourceTypes { get; } = [];

        public Dictionary<(string ResourceType, CompartmentType CompartmentType), HashSet<string>> SearchParams { get; } = [];

        public bool TryGetResourceTypes(CompartmentType compartmentType, out HashSet<string> resourceTypes)
            => ResourceTypes.TryGetValue(compartmentType, out resourceTypes!);

        public bool TryGetSearchParams(string resourceType, CompartmentType compartmentType, out HashSet<string> searchParams)
            => SearchParams.TryGetValue((resourceType, compartmentType), out searchParams!);
    }

    private sealed class FakeSearchParameterDefinitionManager : ISearchParameterDefinitionManager
    {
        public Dictionary<(string ResourceType, string Code), SearchParameterInfo> Parameters { get; } = [];

        public bool TryGetSearchParameter(string resourceType, string code, out SearchParameterInfo searchParameter)
            => Parameters.TryGetValue((resourceType, code), out searchParameter!);

        public IEnumerable<SearchParameterInfo> AllSearchParameters => throw new NotImplementedException();
        public IReadOnlyDictionary<string, string> SearchParameterHashMap => throw new NotImplementedException();
        public IEnumerable<SearchParameterInfo> GetSearchParameters(string resourceType) => throw new NotImplementedException();
        public bool TryGetSearchParameters(string resourceType, out IEnumerable<SearchParameterInfo> searchParameters) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(string resourceType, string code) => throw new NotImplementedException();
        public bool TryGetSearchParameter(Uri definitionUri, out SearchParameterInfo value) => throw new NotImplementedException();
        public SearchParameterInfo GetSearchParameter(Uri definitionUri) => throw new NotImplementedException();
        public void UpdateSearchParameterHashMap(Dictionary<string, string> updatedSearchParamHashMap) => throw new NotImplementedException();
        public string GetSearchParameterHashForResourceType(string resourceType) => throw new NotImplementedException();
        public void AddNewSearchParameters(IReadOnlyCollection<Ignixa.Abstractions.IElement> searchParameters, bool calculateHash = true) => throw new NotImplementedException();
        public void DeleteSearchParameter(string url, bool calculateHash = true) => throw new NotImplementedException();
    }
```

- [ ] **Step 7: Run the full suite**

Run: `dotnet test test/Ignixa.Search.Sql.Tests/Ignixa.Search.Sql.Tests.csproj`
Expected: PASS, all 5 new end-to-end tests plus the entire pre-existing suite.

- [ ] **Step 8: Commit**

```bash
git add test/Ignixa.Search.Sql.Tests/EndToEndCompilationTests.cs
git commit -m "test(search-sql): prove compartment search compiles end to end"
```

---

### Task 5: Combined proof + full regression + SMART-seat verification + final whole-branch review prep

**Files:** none (verification only), plus a roadmap doc update.

**Interfaces:**
- Consumes: everything from Tasks 1-4.
- Produces: a clean `dotnet build All.sln` / `dotnet test All.sln` baseline, a documented verification that the SMART-scope seat (design §5) is genuinely fillable, and a review package for the final whole-branch review.

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

- [ ] **Step 2: Full solution test**

Run: `dotnet test All.sln --filter "FullyQualifiedName!~E2ETests"`
Expected: all passing except the 2 pre-existing `Ignixa.SqlOnFhir.Tests` submodule failures (one per target framework), unrelated to this plan and out of scope on every prior increment.

- [ ] **Step 3: Verify the SMART-scope seat is genuinely fillable (design §5) — documentation only, no code**

Confirm by re-reading `StructuralContext.LowerCompartment` (Task 3) and `IncludeStage.OutputTypeIds`/the include design doc's `OutputScopeFilter` mention (`docs/superpowers/specs/2026-07-17-fhir-to-sql-compiler-include-design.md` §6): the `CteRef` `LowerCompartment` returns (the `Union` of `CompartmentSource` nodes for a compartment type) is exactly the "compartment-membership CTE... Phase 8's business to construct" that seat already names. No code change — this step is a verification a human or the final reviewer can independently re-check, recorded here so it isn't silently assumed.

- [ ] **Step 4: Re-read §6's "explicitly deferred" list and confirm nothing in this plan silently attempted any of it**

Confirm: no `_sort`/continuation-token code was added; no `HttpContext.Items["FhirAuthorizationFilter"]` wiring was added (that lives in `Ignixa.Api`/`Ignixa.Application`, outside this Core-tier project); no instance-level SMART filter (`OutputScopeFilter` or equivalent) was added to any `CteDefinition`. If any of Tasks 1-4's actual committed code drifted into one of these, flag it now rather than let the final reviewer discover unscoped work.

- [ ] **Step 5: Update the roadmap doc**

In `docs/superpowers/plans/2026-07-15-fhir-to-sql-compiler-roadmap.md`, add a new paragraph after the eighth increment's (Phase 7 include) write-up, following that paragraph's exact narrative style/detail level: summarize what shipped (the `CompartmentSource` node, the grouped-by-`SearchParamId` shape matching both production and fhir-server, the `ReferenceResourceTypeId` filter fix, the nullable-`targetResourceType` mechanism and its two named `NotSupportedException` combinations), and explicitly note this is Phase 8 **part 1** — `_sort`/continuation tokens (Phase 8 part 2) are a separate, not-yet-written plan, and Checkpoint 1.5 (stop before Phase 9) is not yet reached.

- [ ] **Step 6: Prepare the final whole-branch review package**

Follow `superpowers:subagent-driven-development`'s final-review step: run `scripts/review-package MERGE_BASE HEAD` (from that skill's directory; `MERGE_BASE` = `git merge-base feature/fhir-to-sql-compiler HEAD` if this plan executed on a dedicated worktree branch off `feature/fhir-to-sql-compiler`) and dispatch the final whole-branch reviewer on the most capable available model, per that skill's Model Selection section.

- [ ] **Step 7: Report to the user before merging or pushing**

Summarize what shipped and what's explicitly still deferred (§1.4's differential-test note, §5/§6's list, and the two `NotSupportedException` combinations this plan itself introduced beyond the design doc's original scope), then ask before merging into `feature/fhir-to-sql-compiler` and again before pushing — matching every prior increment's established pattern on this branch.
