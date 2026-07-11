# Consolidate Hand-Written Facades — Phase 0b Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Before merging any load-bearing resource facade (`Bundle`, `Parameters`, `OperationOutcome`) into a generated `partial class`, make the generator stop stamping those facades — and the datatypes already merged or in-scope for merging (`Extension`, `Identifier`, `Meta`, `Narrative`, `Reference`) — with `[CompatibleFhirVersionsAttribute(R4, R5)]`, so `ResourceJsonNode.As<T>()` stays permissive for STU3/R4B/R6-tagged nodes exactly as it is today via the hand-written `*JsonNode` facades. Un-reserve `Bundle`/`Parameters`/`OperationOutcome` from the generator's `ReservedBaseTypeNames` skip-list so they start being generated at all (a prerequisite this phase also delivers, without yet merging their hand-written logic).

**Architecture:** `CompatibleFhirVersionsAttribute` is read once, at the `ResourceJsonNode.As<T>()` call site, and is a no-op when absent from the target type (see the attribute's own doc comment — this permissive-when-unmarked behavior is already how every hand-written facade behaves today, since none of them carry the attribute). The generator (`CSharpTypedModelLanguage.RenderClass`) currently stamps it onto every generated class unconditionally. Adding a `VersionAgnosticContractTypes` name-set and gating the one `sb.AppendLine($"[CompatibleFhirVersions(...)]")` call on it turns "unmarked" into an explicit, generator-driven decision for a known list of cross-version-stable contract types, instead of an accident of which facades happen to still be hand-written. This is the empirical basis: a classifier probe (`MergeType` structural signature comparison) run across `{R4, R5, STU3, R4B, R6}` for the 15 candidate types found `Bundle`, `Parameters`, `OperationOutcome` (plus the 5 datatypes) wire-shape-identical everywhere including STU3 (enum-literal drift and additive-only elements, no retypes/cardinality flips) — see Task 3 for the full table this plan commits to the investigation doc.

**Tech Stack:** .NET 10 / C#, xunit + Shouldly, the in-repo `Ignixa.Specification.Generators` codegen tool (consumes `Microsoft.Health.Fhir.CodeGen`, a git submodule).

## Global Constraints

- Nullable reference types enabled; warnings treated as errors — do not introduce new nullable warnings.
- 4-space indentation, file-scoped namespaces, `System.*` usings first outside the namespace.
- One type per file for new hand-written files.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA pattern, Shouldly assertions, no `#region` blocks.
- This plan does **not** merge `BundleJsonNode`, `ParametersJsonNode`, or `OperationOutcomeJsonNode` into their generated counterparts. Those hand-written types keep doing everything they do today, unchanged. This plan only makes the *generated* `Ignixa.Models.Bundle`/`Parameters`/`OperationOutcome` exist and be unmarked, so a later plan can merge them without the STU3/R4B/R6 regression this phase exists to prevent. Do not add, remove, or rename any member on the three hand-written `*JsonNode` files.
- `Provenance`, `SearchParameter`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition`, `CapabilityStatement` stay in `ReservedBaseTypeNames` — out of scope for this plan (see Task 3's table for why: real structural divergence from STU3, or in `StructureMap`/`ConceptMap`/`Composition`'s case, from each other between R4 and R5 too).
- Each task ends with a commit. Do **not** push the branch — the prior Phase 0/1a plan carried a specific, one-time push authorization for that session; it does not extend to this plan. Ask before pushing.
- The generator's submodule dependency (`codegen/fhir-codegen`) must be initialized before the generator will build: `git submodule update --init codegen/fhir-codegen`. If `git submodule status` shows a `-` prefix on that path, run the init command first.

---

### Task 1: Generator — un-reserve load-bearing normative types, gate `CompatibleFhirVersionsAttribute` emission, regenerate

**Files:**
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:56-68` (shrink `ReservedBaseTypeNames`)
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs` (add `VersionAgnosticContractTypes`, after the `ReservedBaseTypeNames` declaration)
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:804-813` (gate the attribute line)
- Modify: `codegen/Ignixa.Specification.Generators/Program.cs:257` (add `Bundle`, `Parameters`, `OperationOutcome` to `ResourceAllowList`)
- Regenerate (do not hand-edit): `src/Core/Ignixa.Serialization/Generated/Models/**/*.cs`, `src/Core/Models/Ignixa.Models.R4/Generated/**/*.cs`, `src/Core/Models/Ignixa.Models.R5/Generated/**/*.cs`

**Interfaces:**
- Produces: `Ignixa.Models.Bundle`, `Ignixa.Models.Parameters`, `Ignixa.Models.OperationOutcome` now exist as generated `partial` classes with no `CompatibleFhirVersionsAttribute` — Task 2 depends on these three types existing and being unmarked. `Ignixa.Models.Extension`, `Ignixa.Models.Identifier`, `Ignixa.Models.Meta`, `Ignixa.Models.Narrative`, `Ignixa.Models.Reference` lose their attribute too (already existed; this only removes a stamped attribute, no other shape change).

- [ ] **Step 1: Confirm the submodule is initialized**

Run: `git submodule status`
Expected: `codegen/fhir-codegen` line has NO leading `-`. If it does, run:
```bash
git submodule update --init codegen/fhir-codegen
```

- [ ] **Step 2: Shrink `ReservedBaseTypeNames`**

In `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`, change:

```csharp
    private static readonly HashSet<string> ReservedBaseTypeNames = new(StringComparer.Ordinal)
    {
        "Bundle",
        "OperationOutcome",
        "Parameters",
        "Provenance",
        "SearchParameter",
        "CapabilityStatement",
        "StructureDefinition",
        "StructureMap",
        "ConceptMap",
        "Composition",
    };
```

to:

```csharp
    private static readonly HashSet<string> ReservedBaseTypeNames = new(StringComparer.Ordinal)
    {
        "Provenance",
        "SearchParameter",
        "CapabilityStatement",
        "StructureDefinition",
        "StructureMap",
        "ConceptMap",
        "Composition",
    };

    /// <summary>
    /// Type names whose shared BASE facade is structurally identical enough across every targeted FHIR
    /// version -- including versions this generator does not target yet (STU3, R4B, R6) -- that a
    /// version-tagged node should be able to reach the base via
    /// <see cref="Ignixa.Serialization.SourceNodes.ResourceJsonNode.As{T}"/> regardless of tag. Applies
    /// only to the base emission, never a per-version subclass (see the <c>isVersionSubclass</c> check at
    /// the call site) -- a subclass exists precisely because some element differs by version, so it must
    /// keep enforcing the guard. Empirically determined by a classifier structural-signature probe across
    /// {R4, R5, STU3, R4B, R6}; see the "Normative contract types" table committed to
    /// docs/features/typed-models/investigations/consolidate-handwritten-facades.md by this plan's Task 3
    /// (docs/superpowers/plans/2026-07-10-consolidate-handwritten-facades-phase0b.md carries the same
    /// table if Task 3 has not landed yet). The generator omits <see cref="CompatibleFhirVersionsAttribute"/>
    /// for these types' base facade in <see cref="RenderClass"/>, matching the permissive-when-unmarked
    /// behavior hand-written facades have always had (see that attribute's own doc comment). Types NOT in
    /// this set that get consolidated later (e.g. <c>Provenance</c>) keep their attribute deliberately --
    /// their divergence from STU3 or between R4/R5 is real, so the guard firing for them is correct, not
    /// a regression.
    /// </summary>
    private static readonly HashSet<string> VersionAgnosticContractTypes = new(StringComparer.Ordinal)
    {
        "Bundle",
        "Parameters",
        "OperationOutcome",
        "Extension",
        "Identifier",
        "Meta",
        "Narrative",
        "Reference",
    };
```

- [ ] **Step 3: Gate the attribute emission — base only, never subclasses**

In the same file, find (around line 804-813):

```csharp
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// FHIR {typeName} {kindLabel} facade. Zero-copy view over the underlying JsonObject.");
        sb.AppendLine("/// </summary>");

        // Fully qualified: every generated class inherits the instance property BaseJsonNode.FhirVersion,
        // which shadows the FhirVersion TYPE name in this attribute-argument position (CS0120) despite
        // the `using Ignixa.Abstractions;` above -- simple-name lookup here prefers the inherited member.
        string versionArgs = string.Join(", ", compatibleVersions.Select(v => $"global::Ignixa.Abstractions.FhirVersion.{MapToFhirVersionEnumMember(v)}"));
        sb.AppendLine($"[CompatibleFhirVersions({versionArgs})]");
        sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}partial class {typeName} : {baseClass}");
```

Change to:

```csharp
        sb.AppendLine("/// <summary>");
        sb.AppendLine($"/// FHIR {typeName} {kindLabel} facade. Zero-copy view over the underlying JsonObject.");
        sb.AppendLine("/// </summary>");

        // isVersionSubclass is checked first and short-circuits VersionAgnosticContractTypes deliberately:
        // that set says "the elements common to every classified version are safe to read from any
        // version," which is a claim about the BASE type only. A per-version subclass exists precisely
        // because some element genuinely differs between versions (e.g. Bundle.issues is R5-only,
        // Parameters.parameter.value[x]'s choice-type union differs between R4/R5) -- so a subclass for
        // typeName "Bundle" must keep its own single-version CompatibleFhirVersionsAttribute even though
        // the base type doesn't, or a real cross-version misread through that specific subclass (e.g. an
        // R4-tagged node read via R5.Bundle, silently missing the version-specific shape) would stop
        // being caught.
        if (!isVersionSubclass && VersionAgnosticContractTypes.Contains(typeName))
        {
            sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}partial class {typeName} : {baseClass}");
        }
        else
        {
            // Fully qualified: every generated class inherits the instance property BaseJsonNode.FhirVersion,
            // which shadows the FhirVersion TYPE name in this attribute-argument position (CS0120) despite
            // the `using Ignixa.Abstractions;` above -- simple-name lookup here prefers the inherited member.
            string versionArgs = string.Join(", ", compatibleVersions.Select(v => $"global::Ignixa.Abstractions.FhirVersion.{MapToFhirVersionEnumMember(v)}"));
            sb.AppendLine($"[CompatibleFhirVersions({versionArgs})]");
            sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}partial class {typeName} : {baseClass}");
        }
```

This is a correction to what shipped in this task's first implementation attempt: an earlier version of this step gated purely on `VersionAgnosticContractTypes.Contains(typeName)`, which (discovered when Step 6 below actually ran against the real classifier output) also strips the attribute from per-version subclasses of `Bundle`/`Parameters`/`Extension` — real subclasses the classifier emits because these types are NOT fully identical between R4 and R5 (see Step 6). Suppressing the attribute on those subclasses would have made a genuine cross-version misread through `Ignixa.Models.R4.Bundle`/`R5.Bundle` (etc.) silently pass instead of throwing — the opposite of this plan's goal. The base type is still safe to leave unmarked because the classifier only ever places an element in the shared base when every classified version agrees on its shape (`presentEverywhere && distinctSignatures.Count == 1`) — anything that differs, including an element added in only one version, is excluded from the base and lives only in the subclass. So the base is a genuinely safe conservative subset regardless of how many subclasses exist above it.

- [ ] **Step 4: Add the three resources to `ResourceAllowList`**

`ReservedBaseTypeNames` (Step 2) is not the only gate controlling which resources the generator emits. `codegen/Ignixa.Specification.Generators/Program.cs:257` hard-codes a second, independent one: `TypedModelClassifier.BuildVersionView` only walks resource types listed in `CSharpTypedModelConfig.ResourceAllowList` (datatypes go through a separate, `GenerateAllDatatypes`-driven path — irrelevant here, it's why the 5 datatypes need no change in this step). A resource never in `ResourceAllowList` is never classified, so it never reaches the `ReservedBaseTypeNames` check or emission code at all — un-reserving it in Step 2 is necessary but not sufficient.

In `codegen/Ignixa.Specification.Generators/Program.cs`, find (around line 255-260):

```csharp
    var typedModelConfig = new CSharpTypedModelConfig
    {
        ResourceAllowList = ["Patient", "Observation"],
        DatatypeAllowList = ["HumanName", "CodeableConcept", "Coding", "Quantity", "Identifier", "Period", "ContactPoint"],
        GenerateAllDatatypes = true,
    };
```

Change to:

```csharp
    var typedModelConfig = new CSharpTypedModelConfig
    {
        ResourceAllowList = ["Patient", "Observation", "Bundle", "Parameters", "OperationOutcome"],
        DatatypeAllowList = ["HumanName", "CodeableConcept", "Coding", "Quantity", "Identifier", "Period", "ContactPoint"],
        GenerateAllDatatypes = true,
    };
```

- [ ] **Step 5: Regenerate**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model
```
Expected: ends with `✓ Generation complete!` and no error output. Unlike Phase 0/1a's Task 1, this plan does not pre-compute an exact diff-stat count — un-reserving three resources means the generator classifies and emits them for the first time, and their element counts aren't known until the classifier actually runs. Instead, verify the *shape* of the result in Step 6.

- [ ] **Step 6: Verify the regen result**

```bash
git diff --stat -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated
```
Use `git diff --stat`, not `git status --short` — this worktree has pre-existing CRLF/LF line-ending drift flagging nearly every file under these three directories as modified regardless of this task, and `git status --short` shows all of it. `git diff --stat` normalizes line endings for comparison and shows only real content changes.

Expected:
- `Bundle.cs`, `Parameters.cs`, `OperationOutcome.cs` (plus their base-layer backbone/enum types — e.g. `BundleEntry.cs`, `BundleLink.cs`, `OperationOutcomeIssue.cs`, `ParametersParameter.cs`, `HttpVerb.cs`, `SearchEntryMode.cs`) appear as new files under `src/Core/Ignixa.Serialization/Generated/Models/` with real content — confirm with `git status --short -- src/Core/Ignixa.Serialization/Generated/Models/Bundle.cs src/Core/Ignixa.Serialization/Generated/Models/Parameters.cs src/Core/Ignixa.Serialization/Generated/Models/OperationOutcome.cs`, which is unaffected by the CRLF noise since these files didn't exist before.
- `Extension.cs`, `Identifier.cs`, `Meta.cs`, `Narrative.cs`, `Reference.cs` under the same directory show a one-line removal (the attribute) in `git diff --stat`.
- **Per-version subclass files are expected to appear** under `src/Core/Models/Ignixa.Models.R4/Generated/` and `Ignixa.Models.R5/Generated/` for `Bundle`, `Parameters`, and `OperationOutcome` (and their backbone/enum types, and possibly `Extension`) — this is correct, not a failure. The real classifier finds genuine R4/R5 divergence for these three (confirmed empirically running this exact task): `Bundle.issues` is an R5-only field, `Parameters.parameter.value[x]`'s choice-type union differs between versions (R5 adds `Integer64`/`CodeableReference`/`RatioRange`/`Availability`/`ExtendedContactDetail`, R4's `Contributor` variant isn't in R5), and several enums (`BundleType`, `IssueSeverity`, `IssueType`) gained literals in R5. Per Step 3's corrected gating, these subclasses should each still carry their own single-version `[CompatibleFhirVersions(...)]` — check a few directly:
  ```bash
  grep -c "CompatibleFhirVersions" src/Core/Models/Ignixa.Models.R4/Generated/Bundle.cs src/Core/Models/Ignixa.Models.R5/Generated/Bundle.cs src/Core/Models/Ignixa.Models.R4/Generated/Parameters.cs src/Core/Models/Ignixa.Models.R5/Generated/Parameters.cs src/Core/Models/Ignixa.Models.R4/Generated/OperationOutcome.cs src/Core/Models/Ignixa.Models.R5/Generated/OperationOutcome.cs
  ```
  Expected: every one of these 6 files (if it exists — a type only gets a subclass file in a version where it actually has a delta) prints `:1`, confirming the subclass kept its attribute. If any prints `:0`, Step 3's fix didn't take — stop, do not proceed, report BLOCKED with which file.
- No other files show real content changes in `git diff --stat`.

Then, for the base types (must have NO attribute):
```bash
grep -L "CompatibleFhirVersions" src/Core/Ignixa.Serialization/Generated/Models/Bundle.cs src/Core/Ignixa.Serialization/Generated/Models/Parameters.cs src/Core/Ignixa.Serialization/Generated/Models/OperationOutcome.cs src/Core/Ignixa.Serialization/Generated/Models/Extension.cs src/Core/Ignixa.Serialization/Generated/Models/Identifier.cs src/Core/Ignixa.Serialization/Generated/Models/Meta.cs src/Core/Ignixa.Serialization/Generated/Models/Narrative.cs src/Core/Ignixa.Serialization/Generated/Models/Reference.cs
```
Expected: all 8 file paths printed (i.e. `CompatibleFhirVersions` appears in none of them — these are the BASE files, under `Ignixa.Serialization/Generated/Models`, not the per-version subclasses checked above). If any path is missing from the output, that file still has the attribute — stop and check whether its `typeName` really matches an entry in `VersionAgnosticContractTypes` (case-sensitive, `StringComparer.Ordinal`).

If the console output from Step 5 flags an "incompatible" element on one of these 8 types that looks like more than enum-literal growth or an added/absent field — e.g. a property changing from a single value to a list, or from one type to a structurally unrelated type — stop and report BLOCKED with the specific element; that's a deeper shape change than "safe to exclude from the base," and would call the type's presence in `VersionAgnosticContractTypes` into question entirely, not just its subclass handling.

- [ ] **Step 7: Build and run the existing typed-model test suites**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Core/Models/Ignixa.Models.R4/Ignixa.Models.R4.csproj src/Core/Models/Ignixa.Models.R5/Ignixa.Models.R5.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
```
Expected: build 0 errors. Both test projects should pass at the same pass count as before this change — nothing yet references the three new generated types or depends on the removed attributes, so no existing test's behavior *should* change. If a test fails that enumerates or snapshots the full set of generated types (e.g. a "regen-drift guard" style test), that's a real interaction this plan didn't anticipate, not a flaky run — report DONE_WITH_CONCERNS with the failing test name and output rather than silently adjusting the test.

- [ ] **Step 8: Commit**

```bash
git add codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs \
        codegen/Ignixa.Specification.Generators/Program.cs \
        src/Core/Ignixa.Serialization/Generated/Models \
        src/Core/Models/Ignixa.Models.R4/Generated \
        src/Core/Models/Ignixa.Models.R5/Generated
git commit -m "feat(typed-models): generate Bundle/Parameters/OperationOutcome, exempt normative contract types from version tagging

Un-reserves Bundle/Parameters/OperationOutcome from ReservedBaseTypeNames
and adds them to Program.cs's ResourceAllowList (the second, independent
gate controlling which resources the classifier walks at all) so the
generator emits base facades for them for the first time -- still
unused, the hand-written *JsonNode facades are untouched. Adds
VersionAgnosticContractTypes gating CompatibleFhirVersionsAttribute
emission for these three plus the five datatypes already merged or
in-scope (Extension/Identifier/Meta/Narrative/Reference), so a later
merge of these facades into generated partials does not regress
As<T>() for STU3/R4B/R6-tagged nodes, which today pass through the
unmarked hand-written facades. See docs/features/typed-models/investigations/consolidate-handwritten-facades.md
for the structural analysis this set is based on."
```

---

### Task 2: Guard tests — prove the exemption works, prove the guard still fires for everything else

**Files:**
- Modify: `test/Ignixa.Models.Tests/AsTVersionGuardTests.cs`

**Interfaces:**
- Consumes: `Ignixa.Models.Bundle`, `Ignixa.Models.Parameters`, `Ignixa.Models.OperationOutcome` from Task 1 (unmarked base types, `partial class`, resource kind — same `ResourceJsonNode`-derived shape every other generated resource facade has), plus `Ignixa.Models.R4.Bundle`/`Ignixa.Models.R5.Bundle` (per-version subclasses Task 1's regen also produces — these DO keep their attribute; only the base is unmarked). `ResourceJsonNode.Parse`, `.As<T>()`, `FhirVersion` — all pre-existing, already used elsewhere in this file.

- [ ] **Step 1: Write the new tests**

Add to `test/Ignixa.Models.Tests/AsTVersionGuardTests.cs`, inside the `AsTVersionGuardTests` class, after the existing `GivenMismatchedVersions_WhenValidateFalse_ThenBypassesTheGuard` test:

```csharp

    // Ignixa.Models.Bundle/Parameters/OperationOutcome (the shared BASE type) carry no
    // CompatibleFhirVersionsAttribute (Phase 0b) -- the classifier only places an element in the base
    // when every classified version agrees on its shape, so the base is a safe, conservative common
    // subset for any version, even though real per-version divergence exists elsewhere on these types
    // (Bundle.issues is R5-only, Parameters.parameter.value[x]'s choice-type union differs by version --
    // both live only in the R4/R5 subclasses, which keep their own attribute; see the tests below).
    [Fact]
    public void GivenStu3TaggedNode_WhenAsBundle_ThenSucceeds()
    {
        var node = ResourceJsonNode.Parse("""{ "resourceType": "Bundle" }""");
        node.FhirVersion = FhirVersion.Stu3;

        Should.NotThrow(() => node.As<Bundle>());
    }

    [Fact]
    public void GivenStu3TaggedNode_WhenAsParameters_ThenSucceeds()
    {
        var node = ResourceJsonNode.Parse("""{ "resourceType": "Parameters" }""");
        node.FhirVersion = FhirVersion.Stu3;

        Should.NotThrow(() => node.As<Parameters>());
    }

    [Fact]
    public void GivenStu3TaggedNode_WhenAsOperationOutcome_ThenSucceeds()
    {
        var node = ResourceJsonNode.Parse("""{ "resourceType": "OperationOutcome" }""");
        node.FhirVersion = FhirVersion.Stu3;

        Should.NotThrow(() => node.As<OperationOutcome>());
    }

    [Fact]
    public void GivenR4bTaggedNode_WhenAsBundle_ThenSucceedsAndVersionIsPreserved()
    {
        var node = ResourceJsonNode.Parse("""{ "resourceType": "Bundle" }""");
        node.FhirVersion = FhirVersion.R4B;

        Bundle bundle = node.As<Bundle>();

        bundle.FhirVersion.ShouldBe(FhirVersion.R4B);
    }

    [Fact]
    public void GivenR4TaggedNode_WhenAsR5Bundle_ThenStillThrows()
    {
        // Control specific to this task's own bug: the base Bundle type is unmarked, but its R4/R5
        // subclasses are NOT -- Bundle.issues (R5-only) and BundleType's R5-only "subscription-notification"
        // literal are real per-version divergences, so a genuine cross-version misread through the
        // version-tagged subclass must still throw, exactly like Patient below.
        var r4Bundle = ResourceJsonNode.Parse("""{ "resourceType": "Bundle" }""").As<Ignixa.Models.R4.Bundle>();

        Should.Throw<InvalidCastException>(() => r4Bundle.As<Ignixa.Models.R5.Bundle>());
    }

    [Fact]
    public void GivenR4TaggedNode_WhenAsR5OnlyPatient_ThenStillThrows()
    {
        // Control: Phase 0b only exempted the eight named types. A genuinely version-specific facade
        // (Patient's R4/R5 subclasses) must keep throwing on a real mismatch -- proves the gating in
        // RenderClass didn't accidentally weaken the guard for anything outside VersionAgnosticContractTypes.
        var r4Patient = ResourceJsonNode.Parse("""{ "resourceType": "Patient", "id": "example" }""").As<Ignixa.Models.R4.Patient>();

        Should.Throw<InvalidCastException>(() => r4Patient.As<Ignixa.Models.R5.Patient>());
    }
```

- [ ] **Step 2: Run the tests, confirm the new ones fail if Task 1 weren't there**

```bash
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj --filter "FullyQualifiedName~AsTVersionGuardTests"
```
Expected: all tests pass, including the 5 new facts. Since Task 1 already landed, these should pass on the first run — there is no separate red/green step here (the behavior under test was implemented in Task 1, not this task); this step is purely confirming the new assertions are correct against the real implementation.

- [ ] **Step 3: Commit**

```bash
git add test/Ignixa.Models.Tests/AsTVersionGuardTests.cs
git commit -m "test(typed-models): lock the Phase 0b version-agnostic contract type exemption

Proves Bundle/Parameters/OperationOutcome stay reachable via As<T>()
for STU3/R4B-tagged nodes, and that the guard still throws for a
genuinely version-specific type (R4/R5 Patient) -- the gating in
RenderClass didn't overreach past the named set."
```

---

### Task 3: Document the normative/not-normative split, update investigation status

**Files:**
- Modify: `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`
- Modify: `docs/features/typed-models/readme.md`

**Interfaces:**
- Consumes: nothing new — this task records what Tasks 1-2 shipped and the analysis behind it, so the next phase's investigation starts from a written decision instead of re-deriving it.

- [ ] **Step 1: Add the classification table to the investigation doc**

In `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`, the "Phase 0 + Phase 1a" status section this step was originally written to anchor after does not exist in the file — the earlier plan's task that would have added it (`docs/superpowers/plans/2026-07-09-consolidate-handwritten-facades-phase0-1a.md`'s Task 4) was never run; only that plan's Task 1 (the generator `partial` change) landed. The file currently ends with a `## Version scope` section (which already describes the exact `CompatibleFhirVersionsAttribute` exposure problem Phase 0b solves — see its last paragraph, ending "...confirming datatypes as the correct first increment.") followed immediately by `## Verdict`.

Insert the new content as its own `##`-level section between those two — after `## Version scope`'s last paragraph, before the `## Verdict` heading:

```markdown

## Phase 0b status (implemented): normative contract types

Before merging any load-bearing resource facade, a classifier structural-signature probe (`MergeType`,
the same logic `TypedModelClassifier` uses for real generation) was run across `{R4, R5, STU3, R4B, R6}`
for the 15 candidate consolidation types, to separate genuinely version-agnostic types from ones whose
agnosticism was only ever an accident of staying hand-written. Verdict graded by wire-shape misread
hazard: enum-literal drift and additive/absent elements are near-identical (read as null, safe); retypes,
cardinality flips, and object-vs-string changes are hard divergence.

| Type | R4/R5 | +STU3 | +R4B | +R6 (ballot2) | Verdict |
|---|---|---|---|---|---|
| Narrative | Identical | Identical | Identical | Identical | NORMATIVE |
| Reference | Identical | additive only | Identical | Identical | NORMATIVE |
| Meta | Identical | wire-same | Identical | Identical | NORMATIVE |
| Identifier | Identical | enum drift only | Identical | Identical | NORMATIVE |
| Extension | value[x] drift | value[x] drift | value[x] drift | value[x] drift | NORMATIVE |
| Bundle | enum/additive drift | enum drift | clean (tracks R4) | clean (tracks R5) | NORMATIVE |
| Parameters | value[x] drift only | value[x] subset | clean | clean | NORMATIVE |
| OperationOutcome | enum drift only | enum drift only | clean | clean | NORMATIVE |
| Provenance | R5 additive | **hard**: `agent.who`/`entity.what` choice-type change, `activity` retype | clean | additive | NOT-NORMATIVE |
| SearchParameter | R5 additive | **hard**: `component.definition` string↔object | clean | clean | NOT-NORMATIVE |
| StructureDefinition | soft | **hard**: `context` retype | clean | clean | NOT-NORMATIVE |
| CapabilityStatement | soft | **hard, massive**: 22 incompatible elements, 3 STU3-only backbones | clean | enum drift | NOT-NORMATIVE |
| StructureMap | **hard within R4/R5**: `source.defaultValue[x]` shape change | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |
| ConceptMap | **hard within R4/R5**: `equivalence`→`relationship` rename, cardinality/restructure | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |
| Composition | **hard within R4/R5**: cardinality flips, backbone→type change, `attester.mode` retype | worse | tracks R4 | tracks R5 | NOT-NORMATIVE |

**8 NORMATIVE, 7 NOT-NORMATIVE.** R4B tracked R4 with zero new hard divergence across all 15 types; R6
(ballot2) tracked R5 the same way — STU3 is the sole gatekeeper, and neither "undetermined" version
in the original open question turned out to be undetermined.

**Correction found while implementing this phase:** the table above came from a standalone probe that
linked the classifier's source directly, outside the real `RunTypedModelMultiVersion` pipeline. Running
the actual generator against the real R4/R5 packages (Task 1) found genuine, not-metadata-only R4/R5
divergence for `Bundle` (`Bundle.issues` is an R5-only field), `Parameters`
(`Parameters.parameter.value[x]`'s choice-type union differs: R5 adds `Integer64`/`CodeableReference`/
`RatioRange`/`Availability`/`ExtendedContactDetail`, R4's `Contributor` variant isn't in R5), and enum
growth on `BundleType`/`IssueSeverity`/`IssueType`. This does **not** overturn the NORMATIVE verdict for
these three: FHIR's own multi-version classifier only ever places an element in the shared base when
every classified version agrees on its exact shape, so `Bundle.issues` and the diverging `value[x]`
members are excluded from the base and live only in per-version subclasses (`Ignixa.Models.R4.Bundle`,
`Ignixa.Models.R5.Bundle`, etc.) — the base remains a genuinely safe, conservative common subset for any
version, subclasses included. What it DID require fixing: `CSharpTypedModelLanguage`'s attribute-gating
logic must suppress `CompatibleFhirVersionsAttribute` only on the unmarked set's **base** type, never on
its per-version subclasses — subclasses exist specifically to hold the elements that differ, so they
must keep enforcing the guard. See Task 1 Step 3 for the corrected implementation and Task 2 for the
regression test that locks this in (`GivenR4TaggedNode_WhenAsR5Bundle_ThenStillThrows`).

**Shipped (this phase):** `CSharpTypedModelLanguage` un-reserves `Bundle`/`Parameters`/`OperationOutcome`
from `ReservedBaseTypeNames` and `Program.cs`'s `ResourceAllowList` (they are now generated for the first
time, still unused) and omits `CompatibleFhirVersionsAttribute` for the base type of all 8 NORMATIVE
types via a new `VersionAgnosticContractTypes` set — per-version subclasses of these types, where the
classifier emits any, keep their attribute. This does **not** merge the three hand-written resource
facades yet — it only makes the generated counterparts exist and stay permissive, so that merge (a
separate, larger plan — each of `BundleJsonNode`/`ParametersJsonNode`/`OperationOutcomeJsonNode` has
multiple nested hand-written types and several call sites, comparable in shape to the Phase 1a `Extension`
merge but larger) doesn't regress `As<T>()` for STU3/R4B/R6-tagged nodes when it happens.

**Decision for the 7 NOT-NORMATIVE types:**
- `Provenance`, `SearchParameter`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition`:
  proceed with consolidation in a future phase, but **keep** `CompatibleFhirVersionsAttribute(R4, R5)`
  on the merged type. Their divergence is real (not an artifact of staying hand-written), so `As<T>()`
  throwing for an STU3-tagged node reinterpreted through one of these is correct behavior — the same
  guard ADR-2609 relies on for `Patient`. STU3 typed access to these arrives via ADR-2609's `Stu3.*`
  types, not a shared base.
- `CapabilityStatement`: **excluded from consolidation entirely**, not just deferred pending STU3
  generation. The Application-layer facades (`ResourceComponentJsonNode` and siblings) don't merely
  tolerate STU3 — they implement STU3-specific structural behavior (STU3-only backbones, retyped
  elements) the R4/R5-classified scaffolding cannot represent. Revisit only once ADR-2609 ships and a
  real `Stu3.CapabilityStatement` exists to hold that logic instead.
```

- [ ] **Step 2: Update the readme's Investigations table**

In `docs/features/typed-models/readme.md`, the status cell for the `consolidate-handwritten-facades` row is currently exactly `Proposed` (no `(Phase 0+1a implemented)` suffix — same root cause as Step 1: the earlier plan's Task 4, which would have added that suffix, never ran; only that plan's Task 1, the generator `partial` change, landed — Phase 1a itself, the actual `Extension`/`Narrative` merge, was never done either). Change the status cell from `Proposed` to `Proposed (Phase 0 + Phase 0b implemented; Phase 1a/1b/2/3/4 not started)`, and append to its description: `Bundle/Parameters/OperationOutcome now generated and version-tag-exempt on their base type (not yet merged); CapabilityStatement excluded from consolidation (STU3-only Application logic); Provenance/SearchParameter/StructureDefinition/StructureMap/ConceptMap/Composition to consolidate tagged, not exempt.`

- [ ] **Step 3: Commit**

```bash
git add docs/features/typed-models/investigations/consolidate-handwritten-facades.md docs/features/typed-models/readme.md
git commit -m "docs(typed-models): record Phase 0b normative-type classification and decision"
```

---

## Explicitly out of scope (future plans)

- Merging `BundleJsonNode`/`ParametersJsonNode`/`OperationOutcomeJsonNode` into their now-generated, now-unmarked counterparts. Needs its own investigation pass first: each file declares multiple nested hand-written types beyond the top-level resource (`BundleLinkJsonNode`, `BundleComponentJsonNode`, `ParameterJsonNode`, `OperationOutcomeJsonNode.IssueComponent`, and `OperationOutcomeJsonNode.cs` additionally hand-declares `CodeableConceptJsonNode`/`CodingJsonNode` locally) and an unknown number of call sites — comparable to why Identifier/Reference were deferred out of Phase 1a rather than folded in.
- Consolidating `Provenance`, `SearchParameter`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition` as version-tagged merges.
- ADR-2609 (STU3 as an isolated classification group) — still Proposed, unimplemented. When it ships, `Stu3.Bundle`/`Stu3.Parameters`/etc. would be siblings of the now-unmarked shared base; the two designs compose without rework (per Fable's design analysis).
