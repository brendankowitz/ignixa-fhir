# Reference Un-Fallback Implementation Plan (Plan A)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the typed-model generator emit a real `Reference?` / `MutableJsonList<Reference>` accessor for every `Reference`-typed element and previously-dropped `Reference` choice variant, instead of a raw `JsonNode?`/`JsonArray?` fallback — unblocking the future `Identifier`/`Reference` hand-facade merges (Phase 1) and, as a side effect, fixing a latent bug where switching a choice element's variant does not clear a stale `valueReference` key still sitting in the JSON.

**Architecture:** `CSharpTypedModelLanguage.IsTypedComplex` decides whether an element's FHIR type code gets a typed accessor (`EmitComplexProperty`) or a raw fallback (`EmitFallback`) — and the same decision, reused inside `EmitChoice`, decides whether a choice `[x]` variant is emitted at all or silently dropped. `Reference` is currently hard-coded into the `AbstractOrFallbackTypes` set alongside genuinely abstract FHIR bases (`Resource`, `Element`, etc.), even though `Reference` is a normal concrete datatype that already has a full generated facade (`Ignixa.Models.Reference`, used today as `Identifier.Identifier` — no, as the target type for other complex properties). Removing the one `"Reference"` entry from that set routes every Reference-typed element through the *already-existing* `EmitComplexProperty`/`EmitChoice` machinery — no new emission code is written by this plan, it only widens what those paths already accept. This is Plan A of a larger, multi-plan consolidation effort (`docs/features/typed-models/investigations/consolidate-handwritten-facades.md`); it must land before the `Identifier`/`Reference` hand-facade merge (a separate future plan) because that merge needs `Identifier.Assigner` and similar members to already be typed.

**Tech Stack:** .NET 10 / C#, xunit + Shouldly, the in-repo `Ignixa.Specification.Generators` codegen tool (consumes `Microsoft.Health.Fhir.CodeGen`, a git submodule).

## Global Constraints

- Nullable reference types enabled; warnings treated as errors — do not introduce new nullable warnings.
- 4-space indentation, file-scoped namespaces, `System.*` usings first outside the namespace.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA pattern, Shouldly assertions, no `#region` blocks.
- This plan is generator-only and test-only. Do **not** touch `IdentifierJsonNode.cs`, `ReferenceJsonNode.cs`, or any other hand-written `*JsonNode` file — the actual merge of those hand-written facades into the now-properly-typed generated surface is a separate future plan that depends on this one landing first.
- This plan continues on the existing worktree/branch (`worktree-typed-models-facade-consolidation`, PR #326) rather than opening a new branch — the generator file it touches is the same one recent commits on that branch already modified, and the same regen/verify/test/commit discipline applies. If you'd rather split this into its own PR, say so before execution starts; the plan doesn't depend on which PR it lands in.
- Each task ends with a commit. Do not push without the plan owner's go-ahead for that specific push (standing project rule, not unique to this plan).
- The generator's submodule dependency (`codegen/fhir-codegen`) must be initialized before the generator will build: `git submodule update --init codegen/fhir-codegen`. If `git submodule status` shows a `-` prefix on that path, run the init command first.

---

### Task 1: Generator — remove `Reference` from the fallback set, regenerate, verify

**Files:**
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:504-533` (doc comment + `AbstractOrFallbackTypes` set + stale inline comment)
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelConfig.cs:26-38` (doc comment describing the old, now-incorrect, "Reference always falls back" behavior)
- Regenerate (do not hand-edit): `src/Core/Ignixa.Serialization/Generated/Models/**/*.cs`, `src/Core/Models/Ignixa.Models.R4/Generated/**/*.cs`, `src/Core/Models/Ignixa.Models.R5/Generated/**/*.cs`

**Interfaces:**
- Produces: every `Reference`-typed element across the generated surface (base + R4/R5 subclasses) becomes `Reference?` (single) or `MutableJsonList<Reference>` (list) instead of `JsonNode?`/`JsonArray?`. `Ignixa.Models.R4.Observation.Subject` becomes `Ignixa.Models.Reference?` — Task 2 depends on this exact type. Every previously-dropped `Reference` choice variant gains a `Value{Base}Reference`-shaped property and a `Reference` member on its discriminator enum — e.g. `Ignixa.Models.R4.Extension` gains `ValueReference` (type `Ignixa.Models.Reference?`) and `ExtensionValueType.Reference`. Task 2 depends on this exact name/type too.

- [ ] **Step 1: Confirm the submodule is initialized**

Run: `git submodule status`
Expected: `codegen/fhir-codegen` line has NO leading `-`. If it does, run:
```bash
git submodule update --init codegen/fhir-codegen
```

- [ ] **Step 2: Capture the pre-fix baseline (already known, confirm it still matches)**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model 2>&1 | grep -A 4 "Coverage downgrades"
```
Expected (this is the actual baseline measured when this plan was written — if your numbers differ, the FHIR package cache or generator classification has changed since; stop and re-derive the expected post-fix numbers in Step 5 from your own baseline rather than assuming the ones below still hold):
```
Coverage downgrades (no typed accessor produced):
  value-set enum -> string: 16
  JsonNode fallbacks: 32
  dropped choice variants: 19
  member name collisions (renamed): 4
```
Confirm this run produced no diff (nothing changed yet):
```bash
git status --short -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated
```
Expected: no output.

Of the 32 JsonNode fallbacks, **22 are `Reference`-typed** (verified via `grep -c "JsonNode fallback.*(Reference)"` against the run's output) — the full list:
```
[Ignixa.Models]    CodeableReference.reference, ExtendedContactDetail.organization, Identifier.assigner,
                    Observation.basedOn, Observation.derivedFrom, Observation.device, Observation.encounter,
                    Observation.focus, Observation.hasMember, Observation.partOf, Observation.performer,
                    Observation.specimen, Observation.subject, ObservationTriggeredBy.observation,
                    Patient.generalPractitioner, Patient.managingOrganization, PatientContact.organization,
                    PatientLink.other, Signature.onBehalfOf, Signature.who
[Ignixa.Models.R5] Observation.bodyStructure, RelatedArtifact.resourceReference
```
Of the 19 dropped choice variants, **all 19 are `Reference`-typed**:
```
[Ignixa.Models]    Annotation.author[x], DataRequirement.subject[x], TriggerDefinition.timing[x], UsageContext.value[x]
[Ignixa.Models.R4] ElementDefinition.defaultValue[x]/fixed[x]/pattern[x], ElementDefinitionExample.value[x],
                    Extension.value[x], ParametersParameter.value[x]
[Ignixa.Models.R5] ElementDefinition.defaultValue[x]/fixed[x]/pattern[x], ElementDefinitionExample.value[x],
                    Extension.value[x], Observation.instantiates[x], Observation.value[x],
                    ObservationComponent.value[x], ParametersParameter.value[x]
```
The remaining 10 JsonNode fallbacks (`Observation.contained`, `ObservationComponent.referenceRange`, `OperationOutcome.contained`, `ParametersParameter.part`, `ParametersParameter.resource`, `Patient.contained` — `Resource`/`Element`-typed — plus 3 more) are **out of scope for this plan** (they need the separate Resource-typed-emission and `contentReference`-resolution generator work); do not touch them here.

- [ ] **Step 3: Update the `IsTypedComplex` doc comment and remove `Reference` from `AbstractOrFallbackTypes`**

In `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`, change:

```csharp
    /// <summary>
    /// True for type codes that resolve to a generated facade. Because the base layer carries a facade
    /// for every datatype/backbone present in any version (under <c>Ignixa.Models</c>), a complex
    /// property is emitted as the unqualified type name in both layers and resolves to the base type.
    /// Concrete FHIR datatypes/backbones are PascalCase; primitives and abstract bases are excluded by
    /// the caller's primitive check. We treat any non-primitive PascalCase code as typed-complex except
    /// the known abstract bases and <c>Reference</c>-style fallbacks handled below.
    /// </summary>
    private static bool IsTypedComplex(string typeCode)
    {
        if (string.IsNullOrEmpty(typeCode) || PrimitiveTypeNames.Contains(typeCode))
        {
            return false;
        }

        // Abstract / open types keep the JsonNode fallback (Resource, Element, etc.).
        if (AbstractOrFallbackTypes.Contains(typeCode))
        {
            return false;
        }

        // Reference is intentionally a fallback (it has no generated facade in this cut).
        return char.IsUpper(typeCode[0]);
    }

    private static readonly HashSet<string> AbstractOrFallbackTypes = new(StringComparer.Ordinal)
    {
        "Base", "Element", "BackboneElement", "BackboneType", "DataType",
        "PrimitiveType", "Resource", "DomainResource", "Reference",
    };
```

to:

```csharp
    /// <summary>
    /// True for type codes that resolve to a generated facade. Because the base layer carries a facade
    /// for every datatype/backbone present in any version (under <c>Ignixa.Models</c>), a complex
    /// property is emitted as the unqualified type name in both layers and resolves to the base type.
    /// Concrete FHIR datatypes/backbones are PascalCase; primitives and abstract bases are excluded by
    /// the caller's primitive check. We treat any non-primitive PascalCase code as typed-complex except
    /// the known abstract bases.
    /// </summary>
    private static bool IsTypedComplex(string typeCode)
    {
        if (string.IsNullOrEmpty(typeCode) || PrimitiveTypeNames.Contains(typeCode))
        {
            return false;
        }

        // Abstract / open types keep the JsonNode fallback (Resource, Element, etc.).
        if (AbstractOrFallbackTypes.Contains(typeCode))
        {
            return false;
        }

        return char.IsUpper(typeCode[0]);
    }

    private static readonly HashSet<string> AbstractOrFallbackTypes = new(StringComparer.Ordinal)
    {
        "Base", "Element", "BackboneElement", "BackboneType", "DataType",
        "PrimitiveType", "Resource", "DomainResource",
    };
```

(`Resource` stays in the set deliberately — that's the separate, not-yet-done Resource-typed-emission fix, out of scope here.)

- [ ] **Step 4: Fix the now-stale `CSharpTypedModelConfig.GenerateAllDatatypes` doc comment**

In `codegen/Ignixa.Specification.Generators/CSharpTypedModelConfig.cs`, change:

```csharp
    /// <summary>
    /// Gets or sets a value indicating whether to generate facades for the FULL set of concrete
    /// FHIR complex datatypes for the version (every entry in <c>ComplexTypesByName</c> that is not
    /// an abstract base), rather than the hand-picked <see cref="DatatypeAllowList"/>. Generating the
    /// full closure resolves Extension, Identifier, etc. to real facades and eliminates the JsonNode
    /// fallback for most in-spec complex types. This does NOT change how <c>Reference</c>-typed
    /// elements are emitted: <c>Reference</c> itself still gets a base facade (it's a normal concrete
    /// datatype, needed as e.g. an <c>Identifier</c>'s target), but elements whose type IS Reference
    /// (<c>Patient.generalPractitioner</c>, etc.) always fall back to a raw JsonNode regardless of this
    /// flag -- see <c>AbstractOrFallbackTypes</c> in <c>CSharpTypedModelLanguage</c>. Defaults to
    /// <c>false</c>; the <c>typed-model</c> mode sets it to <c>true</c>.
    /// </summary>
    public bool GenerateAllDatatypes { get; set; }
```

to:

```csharp
    /// <summary>
    /// Gets or sets a value indicating whether to generate facades for the FULL set of concrete
    /// FHIR complex datatypes for the version (every entry in <c>ComplexTypesByName</c> that is not
    /// an abstract base), rather than the hand-picked <see cref="DatatypeAllowList"/>. Generating the
    /// full closure resolves Extension, Identifier, etc. to real facades and eliminates the JsonNode
    /// fallback for most in-spec complex types, including elements whose type IS <c>Reference</c>
    /// (<c>Patient.generalPractitioner</c>, etc.) -- those resolve to a typed <c>Reference?</c>/
    /// <c>MutableJsonList&lt;Reference&gt;</c> accessor like any other concrete datatype; see
    /// <c>AbstractOrFallbackTypes</c> in <c>CSharpTypedModelLanguage</c> for the remaining fallback set
    /// (abstract bases only: <c>Resource</c>, <c>Element</c>, etc.). Defaults to
    /// <c>false</c>; the <c>typed-model</c> mode sets it to <c>true</c>.
    /// </summary>
    public bool GenerateAllDatatypes { get; set; }
```

- [ ] **Step 5: Regenerate and verify the coverage-downgrade deltas**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model 2>&1 | tee /tmp/gen-postfix.txt | grep -A 4 "Coverage downgrades"
```
Expected (derived from Step 2's baseline — all 22 Reference-typed fallbacks and all 19 Reference-typed dropped variants should disappear, nothing else should move):
```
Coverage downgrades (no typed accessor produced):
  value-set enum -> string: 16
  JsonNode fallbacks: 10
  dropped choice variants: 0
  member name collisions (renamed): 4
```
Then confirm no `(Reference)`-typed entries remain:
```bash
grep "JsonNode fallback.*(Reference)\|dropped choice variant.*(Reference)" /tmp/gen-postfix.txt
```
Expected: no output.

If `member name collisions` changed from 4, or `JsonNode fallbacks`/`dropped choice variants` didn't land on exactly 10/0, stop — do not proceed to Step 6. Report BLOCKED with the actual numbers and the new/changed line(s) from the downgrade summary; something about this specific FHIR package's Reference-typed surface differs from what this plan measured.

- [ ] **Step 6: Verify the regen diff shape**

```bash
git diff --stat -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated
```
Expected: real content changes only in the files named across Step 2's fallback/dropped-variant lists (`Identifier.cs`, `Observation.cs`, `ObservationTriggeredBy.cs`, `Patient.cs`, `PatientContact.cs`, `PatientLink.cs`, `Signature.cs`, `CodeableReference.cs`, `ExtendedContactDetail.cs` in the base; `Annotation.cs`, `DataRequirement.cs`, `TriggerDefinition.cs`, `UsageContext.cs` gaining a `Reference` choice variant; `ElementDefinition.cs`, `ElementDefinitionExample.cs`, `Extension.cs`, `ParametersParameter.cs` in both `Ignixa.Models.R4/Generated` and `Ignixa.Models.R5/Generated`; `Observation.cs`, `ObservationComponent.cs`, `RelatedArtifact.cs`, `Bundle*` files touched only if `Observation.bodyStructure`/`RelatedArtifact.resourceReference` land there — matches Step 2's R5-only lines). No file outside this set should show a real content change (pre-existing CRLF-only noise on unrelated files, if any, is not a real content change — `git diff --stat` already filters that out).

Spot-check one simple-element conversion and one choice-variant conversion directly:
```bash
grep -A 4 "public Reference? Subject" src/Core/Models/Ignixa.Models.R4/Generated/Observation.cs
grep -B 2 -A 5 "public Reference? ValueReference" src/Core/Models/Ignixa.Models.R4/Generated/Extension.cs
```
Expected: the first prints a `GetComplexProperty<Reference>("subject")`/`SetProperty("subject", ...)` pair (not a raw `MutableNode["subject"]` fallback); the second prints a `GetComplexProperty<Reference>("valueReference")`/`Set{Base}Variant("valueReference", ...)` pair, and `ExtensionValueType.Reference` should appear in the same file's `ExtensionValueType` enum (`grep "Reference," src/Core/Models/Ignixa.Models.R4/Generated/ExtensionValueType.cs` — the enum is emitted to its own file per the existing choice-enum convention).

- [ ] **Step 7: Build and run the existing test suites — expect exactly two failures**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Core/Models/Ignixa.Models.R4/Ignixa.Models.R4.csproj src/Core/Models/Ignixa.Models.R5/Ignixa.Models.R5.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
```
Expected: build 0 errors. `Ignixa.Models.Tests` passes at its full count (this project has no Reference-fallback-specific tests). `Ignixa.Models.R4.Tests` shows **exactly 2 failures**: `GivenReferenceTypedFallbackElement_WhenSetAndSerialized_ThenRoundTripsThroughReparse` and `GivenReferenceFallbackValueAlreadyAttached_WhenAssignedToAnotherParent_ThenItIsClonedNotThrown` in `TypedFacadeTests.cs`, both failing because `obs.Subject` is now `Reference?`, not `JsonNode?`, so the tests' `new System.Text.Json.Nodes.JsonObject { ... }` assignments no longer compile/match — this is the exact, expected, planned breakage; Task 2 fixes it. If any OTHER test fails, stop and report BLOCKED with which one and why — that's an unplanned regression.

- [ ] **Step 8: Commit**

```bash
git add codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs \
        codegen/Ignixa.Specification.Generators/CSharpTypedModelConfig.cs \
        src/Core/Ignixa.Serialization/Generated/Models \
        src/Core/Models/Ignixa.Models.R4/Generated \
        src/Core/Models/Ignixa.Models.R5/Generated
git commit -m "feat(typed-models): generate typed Reference accessors instead of JsonNode fallback

Removes Reference from AbstractOrFallbackTypes -- it's a normal concrete
FHIR datatype with a full generated facade already (Ignixa.Models.Reference),
not an abstract base like Resource/Element. Every Reference-typed element
(Identifier.assigner, Observation.subject, Patient.generalPractitioner,
etc. -- 22 in this package) now gets a typed Reference?/MutableJsonList<Reference>
accessor instead of a raw JsonNode?/JsonArray? fallback via the existing
EmitComplexProperty path. Every previously-dropped Reference choice variant
(19, all under Extension.value[x], ElementDefinition's default/fixed/pattern[x],
ParametersParameter.value[x], etc.) now gets a real Value{X}Reference property
via the existing EmitChoice path -- which also fixes a latent bug: since a
dropped variant was never added to a choice's VariantKeys array, setting a
different variant never cleared a pre-existing valueReference key, leaving
invalid dual-variant JSON. Unblocks the future Identifier/Reference
hand-facade merge, which needs these members already typed.

Two tests in TypedFacadeTests.cs intentionally break here (Observation.Subject
changing from JsonNode? to Reference?) -- fixed in the next commit."

test/Ignixa.Models.R4.Tests/TypedFacadeTests.cs
```

(Do not `git add` the test file yet — it still fails; Task 2 fixes and commits it separately, matching the failing-tests-expected checkpoint in Step 7.)

---

### Task 2: Fix the two broken tests, add a regression test for the choice-variant-clearing fix

**Files:**
- Modify: `test/Ignixa.Models.R4.Tests/TypedFacadeTests.cs:139-181`

**Interfaces:**
- Consumes: `Ignixa.Models.Reference` (existing type, unchanged by this plan — `Display`, `Reference2` (the `reference` field's accessor; named `Reference2` because a property cannot share its enclosing type's name — the generator's existing collision guard; `Reference2` is a scalar-string collision and keeps its numeric suffix, unlike `Extension`'s list collision, later renamed to `Extensions` — see the addendum in `docs/superpowers/plans/2026-07-09-consolidate-handwritten-facades-phase0-1a.md`), `Type`, `Identifier`). `Ignixa.Models.R4.Observation.Subject` (now `Reference?`, from Task 1). `Ignixa.Models.R4.Extension.ValueReference`/`ValueString`/`ValueType` and `ExtensionValueType` (now exist, from Task 1).

- [ ] **Step 1: Replace the two broken tests and their section comment**

In `test/Ignixa.Models.R4.Tests/TypedFacadeTests.cs`, change (lines 139-181):

```csharp
    // -- Reference-typed fallback elements (no typed Reference facade in this cut; raw JsonNode) -----

    [Fact]
    public void GivenReferenceTypedFallbackElement_WhenSetAndSerialized_ThenRoundTripsThroughReparse()
    {
        // Observation.subject is one of the most heavily-used Reference-typed elements in FHIR, and
        // (like every Reference-typed element -- see AbstractOrFallbackTypes) has no typed facade: the
        // generated accessor is a raw JsonNode?, not a MutableJsonList<Reference>/Reference property.
        var obs = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""")
            .As<Ignixa.Models.R4.Observation>();

        obs.Subject = new System.Text.Json.Nodes.JsonObject
        {
            ["reference"] = "Patient/123",
            ["display"] = "Jean Chalmers",
        };

        obs.Subject!["reference"]!.GetValue<string>().ShouldBe("Patient/123");

        var reparsed = ResourceJsonNode.Parse(obs.SerializeToString()).As<Ignixa.Models.R4.Observation>();
        reparsed.Subject!["reference"]!.GetValue<string>().ShouldBe("Patient/123");
        reparsed.Subject!["display"]!.GetValue<string>().ShouldBe("Jean Chalmers");
    }

    [Fact]
    public void GivenReferenceFallbackValueAlreadyAttached_WhenAssignedToAnotherParent_ThenItIsClonedNotThrown()
    {
        // The fallback setter routes through the same BaseJsonNode.SetProperty as typed complex
        // properties (see GivenComplexValueAlreadyAttached... above), so the same clone-on-reparent
        // guarantee should hold here too -- worth pinning directly rather than assuming it transfers.
        var reference = new System.Text.Json.Nodes.JsonObject { ["reference"] = "Patient/123" };

        var obs1 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();
        var obs2 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();

        obs1.Subject = reference; // attaches `reference` under obs1

        Should.NotThrow(() => obs2.Subject = reference);

        obs1.Subject!["reference"]!.GetValue<string>().ShouldBe("Patient/123");
        obs2.Subject!["reference"]!.GetValue<string>().ShouldBe("Patient/123");
        ReferenceEquals(obs1.Subject, obs2.Subject).ShouldBeFalse();
    }
}
```

to:

```csharp
    // -- Reference-typed elements (typed Reference facade as of the Plan A generator fix) -----------

    [Fact]
    public void GivenReferenceTypedElement_WhenSetAndSerialized_ThenRoundTripsThroughReparse()
    {
        // Observation.subject is one of the most heavily-used Reference-typed elements in FHIR.
        // Reference2 is the `reference` field's accessor -- named Reference2 because a property cannot
        // share its enclosing type's name (the same collision guard that produces Extension.Extension2).
        var obs = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""")
            .As<Ignixa.Models.R4.Observation>();

        obs.Subject = new Ignixa.Models.Reference
        {
            Reference2 = "Patient/123",
            Display = "Jean Chalmers",
        };

        obs.Subject!.Reference2.ShouldBe("Patient/123");

        var reparsed = ResourceJsonNode.Parse(obs.SerializeToString()).As<Ignixa.Models.R4.Observation>();
        reparsed.Subject!.Reference2.ShouldBe("Patient/123");
        reparsed.Subject!.Display.ShouldBe("Jean Chalmers");
    }

    [Fact]
    public void GivenReferenceValueAlreadyAttached_WhenAssignedToAnotherParent_ThenItIsClonedNotThrown()
    {
        // The typed-complex setter (EmitComplexProperty) routes through the same BaseJsonNode.SetProperty
        // as every other complex property (see GivenComplexValueAlreadyAttached... above), so the same
        // clone-on-reparent guarantee should hold here too -- worth pinning directly rather than assuming.
        var reference = new Ignixa.Models.Reference { Reference2 = "Patient/123" };

        var obs1 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();
        var obs2 = ResourceJsonNode.Parse("""{ "resourceType": "Observation", "status": "final" }""").As<Ignixa.Models.R4.Observation>();

        obs1.Subject = reference; // attaches `reference`'s underlying node under obs1

        Should.NotThrow(() => obs2.Subject = reference);

        obs1.Subject!.Reference2.ShouldBe("Patient/123");
        obs2.Subject!.Reference2.ShouldBe("Patient/123");
        ReferenceEquals(obs1.Subject!.MutableNode(), obs2.Subject!.MutableNode()).ShouldBeFalse();
    }

    [Fact]
    public void GivenExtensionWithValueString_WhenValueReferenceIsSetInstead_ThenValueStringIsCleared()
    {
        // Locks in a real bug the Plan A generator fix resolved: Extension.value[x]'s Reference variant
        // was previously dropped entirely (RecordDroppedChoiceVariant), so it was never added to
        // ValueVariantKeys -- meaning Set{Base}Variant, which only clears keys present in that array,
        // could never clear a stale valueReference when a different variant was set (or vice versa,
        // as pinned here). Now that Reference is a real variant, it participates in the same clearing
        // loop as every other variant.
        var ext = new Ignixa.Models.R4.Extension { Url = "http://example.org/ext" };

        ext.ValueString = "hello";
        ext.ValueType.ShouldBe(Ignixa.Models.R4.ExtensionValueType.String);

        ext.ValueReference = new Ignixa.Models.Reference { Reference2 = "Patient/123" };

        ext.ValueType.ShouldBe(Ignixa.Models.R4.ExtensionValueType.Reference);
        ext.ValueString.ShouldBeNull();
        ext.ValueReference!.Reference2.ShouldBe("Patient/123");
    }
}
```

- [ ] **Step 2: Run the tests, confirm the full suite passes**

```bash
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
```
Expected: 0 failures. This adds exactly 1 net-new test (the 2 others are renamed in place, not added) — the verified baseline immediately before this plan's Task 1 was 55 total (`dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj --nologo -v quiet` output's `Total:` line), so expect **56 total**. If the count differs from 56, something other than this plan's changes touched this project since — check `git log --oneline test/Ignixa.Models.R4.Tests` before assuming the plan is wrong.

- [ ] **Step 3: Commit**

```bash
git add test/Ignixa.Models.R4.Tests/TypedFacadeTests.cs
git commit -m "test(typed-models): adapt Reference-fallback tests to typed accessor, lock choice-variant clearing fix

Observation.Subject is now Ignixa.Models.Reference?, not JsonNode? --
rewrites the two affected tests against the typed shape. Adds a new
test proving Extension.ValueReference correctly clears a prior
ValueString (and vice versa), the specific latent bug the Plan A
generator fix resolved as a side effect of adding Reference to
choice variants' VariantKeys array."
```

---

### Task 3: Documentation — record the fix, close the open follow-up

**Files:**
- Modify: `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`
- Modify: `docs/features/typed-models/adr-2608-shared-base-models.md` (only if it names this exact follow-up — check first, see Step 1)

**Interfaces:**
- Consumes: nothing new — this task records what Tasks 1-2 shipped.

- [ ] **Step 1: Check whether ADR-2608 names this follow-up explicitly**

```bash
grep -n -i "contentReference\|reference.*fallback\|fallback.*reference" docs/features/typed-models/adr-2608-shared-base-models.md
```
If this prints matches specifically about `Reference`-typed elements falling back (not `contentReference`, which is the separate, still-open, unrelated follow-up), read that section and update it to note this is resolved, with a one-line pointer to this plan's file. If it prints no matches, or only `contentReference` matches, skip modifying this file entirely — do not invent a section that doesn't exist (this plan's own investigation found stale-anchor assumptions cost real rework twice already; verify before editing).

- [ ] **Step 2: Add a status note to the investigation doc**

In `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`, insert a new `##`-level section after the existing `## Phase 0b status (implemented): normative contract types` section and before `## Verdict` (confirm this is still the file's actual tail structure by running `grep -n "^## " docs/features/typed-models/investigations/consolidate-handwritten-facades.md` before editing — do not assume without checking):

```markdown

## Reference un-fallback status (implemented)

`Reference`-typed elements previously fell back to a raw `JsonNode`/`JsonArray` accessor (`Reference`
was hard-coded into the generator's `AbstractOrFallbackTypes` set alongside genuinely abstract bases
like `Resource`/`Element`, even though it's a normal concrete datatype with its own generated facade).
Fixed by removing that one entry: every `Reference`-typed element (22 in the current R4/R5 package,
including `Identifier.Assigner`, `Observation.Subject`, `Patient.GeneralPractitioner`) now gets a typed
`Reference?`/`MutableJsonList<Reference>` accessor, and every previously-dropped `Reference` choice
variant (19, including `Extension.value[x]`, `ElementDefinition`'s `default/fixed/pattern[x]`,
`Parameters.parameter.value[x]`) now gets a real `Value{X}Reference` property — which also fixed a
latent bug where switching a choice element's variant never cleared a stale `valueReference` key, since
a dropped variant was never added to the choice's key-clearing list.

This was a hard prerequisite for the Phase 1 `Identifier`/`Reference` hand-facade merge (blocked on
exactly this gap per this doc's evidence section) and is now resolved — Phase 1 is unblocked.

**Not in scope for this fix, still open:** `Resource`-typed elements (`Bundle.Entry.Resource`,
`Parameters.Parameter.Resource`, `OperationOutcome.Contained`, etc.) and `contentReference`-based
recursive elements (`Parameters.Parameter.Part`, `Bundle.Entry.Link`) still fall back — separate,
already-scoped generator work, tracked as its own plan.
```

- [ ] **Step 3: Commit**

```bash
git add docs/features/typed-models/investigations/consolidate-handwritten-facades.md
# plus adr-2608-shared-base-models.md if Step 1 found and updated a matching section
git commit -m "docs(typed-models): record Reference un-fallback fix, unblock Phase 1"
```

---

## Explicitly out of scope (future plans)

- **Resource-typed property emission** (`BundleEntry.Resource`, `OperationOutcome.Contained`, etc.) and **`contentReference` resolution** (`ParametersParameter.Part`, `BundleLink` self-reference) — the two remaining generator-fidelity gaps identified alongside this one. Small, disjoint diffs from this plan and from each other; each deserves its own regen-diff review.
- **The `new`-emission mechanism** for hand-written members that shadow a generated per-version subclass member (needed for `Extension.ValueString`/`ValueUri`, `Bundle.Type`, `OperationOutcomeIssue.Severity`/`Code`, and later StructureMap's ~8-10 version-gated members) — unrelated to this plan; a separate generator capability.
- **The actual `Identifier`/`Reference`/`Narrative`/`Meta` hand-facade merges** — this plan only removes the blocker; the merge itself (delete the hand-written files, repoint call sites, strip-to-delta) is separate future work.
