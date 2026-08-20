# Workflow Scenario Packs (DailyAppointmentSchedule vertical slice) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the first working, testable slice of `docs/features/fhir-faker/investigations/workflow-context-data-generation.md`: workflow scenario pack discovery (`WorkflowScenarioCatalog`), a resource-graph/augmentor/composer pipeline, and one built-in pack (`DailyAppointmentSchedule`) reachable from the CLI as `ignixa-fakes {version} workflow DailyAppointmentSchedule`.

**Architecture:** Mirrors the existing, merged `ScenarioCatalog` (PR #299) discovery model instead of inventing a parallel `IWorkflowScenarioProvider` — a sibling `WorkflowScenarioCatalog` discovers public static factory methods in `Ignixa.FhirFakes.Workflow.Predefined` by attribute + reflection, reusing `DiscoveredScenario`/`DiscoveredScenarioParameter` and a newly-extracted shared parameter-binding helper. A new `ResourceGraph` aggregates one-or-more patient-centric `ScenarioContext`s plus non-patient resources (appointments); an `IResourceGraphAugmentor` adds those non-patient resources; an `ISearchResponseComposer` shapes the graph into paged FHIR searchset `BundleJsonNode`s. `ScenarioBuilder` gains a seeded constructor overload so multi-patient composition can share one reproducible seed.

**Tech Stack:** .NET 9/10, C# 12/13 (primary constructors, collection expressions), xUnit + Shouldly, `System.CommandLine` for the CLI, existing `Bogus`-backed `SchemaBasedFhirResourceFaker`.

## Global Constraints

- Target frameworks: `net9.0;net10.0`, `Nullable` enabled, `ImplicitUsings` enabled (per `src/Core/Ignixa.FhirFakes/Ignixa.FhirFakes.csproj`).
- `Ignixa.FhirFakes` is `<PackageStability>stable</PackageStability>` but **pre-v1** — per explicit user direction, all new public types in this plan are designed and shipped as final public surface now; no `internal`-then-promote staging.
- One type per file (project `AGENTS.md` file-organization rule). Every new class/interface/enum/record gets its own file.
- Test naming: `GivenContext_WhenAction_ThenResult`, xUnit `[Fact]`, Shouldly assertions, no `#region` in new test files (existing production files already use `#region` in a few places — don't add new ones).
- No inline "what it does" comments; XML doc `<summary>` comments on public members only, matching this codebase's existing convention (not the terser global default) since this project's public API is consistently documented this way.
- **Determinism scope, stated honestly:** existing `SchemaBasedFhirResourceFaker(schemaProvider, seed)` and the new seeded `ScenarioBuilder` overload make Bogus-driven picks reproducible for a given seed — but `InitialState` (Patient gender/name/age) already uses its own private unseeded `Bogus.Faker`, and every existing `ScenarioState` (`EncounterState`, `PractitionerState`, etc.) assigns resource `id` via `Guid.NewGuid()`, not a seeded source. This plan does not fix either pre-existing limitation — that would mean reworking every state in `src/Core/Ignixa.FhirFakes/Scenarios/States/`, which is out of scope here. Consequently: **no byte-identical JSON pinning tests** in this plan. Tests assert structural/value properties (counts, statuses, reference shapes), matching how `ScenarioCatalogTests` itself tests determinism (birth-year comparison, not full-JSON diff).
- Non-goals for this plan specifically (beyond the investigation doc's own Non-Goals): no `RegisterAssembly`/external extension-package registration (Phase 5, no second pack exists yet to justify it); no flavor adapters (doc itself defers these past the first two packs); no `IncludeCompleteness.Duplicate/Stale/Unrelated` (only `Complete`/`Missing`, the two variants `DailyAppointmentSchedule` actually calls out); no `PractitionerPanel` (separate future plan).

---

## Task 1: Extract shared scenario-parameter-binding helper

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioParameterBinder.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs:36` (`GetAll` unaffected), `:56-170` (`Invoke`, `CoerceAndValidateOverride`, `DefaultForType`), `:172-226` (`Discover`, `BuildParameter`, `Humanize`)
- Test: no new test file — this task must not change behavior. Run existing `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs` unchanged as the regression check.

**Interfaces:**
- Produces: `internal static class ScenarioParameterBinder` with `BuildParameter(ParameterInfo) -> DiscoveredScenarioParameter`, `BuildArguments(string scenarioId, MethodInfo method, IReadOnlyDictionary<string, object?>? overrides, params object[] leadingArgs) -> object?[]`, `Humanize(string id) -> string`. Task 5 (`WorkflowScenarioCatalog`) consumes all three.

This is a pure refactor: today `ScenarioCatalog` hardcodes exactly one leading argument (`schemaProvider`) before override-bound parameters. `WorkflowScenarioCatalog` (Task 5) needs two leading arguments (`schemaProvider`, `options`). Generalizing `BuildArguments` to take `params object[] leadingArgs` lets both catalogs share one tested implementation of override coercion, Min/Max validation, and default-value fallback — this is the concrete fix for the gap Fable's review flagged: the original draft's `IWorkflowScenarioProvider` would have reimplemented this from scratch.

- [ ] **Step 1: Create the shared binder with the exact logic currently private in `ScenarioCatalog`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Shared reflection-based parameter discovery and override-coercion logic used by both
/// <see cref="ScenarioCatalog"/> and <c>WorkflowScenarioCatalog</c>, so the two catalogs share one
/// tested implementation of override coercion, Min/Max validation, and default-value fallback
/// instead of each reimplementing it.
/// </summary>
internal static class ScenarioParameterBinder
{
    private static readonly HashSet<Type> NumericTypes =
    [
        typeof(sbyte), typeof(byte), typeof(short), typeof(ushort),
        typeof(int), typeof(uint), typeof(long), typeof(ulong),
        typeof(float), typeof(double), typeof(decimal),
    ];

    /// <summary>
    /// Builds <see cref="DiscoveredScenarioParameter"/> metadata for one factory method parameter,
    /// reading <see cref="ScenarioParameterAttribute"/> if present.
    /// </summary>
    public static DiscoveredScenarioParameter BuildParameter(ParameterInfo parameter)
    {
        var attribute = parameter.GetCustomAttribute<ScenarioParameterAttribute>();

        return new DiscoveredScenarioParameter
        {
            Name = parameter.Name!,
            Type = parameter.ParameterType,
            DefaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null,
            HasDefaultValue = parameter.HasDefaultValue,
            Min = attribute is null || double.IsNaN(attribute.Min) ? null : attribute.Min,
            Max = attribute is null || double.IsNaN(attribute.Max) ? null : attribute.Max,
            Description = attribute?.Description,
        };
    }

    /// <summary>
    /// Builds the argument array for invoking <paramref name="method"/>: <paramref name="leadingArgs"/>
    /// fill the first parameters positionally, then remaining parameters resolve from
    /// <paramref name="overrides"/> (matched by name, case-insensitive), falling back to the
    /// parameter's own default, then a type-appropriate zero value.
    /// </summary>
    public static object?[] BuildArguments(
        string scenarioId,
        MethodInfo method,
        IReadOnlyDictionary<string, object?>? overrides,
        params object[] leadingArgs)
    {
        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < leadingArgs.Length; i++)
        {
            args[i] = leadingArgs[i];
        }

        var overrideMap = overrides is null
            ? null
            : new Dictionary<string, object?>(overrides, StringComparer.OrdinalIgnoreCase);

        for (var i = leadingArgs.Length; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (overrideMap != null && overrideMap.TryGetValue(parameter.Name!, out var overrideValue))
            {
                args[i] = CoerceAndValidateOverride(scenarioId, parameter, overrideValue);
            }
            else if (parameter.HasDefaultValue)
            {
                args[i] = parameter.DefaultValue;
            }
            else
            {
                args[i] = DefaultForType(parameter.ParameterType);
            }
        }

        return args;
    }

    /// <summary>
    /// Humanizes a PascalCase id into space-separated words (e.g. "DiabeticPatient" -> "Diabetic Patient").
    /// </summary>
    public static string Humanize(string id)
    {
        var builder = new StringBuilder();
        foreach (var c in id)
        {
            if (builder.Length > 0 && char.IsUpper(c) && !char.IsUpper(builder[^1]))
                builder.Append(' ');
            builder.Append(c);
        }

        return builder.ToString();
    }

    private static object? DefaultForType(Type type)
    {
        if (type.IsValueType && Nullable.GetUnderlyingType(type) == null)
        {
            return Activator.CreateInstance(type);
        }

        return null;
    }

    [SuppressMessage("Usage", "CA2208:Instantiate argument exceptions correctly", Justification = "paramName intentionally names the public Invoke argument 'parameterOverrides', the surface a caller can fix.")]
    private static object? CoerceAndValidateOverride(string scenarioId, ParameterInfo parameter, object? value)
    {
        var effectiveType = Nullable.GetUnderlyingType(parameter.ParameterType) ?? parameter.ParameterType;

        if (value is null)
        {
            if (parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) is null)
            {
                throw new ArgumentException(
                    $"Scenario '{scenarioId}': override for parameter '{parameter.Name}' is null, but the parameter type '{parameter.ParameterType.Name}' is a non-nullable value type.",
                    "parameterOverrides");
            }

            return null;
        }

        if (effectiveType.IsInstanceOfType(value))
        {
            return value;
        }

        if (NumericTypes.Contains(effectiveType) && NumericTypes.Contains(value.GetType()))
        {
            try
            {
                return Convert.ChangeType(value, effectiveType, CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
            {
                // Falls through to the throw below (e.g. a long value too large for an int parameter).
            }
        }

        throw new ArgumentException(
            $"Scenario '{scenarioId}': override for parameter '{parameter.Name}' is of type '{value.GetType().Name}', but the parameter expects '{effectiveType.Name}'.",
            "parameterOverrides");
    }
}
```

- [ ] **Step 2: Point `ScenarioCatalog.Invoke` at the shared binder**

Replace lines 56-106 of `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs` (the `Invoke` method body) with:

```csharp
    public static ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var args = ScenarioParameterBinder.BuildArguments(scenario.Id, scenario.Method, parameterOverrides, schemaProvider);

        ScenarioContext context;
        try
        {
            context = (ScenarioContext)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }

        if (scenario.Domain is { } domain)
        {
            context.SetAttribute(ClinicalDomainAttributeKey, domain);
        }

        return context;
    }
```

- [ ] **Step 3: Delete the now-moved private members and update `Discover`**

Delete the private `DefaultForType`, `NumericTypes`, `CoerceAndValidateOverride` members (original lines 108-170) entirely — they now live in `ScenarioParameterBinder`.

Replace the `Discover`/`BuildParameter`/`Humanize` block (original lines 172-239) with:

```csharp
    private static IReadOnlyList<DiscoveredScenario> Discover()
    {
        var assembly = typeof(DiabeticPatientScenario).Assembly;

        var scenarioTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Scenarios.Predefined" && t.IsClass && t.IsPublic);

        var scenarios = new List<DiscoveredScenario>();

        foreach (var type in scenarioTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(ScenarioContext));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length == 0 || parameters[0].ParameterType != typeof(IFhirSchemaProvider))
                    continue;

                var attribute = method.GetCustomAttribute<ScenarioAttribute>();
                var id = attribute?.Id
                    ?? (method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name["Get".Length..] : method.Name);

                scenarios.Add(new DiscoveredScenario
                {
                    Id = id,
                    Category = attribute?.Category,
                    Title = attribute?.Title ?? ScenarioParameterBinder.Humanize(id),
                    Description = attribute?.Description,
                    Parameters = parameters.Skip(1).Select(ScenarioParameterBinder.BuildParameter).ToList(),
                    Domain = attribute is null || attribute.Domain == ClinicalDomain.Unspecified ? null : attribute.Domain,
                    Method = method,
                });
            }
        }

        return scenarios;
    }
```

Also remove the now-unused `using System.Globalization;` and `using System.Text;` from the top of `ScenarioCatalog.cs` if no other member uses them (check with a text search inside the file before removing — `System.Diagnostics.CodeAnalysis` and `System.Reflection` are still needed for `Discover`/attributes).

- [ ] **Step 4: Run the existing regression suite unchanged**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioCatalogTests"`
Expected: PASS, all 17 existing facts in `ScenarioCatalogTests.cs`, with zero test-file changes. This is the proof the refactor is behavior-preserving.

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Scenarios/ScenarioParameterBinder.cs src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs
git commit -m "refactor: extract ScenarioParameterBinder so workflow catalog can reuse it"
```

---

## Task 2: Seeded `ScenarioBuilder` constructor overload

**Files:**
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioBuilder.cs:70-75` (constructor)
- Test: Create `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioBuilderSeedTests.cs`

**Interfaces:**
- Produces: `public ScenarioBuilder(IFhirSchemaProvider schemaProvider, int seed)`. Task 8 (`DailyAppointmentScheduleScenario`) consumes this to give each per-appointment patient a distinct-but-derived seed.

- [ ] **Step 1: Write the failing test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class ScenarioBuilderSeedTests
{
    [Fact]
    public void GivenSameSeed_WhenBuildingTwice_ThenPatientDemographicsMatch()
    {
        var schemaProvider = new R4CoreSchemaProvider();

        var first = new ScenarioBuilder(schemaProvider, 42).WithPatient(p => p.WithAge(50)).Build();
        var second = new ScenarioBuilder(schemaProvider, 42).WithPatient(p => p.WithAge(50)).Build();

        first.Patient!.MutableNode["birthDate"]!.ToString().ShouldBe(second.Patient!.MutableNode["birthDate"]!.ToString());
    }

    [Fact]
    public void GivenDifferentSeeds_WhenBuilding_ThenAtLeastOneFieldDiffers()
    {
        var schemaProvider = new R4CoreSchemaProvider();

        var first = new ScenarioBuilder(schemaProvider, 1).WithPatient().Build();
        var second = new ScenarioBuilder(schemaProvider, 2).WithPatient().Build();

        var firstName = first.Patient!.MutableNode["name"]!.ToJsonString();
        var secondName = second.Patient!.MutableNode["name"]!.ToJsonString();
        (firstName != secondName || first.Patient!.MutableNode["gender"]!.ToString() != second.Patient!.MutableNode["gender"]!.ToString())
            .ShouldBeTrue("expected at least the PatientBuilder-driven fields to differ across seeds");
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioBuilderSeedTests"`
Expected: FAIL — `CS1729: 'ScenarioBuilder' does not contain a constructor that takes 2 arguments`

- [ ] **Step 3: Add the seeded constructor overload**

In `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioBuilder.cs`, immediately after the existing constructor (lines 70-75), add:

```csharp
    /// <summary>
    /// Creates a new scenario builder whose randomness is seeded for reproducible generation.
    /// </summary>
    /// <param name="schemaProvider">The FHIR schema provider for resource generation.</param>
    /// <param name="seed">The seed applied to the internal <see cref="SchemaBasedFhirResourceFaker"/>.</param>
    public ScenarioBuilder(IFhirSchemaProvider schemaProvider, int seed)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        _schemaProvider = schemaProvider;
        _faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed);
    }
```

(Two independent constructors, not chained — this mirrors `SchemaBasedFhirResourceFaker`'s own existing seeded/unseeded constructor pair in the same codebase.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioBuilderSeedTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Scenarios/ScenarioBuilder.cs test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioBuilderSeedTests.cs
git commit -m "feat: add seeded ScenarioBuilder constructor overload"
```

---

## Task 3: `ResourceGraph`, `IResourceGraphAugmentor`, `ResourceGraphAugmentationContext`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/ResourceGraph.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/IResourceGraphAugmentor.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/ResourceGraphAugmentationContext.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/ResourceGraphTests.cs`

**Interfaces:**
- Produces: `ResourceGraph.AllResources: IReadOnlyList<ResourceJsonNode>`, `ResourceGraph.AddScenario(ScenarioContext)`, `ResourceGraph.AddResource(ResourceJsonNode)`; `IResourceGraphAugmentor.Augment(ResourceGraph, ResourceGraphAugmentationContext)`; `ResourceGraphAugmentationContext { SchemaProvider, Faker, Clock }`. Consumed by Tasks 6-8.

- [ ] **Step 1: Write the failing tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class ResourceGraphTests
{
    [Fact]
    public void GivenNewGraph_WhenCreated_ThenAllResourcesIsEmpty()
    {
        var graph = new ResourceGraph();

        graph.AllResources.ShouldBeEmpty();
    }

    [Fact]
    public void GivenScenarioContext_WhenAddingScenario_ThenAllOfItsResourcesAppear()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var context = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var graph = new ResourceGraph();

        graph.AddScenario(context);

        graph.AllResources.Count.ShouldBe(context.AllResources.Count);
        graph.AllResources.ShouldContain(context.Patient);
    }

    [Fact]
    public void GivenTwoScenarios_WhenBothAdded_ThenResourcesFromBothAppear()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var first = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var second = new ScenarioBuilder(schemaProvider).WithPatient().Build();
        var graph = new ResourceGraph();

        graph.AddScenario(first);
        graph.AddScenario(second);

        graph.AllResources.Count.ShouldBe(first.AllResources.Count + second.AllResources.Count);
    }

    [Fact]
    public void GivenNullScenario_WhenAdding_ThenThrowsArgumentNullException()
    {
        var graph = new ResourceGraph();

        Should.Throw<ArgumentNullException>(() => graph.AddScenario(null!));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ResourceGraphTests"`
Expected: FAIL — `Ignixa.FhirFakes.Workflow` namespace / `ResourceGraph` type do not exist.

- [ ] **Step 3: Create `ResourceGraph`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Aggregates resources from one or more patient-centric <see cref="ScenarioContext"/>s, plus
/// non-patient workflow resources (appointments, lists, locations), into a single cross-patient
/// graph. Keeps <see cref="ScenarioBuilder"/>'s one-scenario-one-patient boundary intact: a
/// multi-patient workflow composes several <see cref="ScenarioContext"/>s into one graph rather than
/// growing <see cref="ScenarioContext"/> itself.
/// </summary>
public sealed class ResourceGraph
{
    private readonly List<ResourceJsonNode> _resources = [];

    /// <summary>Gets all resources currently in the graph, in the order they were added.</summary>
    public IReadOnlyList<ResourceJsonNode> AllResources => _resources;

    /// <summary>Adds every resource from a patient-centric scenario to the graph.</summary>
    public void AddScenario(ScenarioContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _resources.AddRange(context.AllResources);
    }

    /// <summary>Adds a single non-patient workflow resource (e.g. an Appointment) to the graph.</summary>
    public void AddResource(ResourceJsonNode resource)
    {
        ArgumentNullException.ThrowIfNull(resource);
        _resources.Add(resource);
    }
}
```

- [ ] **Step 4: Create `IResourceGraphAugmentor` and `ResourceGraphAugmentationContext`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Adds workflow resources (appointments, lists, document references, topology) to an existing
/// resource graph. Implementations should be stateless with respect to execution: all per-run state
/// lives on <see cref="ResourceGraph"/> or <see cref="ResourceGraphAugmentationContext"/> rather than
/// mutable instance fields, so a single configured instance is safe to reuse.
/// </summary>
public interface IResourceGraphAugmentor
{
    /// <summary>Mutates <paramref name="graph"/> in place, adding this augmentor's resources.</summary>
    void Augment(ResourceGraph graph, ResourceGraphAugmentationContext context);
}
```

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Carries the per-run dependencies an <see cref="IResourceGraphAugmentor"/> needs: the schema
/// provider, a faker for any new resources it creates, and the clock backing deterministic timestamps.
/// </summary>
public sealed class ResourceGraphAugmentationContext
{
    /// <summary>The FHIR schema provider for the target FHIR version.</summary>
    public required IFhirSchemaProvider SchemaProvider { get; init; }

    /// <summary>The faker shared across this generation run.</summary>
    public required SchemaBasedFhirResourceFaker Faker { get; init; }

    /// <summary>The clock backing generated timestamps.</summary>
    public required TimeProvider Clock { get; init; }
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ResourceGraphTests"`
Expected: PASS (4 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/ResourceGraph.cs src/Core/Ignixa.FhirFakes/Workflow/IResourceGraphAugmentor.cs src/Core/Ignixa.FhirFakes/Workflow/ResourceGraphAugmentationContext.cs test/Ignixa.FhirFakes.Tests/Workflow/ResourceGraphTests.cs
git commit -m "feat: add ResourceGraph and IResourceGraphAugmentor contracts"
```

---

## Task 4: `WorkflowScenarioOptions`, `WorkflowScenarioResult`, `WorkflowManifest`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioOptions.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/WorkflowManifest.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioResult.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/WorkflowScenarioOptionsTests.cs`

**Interfaces:**
- Consumes: nothing new (plain data types).
- Produces: `WorkflowScenarioOptions { Seed, Clock, Tag }` (sealed record); `WorkflowManifest { ScenarioId, Seed, PrimaryResourceType, ResourceCountsByType }` (required init properties); `WorkflowScenarioResult { Graph, Manifest }`. Consumed by Tasks 5, 6, 8, 9.

- [ ] **Step 1: Write the failing test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Workflow;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class WorkflowScenarioOptionsTests
{
    [Fact]
    public void GivenDefaultOptions_WhenCreated_ThenSeedIsNullAndClockIsSystem()
    {
        var options = new WorkflowScenarioOptions();

        options.Seed.ShouldBeNull();
        options.Clock.ShouldBe(TimeProvider.System);
        options.Tag.ShouldBeNull();
    }

    [Fact]
    public void GivenTwoOptionsWithSameValues_WhenComparing_ThenTheyAreEqual()
    {
        var first = new WorkflowScenarioOptions { Seed = 5, Tag = "test" };
        var second = new WorkflowScenarioOptions { Seed = 5, Tag = "test" };

        first.ShouldBe(second);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~WorkflowScenarioOptionsTests"`
Expected: FAIL — `WorkflowScenarioOptions` type does not exist.

- [ ] **Step 3: Create the three types**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Cross-cutting options for a workflow scenario pack: seed, clock, and tag. Pack-specific knobs
/// (e.g. appointment count) stay as factory-method parameters so they surface through
/// <see cref="Scenarios.DiscoveredScenarioParameter"/> discovery metadata instead of being buried here.
/// </summary>
public sealed record WorkflowScenarioOptions
{
    /// <summary>Seed for reproducible generation. Null means unseeded.</summary>
    public int? Seed { get; init; }

    /// <summary>The clock backing generated timestamps. Defaults to <see cref="TimeProvider.System"/>.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    /// <summary>Tag code applied to generated resources, for test isolation via the <c>_tag</c> search parameter.</summary>
    public string? Tag { get; init; }
}
```

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Metadata describing one workflow scenario generation run. Lets a caller (CLI, test) confirm what
/// was generated, and tells a generic composer what the pack's primary matched resource type is,
/// without the composer needing scenario-specific knowledge.
/// </summary>
public sealed class WorkflowManifest
{
    /// <summary>The invoked scenario id (e.g. "DailyAppointmentSchedule").</summary>
    public required string ScenarioId { get; init; }

    /// <summary>The seed used for this run, or null if unseeded.</summary>
    public int? Seed { get; init; }

    /// <summary>The FHIR resource type this pack's search response should treat as the primary match (e.g. "Appointment").</summary>
    public required string PrimaryResourceType { get; init; }

    /// <summary>Resource counts by FHIR resource type (e.g. "Patient" -> 12).</summary>
    public required IReadOnlyDictionary<string, int> ResourceCountsByType { get; init; }
}
```

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// The output of invoking a workflow scenario pack: the assembled resource graph and its manifest.
/// Response-shaping (bundles, paging, includes) is a separate step via <see cref="ISearchResponseComposer"/>
/// — packs are responsible for graph assembly only.
/// </summary>
public sealed class WorkflowScenarioResult
{
    /// <summary>The assembled, cross-patient resource graph.</summary>
    public required ResourceGraph Graph { get; init; }

    /// <summary>Manifest metadata describing this generation run.</summary>
    public required WorkflowManifest Manifest { get; init; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~WorkflowScenarioOptionsTests"`
Expected: PASS (2 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioOptions.cs src/Core/Ignixa.FhirFakes/Workflow/WorkflowManifest.cs src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioResult.cs test/Ignixa.FhirFakes.Tests/Workflow/WorkflowScenarioOptionsTests.cs
git commit -m "feat: add WorkflowScenarioOptions, WorkflowManifest, WorkflowScenarioResult"
```

---

## Task 5: `WorkflowScenarioCatalog`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioCatalog.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/WorkflowScenarioCatalogTests.cs`

**Interfaces:**
- Consumes: `ScenarioParameterBinder.BuildArguments`/`BuildParameter`/`Humanize` (Task 1); `Scenarios.DiscoveredScenario`/`DiscoveredScenarioParameter`/`ScenarioAttribute`/`ScenarioParameterAttribute`/`ScenarioInvocationException` (existing); `WorkflowScenarioOptions`/`WorkflowScenarioResult` (Task 4).
- Produces: `WorkflowScenarioCatalog.GetAll() -> IReadOnlyList<DiscoveredScenario>`, `.Find(string) -> DiscoveredScenario?`, `.Invoke(DiscoveredScenario, IFhirSchemaProvider, WorkflowScenarioOptions, IReadOnlyDictionary<string, object?>?) -> WorkflowScenarioResult`. Consumed by Task 9 (CLI).

This task references `DailyAppointmentScheduleScenario` (Task 8) only via `typeof(...)` for assembly discovery — write this task's `Discover()` now, but it will find zero scenarios until Task 8 adds the predefined type. The test in this task uses a private test-local method (same pattern `ScenarioCatalogTests.cs` already uses for `RequiredParamScenario`) so it doesn't depend on Task 8.

- [ ] **Step 1: Write the failing tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class WorkflowScenarioCatalogTests
{
    [Fact]
    public void GivenCatalog_WhenGettingAll_ThenIncludesDailyAppointmentSchedule()
    {
        var ids = WorkflowScenarioCatalog.GetAll().Select(s => s.Id).ToList();

        ids.ShouldContain("DailyAppointmentSchedule");
    }

    [Fact]
    public void GivenUnknownId_WhenFinding_ThenReturnsNull()
    {
        WorkflowScenarioCatalog.Find("NotAWorkflow").ShouldBeNull();
    }

    [Fact]
    public void GivenValidPack_WhenInvoking_ThenPassesOptionsThrough()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(WorkflowScenarioCatalogTests).GetMethod(
            nameof(EchoSeedPack), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "EchoSeedPack",
            Title = "EchoSeedPack",
            Parameters = [],
            Method = method,
        };
        var options = new WorkflowScenarioOptions { Seed = 7 };

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, options);

        result.Manifest.Seed.ShouldBe(7);
    }

    [Fact]
    public void GivenPackThatThrows_WhenInvoking_ThenWrapsInScenarioInvocationException()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(WorkflowScenarioCatalogTests).GetMethod(
            nameof(ThrowingPack), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "ThrowingPack",
            Title = "ThrowingPack",
            Parameters = [],
            Method = method,
        };

        var exception = Should.Throw<ScenarioInvocationException>(
            () => WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions()));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    private static WorkflowScenarioResult EchoSeedPack(IFhirSchemaProvider schemaProvider, WorkflowScenarioOptions options) =>
        new()
        {
            Graph = new ResourceGraph(),
            Manifest = new WorkflowManifest
            {
                ScenarioId = "EchoSeedPack",
                Seed = options.Seed,
                PrimaryResourceType = "Basic",
                ResourceCountsByType = new Dictionary<string, int>(),
            },
        };

    private static WorkflowScenarioResult ThrowingPack(IFhirSchemaProvider schemaProvider, WorkflowScenarioOptions options) =>
        throw new InvalidOperationException("boom");
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~WorkflowScenarioCatalogTests"`
Expected: FAIL — `WorkflowScenarioCatalog` type does not exist. (The first test will also fail post-implementation until Task 8 exists — that's expected and re-verified at the end of Task 8.)

- [ ] **Step 3: Create `WorkflowScenarioCatalog`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow.Predefined;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Discovers and invokes predefined workflow scenario packs by convention: public static methods on
/// types in the <c>Ignixa.FhirFakes.Workflow.Predefined</c> namespace whose first two parameters are
/// <see cref="IFhirSchemaProvider"/> and <see cref="WorkflowScenarioOptions"/> and that return
/// <see cref="WorkflowScenarioResult"/>. Sibling of <see cref="ScenarioCatalog"/> rather than a
/// generalization of it — the two return different result types. Scans only its own assembly for
/// now; external-assembly registration is not implemented in this catalog yet (no second consumer
/// exists to justify the extra surface — see the investigation doc's Phase 5).
/// </summary>
public static class WorkflowScenarioCatalog
{
    private static readonly Lazy<IReadOnlyList<DiscoveredScenario>> Scenarios = new(Discover);

    /// <summary>Gets all discovered workflow scenario packs.</summary>
    public static IReadOnlyList<DiscoveredScenario> GetAll() => Scenarios.Value;

    /// <summary>Finds a workflow scenario pack by id (case-insensitive), or null if none matches.</summary>
    public static DiscoveredScenario? Find(string id) =>
        Scenarios.Value.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Invokes a discovered workflow scenario pack's factory method, applying
    /// <paramref name="parameterOverrides"/> over the method's own defaults.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">The pack's factory method threw during invocation.</exception>
    public static WorkflowScenarioResult Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(options);

        var args = ScenarioParameterBinder.BuildArguments(scenario.Id, scenario.Method, parameterOverrides, schemaProvider, options);

        try
        {
            return (WorkflowScenarioResult)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Workflow scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }
    }

    private static IReadOnlyList<DiscoveredScenario> Discover()
    {
        var assembly = typeof(DailyAppointmentScheduleScenario).Assembly;

        var packTypes = assembly.GetTypes()
            .Where(t => t.Namespace == "Ignixa.FhirFakes.Workflow.Predefined" && t.IsClass && t.IsPublic);

        var scenarios = new List<DiscoveredScenario>();

        foreach (var type in packTypes)
        {
            var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.ReturnType == typeof(WorkflowScenarioResult));

            foreach (var method in methods)
            {
                var parameters = method.GetParameters();
                if (parameters.Length < 2
                    || parameters[0].ParameterType != typeof(IFhirSchemaProvider)
                    || parameters[1].ParameterType != typeof(WorkflowScenarioOptions))
                {
                    continue;
                }

                var attribute = method.GetCustomAttribute<ScenarioAttribute>();
                var id = attribute?.Id
                    ?? (method.Name.StartsWith("Get", StringComparison.Ordinal) ? method.Name["Get".Length..] : method.Name);

                scenarios.Add(new DiscoveredScenario
                {
                    Id = id,
                    Category = attribute?.Category,
                    Title = attribute?.Title ?? ScenarioParameterBinder.Humanize(id),
                    Description = attribute?.Description,
                    Parameters = parameters.Skip(2).Select(ScenarioParameterBinder.BuildParameter).ToList(),
                    Domain = attribute is null || attribute.Domain == ClinicalDomain.Unspecified ? null : attribute.Domain,
                    Method = method,
                });
            }
        }

        return scenarios;
    }
}
```

Note: `typeof(DailyAppointmentScheduleScenario)` will not compile until Task 8 adds that type. This is expected — Tasks 5-7 build the pipeline that Task 8's pack plugs into, and the plan's own dependency order means Task 5 alone won't compile in isolation. Do not skip ahead: implement Tasks 5, 6, 7 fully (they compile fine among themselves except for this one forward reference), then Task 8 makes the whole assembly compile and both the `WorkflowScenarioCatalogTests.GivenCatalog_WhenGettingAll_ThenIncludesDailyAppointmentSchedule` test and the rest of the suite pass together. Re-run the full `Workflow` test folder at the end of Task 8, not just after this task.

- [ ] **Step 4: Commit (with the forward reference noted above; full green run happens at the end of Task 8)**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/WorkflowScenarioCatalog.cs test/Ignixa.FhirFakes.Tests/Workflow/WorkflowScenarioCatalogTests.cs
git commit -m "feat: add WorkflowScenarioCatalog discovery/invoke"
```

---

## Task 6: `SearchResponseOptions` and `SearchsetBundleComposer`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/ResponseBundleType.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/IncludeCompleteness.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/SearchResponseOptions.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/ISearchResponseComposer.cs`
- Create: `src/Core/Ignixa.FhirFakes/Workflow/SearchsetBundleComposer.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/SearchsetBundleComposerTests.cs`

**Interfaces:**
- Consumes: `ResourceGraph` (Task 3); `BundleJsonNode` (`Ignixa.Serialization.Models`, existing).
- Produces: `ISearchResponseComposer.Compose(ResourceGraph, SearchResponseOptions) -> IReadOnlyList<BundleJsonNode>`; `SearchsetBundleComposer : ISearchResponseComposer`. Consumed by Task 9 (CLI).

- [ ] **Step 1: Write the failing tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class SearchsetBundleComposerTests
{
    [Fact]
    public void GivenEmptyGraph_WhenComposing_ThenReturnsExactlyOneEmptyPage()
    {
        var graph = new ResourceGraph();
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Appointment", MatchResourceType = "Appointment" });

        pages.Count.ShouldBe(1);
        pages[0].Type.ShouldBe(Ignixa.Serialization.Models.BundleJsonNode.BundleType.Searchset);
        pages[0].Entry.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenMoreMatchesThanPageSize_WhenComposing_ThenSplitsIntoMultiplePagesWithLinks()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        for (var i = 0; i < 5; i++)
        {
            graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().Build());
        }
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Patient", MatchResourceType = "Patient", PageSize = 2 });

        pages.Count.ShouldBe(3);
        pages[0].Total.ShouldBe(5);
        pages[0].Link.Any(l => l.Relation == "next").ShouldBeTrue();
        pages[0].Link.Any(l => l.Relation == "previous").ShouldBeFalse();
        pages[2].Link.Any(l => l.Relation == "next").ShouldBeFalse();
        pages[2].Link.Any(l => l.Relation == "previous").ShouldBeTrue();
    }

    [Fact]
    public void GivenIncludeCompletenessMissing_WhenComposing_ThenNonMatchResourcesAreOmitted()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().AddState(EncounterState.Ambulatory()).Build());
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions
        {
            SearchUrl = "/Encounter",
            MatchResourceType = "Encounter",
            IncludeCompleteness = IncludeCompleteness.Missing,
        });

        pages[0].Entry.Count.ShouldBe(1);
        pages[0].Entry[0].Resource!["resourceType"]!.ToString().ShouldBe("Encounter");
    }
}
```

Note: `EncounterState` is `Ignixa.FhirFakes.Scenarios.States` — add `using Ignixa.FhirFakes.Scenarios.States;` to this test file's usings.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~SearchsetBundleComposerTests"`
Expected: FAIL — `SearchsetBundleComposer`/`SearchResponseOptions`/`IncludeCompleteness` types do not exist.

- [ ] **Step 3: Create the enums and options record**

```csharp
namespace Ignixa.FhirFakes.Workflow;

/// <summary>FHIR Bundle.type values a search response composer can emit.</summary>
public enum ResponseBundleType
{
    Searchset,
    BatchResponse,
    TransactionResponse,
}
```

```csharp
namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// How completely a composed bundle includes resources referenced by its primary matches.
/// <see cref="Complete"/> includes every non-matching resource in the graph once; <see cref="Missing"/>
/// omits them, so a consumer sees a reference it cannot resolve from the bundle alone.
/// </summary>
public enum IncludeCompleteness
{
    Complete,
    Missing,
}
```

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Workflow;

/// <summary>Options controlling how <see cref="ISearchResponseComposer"/> shapes a graph into response bundles.</summary>
public sealed record SearchResponseOptions
{
    /// <summary>The search URL bundles are a response to (used for <c>Bundle.link</c> <c>self</c>/<c>next</c>/<c>previous</c>).</summary>
    public required string SearchUrl { get; init; }

    /// <summary>The primary resource type this search matched (e.g. "Appointment"). Other types in the graph are includes.</summary>
    public required string MatchResourceType { get; init; }

    /// <summary>The bundle type to emit. Defaults to <see cref="ResponseBundleType.Searchset"/>.</summary>
    public ResponseBundleType BundleType { get; init; } = ResponseBundleType.Searchset;

    /// <summary>Maximum matching entries per page. Defaults to 20.</summary>
    public int PageSize { get; init; } = 20;

    /// <summary>Whether included (non-matching) resources are present or omitted. Defaults to <see cref="IncludeCompleteness.Complete"/>.</summary>
    public IncludeCompleteness IncludeCompleteness { get; init; } = IncludeCompleteness.Complete;
}
```

- [ ] **Step 4: Create `ISearchResponseComposer` and `SearchsetBundleComposer`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Emits a resource graph as one or more FHIR search response bundles. Owns response-level shaping
/// (bundle type, paging, include completeness) so packs stay focused on graph assembly.
/// </summary>
public interface ISearchResponseComposer
{
    /// <summary>
    /// Composes <paramref name="graph"/> into one bundle per page. A graph with no matching entries
    /// still returns exactly one (empty) page, never an empty list.
    /// </summary>
    IReadOnlyList<BundleJsonNode> Compose(ResourceGraph graph, SearchResponseOptions options);
}
```

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow;

/// <summary>
/// Default <see cref="ISearchResponseComposer"/>: splits a graph's <see cref="SearchResponseOptions.MatchResourceType"/>
/// entries into pages, attaches non-matching resources as includes per <see cref="IncludeCompleteness"/>,
/// and emits <c>self</c>/<c>next</c>/<c>previous</c> links using a <c>_page</c> query-string convention.
/// </summary>
public sealed class SearchsetBundleComposer : ISearchResponseComposer
{
    public IReadOnlyList<BundleJsonNode> Compose(ResourceGraph graph, SearchResponseOptions options)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(options);

        var matches = graph.AllResources.Where(r => r.ResourceType == options.MatchResourceType).ToList();
        var includes = graph.AllResources.Where(r => r.ResourceType != options.MatchResourceType).ToList();

        var pages = matches.Chunk(options.PageSize).ToList();
        if (pages.Count == 0)
        {
            pages = [[]];
        }

        var bundles = new List<BundleJsonNode>(pages.Count);
        for (var pageIndex = 0; pageIndex < pages.Count; pageIndex++)
        {
            bundles.Add(ComposePage(pages[pageIndex], includes, matches.Count, pageIndex, pages.Count, options));
        }

        return bundles;
    }

    private static BundleJsonNode ComposePage(
        ResourceJsonNode[] pageMatches,
        IReadOnlyList<ResourceJsonNode> includes,
        int totalMatches,
        int pageIndex,
        int pageCount,
        SearchResponseOptions options)
    {
        var entries = new JsonArray();
        foreach (var match in pageMatches)
        {
            entries.Add(CreateEntry(match, searchMode: "match"));
        }

        if (options.IncludeCompleteness == IncludeCompleteness.Complete)
        {
            foreach (var include in includes)
            {
                entries.Add(CreateEntry(include, searchMode: "include"));
            }
        }

        var links = new JsonArray { CreateLink("self", PageUrl(options.SearchUrl, pageIndex)) };
        if (pageIndex > 0)
        {
            links.Add(CreateLink("previous", PageUrl(options.SearchUrl, pageIndex - 1)));
        }
        if (pageIndex < pageCount - 1)
        {
            links.Add(CreateLink("next", PageUrl(options.SearchUrl, pageIndex + 1)));
        }

        var bundleNode = new JsonObject
        {
            ["resourceType"] = "Bundle",
            ["id"] = Guid.NewGuid().ToString(),
            ["type"] = GetBundleTypeLiteral(options.BundleType),
            ["total"] = totalMatches,
            ["link"] = links,
            ["entry"] = entries,
        };

        return new BundleJsonNode(bundleNode);
    }

    private static JsonObject CreateEntry(ResourceJsonNode resource, string searchMode) => new()
    {
        ["fullUrl"] = $"{resource.ResourceType}/{resource.Id}",
        ["resource"] = resource.MutableNode.DeepClone(),
        ["search"] = new JsonObject { ["mode"] = searchMode },
    };

    private static JsonObject CreateLink(string relation, string url) => new()
    {
        ["relation"] = relation,
        ["url"] = url,
    };

    private static string PageUrl(string searchUrl, int pageIndex) =>
        pageIndex == 0 ? searchUrl : $"{searchUrl}&_page={pageIndex}";

    private static string GetBundleTypeLiteral(ResponseBundleType type) => type switch
    {
        ResponseBundleType.Searchset => "searchset",
        ResponseBundleType.BatchResponse => "batch-response",
        ResponseBundleType.TransactionResponse => "transaction-response",
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~SearchsetBundleComposerTests"`
Expected: PASS (3 tests)

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/ResponseBundleType.cs src/Core/Ignixa.FhirFakes/Workflow/IncludeCompleteness.cs src/Core/Ignixa.FhirFakes/Workflow/SearchResponseOptions.cs src/Core/Ignixa.FhirFakes/Workflow/ISearchResponseComposer.cs src/Core/Ignixa.FhirFakes/Workflow/SearchsetBundleComposer.cs test/Ignixa.FhirFakes.Tests/Workflow/SearchsetBundleComposerTests.cs
git commit -m "feat: add SearchsetBundleComposer with paging and include completeness"
```

---

## Task 7: `AppointmentSchedulingAugmentor`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/Augmentors/AppointmentSchedulingAugmentor.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/AppointmentSchedulingAugmentorTests.cs`

**Interfaces:**
- Consumes: `IResourceGraphAugmentor`, `ResourceGraph`, `ResourceGraphAugmentationContext` (Task 3).
- Produces: `AppointmentSchedulingAugmentor(IReadOnlyList<ResourceJsonNode> practitioners, IReadOnlyList<(ResourceJsonNode Patient, ResourceJsonNode Encounter)> appointmentSubjects, DateTimeOffset scheduleDate) : IResourceGraphAugmentor`. Consumed by Task 8.

- [ ] **Step 1: Write the failing tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow;
using Ignixa.FhirFakes.Workflow.Augmentors;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class AppointmentSchedulingAugmentorTests
{
    [Fact]
    public void GivenPractitionersAndSubjects_WhenAugmenting_ThenAppointmentsLinkPatientAndPractitioner()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var practitioner = practitionerContext.CurrentPractitioner!;

        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var augmentor = new AppointmentSchedulingAugmentor(
            [practitioner],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        augmentor.Augment(graph, new ResourceGraphAugmentationContext
        {
            SchemaProvider = schemaProvider,
            Faker = faker,
            Clock = TimeProvider.System,
        });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        var participants = appointment.MutableNode["participant"]!.AsArray();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Patient/{patientContext.Patient!.Id}").ShouldBeTrue();
        participants.Any(p => p!["actor"]!["reference"]!.ToString() == $"Practitioner/{practitioner.Id}").ShouldBeTrue();
    }

    [Fact]
    public void GivenAppointmentCreated_WhenAugmenting_ThenEncounterBackReferencesAppointment()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed: 1);
        var practitionerContext = new ScenarioContext();
        PractitionerState.FamilyPractitioner().Execute(practitionerContext, faker);
        var patientContext = new ScenarioBuilder(schemaProvider, 2).WithPatient().AddState(EncounterState.Ambulatory()).Build();
        var graph = new ResourceGraph();
        graph.AddScenario(practitionerContext);
        graph.AddScenario(patientContext);

        var augmentor = new AppointmentSchedulingAugmentor(
            [practitionerContext.CurrentPractitioner!],
            [(patientContext.Patient!, patientContext.CurrentEncounter!)],
            new DateTimeOffset(2026, 7, 4, 9, 0, 0, TimeSpan.Zero));

        augmentor.Augment(graph, new ResourceGraphAugmentationContext { SchemaProvider = schemaProvider, Faker = faker, Clock = TimeProvider.System });

        var appointment = graph.AllResources.Single(r => r.ResourceType == "Appointment");
        patientContext.CurrentEncounter!.MutableNode["appointment"]!["reference"]!.ToString().ShouldBe($"Appointment/{appointment.Id}");
    }

    [Fact]
    public void GivenNoPractitioners_WhenConstructing_ThenThrowsArgumentException()
    {
        Should.Throw<ArgumentException>(() =>
            new AppointmentSchedulingAugmentor([], [], DateTimeOffset.UtcNow));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~AppointmentSchedulingAugmentorTests"`
Expected: FAIL — `Ignixa.FhirFakes.Workflow.Augmentors` namespace / `AppointmentSchedulingAugmentor` type do not exist.

- [ ] **Step 3: Create `AppointmentSchedulingAugmentor`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow.Augmentors;

/// <summary>
/// Adds Appointment resources linking each (Patient, Encounter) subject to a rotating practitioner,
/// and back-references the appointment from Encounter.appointment. Configuration is fixed at
/// construction and <see cref="Augment"/> mutates no instance state, so one configured instance is
/// safe to reuse across calls.
/// </summary>
public sealed class AppointmentSchedulingAugmentor(
    IReadOnlyList<ResourceJsonNode> practitioners,
    IReadOnlyList<(ResourceJsonNode Patient, ResourceJsonNode Encounter)> appointmentSubjects,
    DateTimeOffset scheduleDate) : IResourceGraphAugmentor
{
    private const int SlotMinutes = 30;

    private static readonly string[] StatusRotation = ["booked", "booked", "booked", "fulfilled", "cancelled", "noshow"];

    private readonly IReadOnlyList<ResourceJsonNode> _practitioners = practitioners switch
    {
        null => throw new ArgumentNullException(nameof(practitioners)),
        { Count: 0 } => throw new ArgumentException("At least one practitioner is required.", nameof(practitioners)),
        _ => practitioners,
    };

    private readonly IReadOnlyList<(ResourceJsonNode Patient, ResourceJsonNode Encounter)> _appointmentSubjects =
        appointmentSubjects ?? throw new ArgumentNullException(nameof(appointmentSubjects));

    public void Augment(ResourceGraph graph, ResourceGraphAugmentationContext context)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(context);

        for (var i = 0; i < _appointmentSubjects.Count; i++)
        {
            var (patient, encounter) = _appointmentSubjects[i];
            var practitioner = _practitioners[i % _practitioners.Count];
            var status = StatusRotation[i % StatusRotation.Length];
            var start = scheduleDate.AddMinutes(i * SlotMinutes);
            var end = start.AddMinutes(SlotMinutes);

            var appointment = context.Faker.Generate("Appointment");
            var node = appointment.MutableNode;
            node["id"] = Guid.NewGuid().ToString();
            node["status"] = status;
            node["start"] = start.UtcDateTime.ToString("o");
            node["end"] = end.UtcDateTime.ToString("o");
            node["participant"] = new JsonArray
            {
                new JsonObject
                {
                    ["actor"] = new JsonObject { ["reference"] = $"Patient/{patient.Id}" },
                    ["status"] = "accepted",
                },
                new JsonObject
                {
                    ["actor"] = new JsonObject { ["reference"] = $"Practitioner/{practitioner.Id}" },
                    ["status"] = "accepted",
                },
            };

            graph.AddResource(appointment);

            var appointmentReference = new JsonObject { ["reference"] = $"Appointment/{appointment.Id}" };
            encounter.MutableNode["appointment"] = context.SchemaProvider.Version >= FhirVersion.R5
                ? new JsonArray { appointmentReference }
                : appointmentReference;
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~AppointmentSchedulingAugmentorTests"`
Expected: PASS (3 tests)

- [ ] **Step 5: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/Augmentors/AppointmentSchedulingAugmentor.cs test/Ignixa.FhirFakes.Tests/Workflow/AppointmentSchedulingAugmentorTests.cs
git commit -m "feat: add AppointmentSchedulingAugmentor"
```

---

## Task 8: `DailyAppointmentScheduleScenario` (the pack)

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Workflow/Predefined/DailyAppointmentScheduleScenario.cs`
- Test: Create `test/Ignixa.FhirFakes.Tests/Workflow/DailyAppointmentScheduleScenarioTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-7.
- Produces: `DailyAppointmentScheduleScenario.GetDailyAppointmentSchedule(IFhirSchemaProvider, WorkflowScenarioOptions, int practitionerCount = 1, int appointmentCount = 12) -> WorkflowScenarioResult`, discoverable as `"DailyAppointmentSchedule"`. Consumed by Task 9 (CLI) and closes the forward reference from Task 5.

- [ ] **Step 1: Write the failing tests**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class DailyAppointmentScheduleScenarioTests
{
    [Fact]
    public void GivenDefaults_WhenInvokedViaCatalog_ThenProducesOneAppointmentPerDefaultCount()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 10 });

        result.Manifest.ResourceCountsByType["Appointment"].ShouldBe(12);
        result.Manifest.ResourceCountsByType["Patient"].ShouldBe(12);
        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(1);
        result.Manifest.PrimaryResourceType.ShouldBe("Appointment");
    }

    [Fact]
    public void GivenParameterOverrides_WhenInvoked_ThenCountsMatchOverrides()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(
            scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 10 },
            new Dictionary<string, object?> { ["practitionerCount"] = 2, ["appointmentCount"] = 4 });

        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(2);
        result.Manifest.ResourceCountsByType["Appointment"].ShouldBe(4);
    }

    [Fact]
    public void GivenAppointmentCountZero_WhenInvoked_ThenGraphStillHasPractitionerOnly()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var result = WorkflowScenarioCatalog.Invoke(
            scenario, schemaProvider, new WorkflowScenarioOptions(),
            new Dictionary<string, object?> { ["appointmentCount"] = 0 });

        result.Manifest.ResourceCountsByType.ContainsKey("Appointment").ShouldBeFalse();
        result.Manifest.ResourceCountsByType["Practitioner"].ShouldBe(1);
    }

    [Fact]
    public void GivenSameSeed_WhenInvokedTwice_ThenAppointmentStatusRotationMatches()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var first = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 99 });
        var second = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 99 });

        var firstStatuses = first.Graph.AllResources.Where(r => r.ResourceType == "Appointment").Select(r => r.MutableNode["status"]!.ToString()).ToList();
        var secondStatuses = second.Graph.AllResources.Where(r => r.ResourceType == "Appointment").Select(r => r.MutableNode["status"]!.ToString()).ToList();
        firstStatuses.ShouldBe(secondStatuses);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~DailyAppointmentScheduleScenarioTests"`
Expected: FAIL to compile — `WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")` returns null and/or `DailyAppointmentScheduleScenario` type does not exist, and Task 5's own forward reference means the whole `Ignixa.FhirFakes` project doesn't build yet.

- [ ] **Step 3: Create `DailyAppointmentScheduleScenario`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow.Augmentors;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.FhirFakes.Workflow.Predefined;

/// <summary>
/// Built-in workflow scenario pack: a practitioner's daily appointment schedule, with each
/// appointment linking a Patient, Practitioner, and Encounter through a search-response-ready graph.
/// </summary>
public static class DailyAppointmentScheduleScenario
{
    private static readonly Func<PractitionerState>[] PractitionerRoster =
    [
        PractitionerState.FamilyPractitioner,
        PractitionerState.Internist,
        PractitionerState.Pediatrician,
    ];

    [Scenario(
        Id = "DailyAppointmentSchedule",
        Category = "Schedule",
        Description = "Practitioner day schedule with appointments linking patient, practitioner, and encounter context")]
    public static WorkflowScenarioResult GetDailyAppointmentSchedule(
        IFhirSchemaProvider schemaProvider,
        WorkflowScenarioOptions options,
        [ScenarioParameter(Min = 1, Max = 10, Description = "Number of practitioners on the schedule")] int practitionerCount = 1,
        [ScenarioParameter(Min = 0, Max = 50, Description = "Number of appointments across all practitioners")] int appointmentCount = 12)
    {
        ArgumentNullException.ThrowIfNull(schemaProvider);
        ArgumentNullException.ThrowIfNull(options);

        var faker = options.Seed is int seed
            ? new SchemaBasedFhirResourceFaker(schemaProvider, seed)
            : new SchemaBasedFhirResourceFaker(schemaProvider);
        if (options.Tag is not null)
        {
            faker.WithTag(options.Tag);
        }

        var graph = new ResourceGraph();

        var practitioners = new List<ResourceJsonNode>(practitionerCount);
        for (var i = 0; i < practitionerCount; i++)
        {
            var carrier = new ScenarioContext();
            PractitionerRoster[i % PractitionerRoster.Length]().Execute(carrier, faker);
            graph.AddScenario(carrier);
            practitioners.Add(carrier.CurrentPractitioner!);
        }

        var appointmentSubjects = new List<(ResourceJsonNode Patient, ResourceJsonNode Encounter)>(appointmentCount);
        for (var i = 0; i < appointmentCount; i++)
        {
            var patientScenario = options.Seed is int baseSeed
                ? new ScenarioBuilder(schemaProvider, baseSeed + i + 1)
                : new ScenarioBuilder(schemaProvider);

            var context = patientScenario
                .WithPatient()
                .AddState(EncounterState.Ambulatory("Scheduled visit"))
                .Build();

            graph.AddScenario(context);
            appointmentSubjects.Add((context.Patient!, context.CurrentEncounter!));
        }

        if (appointmentSubjects.Count > 0)
        {
            var scheduleDate = new DateTimeOffset(options.Clock.GetUtcNow().UtcDateTime.Date, TimeSpan.Zero);
            var augmentor = new AppointmentSchedulingAugmentor(practitioners, appointmentSubjects, scheduleDate);
            augmentor.Augment(graph, new ResourceGraphAugmentationContext
            {
                SchemaProvider = schemaProvider,
                Faker = faker,
                Clock = options.Clock,
            });
        }

        var resourceCounts = graph.AllResources
            .GroupBy(r => r.ResourceType)
            .ToDictionary(g => g.Key, g => g.Count());

        return new WorkflowScenarioResult
        {
            Graph = graph,
            Manifest = new WorkflowManifest
            {
                ScenarioId = "DailyAppointmentSchedule",
                Seed = options.Seed,
                PrimaryResourceType = "Appointment",
                ResourceCountsByType = resourceCounts,
            },
        };
    }
}
```

- [ ] **Step 4: Run the full Workflow test suite (this closes Task 5's forward reference too)**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~Ignixa.FhirFakes.Tests.Workflow"`
Expected: PASS — all tests across `ResourceGraphTests`, `WorkflowScenarioOptionsTests`, `WorkflowScenarioCatalogTests`, `SearchsetBundleComposerTests`, `AppointmentSchedulingAugmentorTests`, and `DailyAppointmentScheduleScenarioTests` (the `WorkflowScenarioCatalogTests.GivenCatalog_WhenGettingAll_ThenIncludesDailyAppointmentSchedule` test from Task 5 now passes for real).

- [ ] **Step 5: Run the full existing scenario suite to confirm no regression**

Run: `dotnet test test/Ignixa.FhirFakes.Tests/Ignixa.FhirFakes.Tests.csproj`
Expected: PASS, all tests (existing + new).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.FhirFakes/Workflow/Predefined/DailyAppointmentScheduleScenario.cs test/Ignixa.FhirFakes.Tests/Workflow/DailyAppointmentScheduleScenarioTests.cs
git commit -m "feat: add DailyAppointmentSchedule workflow scenario pack"
```

---

## Task 9: CLI `workflow` command

**Files:**
- Create: `tools/Ignixa.FhirFakes.Cli/Commands/WorkflowCommand.cs`
- Modify: `tools/Ignixa.FhirFakes.Cli/Program.cs:69` (`AddFhirVersionCommands`)
- Test: Create `test/Ignixa.FhirFakes.Cli.Tests/WorkflowCommandParameterOverrideTests.cs`

**Interfaces:**
- Consumes: `WorkflowScenarioCatalog` (Task 5), `SearchsetBundleComposer`/`SearchResponseOptions` (Task 6), `ScenarioCommand.TryParseParameterOverrides` (existing, reused as-is — same assembly, `internal`).
- Produces: `ignixa-fakes {version} workflow {scenarioName} --out <dir> [--seed N] [--page-size N] [--validate] [--param name=value]...`

- [ ] **Step 1: Write the failing test**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.FhirFakes.Workflow;
using Shouldly;

namespace Ignixa.FhirFakes.Cli.Tests;

public class WorkflowCommandParameterOverrideTests
{
    [Fact]
    public void GivenValidParamValues_WhenParsing_ThenOverridesContainConvertedValues()
    {
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var success = ScenarioCommand.TryParseParameterOverrides(
            scenario.Id, scenario.Parameters, ["appointmentCount=4", "practitionerCount=2"], out var overrides, out var error);

        success.ShouldBeTrue();
        error.ShouldBeNull();
        overrides["appointmentCount"].ShouldBe(4);
        overrides["practitionerCount"].ShouldBe(2);
    }

    [Fact]
    public void GivenOutOfRangeValue_WhenParsing_ThenFails()
    {
        var scenario = WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")!;

        var success = ScenarioCommand.TryParseParameterOverrides(
            scenario.Id, scenario.Parameters, ["practitionerCount=99"], out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ignixa.FhirFakes.Cli.Tests/Ignixa.FhirFakes.Cli.Tests.csproj --filter "FullyQualifiedName~WorkflowCommandParameterOverrideTests"`
Expected: PASS on the parsing logic itself is impossible to fail (it's exercising already-shipped `ScenarioCommand.TryParseParameterOverrides` against the new catalog) — this step should FAIL only because `WorkflowScenarioCatalog.Find("DailyAppointmentSchedule")` doesn't compile/resolve if Task 8 weren't done yet. Since Task 8 is done, expect this to fail only if the CLI test project doesn't yet reference the core `Ignixa.FhirFakes` types it needs — confirm the CLI test project already references `Ignixa.FhirFakes` (it does, via the existing `ScenarioCommandParameterOverrideTests.cs`). If it fails for a different reason, treat that as a real signal, not expected.

- [ ] **Step 3: Create `WorkflowCommand`**

```csharp
using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR workflow scenario packs (searchset-shaped fixture data).
/// </summary>
internal static class WorkflowCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var workflowCommand = new Command("workflow", "Generate a predefined FHIR workflow scenario pack");

        var scenarioNameArg = new Argument<string>("scenarioName")
        {
            Description = "The workflow scenario pack name (e.g., DailyAppointmentSchedule)"
        };

        var outOption = new Option<string>("--out")
        {
            Description = "Output folder for generated files",
            Required = true
        };

        var seedOption = new Option<int?>("--seed")
        {
            Description = "Seed for reproducible generation"
        };

        var pageSizeOption = new Option<int>("--page-size")
        {
            Description = "Maximum matching entries per composed page",
            DefaultValueFactory = _ => 20
        };

        var validateOption = new Option<bool>("--validate")
        {
            Description = "Validate generated resources against schema", DefaultValueFactory = _ => false
        };

        var paramOption = new Option<string[]>("--param")
        {
            Description = "Override a workflow parameter, format name=value (repeatable, e.g. --param appointmentCount=20)",
            DefaultValueFactory = _ => []
        };

        workflowCommand.Arguments.Add(scenarioNameArg);
        workflowCommand.Options.Add(outOption);
        workflowCommand.Options.Add(seedOption);
        workflowCommand.Options.Add(pageSizeOption);
        workflowCommand.Options.Add(validateOption);
        workflowCommand.Options.Add(paramOption);

        workflowCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var seed = parseResult.GetValue(seedOption);
            var pageSize = parseResult.GetValue(pageSizeOption);
            var validate = parseResult.GetValue(validateOption);
            var paramValues = parseResult.GetValue(paramOption) ?? [];

            await HandleWorkflowCommand(schemaProvider, fhirVersion, scenarioName, outFolder, seed, pageSize, validate, paramValues, cancellationToken);
        });

        return workflowCommand;
    }

    private static async Task HandleWorkflowCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        int? seed,
        int pageSize,
        bool validate,
        string[] paramValues,
        CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(outFolder);

            var scenario = WorkflowScenarioCatalog.Find(scenarioName);
            if (scenario == null)
            {
                Console.WriteLine($"X Unknown workflow scenario: {scenarioName}");
                Console.WriteLine("Available workflow scenarios:");
                foreach (var name in WorkflowScenarioCatalog.GetAll().Select(s => s.Id).OrderBy(s => s))
                {
                    Console.WriteLine($"  - {name}");
                }
                Environment.ExitCode = 2;
                return;
            }

            if (!ScenarioCommand.TryParseParameterOverrides(scenario.Id, scenario.Parameters, paramValues, out var overrides, out var parseError))
            {
                Console.WriteLine($"X {parseError}");
                Environment.ExitCode = 2;
                return;
            }

            var options = new WorkflowScenarioOptions { Seed = seed };

            Ignixa.FhirFakes.Workflow.WorkflowScenarioResult result;
            try
            {
                result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, options, overrides);
            }
            catch (Ignixa.FhirFakes.Scenarios.ScenarioInvocationException ex)
            {
                Console.WriteLine($"X Error: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var composer = new SearchsetBundleComposer();
            var searchUrl = $"/{result.Manifest.PrimaryResourceType}";
            var pages = composer.Compose(result.Graph, new SearchResponseOptions
            {
                SearchUrl = searchUrl,
                MatchResourceType = result.Manifest.PrimaryResourceType,
                PageSize = pageSize,
            });

            var runId = Guid.NewGuid().ToString();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

            for (var i = 0; i < pages.Count; i++)
            {
                var filename = $"{fhirVersion}-workflow-{scenario.Id}-{runId}-page{i}.json";
                var outputPath = Path.Combine(outFolder, filename);
                var json = JsonSerializer.Serialize(pages[i].MutableNode, jsonOptions);
                await File.WriteAllTextAsync(outputPath, json, cancellationToken);
                Console.WriteLine($"Generated workflow page {i + 1}/{pages.Count}: {outputPath}");
            }

            var manifestPath = Path.Combine(outFolder, $"{fhirVersion}-workflow-{scenario.Id}-{runId}-manifest.json");
            var manifestJson = JsonSerializer.Serialize(new
            {
                result.Manifest.ScenarioId,
                result.Manifest.Seed,
                result.Manifest.PrimaryResourceType,
                result.Manifest.ResourceCountsByType,
                PageCount = pages.Count,
            }, jsonOptions);
            await File.WriteAllTextAsync(manifestPath, manifestJson, cancellationToken);
            Console.WriteLine($"Generated manifest: {manifestPath}");

            if (validate)
            {
                Console.WriteLine("\n-------------------------------------------------------------------");
                Console.WriteLine("Validating generated resources...");
                Console.WriteLine("-------------------------------------------------------------------");

                var invalidCount = 0;
                foreach (var resource in result.Graph.AllResources)
                {
                    var resourceType = resource.MutableNode["resourceType"]?.ToString() ?? "Unknown";
                    var resourceId = resource.MutableNode["id"]?.ToString() ?? "unknown";
                    var validationResult = ValidationHelper.ValidateResource(resource.MutableNode, schemaProvider);
                    if (!validationResult.IsValid)
                    {
                        invalidCount++;
                    }
                    Console.WriteLine($"  {resourceType}/{resourceId}: {ValidationHelper.GetSummary(validationResult)}");
                }

                Console.WriteLine(invalidCount > 0
                    ? $"\n  {invalidCount} resource(s) have validation issues"
                    : $"\n  All {result.Graph.AllResources.Count} resource(s) passed validation");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"X Error: {ex.Message}");
            Environment.ExitCode = 1;
        }
    }
}
```

- [ ] **Step 4: Wire `WorkflowCommand` into `Program.cs`**

In `tools/Ignixa.FhirFakes.Cli/Program.cs`, in `AddFhirVersionCommands` (around line 69), add the new subcommand alongside the existing three:

```csharp
    private static void AddFhirVersionCommands(RootCommand root, string versionCode, IFhirSchemaProvider schemaProvider)
    {
        var command = new Command(versionCode, $"Use FHIR {versionCode.ToUpperInvariant()} specification");
        command.Subcommands.Add(ResourceCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(ScenarioCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(PopulationCommand.Create(schemaProvider, versionCode));
        command.Subcommands.Add(WorkflowCommand.Create(schemaProvider, versionCode));
        root.Subcommands.Add(command);
    }
```

- [ ] **Step 5: Run test to verify it passes, then build the CLI**

Run: `dotnet test test/Ignixa.FhirFakes.Cli.Tests/Ignixa.FhirFakes.Cli.Tests.csproj --filter "FullyQualifiedName~WorkflowCommandParameterOverrideTests"`
Expected: PASS (2 tests)

Run: `dotnet build tools/Ignixa.FhirFakes.Cli/Ignixa.FhirFakes.Cli.csproj`
Expected: 0 errors, 0 warnings.

- [ ] **Step 6: Commit**

```bash
git add tools/Ignixa.FhirFakes.Cli/Commands/WorkflowCommand.cs tools/Ignixa.FhirFakes.Cli/Program.cs test/Ignixa.FhirFakes.Cli.Tests/WorkflowCommandParameterOverrideTests.cs
git commit -m "feat: add CLI workflow command for DailyAppointmentSchedule"
```

---

## Task 10: End-to-end verification and doc status update

**Files:**
- Modify: `docs/features/fhir-faker/investigations/workflow-context-data-generation.md` (status line + Phase 1/2/3/4 checkboxes, if the doc uses status prose rather than checkboxes, update the `**Status**: Proposed` line and the phase bullet list to note what shipped)
- Modify: `docs/features/fhir-faker/readme.md` (status column for this investigation, from "Proposed" to "MVP Implemented" — same convention as `theme-consistent-generation`'s row)

**Interfaces:** none — this task only runs the CLI and updates docs.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test All.sln`
Expected: 0 failures.

- [ ] **Step 2: Manually exercise the CLI end-to-end**

Run:
```bash
dotnet run --project tools/Ignixa.FhirFakes.Cli -- r4 workflow DailyAppointmentSchedule --out ./tmp-workflow-fixtures --seed 42 --page-size 5 --validate
```
Expected: console output listing each generated page file and the manifest file; with `appointmentCount` defaulting to 12 and `--page-size 5`, expect 3 page files (5 + 5 + 2) plus one manifest file in `./tmp-workflow-fixtures/`; validation output showing all resources passing (or specific failures to investigate — do not silently ignore a validation failure here, it means an earlier task's FHIR field shape is wrong for R4).

Run the same again with `--param appointmentCount=0` and confirm it produces one empty searchset page plus a manifest with `Practitioner: 1` and no `Appointment` key — this is the zero-appointment edge case from Task 8's tests, now confirmed through the real CLI path.

Delete the manually-generated `./tmp-workflow-fixtures` directory afterward (it's scratch output, not a test artifact to commit).

- [ ] **Step 3: Update the investigation doc's status**

In `docs/features/fhir-faker/investigations/workflow-context-data-generation.md`, change:
```
**Status**: Proposed
```
to:
```
**Status**: MVP Implemented (DailyAppointmentSchedule pack; PractitionerPanel and later phases remain proposed)
```

In the "Implementation Phasing" section, prefix the completed items under "Phase 1: Investigation and contracts", "Phase 2: High-value workflow builders" (the Appointment-specific portion only — List/DocumentReference/Basic remain unimplemented), "Phase 3: Search response composition", and "Phase 4: Built-in scenario packs" (the DailyAppointmentSchedule bullet only) with a note that these shipped via `docs/superpowers/plans/2026-07-04-fhir-fakes-workflow-context.md`, without deleting the remaining unimplemented bullets (PractitionerPanel, extension package pattern, remaining Phase 4 packs stay as future work).

- [ ] **Step 4: Update the readme status row**

In `docs/features/fhir-faker/readme.md`, change the `workflow-context-data-generation` row's status from `Proposed` to `MVP Implemented`, matching the existing convention used for the `theme-consistent-generation` row.

- [ ] **Step 5: Final full-repo verification and commit**

Run: `dotnet build All.sln`
Expected: 0 warnings, 0 errors.

Run: `dotnet test All.sln`
Expected: 0 failures.

```bash
git add docs/features/fhir-faker/investigations/workflow-context-data-generation.md docs/features/fhir-faker/readme.md
git commit -m "docs: mark DailyAppointmentSchedule workflow pack MVP implemented"
```

---

## Self-Review

**Spec coverage** (against the investigation doc's Recommended Next Step: "Start with the contracts and the smallest useful built-in scenario pack: DailyAppointmentSchedule"):
- Resource graph, augmentor, composer, manifest contracts → Tasks 3, 4, 6.
- Reused/extended `ScenarioCatalog` discovery model rather than a parallel provider interface → Tasks 1, 5 (this was the explicit open question from the Fable review; resolved here as "sibling catalog, shared binder," matching the recommendation the user confirmed in conversation).
- Appointment-specific state/augmentation, organization/practitioner/patient/encounter relationships → Tasks 7, 8.
- Paging/link metadata → Task 6.
- Deterministic ID/clock/seed options → `WorkflowScenarioOptions`/`ResourceGraphAugmentationContext` (Tasks 3, 4), with the honest scope-limitation called out in Global Constraints (existing `Guid.NewGuid()` ID generation and `InitialState`'s unseeded picks are pre-existing and not reworked here).
- CLI entry point → Task 9.
- Flavor adapters → explicitly deferred (doc itself says neither committed pack needs one yet).
- PractitionerPanel, RegisterAssembly/extension packages, remaining Phase 2 builders (List, DocumentReference, Basic) → explicitly out of scope, called out in Global Constraints and Task 10.

**Placeholder scan:** no TODO/FIXME, no "add appropriate error handling," no bare "write tests for the above" — every test task has literal test code. The one explicit forward-reference caveat (Task 5 referencing `DailyAppointmentScheduleScenario` before Task 8 exists) is flagged directly with instructions on when to expect it to compile, not glossed over.

**Type consistency:** `DiscoveredScenario`/`DiscoveredScenarioParameter` used identically across Tasks 1, 5; `ResourceGraph`/`ResourceGraphAugmentationContext` signatures match between Tasks 3, 7, 8; `WorkflowScenarioOptions`/`WorkflowManifest`/`WorkflowScenarioResult` field names match between Tasks 4, 5, 8, 9; `SearchResponseOptions`/`IncludeCompleteness`/`ResponseBundleType` match between Tasks 6, 9.

---

Plan complete and saved to `docs/superpowers/plans/2026-07-04-fhir-fakes-workflow-context.md`. Two execution options:

**1. Subagent-Driven (recommended)** - dispatch a fresh subagent per task, review between tasks, fast iteration.

**2. Inline Execution** - execute tasks in this session using executing-plans, batch execution with checkpoints.

Which approach?
