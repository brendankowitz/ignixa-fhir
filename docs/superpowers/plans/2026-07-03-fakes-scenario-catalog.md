# Fakes Scenario Catalog Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move `ScenarioDiscovery`/`StateDiscovery` out of the CLI-only `Ignixa.FhirFakes.Cli` tool and into the core `Ignixa.FhirFakes` library as a public `ScenarioCatalog`/`ObservationStateCatalog`, adding structured `[Scenario]`/`[ScenarioParameter]` metadata (category, title, description, numeric bounds) so downstream UIs (e.g. the `ignixa-lab` Fakes bench) can build richer scenario pickers without hand-authoring metadata or reimplementing reflection-based discovery.

**Architecture:** `ScenarioCatalog` (new, in `src/Core/Ignixa.FhirFakes/Scenarios/`) replaces `Ignixa.FhirFakes.Cli.Discovery.ScenarioDiscovery` with the same reflection-based convention scan, but exposes `DiscoveredScenario`/`DiscoveredScenarioParameter` metadata records instead of raw `MethodInfo`, reads new optional `[Scenario]`/`[ScenarioParameter]` attributes for Category/Title/Description/Min/Max, and fixes a real bug: invocation failures are wrapped in `ScenarioInvocationException` and propagated instead of being silently swallowed (`catch (Exception) { return null; }`). `ObservationStateCatalog` (new, in `src/Core/Ignixa.FhirFakes/Scenarios/States/`) is a straightforward, unchanged-behavior port of `StateDiscovery` (minus `FindCity`, which stays CLI-side). The CLI's `Discovery/` folder is deleted; its 4 call sites are rewired to the new catalogs, and `ScenarioCommand` gains a `--param name=value` override option.

**Tech Stack:** .NET 9/10, C# (nullable enabled, file-scoped namespaces), xUnit + Shouldly, System.CommandLine 2.0.1, reflection (`System.Reflection`).

**Design spec:** `docs/superpowers/specs/2026-07-03-fakes-scenario-catalog-design.md` (read this first if anything below is ambiguous — it is the source of truth).

---

## Task 1: `ScenarioAttribute` and `ScenarioParameterAttribute`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioAttribute.cs`
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioParameterAttribute.cs`

These are plain attribute/data classes with no branching logic, so there is nothing meaningful to TDD in isolation — they are exercised indirectly by the `ScenarioCatalog` tests in Task 4. Write them directly.

- [ ] **Step 1: Create `ScenarioAttribute.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Annotates a predefined scenario factory method with catalog metadata (category, title, description)
/// consumed by <see cref="ScenarioCatalog"/> and surfaced to downstream UIs. Optional: unannotated
/// methods still work, falling back to a humanized id for <see cref="Title"/> and null for the rest.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioAttribute : Attribute
{
    /// <summary>
    /// Free-text grouping label (e.g. "Chronic", "Emergency", "Pediatric"). Null if uncategorized.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Human-readable title. If not set, <see cref="ScenarioCatalog"/> derives one from the scenario id
    /// by inserting spaces before internal capital letters (e.g. "DiabeticPatient" -> "Diabetic Patient").
    /// </summary>
    public string? Title { get; init; }

    /// <summary>
    /// One-line description of what the scenario generates.
    /// </summary>
    public string? Description { get; init; }
}
```

- [ ] **Step 2: Create `ScenarioParameterAttribute.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Annotates a scenario factory method parameter with UI hints (numeric bounds, description) consumed
/// by <see cref="ScenarioCatalog"/> and surfaced to downstream UIs (e.g. slider min/max). Optional.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioParameterAttribute : Attribute
{
    /// <summary>
    /// Minimum value hint for numeric parameters. <see cref="double.NaN"/> (the default) means "unset";
    /// attributes cannot take a nullable <see cref="double"/>, so <see cref="ScenarioCatalog"/> converts
    /// NaN to <see langword="null"/> when building <see cref="DiscoveredScenarioParameter"/> metadata.
    /// </summary>
    public double Min { get; init; } = double.NaN;

    /// <summary>
    /// Maximum value hint for numeric parameters. See <see cref="Min"/> for the NaN-as-unset convention.
    /// </summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>
    /// One-line description of what the parameter controls.
    /// </summary>
    public string? Description { get; init; }
}
```

- [ ] **Step 3: Build to confirm no errors**

Run: `dotnet build src\Core\Ignixa.FhirFakes\Ignixa.FhirFakes.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add src/Core/Ignixa.FhirFakes/Scenarios/ScenarioAttribute.cs src/Core/Ignixa.FhirFakes/Scenarios/ScenarioParameterAttribute.cs
git commit -m "Add Scenario and ScenarioParameter metadata attributes"
```

---

## Task 2: `DiscoveredScenarioParameter`, `DiscoveredScenario`, `ScenarioInvocationException`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/DiscoveredScenarioParameter.cs`
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/DiscoveredScenario.cs`
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioInvocationException.cs`

Same rationale as Task 1: these are data holders exercised through Task 4's `ScenarioCatalog` tests.

- [ ] **Step 1: Create `DiscoveredScenarioParameter.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Metadata describing one parameter of a <see cref="DiscoveredScenario"/> factory method, as produced
/// by <see cref="ScenarioCatalog"/>.
/// </summary>
public sealed class DiscoveredScenarioParameter
{
    /// <summary>
    /// The parameter name, matching the factory method's parameter name exactly (used as the key for
    /// <see cref="ScenarioCatalog.Invoke"/> parameter overrides).
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The parameter's CLR type.
    /// </summary>
    public required Type Type { get; init; }

    /// <summary>
    /// The parameter's own default value, if it has one. Null when <see cref="HasDefaultValue"/> is false.
    /// </summary>
    public object? DefaultValue { get; init; }

    /// <summary>
    /// True if the factory method parameter declares a default value.
    /// </summary>
    public bool HasDefaultValue { get; init; }

    /// <summary>
    /// Minimum value hint from <see cref="ScenarioParameterAttribute.Min"/>, or null if unset/unannotated.
    /// </summary>
    public double? Min { get; init; }

    /// <summary>
    /// Maximum value hint from <see cref="ScenarioParameterAttribute.Max"/>, or null if unset/unannotated.
    /// </summary>
    public double? Max { get; init; }

    /// <summary>
    /// One-line description from <see cref="ScenarioParameterAttribute.Description"/>, or null if unset.
    /// </summary>
    public string? Description { get; init; }
}
```

- [ ] **Step 2: Create `DiscoveredScenario.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Metadata describing a discovered predefined scenario, produced by <see cref="ScenarioCatalog"/>.
/// </summary>
public sealed class DiscoveredScenario
{
    /// <summary>
    /// The scenario id (e.g. "DiabeticPatient"), derived from the factory method name with a leading
    /// "Get" stripped. Matched case-insensitively by <see cref="ScenarioCatalog.Find"/>.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Free-text grouping label from <see cref="ScenarioAttribute.Category"/>, or null if unannotated.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Human-readable title, either from <see cref="ScenarioAttribute.Title"/> or a humanized <see cref="Id"/>.
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// One-line description from <see cref="ScenarioAttribute.Description"/>, or null if unannotated.
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Metadata for each factory method parameter after the leading <c>IFhirSchemaProvider</c> parameter.
    /// </summary>
    public required IReadOnlyList<DiscoveredScenarioParameter> Parameters { get; init; }

    /// <summary>
    /// The underlying factory method. Internal so callers cannot bypass <see cref="ScenarioCatalog.Invoke"/>
    /// and its parameter-fallback / exception-wrapping behavior via raw reflection. Visible to
    /// <c>Ignixa.FhirFakes.Tests</c> via <c>InternalsVisibleTo</c> so tests can construct synthetic
    /// scenarios pointing at test-local methods.
    /// </summary>
    internal required MethodInfo Method { get; init; }
}
```

- [ ] **Step 3: Create `ScenarioInvocationException.cs`**

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Thrown by <see cref="ScenarioCatalog.Invoke"/> when the underlying scenario factory method throws
/// during invocation. Wraps the original exception (available via <see cref="Exception.InnerException"/>)
/// rather than silently swallowing it.
/// </summary>
public sealed class ScenarioInvocationException : Exception
{
    public ScenarioInvocationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

- [ ] **Step 4: Build to confirm no errors**

Run: `dotnet build src\Core\Ignixa.FhirFakes\Ignixa.FhirFakes.csproj`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.FhirFakes/Scenarios/DiscoveredScenarioParameter.cs src/Core/Ignixa.FhirFakes/Scenarios/DiscoveredScenario.cs src/Core/Ignixa.FhirFakes/Scenarios/ScenarioInvocationException.cs
git commit -m "Add DiscoveredScenario, DiscoveredScenarioParameter, ScenarioInvocationException"
```

---

## Task 3: `ScenarioCatalog`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs`
- Test: `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs`

This is the core of the feature. Write the full test file first (it won't compile until `ScenarioCatalog` exists — that failure to compile **is** "the test fails"), then implement.

- [ ] **Step 1: Write the failing test file**

Create `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios;

public class ScenarioCatalogTests
{
    [Fact]
    public void GivenScenarioCatalog_WhenGettingAll_ThenReturnsKnownScenarios()
    {
        var ids = ScenarioCatalog.All().Select(s => s.Id).ToList();

        ids.ShouldContain("DiabeticPatient");
        ids.ShouldContain("AsthmaticChild");
        ids.ShouldContain("PediatricEarInfection");
    }

    [Fact]
    public void GivenValidScenarioId_WhenFinding_ThenReturnsScenario()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient");

        scenario.ShouldNotBeNull();
        scenario!.Id.ShouldBe("DiabeticPatient");
    }

    [Fact]
    public void GivenDifferentCasing_WhenFinding_ThenStillMatches()
    {
        var scenario = ScenarioCatalog.Find("diabeticpatient");

        scenario.ShouldNotBeNull();
    }

    [Fact]
    public void GivenUnknownScenarioId_WhenFinding_ThenReturnsNull()
    {
        var scenario = ScenarioCatalog.Find("NotAScenario");

        scenario.ShouldBeNull();
    }

    [Fact]
    public void GivenUnannotatedScenario_WhenFinding_ThenTitleFallsBackToHumanizedId()
    {
        // WellnessVisit is annotated later in this plan (Task 5); until then, any
        // as-yet-unannotated scenario id demonstrates the humanization fallback.
        // PediatricEarInfection has no consecutive-capital edge cases, so it humanizes cleanly.
        var scenario = ScenarioCatalog.Find("PediatricEarInfection")!;

        scenario.Title.ShouldBe("Pediatric Ear Infection");
        scenario.Category.ShouldBeNull();
    }

    [Fact]
    public void GivenValidScenario_WhenInvoking_ThenReturnsContextWithPatient()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.Patient.ShouldNotBeNull();
        context.AllResources.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenParameterOverride_WhenInvoking_ThenOverriddenValueChangesGeneratedPatient()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        var defaultContext = ScenarioCatalog.Invoke(scenario, schemaProvider);
        var overriddenContext = ScenarioCatalog.Invoke(
            scenario, schemaProvider, new Dictionary<string, object?> { ["age"] = 85 });

        var defaultBirthYear = int.Parse(defaultContext.Patient!.MutableNode["birthDate"]!.ToString()![..4]);
        var overriddenBirthYear = int.Parse(overriddenContext.Patient!.MutableNode["birthDate"]!.ToString()![..4]);

        overriddenBirthYear.ShouldBeLessThan(defaultBirthYear);
    }

    [Fact]
    public void GivenParameterWithNoOverrideAndNoDefault_WhenInvoking_ThenFallsBackToTypeAppropriateDefault()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(ScenarioCatalogTests).GetMethod(
            nameof(RequiredParamScenario), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "RequiredParamScenario",
            Title = "RequiredParamScenario",
            Parameters = [],
            Method = method,
        };

        var context = ScenarioCatalog.Invoke(scenario, schemaProvider);

        context.GetAttribute<int>("requiredValue").ShouldBe(0);
    }

    [Fact]
    public void GivenScenarioMethodThatThrows_WhenInvoking_ThenWrapsInScenarioInvocationException()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(ScenarioCatalogTests).GetMethod(
            nameof(ThrowingScenario), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "ThrowingScenario",
            Title = "ThrowingScenario",
            Parameters = [],
            Method = method,
        };

        var exception = Should.Throw<ScenarioInvocationException>(
            () => ScenarioCatalog.Invoke(scenario, schemaProvider));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
        exception.InnerException!.Message.ShouldBe("boom");
    }

    private static ScenarioContext RequiredParamScenario(IFhirSchemaProvider schemaProvider, int requiredValue)
    {
        var context = new ScenarioContext();
        context.SetAttribute("requiredValue", requiredValue);
        return context;
    }

    private static ScenarioContext ThrowingScenario(IFhirSchemaProvider schemaProvider) =>
        throw new InvalidOperationException("boom");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioCatalogTests"`
Expected: Build error — `ScenarioCatalog`, `DiscoveredScenario` (as a constructible type used in tests) and related symbols do not yet expose `All`/`Find`/`Invoke`. (`DiscoveredScenario` itself compiles from Task 2, but `ScenarioCatalog` does not exist yet.)

- [ ] **Step 3: Implement `ScenarioCatalog`**

Create `src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios.Predefined;

namespace Ignixa.FhirFakes.Scenarios;

/// <summary>
/// Discovers and invokes predefined FHIR scenarios by convention: public static extension methods on
/// types in the <c>Ignixa.FhirFakes.Scenarios.Predefined</c> namespace whose first parameter is
/// <see cref="IFhirSchemaProvider"/> and that return <see cref="ScenarioContext"/>. A leading "Get" is
/// stripped from the method name to form the scenario id (e.g. "GetDiabeticPatient" -> "DiabeticPatient").
/// </summary>
public static class ScenarioCatalog
{
    private static readonly Lazy<IReadOnlyList<DiscoveredScenario>> s_scenarios = new(Discover);

    /// <summary>
    /// Gets all discovered scenarios.
    /// </summary>
    public static IReadOnlyList<DiscoveredScenario> All() => s_scenarios.Value;

    /// <summary>
    /// Finds a scenario by id (case-insensitive). Returns <see langword="null"/> if no scenario matches —
    /// this is expected control flow for an unknown id, not an error.
    /// </summary>
    public static DiscoveredScenario? Find(string id) =>
        s_scenarios.Value.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Invokes a discovered scenario's factory method, applying <paramref name="parameterOverrides"/>
    /// (matched by parameter name) over the method's own default values. A parameter with neither an
    /// override nor a default falls back to a type-appropriate value (0 for <see langword="int"/>, false
    /// for <see langword="bool"/>, null otherwise) instead of passing reflection's uninitialized
    /// sentinel through.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">
    /// The scenario's factory method itself threw during invocation. The original exception is available
    /// via <see cref="Exception.InnerException"/>.
    /// </exception>
    public static ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        var parameters = scenario.Method.GetParameters();
        var args = new object?[parameters.Length];
        args[0] = schemaProvider;

        for (var i = 1; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            if (parameterOverrides != null && parameterOverrides.TryGetValue(parameter.Name!, out var overrideValue))
            {
                args[i] = overrideValue;
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

        try
        {
            return (ScenarioContext)scenario.Method.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            throw new ScenarioInvocationException(
                $"Scenario '{scenario.Id}' threw during invocation: {ex.InnerException.Message}", ex.InnerException);
        }
    }

    private static object? DefaultForType(Type type)
    {
        if (type == typeof(int))
            return 0;
        if (type == typeof(bool))
            return false;
        return null;
    }

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

                var id = method.Name.StartsWith("Get", StringComparison.Ordinal)
                    ? method.Name["Get".Length..]
                    : method.Name;

                var attribute = method.GetCustomAttribute<ScenarioAttribute>();

                scenarios.Add(new DiscoveredScenario
                {
                    Id = id,
                    Category = attribute?.Category,
                    Title = attribute?.Title ?? Humanize(id),
                    Description = attribute?.Description,
                    Parameters = parameters.Skip(1).Select(BuildParameter).ToList(),
                    Method = method,
                });
            }
        }

        return scenarios;
    }

    private static DiscoveredScenarioParameter BuildParameter(ParameterInfo parameter)
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

    private static string Humanize(string id)
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
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioCatalogTests"`
Expected: PASS (9 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.FhirFakes/Scenarios/ScenarioCatalog.cs test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs
git commit -m "Add ScenarioCatalog: discover, find, and invoke predefined scenarios"
```

---

## Task 4: `ObservationStateCatalog`

**Files:**
- Create: `src/Core/Ignixa.FhirFakes/Scenarios/States/ObservationStateCatalog.cs`
- Test: `test/Ignixa.FhirFakes.Tests/Scenarios/States/ObservationStateCatalogTests.cs`

This is a direct, unchanged-behavior port of the CLI's `StateDiscovery` (minus `FindCity`, which is CLI-only — see Task 6).

- [ ] **Step 1: Write the failing test file**

Create `test/Ignixa.FhirFakes.Tests/Scenarios/States/ObservationStateCatalogTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios.States;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Scenarios.States;

public class ObservationStateCatalogTests
{
    [Fact]
    public void GivenObservationStateCatalog_WhenGettingNames_ThenReturnsKnownStates()
    {
        var names = ObservationStateCatalog.Names().ToList();

        names.ShouldContain("BloodGlucose");
        names.ShouldContain("HemoglobinA1c");
        names.ShouldContain("BloodPressure");
    }

    [Fact]
    public void GivenValidStateName_WhenCreating_ThenReturnsState()
    {
        var state = ObservationStateCatalog.Create("BloodGlucose");

        state.ShouldNotBeNull();
        state!.Code.ShouldNotBeNull();
    }

    [Fact]
    public void GivenDifferentCasing_WhenCreating_ThenStillMatches()
    {
        var state = ObservationStateCatalog.Create("bloodglucose");

        state.ShouldNotBeNull();
    }

    [Fact]
    public void GivenInvalidStateName_WhenCreating_ThenReturnsNull()
    {
        var state = ObservationStateCatalog.Create("InvalidState");

        state.ShouldBeNull();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ObservationStateCatalogTests"`
Expected: Build error — `Ignixa.FhirFakes.Scenarios.States.ObservationStateCatalog` does not exist yet.

- [ ] **Step 3: Implement `ObservationStateCatalog`**

Create `src/Core/Ignixa.FhirFakes/Scenarios/States/ObservationStateCatalog.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;

namespace Ignixa.FhirFakes.Scenarios.States;

/// <summary>
/// Discovers and creates predefined <see cref="ObservationState"/> instances by convention: public
/// static factory methods on <see cref="ObservationState"/> that return <see cref="ObservationState"/>
/// and whose parameters all have default values.
/// </summary>
public static class ObservationStateCatalog
{
    private static readonly Lazy<IReadOnlyDictionary<string, MethodInfo>> s_states = new(Discover);

    /// <summary>
    /// Gets all available observation state names.
    /// </summary>
    public static IReadOnlyList<string> Names() => s_states.Value.Keys.ToList();

    /// <summary>
    /// Creates an <see cref="ObservationState"/> by name (case-insensitive), using each factory
    /// parameter's own default value. Returns <see langword="null"/> if no state matches.
    /// </summary>
    public static ObservationState? Create(string name)
    {
        if (!s_states.Value.TryGetValue(name, out var method))
            return null;

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = parameters[i].DefaultValue;

        return method.Invoke(null, args) as ObservationState;
    }

    private static IReadOnlyDictionary<string, MethodInfo> Discover()
    {
        var states = new Dictionary<string, MethodInfo>(StringComparer.OrdinalIgnoreCase);
        var observationStateType = typeof(ObservationState);

        var methods = observationStateType.GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => m.ReturnType == observationStateType && m.GetParameters().All(p => p.HasDefaultValue));

        foreach (var method in methods)
            states[method.Name] = method;

        return states;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ObservationStateCatalogTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.FhirFakes/Scenarios/States/ObservationStateCatalog.cs test/Ignixa.FhirFakes.Tests/Scenarios/States/ObservationStateCatalogTests.cs
git commit -m "Add ObservationStateCatalog: discover and create observation states"
```

---

## Task 5: Migrate the CLI to the new catalogs

**Files:**
- Delete: `tools/Ignixa.FhirFakes.Cli/Discovery/ScenarioDiscovery.cs`
- Delete: `tools/Ignixa.FhirFakes.Cli/Discovery/StateDiscovery.cs`
- Modify: `tools/Ignixa.FhirFakes.Cli/Commands/ResourceCommand.cs`
- Modify: `tools/Ignixa.FhirFakes.Cli/Commands/ScenarioCommand.cs`
- Modify: `tools/Ignixa.FhirFakes.Cli/Commands/HelpCommand.cs`
- Delete: `test/Ignixa.FhirFakes.Cli.Tests/ScenarioDiscoveryTests.cs`
- Delete: `test/Ignixa.FhirFakes.Cli.Tests/StateDiscoveryTests.cs`
- Delete: `test/Ignixa.FhirFakes.Cli.Tests/StateDiscoveryDebugTests.cs`
- Create: `test/Ignixa.FhirFakes.Cli.Tests/ResourceCommandFindCityTests.cs`

`FindCity` does **not** move into `ObservationStateCatalog` (per spec) — it stays CLI-side as a small `internal` helper in `ResourceCommand` so it can still be unit tested (the CLI project already has `<InternalsVisibleTo Include="Ignixa.FhirFakes.Cli.Tests" />`).

- [ ] **Step 1: Delete the old CLI discovery classes and their tests**

```powershell
git rm tools/Ignixa.FhirFakes.Cli/Discovery/ScenarioDiscovery.cs
git rm tools/Ignixa.FhirFakes.Cli/Discovery/StateDiscovery.cs
git rm test/Ignixa.FhirFakes.Cli.Tests/ScenarioDiscoveryTests.cs
git rm test/Ignixa.FhirFakes.Cli.Tests/StateDiscoveryTests.cs
git rm test/Ignixa.FhirFakes.Cli.Tests/StateDiscoveryDebugTests.cs
```

If the `Discovery` folder is now empty, it is removed automatically by `git rm` once both files are gone (no `.gitkeep` or other tracked file lives there).

- [ ] **Step 2: Update `ResourceCommand.cs`**

Replace the `using` block at the top of `tools/Ignixa.FhirFakes.Cli/Commands/ResourceCommand.cs`:

```csharp
using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Cli.Discovery;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
```

with:

```csharp
using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
```

Replace this block (city lookup inside `HandleResourceCommand`):

```csharp
                if (!string.IsNullOrEmpty(from))
                {
                    var city = StateDiscovery.FindCity(from);
                    if (city != null)
                        builder.FromCity(city);
                    else
                        builder.WithCity(from);
                }
```

with:

```csharp
                if (!string.IsNullOrEmpty(from))
                {
                    var city = FindCity(from);
                    if (city != null)
                        builder.FromCity(city);
                    else
                        builder.WithCity(from);
                }
```

Replace this block (observation state lookup):

```csharp
                var observationState = StateDiscovery.CreateObservationState(stateName);
                if (observationState == null)
                {
                    await Console.Error.WriteLineAsync($"✗ Unknown observation state: {stateName}");
                    await Console.Error.WriteLineAsync("Available states:");
                    foreach (var name in StateDiscovery.GetObservationStateNames())
                        await Console.Error.WriteLineAsync($"  - {name}");
                    Environment.ExitCode = 2;
                    return;
                }
```

with:

```csharp
                var observationState = ObservationStateCatalog.Create(stateName);
                if (observationState == null)
                {
                    await Console.Error.WriteLineAsync($"✗ Unknown observation state: {stateName}");
                    await Console.Error.WriteLineAsync("Available states:");
                    foreach (var name in ObservationStateCatalog.Names())
                        await Console.Error.WriteLineAsync($"  - {name}");
                    Environment.ExitCode = 2;
                    return;
                }
```

Add the new `FindCity` helper method to the `ResourceCommand` class (e.g. directly below `GenerateSeed`):

```csharp
    /// <summary>
    /// Finds a city by name. Internal (not private) so <c>ResourceCommandFindCityTests</c> can call it
    /// directly via the CLI project's <c>InternalsVisibleTo</c>.
    /// </summary>
    internal static CityDemographics? FindCity(string cityName) =>
        DemographicsDataProvider.CreateDefault().Cities
            .FirstOrDefault(c => c.Name.Equals(cityName, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 3: Update `ScenarioCommand.cs`**

Replace the entire file `tools/Ignixa.FhirFakes.Cli/Commands/ScenarioCommand.cs` with:

```csharp
using Ignixa.Abstractions;
using System.CommandLine;
using System.Text.Json;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Specification;

namespace Ignixa.FhirFakes.Cli.Commands;

/// <summary>
/// Command for generating predefined FHIR scenarios.
/// </summary>
internal static class ScenarioCommand
{
    public static Command Create(IFhirSchemaProvider schemaProvider, string fhirVersion)
    {
        var scenarioCommand = new Command("scenario", "Generate a predefined FHIR scenario");

        var scenarioNameArg = new Argument<string>("scenarioName")
        {
            Description = "The scenario name (e.g., DiabeticPatient)"
        };

        var outOption = new Option<string>("--out")
        {
            Description = "Output folder for generated files",
            Required = true
        };

        var resolvedReferencesOption = new Option<bool>("--resolved-references")
        {
            Description = "Create a batch bundle instead of references"
        };

        var validateOption = new Option<bool>("--validate")
        {
            Description = "Validate generated resources against schema", DefaultValueFactory = _ => false
        };

        var paramOption = new Option<string[]>("--param")
        {
            Description = "Override a scenario parameter, format name=value (repeatable, e.g. --param age=60 --param severity=3)",
            DefaultValueFactory = _ => []
        };

        scenarioCommand.Arguments.Add(scenarioNameArg);
        scenarioCommand.Options.Add(outOption);
        scenarioCommand.Options.Add(resolvedReferencesOption);
        scenarioCommand.Options.Add(validateOption);
        scenarioCommand.Options.Add(paramOption);

        scenarioCommand.SetAction(async (parseResult, cancellationToken) =>
        {
            var scenarioName = parseResult.GetValue(scenarioNameArg)!;
            var outFolder = parseResult.GetValue(outOption)!;
            var resolvedReferences = parseResult.GetValue(resolvedReferencesOption);
            var validate = parseResult.GetValue(validateOption);
            var paramValues = parseResult.GetValue(paramOption) ?? [];

            await HandleScenarioCommand(schemaProvider, fhirVersion, scenarioName, outFolder, resolvedReferences, validate, paramValues);
        });

        return scenarioCommand;
    }

    private static async Task HandleScenarioCommand(
        IFhirSchemaProvider schemaProvider,
        string fhirVersion,
        string scenarioName,
        string outFolder,
        bool resolvedReferences,
        bool validate,
        string[] paramValues)
    {
        try
        {
            // Ensure output directory exists
            Directory.CreateDirectory(outFolder);

            // Discover the scenario
            var scenario = ScenarioCatalog.Find(scenarioName);
            if (scenario == null)
            {
                Console.WriteLine($"X Unknown scenario: {scenarioName}");
                Console.WriteLine("Available scenarios:");
                foreach (var name in ScenarioCatalog.All().Select(s => s.Id).OrderBy(s => s))
                {
                    Console.WriteLine($"  - {name}");
                }
                Environment.ExitCode = 2;
                return;
            }

            if (!TryParseParameterOverrides(scenario, paramValues, out var overrides, out var parseError))
            {
                Console.WriteLine($"X {parseError}");
                Environment.ExitCode = 2;
                return;
            }

            ScenarioContext context;
            try
            {
                context = ScenarioCatalog.Invoke(scenario, schemaProvider, overrides);
            }
            catch (ScenarioInvocationException ex)
            {
                Console.WriteLine($"X Error: {ex.Message}");
                Environment.ExitCode = 1;
                return;
            }

            var id = Guid.NewGuid().ToString();
            var filename = $"{fhirVersion}-bundle-{scenarioName}-{id}.json";
            var outputPath = Path.Combine(outFolder, filename);

            JsonSerializerOptions options = new()
            {
                WriteIndented = true
            };

            // Rewrite references if using batch bundle (resolved references)
            // Transaction bundles use urn:uuid by default, batch bundles need Patient/id format
            if (resolvedReferences)
            {
                context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
            }

            // Create a transaction bundle (default behavior)
            // Use ToBatchBundle if resolved references is requested
            var bundle = resolvedReferences ? context.ToBatchBundle() : context.ToBundle();
            var json = JsonSerializer.Serialize(bundle.MutableNode, options);
            await File.WriteAllTextAsync(outputPath, json);

            var bundleType = resolvedReferences ? "batch" : "transaction";
            Console.WriteLine($"Generated scenario bundle ({bundleType}): {outputPath}");
            Console.WriteLine($"  Resources: {context.AllResources.Count}");

            // Validate each resource in the scenario if requested
            if (validate)
            {
                Console.WriteLine("\n-------------------------------------------------------------------");
                Console.WriteLine("Validating generated resources...");
                Console.WriteLine("-------------------------------------------------------------------");

                var validationResults = new Dictionary<string, Ignixa.Validation.ValidationResult>();
                foreach (var resource in context.AllResources)
                {
                    var resourceType = resource.MutableNode["resourceType"]?.ToString() ?? "Unknown";
                    var resourceId = resource.MutableNode["id"]?.ToString() ?? "unknown";
                    var key = $"{resourceType}/{resourceId}";

                    var result = ValidationHelper.ValidateResource(resource.MutableNode, schemaProvider);
                    validationResults[key] = result;

                    var summary = ValidationHelper.GetSummary(result);
                    Console.WriteLine($"  {key}: {summary}");
                }

                // Show summary of validation results
                var invalidCount = validationResults.Count(r => !r.Value.IsValid);
                if (invalidCount > 0)
                {
                    Console.WriteLine($"\n  {invalidCount} resource(s) have validation issues");
                }
                else
                {
                    Console.WriteLine($"\n  All {context.AllResources.Count} resource(s) passed validation");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"X Error: {ex.Message}");
        }
    }

    /// <summary>
    /// Parses <c>--param name=value</c> overrides into a name-to-value dictionary, converting each raw
    /// string to the scenario parameter's declared CLR type. Internal (not private) so
    /// <c>ScenarioCommandParameterOverrideTests</c> can call it directly.
    /// </summary>
    internal static bool TryParseParameterOverrides(
        DiscoveredScenario scenario,
        string[] paramValues,
        out Dictionary<string, object?> overrides,
        out string? error)
    {
        overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        error = null;

        foreach (var raw in paramValues)
        {
            var separatorIndex = raw.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                error = $"Invalid --param value '{raw}'. Expected format name=value.";
                return false;
            }

            var name = raw[..separatorIndex];
            var rawValue = raw[(separatorIndex + 1)..];

            var parameter = scenario.Parameters.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (parameter == null)
            {
                error = $"Scenario '{scenario.Id}' has no parameter named '{name}'. Available: {string.Join(", ", scenario.Parameters.Select(p => p.Name))}";
                return false;
            }

            if (!TryConvert(rawValue, parameter.Type, out var converted))
            {
                error = $"Cannot convert value '{rawValue}' for parameter '{name}' to {parameter.Type.Name}.";
                return false;
            }

            overrides[parameter.Name] = converted;
        }

        return true;
    }

    private static bool TryConvert(string rawValue, Type targetType, out object? converted)
    {
        var underlyingType = Nullable.GetUnderlyingType(targetType) ?? targetType;

        if (underlyingType == typeof(int) && int.TryParse(rawValue, out var intValue))
        {
            converted = intValue;
            return true;
        }

        if (underlyingType == typeof(decimal) && decimal.TryParse(rawValue, out var decimalValue))
        {
            converted = decimalValue;
            return true;
        }

        if (underlyingType == typeof(bool) && bool.TryParse(rawValue, out var boolValue))
        {
            converted = boolValue;
            return true;
        }

        if (underlyingType == typeof(string))
        {
            converted = rawValue;
            return true;
        }

        converted = null;
        return false;
    }
}
```

- [ ] **Step 4: Update `HelpCommand.cs`**

Replace the `using` block at the top:

```csharp
using System.CommandLine;
using Ignixa.FhirFakes.Cli.Discovery;
```

with:

```csharp
using System.CommandLine;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
```

Replace the `ShowScenarios` method:

```csharp
    private static void ShowScenarios()
    {
        Console.WriteLine("Available Predefined Scenarios:");
        Console.WriteLine();
        
        var scenarios = ScenarioDiscovery.GetScenarioNames().OrderBy(s => s).ToList();
        
        Console.WriteLine($"Found {scenarios.Count} scenarios:");
        Console.WriteLine();
        
        foreach (var scenario in scenarios)
        {
            Console.WriteLine($"  - {scenario}");
        }
        
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine($"  ignixa-fakes r4 scenario <ScenarioName> --out <folder> [--resolved-references]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ignixa-fakes r4 scenario DiabeticPatient --out ./output --resolved-references");
    }
```

with:

```csharp
    private static void ShowScenarios()
    {
        Console.WriteLine("Available Predefined Scenarios:");
        Console.WriteLine();

        var scenarios = ScenarioCatalog.All()
            .OrderBy(s => s.Category ?? "Uncategorized")
            .ThenBy(s => s.Title)
            .ToList();

        Console.WriteLine($"Found {scenarios.Count} scenarios:");
        Console.WriteLine();

        foreach (var group in scenarios.GroupBy(s => s.Category ?? "Uncategorized"))
        {
            Console.WriteLine($"{group.Key}:");
            foreach (var scenario in group)
            {
                var description = string.IsNullOrEmpty(scenario.Description) ? string.Empty : $" - {scenario.Description}";
                Console.WriteLine($"  - {scenario.Id} ({scenario.Title}){description}");
            }
            Console.WriteLine();
        }

        Console.WriteLine("Usage:");
        Console.WriteLine($"  ignixa-fakes r4 scenario <ScenarioName> --out <folder> [--param name=value ...] [--resolved-references]");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  ignixa-fakes r4 scenario DiabeticPatient --out ./output --param age=60 --param severity=3");
    }
```

Replace the single line inside `ShowObservationStates`:

```csharp
        var states = StateDiscovery.GetObservationStateNames().OrderBy(s => s).ToList();
```

with:

```csharp
        var states = ObservationStateCatalog.Names().OrderBy(s => s).ToList();
```

- [ ] **Step 5: Create `ResourceCommandFindCityTests.cs`**

Create `test/Ignixa.FhirFakes.Cli.Tests/ResourceCommandFindCityTests.cs`:

```csharp
using Shouldly;
using Ignixa.FhirFakes.Cli.Commands;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ResourceCommandFindCityTests
{
    [Fact]
    public void GivenValidCityName_WhenFindingCity_ThenReturnsCity()
    {
        var city = ResourceCommand.FindCity("Seattle");

        city.ShouldNotBeNull();
        city!.Name.ShouldBe("Seattle");
    }

    [Fact]
    public void GivenInvalidCityName_WhenFindingCity_ThenReturnsNull()
    {
        var city = ResourceCommand.FindCity("NonExistentCity");

        city.ShouldBeNull();
    }
}
```

- [ ] **Step 6: Create `ScenarioCommandParameterOverrideTests.cs`**

Create `test/Ignixa.FhirFakes.Cli.Tests/ScenarioCommandParameterOverrideTests.cs`:

```csharp
using Shouldly;
using Ignixa.FhirFakes.Cli.Commands;
using Ignixa.FhirFakes.Scenarios;

namespace Ignixa.FhirFakes.Cli.Tests;

public class ScenarioCommandParameterOverrideTests
{
    private static DiscoveredScenario GetDiabeticPatientScenario()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient");
        scenario.ShouldNotBeNull();
        return scenario!;
    }

    [Fact]
    public void GivenValidParamValues_WhenParsingOverrides_ThenReturnsConvertedValues()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=70", "severity=4", "gender=female" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out var overrides, out var error);

        success.ShouldBeTrue();
        error.ShouldBeNull();
        overrides["age"].ShouldBe(70);
        overrides["severity"].ShouldBe(4);
        overrides["gender"].ShouldBe("female");
    }

    [Fact]
    public void GivenUnknownParameterName_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "notAParameter=123" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("notAParameter");
    }

    [Fact]
    public void GivenNonNumericValueForIntParameter_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age=notanumber" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("age");
    }

    [Fact]
    public void GivenMalformedParamValue_WhenParsingOverrides_ThenReturnsFalseWithError()
    {
        var scenario = GetDiabeticPatientScenario();
        var paramValues = new[] { "age" };

        var success = ScenarioCommand.TryParseParameterOverrides(scenario, paramValues, out _, out var error);

        success.ShouldBeFalse();
        error.ShouldNotBeNull();
        error.ShouldContain("Invalid --param value");
    }
}
```

- [ ] **Step 7: Build and run the CLI and core test suites**

Run: `dotnet build All.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

Run: `dotnet test test\Ignixa.FhirFakes.Cli.Tests\Ignixa.FhirFakes.Cli.Tests.csproj`
Expected: PASS (including the 2 new `ResourceCommandFindCityTests`, the 4 new `ScenarioCommandParameterOverrideTests`, and the pre-existing `ConsoleEncodingTests`).

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj`
Expected: PASS (all tests, including Tasks 3-4's new tests).

- [ ] **Step 8: Manually smoke-test the CLI's new `--param` option**

Run:
```powershell
dotnet run --project tools\Ignixa.FhirFakes.Cli\Ignixa.FhirFakes.Cli.csproj -- r4 scenario DiabeticPatient --out $env:TEMP\fakes-smoke-test --param age=70 --param severity=4
```
Expected: `Generated scenario bundle (transaction): ...` printed, with a `.json` file written to the temp folder. Then clean up:
```powershell
Remove-Item -Recurse -Force $env:TEMP\fakes-smoke-test
```

- [ ] **Step 9: Commit**

```powershell
git add -A
git commit -m "Migrate CLI to ScenarioCatalog/ObservationStateCatalog; add --param override option"
```

---

## Task 6: Annotate the 14 screenshot-mapped scenarios with `[Scenario]`/`[ScenarioParameter]`

**Files:**
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/DiabeticPatientScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/HypertensivePatientScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/ChronicDiseaseScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/EmergencyDepartmentScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/CardiovascularScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/AsthmaticChildScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/EarInfectionScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/PregnantPatientScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/CancerCarePathwayScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/UrinaryTractInfectionScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/MetabolicSyndromeProgressionScenario.cs`
- Modify: `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/WellnessVisitScenario.cs`
- Modify: `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs`

Annotation is incremental per the design spec — these are the 14 scenarios visible as named cards in the target UI screenshot. The remaining ~18 predefined scenarios are unannotated for now and continue to work via the humanized-id/null-category fallback proven in Task 3.

- [ ] **Step 1: Write the failing metadata assertion**

Add this test to `test/Ignixa.FhirFakes.Tests/Scenarios/ScenarioCatalogTests.cs` (inside the `ScenarioCatalogTests` class, e.g. after `GivenUnannotatedScenario_WhenFinding_ThenTitleFallsBackToHumanizedId`):

```csharp
    [Fact]
    public void GivenAnnotatedScenario_WhenFindingDiabeticPatient_ThenHasExpectedMetadata()
    {
        var scenario = ScenarioCatalog.Find("DiabeticPatient")!;

        scenario.Category.ShouldBe("Chronic");
        scenario.Title.ShouldBe("Type 2 Diabetes");
        var age = scenario.Parameters.Single(p => p.Name == "age");
        age.Min.ShouldBe(18);
        age.Max.ShouldBe(90);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~GivenAnnotatedScenario_WhenFindingDiabeticPatient_ThenHasExpectedMetadata"`
Expected: FAIL — `scenario.Category` is null (expected "Chronic"), `Title` is "Diabetic Patient" (humanized fallback, expected "Type 2 Diabetes").

- [ ] **Step 3: Annotate `DiabeticPatientScenario.GetDiabeticPatient`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/DiabeticPatientScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetDiabeticPatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 52,
        string? gender = null,
        int severity = 2)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Chronic", Title = "Type 2 Diabetes", Description = "A1C, glucose, and Metformin dose escalation across follow-up encounters.")]
    public static ScenarioContext GetDiabeticPatient(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 18, Max = 90, Description = "Patient age")] int age = 52,
        [ScenarioParameter(Description = "Patient gender; random if not specified")] string? gender = null,
        [ScenarioParameter(Min = 1, Max = 5, Description = "Initial diabetes severity (1-5)")] int severity = 2)
```

- [ ] **Step 4: Annotate `HypertensivePatientScenario.GetHypertensivePatient`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/HypertensivePatientScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetHypertensivePatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string? gender = null,
        int severity = 2)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Chronic", Title = "Hypertension", Description = "ACE inhibitor treatment with monthly blood pressure monitoring and escalation.")]
    public static ScenarioContext GetHypertensivePatient(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 18, Max = 90, Description = "Patient age")] int age = 58,
        [ScenarioParameter(Description = "Patient gender; random if not specified")] string? gender = null,
        [ScenarioParameter(Min = 1, Max = 4, Description = "Initial hypertension severity (1-4)")] int severity = 2)
```

- [ ] **Step 5: Annotate `ChronicDiseaseScenario.GetChronicKidneyDiseaseProgression` and `GetCOPDManagementWithExacerbations`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/ChronicDiseaseScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with CKD progression pathway.</returns>
    public static ScenarioContext GetChronicKidneyDiseaseProgression(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string gender = "male")
```

with:

```csharp
    /// <returns>A complete scenario context with CKD progression pathway.</returns>
    [Scenario(Category = "Chronic", Title = "CKD Progression", Description = "KDIGO-staged chronic kidney disease progression with nephrology referral and dialysis prep.")]
    public static ScenarioContext GetChronicKidneyDiseaseProgression(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 30, Max = 90, Description = "Patient age")] int age = 58,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male")
```

Then replace:

```csharp
    /// <returns>A complete scenario context with COPD management pathway.</returns>
    public static ScenarioContext GetCOPDManagementWithExacerbations(
        this IFhirSchemaProvider schemaProvider,
        int age = 62,
        string gender = "male")
```

with:

```csharp
    /// <returns>A complete scenario context with COPD management pathway.</returns>
    [Scenario(Category = "Chronic", Title = "COPD", Description = "GOLD-staged COPD management from diagnosis through exacerbation and oxygen therapy.")]
    public static ScenarioContext GetCOPDManagementWithExacerbations(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 40, Max = 90, Description = "Patient age")] int age = 62,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male")
```

- [ ] **Step 6: Annotate `EmergencyDepartmentScenario.GetChestPainVisit` and `GetAbdominalPainVisit`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/EmergencyDepartmentScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with ED chest pain workup.</returns>
    public static ScenarioContext GetChestPainVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 58,
        string gender = "male")
```

with:

```csharp
    /// <returns>A complete scenario context with ED chest pain workup.</returns>
    [Scenario(Category = "Emergency", Title = "Emergency \u2014 Chest Pain", Description = "ESI-2 chest pain workup with serial troponin, EKG, and probabilistic disposition.")]
    public static ScenarioContext GetChestPainVisit(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 18, Max = 95, Description = "Patient age")] int age = 58,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male")
```

Then replace:

```csharp
    /// <returns>A complete scenario context with ED abdominal pain workup.</returns>
    public static ScenarioContext GetAbdominalPainVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 28,
        string gender = "male")
```

with:

```csharp
    /// <returns>A complete scenario context with ED abdominal pain workup.</returns>
    [Scenario(Category = "Emergency", Title = "Emergency \u2014 Abdominal Pain", Description = "Abdominal pain workup with labs, CT imaging, and possible appendectomy.")]
    public static ScenarioContext GetAbdominalPainVisit(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 5, Max = 90, Description = "Patient age")] int age = 28,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male")
```

- [ ] **Step 7: Annotate `CardiovascularScenario.GetAcuteMyocardialInfarction`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/CardiovascularScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with acute MI pathway.</returns>
    public static ScenarioContext GetAcuteMyocardialInfarction(
        this IFhirSchemaProvider schemaProvider,
        int age = 62,
        string gender = "male")
```

with:

```csharp
    /// <returns>A complete scenario context with acute MI pathway.</returns>
    [Scenario(Category = "Emergency", Title = "Acute MI", Description = "Acute myocardial infarction pathway with cardiac biomarkers and secondary-prevention CarePlan.")]
    public static ScenarioContext GetAcuteMyocardialInfarction(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 35, Max = 95, Description = "Patient age")] int age = 62,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male")
```

- [ ] **Step 8: Annotate `AsthmaticChildScenario.GetAsthmaticChild`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/AsthmaticChildScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetAsthmaticChild(
        this IFhirSchemaProvider schemaProvider,
        int age = 7,
        string? gender = null,
        int severity = 2)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Pediatric", Title = "Asthma (Pediatric)", Description = "Persistent asthma management with controller therapy and peak-flow monitoring in children.")]
    public static ScenarioContext GetAsthmaticChild(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 2, Max = 17, Description = "Child's age")] int age = 7,
        [ScenarioParameter(Description = "Child's gender; random if not specified")] string? gender = null,
        [ScenarioParameter(Min = 1, Max = 4, Description = "Asthma severity (1-4)")] int severity = 2)
```

- [ ] **Step 9: Annotate `EarInfectionScenario.GetPediatricEarInfection`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/EarInfectionScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetPediatricEarInfection(
        this IFhirSchemaProvider schemaProvider,
        int age = 4,
        string? gender = null,
        bool includeFollowUp = true)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Pediatric", Title = "Pediatric Ear Infection", Description = "Acute otitis media diagnosis with amoxicillin and optional follow-up resolution visit.")]
    public static ScenarioContext GetPediatricEarInfection(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 2, Max = 10, Description = "Child's age in years")] int age = 4,
        [ScenarioParameter(Description = "Child's gender; random if not specified")] string? gender = null,
        [ScenarioParameter(Description = "Whether to include a follow-up visit with resolution")] bool includeFollowUp = true)
```

- [ ] **Step 10: Annotate `PregnantPatientScenario.GetPregnantPatient`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/PregnantPatientScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetPregnantPatient(
        this IFhirSchemaProvider schemaProvider,
        int age = 28,
        int weekOfPregnancy = 8)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Journey", Title = "Pregnancy Journey", Description = "Trimester-by-trimester prenatal visit journey from confirmation through delivery approach.")]
    public static ScenarioContext GetPregnantPatient(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 15, Max = 45, Description = "Patient age")] int age = 28,
        [ScenarioParameter(Min = 1, Max = 40, Description = "Starting week of pregnancy for the scenario")] int weekOfPregnancy = 8)
```

- [ ] **Step 11: Annotate `CancerCarePathwayScenario.GetBreastCancerPathway`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/CancerCarePathwayScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with breast cancer care pathway.</returns>
    public static ScenarioContext GetBreastCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        int age = 55,
        string gender = "female")
```

with:

```csharp
    /// <returns>A complete scenario context with breast cancer care pathway.</returns>
    [Scenario(Category = "Oncology", Title = "Breast Cancer", Description = "Breast cancer care pathway from screening through biopsy, surgery, and chemotherapy.")]
    public static ScenarioContext GetBreastCancerPathway(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 30, Max = 85, Description = "Patient age")] int age = 55,
        [ScenarioParameter(Description = "Patient gender")] string gender = "female")
```

- [ ] **Step 12: Annotate `UrinaryTractInfectionScenario.GetUrinaryTractInfection`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/UrinaryTractInfectionScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    public static ScenarioContext GetUrinaryTractInfection(
        this IFhirSchemaProvider schemaProvider,
        int age = 35,
        string gender = "female",
        bool includeFollowUp = true)
```

with:

```csharp
    /// <returns>A complete scenario context with patient journey.</returns>
    [Scenario(Category = "Acute", Title = "UTI", Description = "Uncomplicated UTI diagnosis with nitrofurantoin and optional resolution follow-up.")]
    public static ScenarioContext GetUrinaryTractInfection(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 18, Max = 90, Description = "Patient age")] int age = 35,
        [ScenarioParameter(Description = "Patient gender")] string gender = "female",
        [ScenarioParameter(Description = "Whether to include a follow-up visit with resolution")] bool includeFollowUp = true)
```

- [ ] **Step 13: Annotate `MetabolicSyndromeProgressionScenario.GetMetabolicSyndromeProgression`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/MetabolicSyndromeProgressionScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with metabolic syndrome progression.</returns>
    public static ScenarioContext GetMetabolicSyndromeProgression(
        this IFhirSchemaProvider schemaProvider,
        int age = 48,
        string gender = "male",
        decimal startingBMI = 35.0m)
```

with:

```csharp
    /// <returns>A complete scenario context with metabolic syndrome progression.</returns>
    [Scenario(Category = "Metabolic", Title = "Metabolic Syndrome", Description = "BMI-correlated progression of obesity, hypertension, diabetes, and hyperlipidemia risk.")]
    public static ScenarioContext GetMetabolicSyndromeProgression(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 25, Max = 75, Description = "Patient age")] int age = 48,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male",
        [ScenarioParameter(Min = 18, Max = 50, Description = "Starting BMI value")] decimal startingBMI = 35.0m)
```

- [ ] **Step 14: Annotate `WellnessVisitScenario.GetWellnessVisit`**

In `src/Core/Ignixa.FhirFakes/Scenarios/Predefined/WellnessVisitScenario.cs`, replace:

```csharp
    /// <returns>A complete scenario context with wellness visit resources.</returns>
    public static ScenarioContext GetWellnessVisit(
        this IFhirSchemaProvider schemaProvider,
        int age = 45,
        string gender = "male",
        bool includeLipidPanel = true)
```

with:

```csharp
    /// <returns>A complete scenario context with wellness visit resources.</returns>
    [Scenario(Category = "Preventive", Title = "Wellness Visit", Description = "Routine wellness visit with vitals, metabolic panel, and age-appropriate lipid screening.")]
    public static ScenarioContext GetWellnessVisit(
        this IFhirSchemaProvider schemaProvider,
        [ScenarioParameter(Min = 18, Max = 90, Description = "Patient age")] int age = 45,
        [ScenarioParameter(Description = "Patient gender")] string gender = "male",
        [ScenarioParameter(Description = "Whether to include a lipid panel (automatic for age >= 30)")] bool includeLipidPanel = true)
```

- [ ] **Step 15: Run test to verify it passes**

Run: `dotnet test test\Ignixa.FhirFakes.Tests\Ignixa.FhirFakes.Tests.csproj --filter "FullyQualifiedName~ScenarioCatalogTests"`
Expected: PASS (all tests, including the new `GivenAnnotatedScenario_WhenFindingDiabeticPatient_ThenHasExpectedMetadata`).

- [ ] **Step 16: Commit**

```powershell
git add -A
git commit -m "Annotate the 14 screenshot-mapped scenarios with Scenario/ScenarioParameter metadata"
```

---

## Task 7: Full solution verification

**Files:** none (verification only).

- [ ] **Step 1: Full solution build**

Run: `dotnet build All.sln`
Expected: Build succeeded, 0 warnings, 0 errors.

- [ ] **Step 2: Full solution test run**

Run: `dotnet test All.sln`
Expected: All tests passing, including the new `Ignixa.FhirFakes.Tests\Scenarios\ScenarioCatalogTests.cs`, `Ignixa.FhirFakes.Tests\Scenarios\States\ObservationStateCatalogTests.cs`, and `Ignixa.FhirFakes.Cli.Tests\ResourceCommandFindCityTests.cs`.

- [ ] **Step 3: Confirm the old CLI-only types are gone**

Run: `Get-ChildItem tools\Ignixa.FhirFakes.Cli\Discovery -ErrorAction SilentlyContinue`
Expected: No output (folder no longer exists).

Run: `git status`
Expected: Clean working tree (everything from this plan already committed).

- [ ] **Step 4: Final commit (if anything is outstanding)**

```powershell
git add -A
git commit -m "Finish fakes scenario catalog migration"
```

(Skip if `git status` in Step 3 was already clean.)
