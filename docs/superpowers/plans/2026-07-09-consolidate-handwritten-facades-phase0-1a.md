# Consolidate Hand-Written Facades — Phase 0 + Phase 1a Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the typed-model generator emit `partial` classes, then merge the `Extension` and `Narrative` hand-written `*JsonNode` facades into their generated `Ignixa.Models` counterparts as single types, proving the consolidation pattern from `docs/features/typed-models/investigations/consolidate-handwritten-facades.md` on the two datatypes that need zero generator-fidelity workarounds beyond the one already applied here.

**Architecture:** The generator (`codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`) currently emits non-`partial` classes under namespace `Ignixa.Models`. Making them `partial` lets a hand-maintained file declare additional members (business logic the generator can't derive from a StructureDefinition) in the *same* type, instead of a second, differently-named type existing alongside it. This task also fixes a real generator gap discovered during research: `xhtml`-typed elements (only `Narrative.div` in the FHIR spec) fell back to raw `JsonNode` instead of `string`, which would have blocked a faithful `Narrative` merge.

**Tech Stack:** .NET 10 / C#, xunit + Shouldly, the in-repo `Ignixa.Specification.Generators` codegen tool (consumes `Microsoft.Health.Fhir.CodeGen`, a git submodule).

## Global Constraints

- Nullable reference types enabled; warnings treated as errors (project-wide `.editorconfig`/`Directory.Build.props` convention — do not introduce new nullable warnings).
- 4-space indentation, file-scoped namespaces, `System.*` usings first outside the namespace.
- One type per file for new hand-written files (existing generated files are the generator's own concern, not this rule).
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA pattern (Arrange-Act-Assert), Shouldly assertions (`.ShouldBe(...)`), no `#region` blocks.
- Do not touch `Ignixa.Models.Identifier`, `Ignixa.Models.Reference`, `Ignixa.Models.Meta`, or any of the 10 PR-#319-reserved resources (`Bundle`, `OperationOutcome`, `Parameters`, `Provenance`, `SearchParameter`, `CapabilityStatement`, `StructureDefinition`, `StructureMap`, `ConceptMap`, `Composition`'s own class) — those are explicitly out of scope for this plan (see investigation doc's Phase 2/3/4 and the `Identifier`/`Reference` fallback-fidelity blocker). This plan only touches the `Extension` and `Narrative` **datatypes**, and files that reference them.
- Each task ends with a commit. The user has already authorized pushing the final branch (`worktree-typed-models-facade-consolidation`) once all tasks are complete and reviewed — do not push before Task 4 finishes.
- The generator's submodule dependency (`codegen/fhir-codegen`) must be initialized before the generator will build: `git submodule update --init codegen/fhir-codegen`. If `git submodule status` shows a `-` prefix on that path, run the init command first.

---

### Task 1: Generator — emit `partial` classes, fix `xhtml` fallback, regenerate

**Files:**
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:30-41` (add `xhtml` to two HashSets)
- Modify: `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs:813` (add `partial` keyword)
- Regenerate (do not hand-edit): `src/Core/Ignixa.Serialization/Generated/Models/**/*.cs`, `src/Core/Models/Ignixa.Models.R4/Generated/**/*.cs`, `src/Core/Models/Ignixa.Models.R5/Generated/**/*.cs`

**Interfaces:**
- Produces: every generated facade class becomes `partial` (e.g. `public sealed partial class Narrative : BaseJsonNode`), so Task 2 and Task 3 can add a same-named hand-written partial file. `Narrative.Div` becomes `string? Div` (was `JsonNode? Div`) — Task 3 depends on this exact type.

- [ ] **Step 1: Confirm the submodule is initialized**

Run: `git submodule status`
Expected: `codegen/fhir-codegen` line has NO leading `-`. If it does, run:
```bash
git submodule update --init codegen/fhir-codegen
```

- [ ] **Step 2: Edit the two primitive-type HashSets**

In `codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs`, change:

```csharp
    private static readonly HashSet<string> PrimitiveTypeNames =
    [
        "boolean", "integer", "string", "decimal", "uri", "url", "canonical", "base64Binary",
        "instant", "date", "dateTime", "time", "code", "oid", "id", "markdown", "unsignedInt",
        "positiveInt", "uuid", "integer64",
    ];

    private static readonly HashSet<string> StringLikePrimitives =
    [
        "string", "code", "uri", "url", "canonical", "oid", "id", "markdown", "base64Binary",
        "date", "dateTime", "time", "instant", "uuid",
    ];
```

to:

```csharp
    private static readonly HashSet<string> PrimitiveTypeNames =
    [
        "boolean", "integer", "string", "decimal", "uri", "url", "canonical", "base64Binary",
        "instant", "date", "dateTime", "time", "code", "oid", "id", "markdown", "unsignedInt",
        "positiveInt", "uuid", "integer64", "xhtml",
    ];

    private static readonly HashSet<string> StringLikePrimitives =
    [
        "string", "code", "uri", "url", "canonical", "oid", "id", "markdown", "base64Binary",
        "date", "dateTime", "time", "instant", "uuid", "xhtml",
    ];
```

`xhtml` only appears in the FHIR spec as `Narrative.div`'s type. Without this, the generator treats it as an untyped element and emits `EmitFallback` (raw `JsonNode? Div`) instead of a proper string accessor — confirmed by running the generator before this change (log line: `[Ignixa.Models] JsonNode fallback: Narrative.div (xhtml)`).

- [ ] **Step 3: Add `partial` to the emitted class declaration**

In the same file, find (around line 813):

```csharp
        sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}class {typeName} : {baseClass}");
```

Change to:

```csharp
        sb.AppendLine($"public {(sealedType ? "sealed " : string.Empty)}partial class {typeName} : {baseClass}");
```

- [ ] **Step 4: Regenerate and verify the diff is exactly the expected shape**

Run:
```bash
dotnet run --project codegen/Ignixa.Specification.Generators -- typed-model
```
Expected: ends with `✓ Generation complete!` and no error output.

Then:
```bash
git diff --stat -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated
```
Expected: **135 files changed, 141 insertions(+), 139 deletions(-)** (verified exact numbers from a dry run of this exact change against the current generator state — if your numbers differ, the FHIR package cache or generator classification has changed since this plan was written; stop and compare against a fresh `git diff` reading before proceeding, don't assume it's fine).

Then confirm the diff contains only two kinds of changes — the `partial` insertion on every class line, and the `Narrative.Div` fidelity fix:
```bash
git diff -- src/Core/Ignixa.Serialization/Generated/Models src/Core/Models/Ignixa.Models.R4/Generated src/Core/Models/Ignixa.Models.R5/Generated | grep -E "^\+|^-" | grep -v "^+++|^---" | grep -v "partial class"
```
Expected output is exactly these 8 lines (order may vary):
```
-    // fallback: xhtml
-    public JsonNode? Div
+    public PrimitiveElement<string> DivElement => new(MutableNode, "div");
+    [JsonIgnore]
+    public string? Div
-        get => MutableNode["div"];
-        set => SetProperty("div", value);
+        get => DivElement.Value;
+        set => DivElement.Value = value;
```
If anything else appears, stop — do not proceed to Task 2/3 with an unexpected regen diff. Report BLOCKED with the unexpected lines.

- [ ] **Step 5: Build and run the existing typed-model test suites**

Run:
```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Core/Models/Ignixa.Models.R4/Ignixa.Models.R4.csproj src/Core/Models/Ignixa.Models.R5/Ignixa.Models.R5.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
```
Expected: build 0 errors, both test projects 100% pass (same pass count as before this change — the `partial` keyword and `xhtml` fix are additive and should not change any existing test's behavior).

- [ ] **Step 6: Commit**

```bash
git add codegen/Ignixa.Specification.Generators/CSharpTypedModelLanguage.cs \
        src/Core/Ignixa.Serialization/Generated/Models \
        src/Core/Models/Ignixa.Models.R4/Generated \
        src/Core/Models/Ignixa.Models.R5/Generated
git commit -m "feat(typed-models): emit partial classes, fix xhtml fallback

Generated facades are now partial, enabling hand-written business-logic
members to live in the same type instead of a second, differently-named
type (see docs/features/typed-models/investigations/consolidate-handwritten-facades.md).
Also fixes xhtml (Narrative.div) falling back to raw JsonNode instead of
a typed string accessor -- xhtml only appears in this one FHIR element."
```

---

### Task 2: Merge `ExtensionJsonNode` into `Ignixa.Models.Extension`

**Files:**
- Delete: `src/Core/Ignixa.Serialization/Models/ExtensionJsonNode.cs`
- Create: `src/Core/Ignixa.Serialization/Models/Extension.cs`
- Modify: `src/Application/Ignixa.Application/Features/Metadata/Models/SecurityComponentJsonNode.cs:11,49`
- Modify: `src/Application/Ignixa.Application/Features/Metadata/Segments/SecurityCapabilitySegment.cs:10,88,95,103,113,124`
- Modify: `src/Core/Ignixa.FhirFakes/Builders/PatientBuilder.cs:14,90,421,426,444`
- Modify: `test/Ignixa.Api.E2ETests/Operations/GraphQl/GraphQlQueryTests.cs:15,472,477`
- Test: `test/Ignixa.Models.Tests/ExtensionFacadeTests.cs` (new)

**Interfaces:**
- Consumes: `Ignixa.Models.Extension` (generated, from Task 1 — `partial class Extension : BaseJsonNode`, members `Extension2` (nested list, renamed by the generator's collision guard since a member can't share its enclosing type's name), `Id`/`IdElement`, `Url`/`UrlElement`).
- Produces: `Ignixa.Models.Extension` now also carries `ValueUri` (`string?`) and `ValueString` (`string?`) — the two `value[x]` variants this codebase actually uses. Any other code that needs `Extension` (there is none yet outside the files listed above) references `Ignixa.Models.Extension`.

- [ ] **Step 1: Delete the hand-written file and create its replacement**

Delete `src/Core/Ignixa.Serialization/Models/ExtensionJsonNode.cs`.

Create `src/Core/Ignixa.Serialization/Models/Extension.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Ignixa.Models;

public partial class Extension
{
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "FHIR valueUri is a string.")]
    [JsonIgnore]
    public string? ValueUri
    {
        get => GetProperty<string>("valueUri");
        set => SetProperty("valueUri", value);
    }

    [JsonIgnore]
    public string? ValueString
    {
        get => GetProperty<string>("valueString");
        set => SetProperty("valueString", value);
    }
}
```

Everything else the old `ExtensionJsonNode` declared (`Url`, the parameterless/2-arg constructors, the nested `Extension` list) is now provided by the generated part: `Url` as `string?` (was non-nullable `string` in the old hand-written code, which was already capable of returning `null` at runtime via `GetProperty<string>`'s `default` fallback — the generated nullable annotation is more honest, not a behavior change), both constructors identically (the old parameterless ctor's `this(new JsonObject(), null)` was equivalent to what `BaseJsonNode()`'s own parameterless ctor already does), and the nested list as `Extension2` (renamed by the generator because a member cannot share its enclosing type's name — `CS0542`).

- [ ] **Step 2: Update `SecurityComponentJsonNode.cs`**

In `src/Application/Ignixa.Application/Features/Metadata/Models/SecurityComponentJsonNode.cs`, change the using block (line 11) from:
```csharp
using Ignixa.Serialization.Models;
```
to:
```csharp
using Ignixa.Models;
```
(verified: `Ignixa.Serialization.Models` is imported in this file only for `ExtensionJsonNode`; `CodeableConceptJsonNode` used elsewhere in the same file is declared locally in this file's own namespace, not imported from `Ignixa.Serialization.Models`.)

Change line 49 from:
```csharp
    public MutableJsonList<ExtensionJsonNode> Extension => GetListProperty<ExtensionJsonNode>("extension");
```
to:
```csharp
    public MutableJsonList<Extension> Extension => GetListProperty<Extension>("extension");
```

- [ ] **Step 3: Update `SecurityCapabilitySegment.cs`**

In `src/Application/Ignixa.Application/Features/Metadata/Segments/SecurityCapabilitySegment.cs`, remove the alias (line 10):
```csharp
using ExtensionJsonNode = Ignixa.Serialization.Models.ExtensionJsonNode;
```
Add instead:
```csharp
using Ignixa.Models;
```

Change line 88 from `new ExtensionJsonNode` to `new Extension` (the outer `oauthExtension` object).

Change lines 95, 103, 113, 124 from `oauthExtension.Extension.Add(new ExtensionJsonNode` to `oauthExtension.Extension2.Add(new Extension` — the `Extension` (nested-list) member on the `Extension` type itself is named `Extension2` (see Step 1's note); this is the recursive "extension on an extension" case, which is exactly what these 4 call sites do (adding nested `authorize`/`token`/`introspect`/`revoke` sub-extensions to the `oauth-uris` extension).

Do NOT change line 132 (`security.Extension.Add(oauthExtension);`) — `security` is a `SecurityComponentJsonNode`, whose own `Extension` property (Step 2) is unaffected by this rename since `SecurityComponentJsonNode` is not itself named `Extension`.

- [ ] **Step 4: Update `PatientBuilder.cs`**

In `src/Core/Ignixa.FhirFakes/Builders/PatientBuilder.cs`, change the using block (line 14) from `using Ignixa.Serialization.Models;` to `using Ignixa.Models;` (verified: this file uses `Ignixa.Serialization.Models` only for `ExtensionJsonNode`).

Change line 90 from `private readonly List<ExtensionJsonNode> _extensions = [];` to `private readonly List<Extension> _extensions = [];`.

Change line 421 from `public PatientBuilder WithExtension(string url, Action<ExtensionJsonNode> configure)` to `public PatientBuilder WithExtension(string url, Action<Extension> configure)`.

Change lines 426 and 444 from `var ext = new ExtensionJsonNode();` to `var ext = new Extension();`.

- [ ] **Step 5: Update `GraphQlQueryTests.cs`**

In `test/Ignixa.Api.E2ETests/Operations/GraphQl/GraphQlQueryTests.cs`, change the using block (line 15) from `using Ignixa.Serialization.Models;` to `using Ignixa.Models;` (verified: this file uses `Ignixa.Serialization.Models` only for `ExtensionJsonNode`).

Change lines 472 and 477 from:
```csharp
                    ext.Extension.Add(new ExtensionJsonNode(new JsonObject
```
to:
```csharp
                    ext.Extension2.Add(new Extension(new JsonObject
```
(`ext` is the `Action<Extension>` lambda parameter from `PatientBuilder.WithExtension`, Step 4 — its nested-list member is `Extension2`, same reasoning as Step 3.)

- [ ] **Step 6: Write the parity test**

Create `test/Ignixa.Models.Tests/ExtensionFacadeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ExtensionFacadeTests
{
    [Fact]
    public void GivenExtensionWithUrlAndValueString_WhenReadBack_ThenValuesRoundTrip()
    {
        var ext = new Extension
        {
            Url = "http://example.org/ext1",
            ValueString = "hello",
        };

        ext.Url.ShouldBe("http://example.org/ext1");
        ext.ValueString.ShouldBe("hello");
        ext.MutableNode()["url"]!.GetValue<string>().ShouldBe("http://example.org/ext1");
        ext.MutableNode()["valueString"]!.GetValue<string>().ShouldBe("hello");
    }

    [Fact]
    public void GivenExtensionWithValueUri_WhenReadBack_ThenValueRoundTrips()
    {
        var ext = new Extension { Url = "http://example.org/ext2", ValueUri = "http://example.org/target" };

        ext.ValueUri.ShouldBe("http://example.org/target");
    }

    [Fact]
    public void GivenExtensionWithNestedExtensions_WhenAddedViaExtension2_ThenBothAreReadable()
    {
        var outer = new Extension { Url = "http://example.org/complex" };

        outer.Extension2.Add(new Extension { Url = "nested1", ValueString = "a" });
        outer.Extension2.Add(new Extension { Url = "nested2", ValueString = "b" });

        outer.Extension2.Count.ShouldBe(2);
        outer.Extension2[0].Url.ShouldBe("nested1");
        outer.Extension2[1].ValueString.ShouldBe("b");
    }

    [Fact]
    public void GivenExistingJsonObject_WhenWrappedAsExtension_ThenAllFieldsAreVisible()
    {
        var node = new JsonObject
        {
            ["url"] = "http://example.org/ext3",
            ["valueString"] = "wrapped",
        };

        var ext = new Extension(node);

        ext.Url.ShouldBe("http://example.org/ext3");
        ext.ValueString.ShouldBe("wrapped");
    }
}
```

Note: `MutableNode()` above is the internal-visible-to-tests accessor already used elsewhere in `test/Ignixa.Models.Tests` (see `CrossVersionTests.cs`'s `p4.MutableNode()` calls) — this project has `InternalsVisibleTo` access via `Ignixa.Serialization.TestSupport` (already referenced in the `.csproj`, per Task 1's Step 5 build). If `MutableNode()` is not resolvable, check `test/Ignixa.Serialization.TestSupport` for the exact extension method name and adjust the two calls in the first test accordingly — do not delete the assertions, fix the accessor name.

- [ ] **Step 7: Build and test**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Application/Ignixa.Application/Ignixa.Application.csproj src/Core/Ignixa.FhirFakes/Ignixa.FhirFakes.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj --filter "FullyQualifiedName~GraphQlQueryTests"
```
Expected: 0 build errors/warnings; `Ignixa.Models.Tests` includes the 4 new `ExtensionFacadeTests` passing; the two `GraphQlQueryTests` nested-extension tests (`GivenPatientWithNestedExtensions_WhenFilteringByUrl_ThenReturnsMatchingNestedExtensions` and its sibling) pass unchanged (E2E tests require the API test harness — if the harness can't run in this environment, report DONE_WITH_CONCERNS noting the E2E filter step was skipped, but do not skip the `Ignixa.Models.Tests` run).

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.Serialization/Models/ExtensionJsonNode.cs \
        src/Core/Ignixa.Serialization/Models/Extension.cs \
        src/Application/Ignixa.Application/Features/Metadata/Models/SecurityComponentJsonNode.cs \
        src/Application/Ignixa.Application/Features/Metadata/Segments/SecurityCapabilitySegment.cs \
        src/Core/Ignixa.FhirFakes/Builders/PatientBuilder.cs \
        test/Ignixa.Api.E2ETests/Operations/GraphQl/GraphQlQueryTests.cs \
        test/Ignixa.Models.Tests/ExtensionFacadeTests.cs
git commit -m "refactor(typed-models): merge ExtensionJsonNode into Ignixa.Models.Extension

Single partial-class type per docs/features/typed-models/investigations/consolidate-handwritten-facades.md.
The recursive nested-extension-list member is Extension2 (a member
cannot share its enclosing type's name); all 4 call sites updated."
```

---

### Task 3: Merge `NarrativeJsonNode` into `Ignixa.Models.Narrative`

**Files:**
- Delete: `src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs`
- Modify: `src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs:10,402,404`
- Modify: `src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs:318-331`
- Test: `test/Ignixa.Models.Tests/NarrativeFacadeTests.cs` (new)

**Interfaces:**
- Consumes: `Ignixa.Models.Narrative` (generated, from Task 1 — `sealed partial class Narrative : BaseJsonNode`, members `Div` (`string?`, fixed by Task 1's `xhtml` change), `Extension` (nested list — no name collision here since the enclosing type is `Narrative`, not `Extension`), `Id`/`IdElement`, `Status` (`NarrativeStatus?`)). `Ignixa.Models.NarrativeStatus` (generated, top-level enum in `Ignixa.Models`, members `Generated`/`Extensions`/`Additional`/`Empty` — same literal names as the old nested `NarrativeJsonNode.NarrativeStatus`).
- Produces: no new hand-written file — every member the old `NarrativeJsonNode` declared (`Status`, `Div`, the two private parse helpers, the nested enum) is now fully covered by the generated type with no fidelity loss, so nothing survives to hand-write. This is the simplest possible case of the merge pattern: delete the hand-written file, repoint call sites at the generated type.

- [ ] **Step 1: Delete the hand-written file**

Delete `src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs`.

- [ ] **Step 2: Update `CompositionJsonNode.cs`**

Add a using (after line 9, before the `namespace` declaration) in `src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs`:
```csharp
using Ignixa.Models;
```

Change lines 402 and 404 from:
```csharp
        public NarrativeJsonNode? Text
        {
            get => GetComplexProperty<NarrativeJsonNode>("text");
```
to:
```csharp
        public Narrative? Text
        {
            get => GetComplexProperty<Narrative>("text");
```
(the setter body at lines 405-414 is unchanged — it already uses `value.MutableNode` generically, which works identically against the generated `Narrative` type, verified: `BaseJsonNode.MutableNode` is `internal`, and generated facades compile into the same `Ignixa.Serialization` assembly as `CompositionJsonNode`.)

- [ ] **Step 3: Update `IpsGeneratorService.cs`**

Add a using in `src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs` (this file already has `using Ignixa.Serialization.Models;` at line 23, which stays — it's still used for `ReferenceJsonNode`/`CodeableConceptJsonNode` in this same file, untouched by this plan):
```csharp
using Ignixa.Models;
```

Change lines 318-322 from:
```csharp
            sectionComponent.Text = new NarrativeJsonNode
            {
                Status = NarrativeJsonNode.NarrativeStatus.Generated,
                Div = GenerateSectionNarrative(section, resources)
            };
```
to:
```csharp
            sectionComponent.Text = new Narrative
            {
                Status = NarrativeStatus.Generated,
                Div = GenerateSectionNarrative(section, resources)
            };
```

Change lines 327-331 from:
```csharp
            sectionComponent.Text = new NarrativeJsonNode
            {
                Status = NarrativeJsonNode.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>No {section.Title.ToLower(CultureInfo.InvariantCulture)} information available.</p></div>"
            };
```
to:
```csharp
            sectionComponent.Text = new Narrative
            {
                Status = NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>No {section.Title.ToLower(CultureInfo.InvariantCulture)} information available.</p></div>"
            };
```

- [ ] **Step 4: Write the parity test**

Create `test/Ignixa.Models.Tests/NarrativeFacadeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class NarrativeFacadeTests
{
    [Fact]
    public void GivenNarrativeWithStatusAndDiv_WhenReadBack_ThenValuesRoundTrip()
    {
        var narrative = new Narrative
        {
            Status = NarrativeStatus.Generated,
            Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>",
        };

        narrative.Status.ShouldBe(NarrativeStatus.Generated);
        narrative.Div.ShouldBe("<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>");
        narrative.MutableNode()["status"]!.GetValue<string>().ShouldBe("generated");
        narrative.MutableNode()["div"]!.GetValue<string>().ShouldBe("<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>hello</p></div>");
    }

    [Theory]
    [InlineData(NarrativeStatus.Generated, "generated")]
    [InlineData(NarrativeStatus.Extensions, "extensions")]
    [InlineData(NarrativeStatus.Additional, "additional")]
    [InlineData(NarrativeStatus.Empty, "empty")]
    public void GivenEachStatusValue_WhenSerialized_ThenMatchesFhirLiteral(NarrativeStatus status, string expectedLiteral)
    {
        var narrative = new Narrative { Status = status };

        narrative.MutableNode()["status"]!.GetValue<string>().ShouldBe(expectedLiteral);
    }
}
```

- [ ] **Step 5: Build and test**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Application/Ignixa.Application/Ignixa.Application.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
```
Expected: 0 build errors/warnings; the 5 new `NarrativeFacadeTests` (1 fact + 4 theory cases) pass.

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs \
        src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs \
        src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs \
        test/Ignixa.Models.Tests/NarrativeFacadeTests.cs
git commit -m "refactor(typed-models): merge NarrativeJsonNode into Ignixa.Models.Narrative

No hand-written partial needed -- every member (Status, Div, the nested
enum) is now fully covered by the generated facade with no fidelity
loss, now that Task 1 fixed the xhtml (Div) fallback."
```

---

### Task 4: Full verification, docs update, push

**Files:**
- Modify: `docs/features/typed-models/investigations/consolidate-handwritten-facades.md` (status update)
- Modify: `docs/features/typed-models/readme.md` (Investigations table status update)

**Interfaces:**
- Consumes: nothing new — this task only verifies the whole solution and updates documentation to match what Tasks 1-3 actually shipped.

- [ ] **Step 1: Full solution build**

```bash
dotnet build All.sln
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Full test run for every project touched by Tasks 1-3**

```bash
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
dotnet test test/Ignixa.Serialization.Tests/Ignixa.Serialization.Tests.csproj
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj --filter "FullyQualifiedName~Ips|FullyQualifiedName~Security"
```
Expected: all pass, 0 failures. If `Ignixa.Application.Tests` has no tests matching that filter, run the full project instead and confirm 0 failures.

- [ ] **Step 3: Update the investigation doc**

In `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`, change the header:
```markdown
**Status**: Proposed
**Created**: 2026-07-09
```
to:
```markdown
**Status**: Proposed (Phase 0 + Phase 1a implemented; Phase 1b/2/3/4 not started)
**Created**: 2026-07-09
```

At the end of the "Phased plan" section, add:
```markdown

### Phase 0 + Phase 1a status (implemented)

Generator now emits `partial` classes (one-line change) plus a fix for `xhtml`-typed elements
(`Narrative.div`) that previously fell back to raw `JsonNode`. Two datatypes fully merged:
`Extension` (hand-written partial retains `ValueUri`/`ValueString`, the two `value[x]` variants
this codebase uses — everything else is generator-owned) and `Narrative` (no hand-written partial
needed at all; every member is now generator-covered).

**Phase 1b, explicitly deferred**: `Identifier` and `Reference` were investigated and found blocked
on a separate, real generator gap: any `Reference`-typed *element* (e.g. `Identifier.assigner`,
`Patient.generalPractitioner`, `Observation.subject` — dozens of elements across the whole generated
surface) falls back to raw `JsonNode` instead of the typed `Reference` facade, because
`CSharpTypedModelLanguage.AbstractOrFallbackTypes` still special-cases `"Reference"` as a
fallback-only type from before a standalone `Reference` facade existed. Fixing this (removing
`"Reference"` from that set) is a larger, more broadly-rippling generator change than the narrow
`xhtml` fix here — it touches every generated file with a `Reference`-typed element, not just one
type — and deserves its own plan with its own regen-diff review, not a fold-in here. `Meta` was
also excluded from this increment: it has no fidelity gap, but `ResourceJsonNode.Meta` is the
`Meta` property on every single resource in the codebase (hand-written and generated alike), so
that merge is a shared-runtime-base change deserving full-suite regression review in its own task,
not bundled with two contained datatypes.
```

- [ ] **Step 4: Update the readme's Investigations table**

In `docs/features/typed-models/readme.md`, change:
```markdown
| [consolidate-handwritten-facades](investigations/consolidate-handwritten-facades.md) | Proposed | Merges the 39 hand-written `*JsonNode` facades into the generated base via `partial class`, one type per resource — no rename-and-flip, no `ResourceTypeRegistry` dual-dispatch window. Phased: datatypes → contained resources → Application-layer facades → load-bearing `Bundle`/`OperationOutcome`/`Parameters` last. |
```
to:
```markdown
| [consolidate-handwritten-facades](investigations/consolidate-handwritten-facades.md) | Proposed (Phase 0+1a implemented) | Merges the 41 hand-written `*JsonNode` facades into the generated base via `partial class`, one type per resource — no rename-and-flip, no `ResourceTypeRegistry` dual-dispatch window. `Extension`/`Narrative` merged; `Identifier`/`Reference` blocked on a generator fallback gap; `Meta`/contained resources/Application facades/load-bearing resources not started. |
```

- [ ] **Step 5: Commit the doc updates**

```bash
git add docs/features/typed-models/investigations/consolidate-handwritten-facades.md docs/features/typed-models/readme.md
git commit -m "docs(typed-models): mark facade-consolidation Phase 0+1a implemented"
```

- [ ] **Step 6: Push the branch**

```bash
git push -u origin worktree-typed-models-facade-consolidation
```
Expected: push succeeds, prints the new remote branch and a compare/PR URL. Report the URL back — do not open a PR unless separately asked.
