# Fakes Scenario/State Catalog — Public API Design

**Date:** 2026-07-03
**Branch:** `brendankowitz-fakes-discovery-public-api`
**Resolves:** [#296 - Expose ScenarioDiscovery/StateDiscovery as a public API, not CLI-only](https://github.com/brendankowitz/ignixa-fhir/issues/296)
**Related:** ignixa-lab PR [#10](https://github.com/brendankowitz/ignixa-lab/pull/10) (the "Fakes" bench that motivated this issue)

---

## Goal

`ScenarioDiscovery`/`StateDiscovery` — the reflection-based convention that enumerates predefined
scenarios (`Ignixa.FhirFakes.Scenarios.Predefined`) and observation states (`ObservationState`
static factories) — only exists inside `Ignixa.FhirFakes.Cli`, a `PackAsTool`-only project. It
can't be referenced by anything else. ignixa-lab's Fakes bench needed the same capability
server-side and reimplemented the convention from scratch, and its port is already missing a
defensive fallback the CLI has (parameters without a default value).

Move this discovery logic into the core `Ignixa.FhirFakes` library as a small public API, and
extend it with structured metadata (category/title/description on scenarios, min/max/description
on parameters) so downstream UIs — like the ignixa-lab Fakes bench, which is being rewritten
toward a card-based layout with category badges, human titles, descriptions, and bounded sliders —
can render themselves entirely from what the library reports, instead of hand-authoring a second
copy of that presentation data per scenario.

---

## Non-goals

- No change to how scenarios are *authored*. Scenarios stay hand-written C# extension methods
  using `ScenarioBuilder`, discovered by naming convention. This is not a move to a declarative/
  data-driven scenario format — that's a much larger rewrite the issue doesn't ask for and the
  existing convention (an established Layer 2 decision — see `docs/features/fhir-faker/readme.md`)
  works well.
- No change to `PopulationGenerator`, `SchemaBasedFhirResourceFaker`, `EdgeCaseCatalog`, or
  `PatientBuilderFactory` — these are already public and already used directly by ignixa-lab.
- Attribute annotation (`Category`/`Title`/`Description`/`Min`/`Max`) is applied incrementally.
  Nothing requires every one of the ~32 existing scenario methods to be annotated before this ships;
  unannotated methods degrade gracefully (see Metadata Fallback below).

---

## Architecture

```
Ignixa.FhirFakes (core, public)
├─ Scenarios/
│  ├─ ScenarioAttribute.cs            (new)
│  ├─ ScenarioParameterAttribute.cs   (new)
│  ├─ DiscoveredScenario.cs           (new)
│  ├─ DiscoveredScenarioParameter.cs  (new)
│  ├─ ScenarioCatalog.cs              (new — the public API)
│  └─ States/
│     └─ ObservationStateCatalog.cs   (new — the public API)

Ignixa.FhirFakes.Cli (tool)
├─ Discovery/                          (deleted — was the only implementation before)
└─ Commands/
   ├─ ScenarioCommand.cs               (updated: calls ScenarioCatalog, adds --param)
   ├─ ResourceCommand.cs               (updated: calls ObservationStateCatalog)
   └─ HelpCommand.cs                   (updated: calls both catalogs, groups by Category)
```

`ignixa-lab`'s `ScenarioDiscovery`/`ObservationStateDiscovery`/`ConvertParameter` (321-line
`FakesService.cs` plus two ~60-120 line discovery files) are deleted from that repo in a follow-up
PR there and replaced with direct calls to `Ignixa.FhirFakes.Scenarios.ScenarioCatalog` /
`ObservationStateCatalog` (out of scope for *this* repo's PR, but this design is written so that
swap is a mechanical drop-in).

---

## Public API

### `ScenarioCatalog` (`Ignixa.FhirFakes.Scenarios`)

```csharp
public static class ScenarioCatalog
{
    public static IReadOnlyList<DiscoveredScenario> All();
    public static DiscoveredScenario? Find(string id);

    /// <summary>
    /// Invokes a discovered scenario's factory method. Each entry in <paramref name="parameterOverrides"/>
    /// (matched by parameter name, case-insensitive) overrides that parameter's own default value.
    /// Parameters with neither an override nor their own default value fall back to a type-appropriate
    /// default (0 / null / false) rather than passing reflection's DBNull.Value sentinel through.
    /// </summary>
    /// <exception cref="ScenarioInvocationException">
    /// The scenario method itself threw during invocation. The original exception is the InnerException.
    /// </exception>
    public static ScenarioContext Invoke(
        DiscoveredScenario scenario,
        IFhirSchemaProvider schemaProvider,
        IReadOnlyDictionary<string, object?>? parameterOverrides = null);
}

public sealed class ScenarioInvocationException(string message, Exception innerException)
    : Exception(message, innerException);
```

`Find` returns `null` for an unknown id — that's expected control flow (a caller typed a bad
scenario name). `Invoke` does **not** swallow exceptions from the scenario method itself; those
propagate (wrapped in `ScenarioInvocationException` so callers can distinguish "bad input to
reflection" from "the scenario builder had a bug") rather than silently returning null the way the
current CLI's `catch (Exception) { return null; }` does. That catch-all in
`Ignixa.FhirFakes.Cli.Discovery.ScenarioDiscovery.CreateScenario` is a real bug per this repo's own
error-handling rule (no silent failures) — a scenario method throwing a genuine
`NullReferenceException` today is indistinguishable from "scenario not found." Callers that want
CLI-style `try/return-null-with-message` behavior do that at their own call site (see CLI changes
below), where it's visible and intentional rather than baked into the shared library.

### `DiscoveredScenario` / `DiscoveredScenarioParameter`

```csharp
public sealed class DiscoveredScenario
{
    public required string Id { get; init; }             // e.g. "DiabeticPatient"
    public string? Category { get; init; }                // e.g. "Chronic" — null if unannotated
    public required string Title { get; init; }           // e.g. "Type 2 Diabetes" — falls back to a
                                                           // space-separated Id if unannotated
    public string? Description { get; init; }             // null if unannotated
    public required IReadOnlyList<DiscoveredScenarioParameter> Parameters { get; init; }

    internal required MethodInfo Method { get; init; }    // internal — reflection stays an
                                                           // implementation detail, not exposed
}

public sealed class DiscoveredScenarioParameter
{
    public required string Name { get; init; }
    public required Type Type { get; init; }
    public object? DefaultValue { get; init; }
    public bool HasDefaultValue { get; init; }
    public double? Min { get; init; }                     // null unless annotated
    public double? Max { get; init; }                     // null unless annotated
    public string? Description { get; init; }             // null unless annotated
}
```

`Method` (`MethodInfo`) stays `internal` — only `ScenarioCatalog.Invoke` needs it. Exposing raw
reflection primitives on a public metadata type would just move the "everyone hand-rolls their own
invocation logic" problem down a level instead of solving it; `Invoke` is the one supported way to
run a scenario.

### `[Scenario]` / `[ScenarioParameter]` attributes

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioAttribute : Attribute
{
    public string? Category { get; init; }
    public string? Title { get; init; }
    public string? Description { get; init; }
}

[AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = false)]
public sealed class ScenarioParameterAttribute : Attribute
{
    public double Min { get; init; } = double.NaN;   // NaN sentinel = "not set" (attributes can't take double?)
    public double Max { get; init; } = double.NaN;
    public string? Description { get; init; }
}
```

`ScenarioCatalog`'s discovery step converts the `NaN` sentinel to `null` when building
`DiscoveredScenarioParameter` — attribute authors and callers only ever see `double` on the
attribute and `double?` on the metadata; the sentinel is an internal encoding detail.

Example annotation on an existing scenario:

```csharp
[Scenario(Category = "Chronic", Title = "Type 2 Diabetes",
    Description = "A1C, glucose, and Metformin across follow-up encounters.")]
public static ScenarioContext GetDiabeticPatient(
    this IFhirSchemaProvider schemaProvider,
    [ScenarioParameter(Min = 18, Max = 90, Description = "Patient age")] int age = 52,
    string? gender = null,
    [ScenarioParameter(Min = 1, Max = 5, Description = "Initial diabetes severity")] int severity = 2)
```

### Metadata fallback (unannotated methods)

Discovery must not require every scenario method to carry `[Scenario]`. When absent:
- `Category` → `null`
- `Title` → `Id` with a space inserted before each internal capital (`"DiabeticPatient"` →
  `"Diabetic Patient"`) — a simple, deterministic humanizer, not a guess at prose.
- `Description` → `null`
- Per-parameter `Min`/`Max`/`Description` → `null` when `[ScenarioParameter]` is absent on that
  parameter.

This keeps the catalog usable immediately for all ~32 existing methods; annotating them with real
categories/descriptions is incremental follow-up work (tracked as its own todo, not a blocker for
this API shipping).

### `ObservationStateCatalog` (`Ignixa.FhirFakes.Scenarios.States`)

```csharp
public static class ObservationStateCatalog
{
    public static IReadOnlyList<string> Names();
    public static ObservationState? Create(string name);
}
```

Unchanged in shape from today's CLI `StateDiscovery` (minus `FindCity`, see below) — the issue and
the ignixa-lab port both only need name enumeration and no-arg creation here; there isn't a
screenshot-driven need for richer per-state metadata the way there is for scenarios.

`StateDiscovery.FindCity` does **not** move into `ObservationStateCatalog` — it's unrelated to
observation state discovery (it's `DemographicsDataProvider.Cities` lookup, already fully public
via `Ignixa.FhirFakes.Population.DemographicsDataProvider.CreateDefault().Cities`). The CLI keeps a
one-line private helper calling that directly; no new public surface needed for it.

---

## Behavior preserved from the CLI's existing (correct) implementation

- Discovery scans `Ignixa.FhirFakes.Scenarios.Predefined` for public static methods returning
  `ScenarioContext` whose first parameter is `IFhirSchemaProvider`; the `Get` prefix (if present) is
  stripped to form the id. Case-insensitive id lookup is preserved.
- `ObservationStateCatalog` scans `ObservationState`'s public static methods returning
  `ObservationState` where every parameter has a default value.
- The "parameter has no override and no default value → type-appropriate fallback (0 / null /
  false), not `DBNull.Value`" defensive behavior from the CLI's `CreateScenario` — now lives once,
  inside `ScenarioCatalog.Invoke`, so ignixa-lab's `Invoke` port (which is missing this today) gets
  it for free once it switches over, closing the "foot-gun waiting for the next scenario with a
  required trailing parameter" the issue calls out.

---

## CLI changes (`Ignixa.FhirFakes.Cli`)

- `Discovery/ScenarioDiscovery.cs` and `Discovery/StateDiscovery.cs` are deleted. `ScenarioCommand`,
  `ResourceCommand`, and `HelpCommand` call `ScenarioCatalog`/`ObservationStateCatalog` directly.
- `ScenarioCommand` gains a repeatable `--param name=value` option. Values are parsed as strings and
  converted to each parameter's declared `Type` (int/decimal/bool/string/enum) by a small internal
  CLI-side converter — string→CLR-type conversion is a CLI (command-line argument parsing) concern,
  not something the core library should own, so it isn't part of `ScenarioCatalog`'s public surface.
  Unknown parameter names or conversion failures print a clear error and exit non-zero (matching the
  existing `--density`/`--edge-cases` validation style in `ResourceCommand`), not a silent no-op.
- `Invoke` failures (a real bug in a scenario method) are caught at the command level and printed as
  `✗ Error: {message}` — the try/catch that used to hide inside `ScenarioDiscovery.CreateScenario`
  moves to this one, visible call site.
- `help scenarios` groups its listing by `Category` (falling back to an "Uncategorized" bucket for
  unannotated scenarios) and prints each scenario's `Title`/`Description` alongside its id.

---

## Error handling summary

| Situation | Behavior |
|---|---|
| Unknown scenario id | `ScenarioCatalog.Find` returns `null` (expected control flow) |
| Unknown observation state name | `ObservationStateCatalog.Create` returns `null` (expected control flow) |
| Scenario method throws during `Invoke` | `ScenarioInvocationException` propagates (wraps original exception) |
| Parameter override doesn't match any parameter name | CLI: error + non-zero exit (validated against `DiscoveredScenario.Parameters` before calling `Invoke`). Library: deliberately permissive — `Invoke` only ever reads override keys matching *this* scenario's own parameters and ignores the rest, so one caller-built override set (e.g. a generic "age, gender, severity" form) can be reused across scenarios with different parameter shapes without per-scenario branching. Validating "did the caller mean to override something that doesn't exist here" is a caller-side concern (the CLI does it; a web layer would do it before calling `Invoke` too). |
| Parameter has no override and no default value | Type-appropriate fallback (`0`/`null`/`false`), not a thrown exception |

---

## Testing

- Move `test/Ignixa.FhirFakes.Cli.Tests/{ScenarioDiscoveryTests,StateDiscoveryTests,StateDiscoveryDebugTests}.cs`
  to `test/Ignixa.FhirFakes.Tests/Scenarios/{ScenarioCatalogTests,States/ObservationStateCatalogTests}.cs`,
  updated to the new class/method names. `StateDiscoveryDebugTests` (a debug-output scratch test) is
  folded into the regular test rather than kept as a separate ad-hoc file.
- New tests: `[Scenario]`/`[ScenarioParameter]` metadata is read correctly when present; unannotated
  methods fall back correctly (`Title` humanization, null `Category`/`Description`/`Min`/`Max`).
- New test: `Invoke` with a parameter that has neither an override nor a default value falls back
  to the type-appropriate default instead of throwing.
- New test: `Invoke` wraps a throwing scenario method in `ScenarioInvocationException` rather than
  swallowing it.
- CLI: update existing scenario/resource/help command tests for the new call sites; add a test for
  `--param` (happy path + unknown parameter name + bad value).

---

## Migration / rollout

1. Add the new types to `Ignixa.FhirFakes` (additive, no breaking change to existing public API).
2. Delete `Ignixa.FhirFakes.Cli.Discovery` and repoint the three CLI commands.
3. Move/rename the CLI discovery tests into `Ignixa.FhirFakes.Tests`.
4. Annotate scenario methods with `[Scenario]`/`[ScenarioParameter]` incrementally (separate,
   trackable follow-up — not required for the catalog API itself to be useful or correct).
5. (Separate repo, separate PR) ignixa-lab swaps its hand-rolled `ScenarioDiscovery` /
   `ObservationStateDiscovery` / `ConvertParameter` for direct calls into the new public catalog.
