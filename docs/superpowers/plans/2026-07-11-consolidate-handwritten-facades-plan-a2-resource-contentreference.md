# Resource-Typed Emission + ContentReference Resolution Implementation Plan (Plan A2)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the two remaining generator-fidelity gaps left after Plan A (Reference un-fallback) — `Resource`-typed elements (`BundleEntry.Resource`, `OperationOutcome.Contained`, etc.) and `contentReference`-based recursive/reused elements (`Parameters.parameter.part`, `Bundle.entry.link`, `Observation.component.referenceRange`) — so the generated surface has zero remaining `JsonNode`/`JsonArray` fallbacks for the 10 elements currently affected.

**Architecture:** Two independent, disjoint additions to `EmitSimpleElement` (`CSharpTypedModelLanguage.cs`), landed together since both require the same single regen pass to observe: (1) a `fhirTypeCode == "Resource"` special case that routes to `EmitComplexProperty` typed as `Ignixa.Serialization.SourceNodes.ResourceJsonNode` — a hand-written, concrete runtime base type with no generated facade of its own, unlocked by a one-line accessibility fix to its constructor; (2) a `contentReference` special case that resolves the referenced element's own backbone type name (using the exact same `parentType + PascalCase(segment)` naming rule the generator already uses for real backbones, folded over the referenced path) and routes to `EmitComplexProperty` with that name — no new naming scheme, just reusing an existing one via a different input.

**Tech Stack:** .NET 10 / C#, xunit + Shouldly, the in-repo `Ignixa.Specification.Generators` codegen tool.

## Global Constraints

- Nullable reference types enabled; warnings treated as errors — do not introduce new nullable warnings.
- 4-space indentation, file-scoped namespaces, `System.*` usings first outside the namespace.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA pattern, Shouldly assertions, no `#region` blocks.
- This plan is generator + one-line-runtime-visibility + test-only. Do **not** touch any hand-written `*JsonNode` file's *content* — `StructureMapJsonNode.cs` is read by a new test in this plan (to prove a latent bug is fixed) but not modified.
- Continue on the existing worktree/branch (`worktree-typed-models-facade-consolidation`, PR #326).
- Each task ends with a commit. Do not push without the plan owner's go-ahead for that specific push.
- The generator's submodule dependency (`codegen/fhir-codegen`) must be initialized before the generator will build: `git submodule update --init codegen/fhir-codegen`. If `git submodule status` shows a `-` prefix on that path, run the init command first.

---

### Task 1: Runtime constructor visibility + generator emission fixes, regenerate, verify

**Files:**
- Modify: `src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs:49` (constructor accessibility)
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:455-502` (`EmitSimpleElement`) and add one new private method
- Regenerate (do not hand-edit): `src/Core/Ignixa.Serialization/Generated/Models/**/*.cs`, `src/Core/Models/Ignixa.Models.R4/Generated/**/*.cs`, `src/Core/Models/Ignixa.Models.R5/Generated/**/*.cs`

**Interfaces:**
- Produces: `Ignixa.Serialization.SourceNodes.ResourceJsonNode` now has a `public` 2-arg constructor `(JsonObject, FhirVersion?)` — Task 2's `StructureMapJsonNode.Contained` regression test depends on this. `BundleEntry.Resource`, `BundleEntryResponse.Outcome`, `OperationOutcome.Contained`, `Observation.Contained`, `Patient.Contained`, `ParametersParameter.Resource`, `Bundle.Issues` (R5) become `ResourceJsonNode?`/`MutableJsonList<ResourceJsonNode>` instead of `JsonNode?`/`JsonArray?`. `ParametersParameter.Part` becomes `MutableJsonList<ParametersParameter>` (self-referential), `BundleEntry.Link` becomes `MutableJsonList<BundleLink>`, `ObservationComponent.ReferenceRange` becomes `MutableJsonList<ObservationReferenceRange>` — Task 2 depends on these exact types.

- [ ] **Step 1: Confirm the submodule is initialized**

Run: `git submodule status`
Expected: `codegen/fhir-codegen` line has NO leading `-`. If it does, run:
```bash
git submodule update --init codegen/fhir-codegen
```

- [ ] **Step 2: Capture the pre-fix baseline**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model 2>&1 | grep -A 4 "Coverage downgrades"
```
Expected (the actual baseline measured when this plan was written — if different, stop and re-derive Step 6's expected numbers from your own baseline before proceeding):
```
Coverage downgrades (no typed accessor produced):
  value-set enum -> string: 16
  JsonNode fallbacks: 10
  dropped choice variants: 0
  member name collisions (renamed): 4
```
Confirm no diff (nothing changed yet): `git status --short -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated` — expect no output.

All 10 fallbacks, verified via `grep "JsonNode fallback:" <run output>`:
```
[Ignixa.Models]    BundleEntry.link (Element)              -- contentReference #Bundle.link         -> BundleLink
[Ignixa.Models]    BundleEntry.resource (Resource)          -> ResourceJsonNode
[Ignixa.Models]    BundleEntryResponse.outcome (Resource)   -> ResourceJsonNode
[Ignixa.Models]    Observation.contained (Resource)         -> ResourceJsonNode (list)
[Ignixa.Models]    ObservationComponent.referenceRange (Element) -- contentReference #Observation.referenceRange -> ObservationReferenceRange
[Ignixa.Models]    OperationOutcome.contained (Resource)    -> ResourceJsonNode (list)
[Ignixa.Models]    ParametersParameter.part (Element)       -- contentReference #Parameters.parameter -> ParametersParameter (list, self-referential)
[Ignixa.Models]    ParametersParameter.resource (Resource)  -> ResourceJsonNode
[Ignixa.Models]    Patient.contained (Resource)             -> ResourceJsonNode (list)
[Ignixa.Models.R5] Bundle.issues (Resource)                 -> ResourceJsonNode
```

- [ ] **Step 3: Make `ResourceJsonNode`'s 2-arg constructor public**

In `src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs`, find (around line 49):

```csharp
    protected internal ResourceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion)
```

Change to:

```csharp
    public ResourceJsonNode(JsonObject jsonObject, FhirVersion? fhirVersion = null)
```

(Adding the `= null` default matches the convention every generated facade's 2-arg constructor already uses, e.g. `public Patient(JsonObject jsonObject, FhirVersion? fhirVersion = null)` in `src/Core/Ignixa.Serialization/Generated/Models/Patient.cs:33` — consistency, not strictly required for this fix.)

This is why the fix is needed: `BaseJsonNode.GetComplexProperty<T>` (`src/Core/Ignixa.Serialization/SourceNodes/BaseJsonNode.cs:133`) calls `Activator.CreateInstance(typeof(T), jsonObject, FhirVersion)`, and `MutableJsonList<T>`'s static factory (`src/Core/Ignixa.Serialization/MutableJsonList.cs:17`) calls `typeof(T).GetConstructor(new[] { typeof(JsonObject), typeof(FhirVersion) })` — both overloads search **public constructors only**. With the constructor `protected internal`, both throw at runtime the first time either is used with `T = ResourceJsonNode` (`MissingMethodException` / `InvalidOperationException` respectively). This is why `StructureMapJsonNode.Contained` (`src/Core/Ignixa.Serialization/Models/StructureMapJsonNode.cs:203`, `MutableJsonList<ResourceJsonNode>`, unmodified by this plan) has been silently broken since it was written — nothing exercised it until now. `ResourceJsonNode` itself is `public class ResourceJsonNode : BaseJsonNode, IResourceNode` (concrete, not abstract), so once its constructor is public, both lookup paths succeed.

- [ ] **Step 4: Add the `Resource`-typed and `contentReference` special cases to `EmitSimpleElement`**

In `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`, change (lines 455-502):

```csharp
    private void EmitSimpleElement(
        GenerationContext context,
        StringBuilder body,
        string rootStructureName,
        string typeName,
        StructureDefinition sd,
        string jsonName,
        ElementDefinition element,
        bool isResource,
        MemberNameAllocator memberNames)
    {
        bool isArray = element.cgIsArray();
        string fhirTypeCode = ResolveTypeCode(element);

        if (IsBackbone(element, sd))
        {
            // Backbone type name mirrors the classifier: parentType + PascalCase(jsonName).
            string backboneTypeName = typeName + ToPascalCase(jsonName);
            string complexName = memberNames.Allocate(ToPascalCase(jsonName));
            EmitComplexProperty(body, backboneTypeName, complexName, jsonName, isArray);
            return;
        }

        if (PrimitiveTypeNames.Contains(fhirTypeCode))
        {
            if (!isArray && element.cgHasCodes())
            {
                string? enumName = context.TryResolveValueSetEnum(element, $"{typeName}.{jsonName}");
                if (enumName is not null)
                {
                    EmitEnumAccessor(body, enumName, memberNames.Allocate(ToPascalCase(jsonName)), jsonName);
                    return;
                }
            }

            EmitPrimitive(body, memberNames.AllocatePrimitive(ToPascalCase(jsonName), fhirTypeCode, isArray), jsonName, fhirTypeCode, isArray);
            return;
        }

        if (IsTypedComplex(fhirTypeCode))
        {
            EmitComplexProperty(body, fhirTypeCode, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, isArray);
            return;
        }

        context.RecordJsonNodeFallback($"{typeName}.{jsonName}", fhirTypeCode);
        EmitFallback(body, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, fhirTypeCode, isArray);
    }
```

to:

```csharp
    private void EmitSimpleElement(
        GenerationContext context,
        StringBuilder body,
        string rootStructureName,
        string typeName,
        StructureDefinition sd,
        string jsonName,
        ElementDefinition element,
        bool isResource,
        MemberNameAllocator memberNames)
    {
        bool isArray = element.cgIsArray();

        if (!string.IsNullOrEmpty(element.ContentReference))
        {
            // FHIR's contentReference mechanism: this element reuses another element's own shape
            // (e.g. Parameters.parameter.part reuses Parameters.parameter itself, recursively; checked
            // first because a contentReference element always has an empty Type list, so IsBackbone and
            // ResolveTypeCode below would otherwise treat it as an untyped Element fallback). The
            // referenced path resolves to the exact type name that path's own element would get if
            // walked directly -- same naming rule as the backbone branch below (parentType +
            // PascalCase(segment), folded over every segment after the root).
            string referencedTypeName = ResolveContentReferenceTypeName(element.ContentReference);
            string refComplexName = memberNames.Allocate(ToPascalCase(jsonName));
            EmitComplexProperty(body, referencedTypeName, refComplexName, jsonName, isArray);
            return;
        }

        string fhirTypeCode = ResolveTypeCode(element);

        if (IsBackbone(element, sd))
        {
            // Backbone type name mirrors the classifier: parentType + PascalCase(jsonName).
            string backboneTypeName = typeName + ToPascalCase(jsonName);
            string complexName = memberNames.Allocate(ToPascalCase(jsonName));
            EmitComplexProperty(body, backboneTypeName, complexName, jsonName, isArray);
            return;
        }

        if (fhirTypeCode == "Resource")
        {
            // Resource is a hand-written runtime base (Ignixa.Serialization.SourceNodes.ResourceJsonNode),
            // not a generated Ignixa.Models facade -- there is no single concrete type an "any resource"
            // element could name. ResourceJsonNode is concrete with a public (JsonObject, FhirVersion?)
            // constructor, so GetComplexProperty<ResourceJsonNode>/MutableJsonList<ResourceJsonNode>
            // resolve via the same generic constructor lookup every other complex property already uses.
            EmitComplexProperty(body, "ResourceJsonNode", memberNames.Allocate(ToPascalCase(jsonName)), jsonName, isArray);
            return;
        }

        if (PrimitiveTypeNames.Contains(fhirTypeCode))
        {
            if (!isArray && element.cgHasCodes())
            {
                string? enumName = context.TryResolveValueSetEnum(element, $"{typeName}.{jsonName}");
                if (enumName is not null)
                {
                    EmitEnumAccessor(body, enumName, memberNames.Allocate(ToPascalCase(jsonName)), jsonName);
                    return;
                }
            }

            EmitPrimitive(body, memberNames.AllocatePrimitive(ToPascalCase(jsonName), fhirTypeCode, isArray), jsonName, fhirTypeCode, isArray);
            return;
        }

        if (IsTypedComplex(fhirTypeCode))
        {
            EmitComplexProperty(body, fhirTypeCode, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, isArray);
            return;
        }

        context.RecordJsonNodeFallback($"{typeName}.{jsonName}", fhirTypeCode);
        EmitFallback(body, memberNames.Allocate(ToPascalCase(jsonName)), jsonName, fhirTypeCode, isArray);
    }

    /// <summary>
    /// Resolves a <c>contentReference</c> value (e.g. <c>#Parameters.parameter</c>) to the CLR type name
    /// the referenced element's own path would produce. Folds the backbone-naming rule (parentType +
    /// PascalCase(segment)) over every path segment after the root, so a multi-level reference resolves
    /// the same way a multi-level backbone walk would -- verified against the three references present in
    /// the current R4/R5 package, all exactly two segments (root resource + one field): <c>#Bundle.link</c>
    /// -> <c>BundleLink</c>, <c>#Observation.referenceRange</c> -> <c>ObservationReferenceRange</c>,
    /// <c>#Parameters.parameter</c> -> <c>ParametersParameter</c> (self-referential).
    /// </summary>
    private static string ResolveContentReferenceTypeName(string contentReference)
    {
        string path = contentReference.StartsWith('#') ? contentReference[1..] : contentReference;
        string[] segments = path.Split('.');

        var typeName = new StringBuilder(segments[0]);
        for (int i = 1; i < segments.Length; i++)
        {
            typeName.Append(ToPascalCase(segments[i]));
        }

        return typeName.ToString();
    }
```

- [ ] **Step 5: Regenerate**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model
```
Expected: ends with `✓ Generation complete!`, no error output. If the generator itself throws or logs an error about a type it can't resolve, stop and report BLOCKED — do not guess a fix to `ResolveContentReferenceTypeName`.

- [ ] **Step 6: Verify the coverage-downgrade deltas**

```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model 2>&1 | tee /tmp/gen-a2-postfix.txt | grep -A 4 "Coverage downgrades"
```
Expected (all 10 fallbacks resolved, nothing else moves):
```
Coverage downgrades (no typed accessor produced):
  value-set enum -> string: 16
  JsonNode fallbacks: 0
  dropped choice variants: 0
  member name collisions (renamed): 4
```
If `JsonNode fallbacks` isn't exactly 0, or `member name collisions` changed from 4, stop — do not proceed. Report BLOCKED with the actual numbers and which fallback(s) remain or which new collision appeared.

- [ ] **Step 7: Verify the regen diff builds — this is where a wrong `ResolveContentReferenceTypeName` result would surface**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Core/Models/Ignixa.Models.R4/Ignixa.Models.R4.csproj src/Core/Models/Ignixa.Models.R5/Ignixa.Models.R5.csproj
```
Expected: 0 errors, 0 warnings. If you see `CS0246: The type or namespace name 'X' could not be found`, that means `ResolveContentReferenceTypeName` produced a type name that doesn't exist as a real generated type — stop, do not proceed, report BLOCKED with the exact type name and which element it came from; the naming-fold assumption needs revisiting for that specific `contentReference` value, not a guessed patch.

Spot-check the three contentReference resolutions and the constructor fix directly:
```bash
grep -A 2 "public MutableJsonList<BundleLink> Link" src/Core/Ignixa.Serialization/Generated/Models/BundleEntry.cs
grep -A 2 "public MutableJsonList<ParametersParameter> Part" src/Core/Ignixa.Serialization/Generated/Models/ParametersParameter.cs
grep -A 2 "public MutableJsonList<ObservationReferenceRange> ReferenceRange" src/Core/Ignixa.Serialization/Generated/Models/ObservationComponent.cs
grep -A 5 "public.*Resource.*Resource$" src/Core/Ignixa.Serialization/Generated/Models/BundleEntry.cs
```
Expected: each prints a `GetListProperty<...>`/`GetComplexProperty<...>` pair (not a raw `JsonArray?`/`JsonNode?` fallback).

- [ ] **Step 8: Build and run the existing test suites**

```bash
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
dotnet test test/Ignixa.Serialization.Tests/Ignixa.Serialization.Tests.csproj
```
Expected: `Ignixa.Models.Tests` and `Ignixa.Models.R4.Tests` pass at their prior counts (42, 56) — this task adds no test to either project. Run `Ignixa.Serialization.Tests` too (not run by Plan A, since this task changes a hand-written runtime file — `ResourceJsonNode.cs` — for the first time in this effort); expect it passes at whatever its current count is, with zero failures. If any test fails, stop and report BLOCKED with which one — a constructor accessibility change touches a shared runtime type, so an unexpected failure here is a real signal, not noise.

- [ ] **Step 9: Commit**

```bash
git add src/Core/Ignixa.Serialization/SourceNodes/ResourceJsonNode.cs \
        codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs \
        src/Core/Ignixa.Serialization/Generated/Models \
        src/Core/Models/Ignixa.Models.R4/Generated \
        src/Core/Models/Ignixa.Models.R5/Generated
git commit -m "feat(typed-models): emit typed Resource and contentReference accessors instead of JsonNode fallback

Closes the last two generator-fidelity gaps left after the Reference
un-fallback fix: Resource-typed elements (BundleEntry.Resource,
OperationOutcome.Contained, etc.) and contentReference-based recursive/
reused elements (Parameters.parameter.part, Bundle.entry.link,
Observation.component.referenceRange) both fell back to raw JsonNode/
JsonArray. Resource-typed elements now resolve to the hand-written
ResourceJsonNode runtime base (there is no single generated facade for
'any resource'); contentReference elements resolve to the referenced
path's own backbone type name via the same naming rule already used
for real backbones.

Required making ResourceJsonNode's (JsonObject, FhirVersion?) constructor
public -- GetComplexProperty<T>/MutableJsonList<T>'s generic constructor
lookup only binds public constructors, so this was previously unreachable
for T = ResourceJsonNode. This also fixes a pre-existing, never-exercised
latent bug: StructureMapJsonNode.Contained (MutableJsonList<ResourceJsonNode>)
threw InvalidOperationException on first access, since its static factory
initializer could never find a matching public constructor."
```

---

### Task 2: Regression tests for both fixes and the latent `Contained` bug

**Files:**
- Create: `test/Ignixa.Models.R4.Tests/ResourceAndContentReferenceFacadeTests.cs`

**Interfaces:**
- Consumes: `Ignixa.Models.BundleEntry` (base only — verified this type has NO R4/R5 subclass, use the base directly, not `Ignixa.Models.R4.BundleEntry`, which does not exist), `Ignixa.Models.BundleLink` (base only, same reason), `Ignixa.Models.R4.OperationOutcome` (verified this DOES have an R4 subclass), `Ignixa.Models.R4.ParametersParameter` (verified this DOES have an R4 subclass, with `ValueString`/`Part` members confirmed present), `Ignixa.Models.Patient`, `Ignixa.Serialization.SourceNodes.ResourceJsonNode`, `Ignixa.Serialization.Models.StructureMapJsonNode` (existing hand-written type, unmodified — used only to prove the runtime constructor fix, per Task 1's interface note).

- [ ] **Step 1: Write the tests**

Create `test/Ignixa.Models.R4.Tests/ResourceAndContentReferenceFacadeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.R4.Tests;

public sealed class ResourceAndContentReferenceFacadeTests
{
    [Fact]
    public void GivenBundleEntryWithResource_WhenSetAndRead_ThenReturnsTypedResourceJsonNode()
    {
        // BundleEntry has no R4/R5 subclass (fully base-only, no cross-version divergence) -- the base
        // Ignixa.Models.BundleEntry is the only type that exists.
        var entry = new Ignixa.Models.BundleEntry();
        var patient = new Ignixa.Models.Patient { Active = true };

        entry.Resource = patient;

        entry.Resource.ShouldNotBeNull();
        entry.Resource.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenOperationOutcomeWithContainedResources_WhenAdded_ThenListIsTypedResourceJsonNode()
    {
        var outcome = new Ignixa.Models.R4.OperationOutcome();
        var patient = new Ignixa.Models.Patient { Active = true };

        outcome.Contained.Add(patient);

        outcome.Contained.Count.ShouldBe(1);
        outcome.Contained[0].ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenParametersParameterWithNestedParts_WhenAdded_ThenPartIsSelfTyped()
    {
        var outer = new Ignixa.Models.R4.ParametersParameter { Name = "outer" };

        outer.Part.Add(new Ignixa.Models.R4.ParametersParameter { Name = "inner", ValueString = "hello" });

        outer.Part.Count.ShouldBe(1);
        outer.Part[0].Name.ShouldBe("inner");
        outer.Part[0].ValueString.ShouldBe("hello");
    }

    [Fact]
    public void GivenBundleEntryWithLink_WhenAdded_ThenLinkIsTypedBundleLink()
    {
        var entry = new Ignixa.Models.BundleEntry();

        entry.Link.Add(new Ignixa.Models.BundleLink { Relation = "self", Url = "http://example.org/next" });

        entry.Link.Count.ShouldBe(1);
        entry.Link[0].Relation.ShouldBe("self");
        entry.Link[0].Url.ShouldBe("http://example.org/next");
    }

    [Fact]
    public void GivenStructureMapContained_WhenAccessed_ThenNoLongerThrows()
    {
        // Locks in the latent MutableJsonList<ResourceJsonNode> constructor-binding bug this plan's
        // Task 1 fixed as a side effect: before the fix, this threw InvalidOperationException on the
        // FIRST use of MutableJsonList<ResourceJsonNode> anywhere in the process (a static factory
        // initializer that could never find a public (JsonObject, FhirVersion) constructor). Nothing in
        // the codebase exercised this property before now.
        var map = new StructureMapJsonNode();

        Should.NotThrow(() => map.Contained.Count);

        var patient = new Ignixa.Models.Patient { Active = true };
        map.Contained.Add(patient);

        map.Contained.Count.ShouldBe(1);
        map.Contained[0].ResourceType.ShouldBe("Patient");
    }
}
```

- [ ] **Step 2: Run the tests, confirm they pass**

```bash
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
```
Expected: all pass, including the 5 new tests (61 total: 56 + 5). If `ValueString` doesn't exist on `Ignixa.Models.R4.ParametersParameter` as written, check the actual generated member name via `grep "public string? Value" src/Core/Models/Ignixa.Models.R4/Generated/ParametersParameter.cs` and use the real name — do not delete the assertion.

- [ ] **Step 3: Commit**

```bash
git add test/Ignixa.Models.R4.Tests/ResourceAndContentReferenceFacadeTests.cs
git commit -m "test(typed-models): lock Resource-typed and contentReference accessor fixes, latent Contained bug"
```

---

### Task 3: Documentation

**Files:**
- Modify: `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`
- Modify: `docs/features/typed-models/readme.md`

**Interfaces:**
- Consumes: nothing new.

- [ ] **Step 1: Verify current file structure before editing**

```bash
grep -n "^## " docs/features/typed-models/investigations/consolidate-handwritten-facades.md
```
Confirm `## Reference un-fallback status (implemented)` (added by Plan A) is present, immediately followed by `## Verdict`. If the structure doesn't match, stop and report NEEDS_CONTEXT with the actual heading list — do not assume.

- [ ] **Step 2: Add a status section**

Insert a new `##`-level section immediately after `## Reference un-fallback status (implemented)`'s content and before `## Verdict`:

```markdown

## Resource-typed and contentReference accessor status (implemented)

The two remaining generator-fidelity gaps are closed. `Resource`-typed elements (`BundleEntry.Resource`,
`BundleEntryResponse.Outcome`, `Observation.Contained`, `OperationOutcome.Contained`, `Patient.Contained`,
`ParametersParameter.Resource`, `Bundle.Issues`) now resolve to the hand-written
`Ignixa.Serialization.SourceNodes.ResourceJsonNode` runtime base — there is no single generated facade for
"any resource," so this is a deliberate exception to routing through `Ignixa.Models`, not an oversight.
`contentReference`-based elements (`Bundle.Entry.Link`, `Observation.Component.ReferenceRange`,
`Parameters.Parameter.Part`) now resolve to the referenced element's own backbone type name, reusing the
existing backbone-naming rule against a different input rather than inventing a new one.

Required making `ResourceJsonNode`'s `(JsonObject, FhirVersion?)` constructor `public` — this also fixed a
pre-existing, never-exercised latent bug where `MutableJsonList<ResourceJsonNode>` (used today only by the
hand-written `StructureMapJsonNode.Contained`) threw on first access.

`JsonNode fallbacks` in the generator's coverage-downgrade summary is now **0** for the R4/R5 package —
every complex element in scope has a typed accessor. `Reference` choice variants (Plan A) and this task's
fixes together account for all 41 downgrades present when this consolidation effort started (22 Reference
fallbacks, 19 dropped Reference variants, 10 Resource/contentReference fallbacks — the remaining
`value-set enum -> string: 16` downgrades are unrelated: real value-set binding metadata gaps like
`all-languages`, not element-typing gaps, and out of scope for this effort).
```

- [ ] **Step 3: Update the readme's Investigations table**

In `docs/features/typed-models/readme.md`, find the `consolidate-handwritten-facades` row (status cell currently `Proposed (Phase 0 + Phase 0b implemented; Phase 1a/1b/2/3/4 not started)`) and append to the description cell: ` Generator now has zero JsonNode fallbacks for the R4/R5 package (Reference, Resource, and contentReference elements all typed) — Phase 1 (the datatype merges) is fully unblocked.`

- [ ] **Step 4: Commit**

```bash
git add docs/features/typed-models/investigations/consolidate-handwritten-facades.md docs/features/typed-models/readme.md
git commit -m "docs(typed-models): record Resource-typed and contentReference accessor fixes"
```

---

## Explicitly out of scope (future plans)

- **`EmitChoice`'s Resource handling**: choice `[x]` variants of type `Resource` still get dropped (`IsTypedComplex("Resource")` is unchanged, still false) — zero occurrences in the current R4/R5 package, so left alone (YAGNI). If a future FHIR version introduces one, `EmitChoice`'s `isComplexInList` check and the variant-emission code (which currently writes `variant.FhirTypeCode` directly as the C# type name) would both need the same `"Resource"` -> `"ResourceJsonNode"` name mapping this plan added to `EmitSimpleElement`.
- **The `new`-emission mechanism** for hand-written members that shadow a generated per-version subclass member (`Extension.ValueString`/`ValueUri`, `Bundle.Type`, `OperationOutcomeIssue.Severity`/`Code`) — a separate, still-needed generator capability, unrelated to this plan's fixes.
- **The actual `Identifier`/`Reference`/`Narrative`/`Meta`/`Extension` hand-facade merges** — this plan (and Plan A before it) only remove blockers; the merge itself is separate future work, now fully unblocked at the generator level.
