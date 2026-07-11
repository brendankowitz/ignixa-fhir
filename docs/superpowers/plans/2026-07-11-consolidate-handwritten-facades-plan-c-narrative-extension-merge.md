# Narrative + Extension Facade Merge Implementation Plan (Plan C)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Perform the first two real hand-facade merges of this consolidation effort — delete the hand-written `NarrativeJsonNode`/`ExtensionJsonNode` classes and repoint every call site at the generated, `partial`-mergeable `Ignixa.Models.Narrative`/`Ignixa.Models.Extension` types — proving the merge pattern end-to-end (delete, repoint, verify) on the two lowest-risk types in the investigation's own phased plan.

**Architecture:** `Narrative` needs zero hand-written code: the generator's `partial class Narrative` already covers every member the hand-written version had (`Status`, `Div`, the nested enum — now backed by a proper `string` accessor after Plan A/A2's `xhtml` and fallback fixes from the original Phase 0 work). It is a pure deletion + repoint. `Extension` is more interesting: two of its members (`ValueString`, `ValueUri`) only exist on the R4/R5 *subclasses* (the classifier excludes `value[x]` from the shared base because the choice-type union genuinely differs by version — verified empirically in Plan A2). Per an explicit decision this session: **no hand-written partial re-adds these to the base.** A base-level hand-written `ValueString`/`ValueUri` would need a C# `new` modifier to avoid a build error, and `new` is compile-time-dispatched — any code holding a base-typed `Extension` reference (the common case) would silently get a simpler, non-choice-clearing implementation instead of the correct one the generated subclass provides. Instead, callers construct a **version-specific** `Extension` when they need `value[x]` access. One call site (`SecurityCapabilitySegment.cs`) determines its FHIR version only at runtime (multi-tenant), so it can't hardcode `Ignixa.Models.R4.Extension` at compile time — for that case, this plan adds one small, genuinely-necessary piece of hand-written functionality: a **static factory method** on the `Extension` partial (`Extension.CreateWithValueUri(FhirVersion?, url, valueUri)`), which dispatches to the right subclass by version. This is safe from the `new`-shadowing trap entirely — it's a new static method, not an instance property shadowing anything generated.

**Tech Stack:** .NET 10 / C#, xunit + Shouldly, the in-repo `Ignixa.Specification.Generators` codegen tool (read-only in this plan — no generator changes).

## Global Constraints

- Nullable reference types enabled; warnings treated as errors — do not introduce new nullable warnings.
- 4-space indentation, file-scoped namespaces, `System.*` usings first outside the namespace.
- One type per file for new hand-written files.
- Test naming: `GivenContext_WhenAction_ThenResult`, AAA pattern, Shouldly assertions, no `#region` blocks.
- Continue on the existing worktree/branch (`worktree-typed-models-facade-consolidation`, PR #326).
- Each task ends with a commit. Do not push without the plan owner's go-ahead for that specific push.
- **Do not add a hand-written `ValueString`/`ValueUri` instance property to `Ignixa.Models.Extension`'s base partial.** This is a deliberate architectural decision (see Architecture above), not an oversight — do not "fix" it back in.
- `Ignixa.Models.Extension`'s nested `Extension` list member is named `Extension2` (the generator renames a member that would otherwise share its enclosing type's name — `CS0542`). Every call site touching the nested list must use `Extension2`, not `Extension`.

---

### Task 1: Merge `NarrativeJsonNode` into `Ignixa.Models.Narrative`

**Files:**
- Delete: `src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs`
- Modify: `src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs:398-416`
- Modify: `src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs:318-332`
- Test: `test/Ignixa.Models.Tests/NarrativeFacadeTests.cs` (new)

**Interfaces:**
- Consumes: `Ignixa.Models.Narrative` (generated, already exists — `sealed partial class Narrative : BaseJsonNode`, members `Status` (`NarrativeStatus?`), `Div`/`DivElement` (`string?`), `Id`/`IdElement`, `Extension` (list — no rename needed here, since the enclosing type is `Narrative`, not `Extension`)). `Ignixa.Models.NarrativeStatus` (generated top-level enum, members `Generated`/`Extensions`/`Additional`/`Empty`).
- Produces: no new hand-written file — every member the old `NarrativeJsonNode` declared is fully covered by the generated type. Nothing survives to hand-write.

- [ ] **Step 1: Delete the hand-written file**

Delete `src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs`.

- [ ] **Step 2: Update `CompositionJsonNode.cs`**

In `src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs`, add a using (after the existing `using Ignixa.Serialization.SourceNodes;` line near the top, before the `namespace` declaration):
```csharp
using Ignixa.Models;
```

Change (lines 398-416):
```csharp
        /// <summary>
        /// Text summary of the section for human interpretation.
        /// </summary>
        [JsonIgnore]
        public NarrativeJsonNode? Text
        {
            get => GetComplexProperty<NarrativeJsonNode>("text");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("text");
                }
                else
                {
                    MutableNode["text"] = value.MutableNode;
                }
            }
        }
```
to:
```csharp
        /// <summary>
        /// Text summary of the section for human interpretation.
        /// </summary>
        [JsonIgnore]
        public Narrative? Text
        {
            get => GetComplexProperty<Narrative>("text");
            set
            {
                if (value is null)
                {
                    MutableNode.Remove("text");
                }
                else
                {
                    MutableNode["text"] = value.MutableNode;
                }
            }
        }
```
(Only the type name changes — `GetComplexProperty<T>` and `value.MutableNode` work identically against the generated `Narrative` type, since `BaseJsonNode.MutableNode` is `internal` and both types compile into assemblies that can see it.)

- [ ] **Step 3: Update `IpsGeneratorService.cs`**

In `src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs`, add a using (this file already has other `Ignixa.Serialization.Models` usings for unrelated types — those stay untouched):
```csharp
using Ignixa.Models;
```

Change (lines 318-322):
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

Change (lines 327-331):
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

`MutableNode()` is the internal-visible-to-tests accessor already used elsewhere in `test/Ignixa.Models.Tests` (e.g. `CrossVersionTests.cs`). If it's not resolvable, check `test/Ignixa.Serialization.TestSupport` for the exact extension method name and use the real one — do not delete the assertions.

- [ ] **Step 5: Build and test**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Application/Ignixa.Application/Ignixa.Application.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
```
Expected: 0 build errors/warnings; `Ignixa.Models.Tests` includes the 5 new `NarrativeFacadeTests` (1 fact + 4 theory cases) passing, total count 47 (42 + 5).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.Serialization/Models/NarrativeJsonNode.cs \
        src/Core/Ignixa.Serialization/Models/CompositionJsonNode.cs \
        src/Application/Ignixa.Application/Features/Experimental/Ips/Generator/IpsGeneratorService.cs \
        test/Ignixa.Models.Tests/NarrativeFacadeTests.cs
git commit -m "refactor(typed-models): merge NarrativeJsonNode into Ignixa.Models.Narrative

No hand-written partial needed -- every member (Status, Div, the nested
enum) is fully covered by the generated facade with no fidelity loss.
First real hand-facade merge completed in this consolidation effort:
one canonical type per resource/datatype, no parallel hand-written
type left behind."
```

---

### Task 2: Merge `ExtensionJsonNode` into `Ignixa.Models.Extension`

**Files:**
- Delete: `src/Core/Ignixa.Serialization/Models/ExtensionJsonNode.cs`
- Create: `src/Core/Ignixa.Serialization/Models/Extension.cs` (hand-written partial: the `CreateWithValueUri` static factory only)
- Modify: `src/Application/Ignixa.Application/Features/Metadata/Models/SecurityComponentJsonNode.cs:11,49`
- Modify: `src/Application/Ignixa.Application/Features/Metadata/Segments/SecurityCapabilitySegment.cs:10,88-132`
- Modify: `src/Core/Ignixa.FhirFakes/Builders/PatientBuilder.cs:14,90,417-449`
- Modify: `test/Ignixa.Api.E2ETests/Operations/GraphQl/GraphQlQueryTests.cs:15,472,477`
- Test: `test/Ignixa.Models.Tests/ExtensionFacadeTests.cs` (new)

**Interfaces:**
- Consumes: `Ignixa.Models.Extension` (generated base — `Url`/`UrlElement`, `Id`/`IdElement`, `Extension2` (nested list, renamed per the Global Constraints note)), `Ignixa.Models.R4.Extension`/`Ignixa.Models.R5.Extension` (generated subclasses — `ValueString`, `ValueUri`, `ValueType`, and the rest of the `value[x]` union, both get/set, verified directly against the generated source; no `SuppressMessage` attribute on the generated `ValueUri` — generated files carry `// <auto-generated/>`, which exempts them from CA1056 by default Roslyn analyzer behavior, so none is needed).
- Produces: `Extension.CreateWithValueUri(FhirVersion? version, string url, string valueUri)` — a static factory on the base `Extension` partial. Task 2's own call sites depend on this exact signature.

- [ ] **Step 1: Delete the hand-written file, create its replacement (factory method only)**

Delete `src/Core/Ignixa.Serialization/Models/ExtensionJsonNode.cs`.

Create `src/Core/Ignixa.Serialization/Models/Extension.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Models;

public partial class Extension
{
    /// <summary>
    /// Creates a version-appropriate <see cref="Extension"/> with <c>url</c> and a string-valued
    /// <c>valueUri</c> already set. <c>value[x]</c> (including <c>valueUri</c>) is only generated on
    /// the R4/R5 subclasses -- it genuinely differs by version, so it's excluded from this shared base
    /// (see docs/features/typed-models/investigations/consolidate-handwritten-facades.md). Callers that
    /// know their target version at compile time should just use
    /// <c>new Ignixa.Models.R4.Extension { ValueUri = ... }</c> directly; this factory exists only for
    /// callers (like tenant-driven CapabilityStatement generation) that only know the version at
    /// runtime.
    /// </summary>
    /// <exception cref="NotSupportedException">
    /// No generated <see cref="Extension"/> subclass exists yet for <paramref name="version"/> (anything
    /// other than R4/R5 -- STU3/R4B/R6 have no generated typed models in this codebase yet).
    /// </exception>
    public static Extension CreateWithValueUri(FhirVersion? version, string url, string valueUri) => version switch
    {
        FhirVersion.R4 => new Ignixa.Models.R4.Extension { FhirVersion = version, Url = url, ValueUri = valueUri },
        FhirVersion.R5 => new Ignixa.Models.R5.Extension { FhirVersion = version, Url = url, ValueUri = valueUri },
        _ => throw new NotSupportedException(
            $"No generated Extension facade exists for FHIR version '{version}' yet -- only R4 and R5 are supported. " +
            "See docs/features/typed-models/investigations/consolidate-handwritten-facades.md."),
    };
}
```

- [ ] **Step 2: Update `SecurityComponentJsonNode.cs`**

In `src/Application/Ignixa.Application/Features/Metadata/Models/SecurityComponentJsonNode.cs`, change the using block (line 11) from:
```csharp
using Ignixa.Serialization.Models;
```
to:
```csharp
using Ignixa.Models;
```
(verified: `Ignixa.Serialization.Models` is imported in this file only for `ExtensionJsonNode`; `CodeableConceptJsonNode` used elsewhere in the same file is declared locally in this file's own namespace.)

Change line 49 from:
```csharp
    public MutableJsonList<ExtensionJsonNode> Extension => GetListProperty<ExtensionJsonNode>("extension");
```
to:
```csharp
    public MutableJsonList<Extension> Extension => GetListProperty<Extension>("extension");
```
(This property is named `Extension` on `SecurityComponentJsonNode`, not on `Ignixa.Models.Extension` itself — no rename collision here, `SecurityComponentJsonNode` isn't named `Extension`.)

- [ ] **Step 3: Update `SecurityCapabilitySegment.cs`**

In `src/Application/Ignixa.Application/Features/Metadata/Segments/SecurityCapabilitySegment.cs`, remove the alias (line 10):
```csharp
using ExtensionJsonNode = Ignixa.Serialization.Models.ExtensionJsonNode;
```
Add instead:
```csharp
using Ignixa.Models;
```

Change (lines 88-92):
```csharp
            var oauthExtension = new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris",
            };
```
to:
```csharp
            var oauthExtension = new Extension
            {
                FhirVersion = context.FhirVersion,
                Url = "http://fhir-registry.smarthealthit.org/StructureDefinition/oauth-uris",
            };
```
(`oauthExtension` never reads/writes `.ValueUri`/`.ValueString` itself — only `.Url` and the nested `.Extension2` list, both on the base — so it does NOT need the version-specific factory. Only the four nested extensions below do.)

Change (lines 94-100):
```csharp
            // Add authorize endpoint
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "authorize",
                ValueUri = smartOptions.AuthorizeUrl,
            });
```
to:
```csharp
            // Add authorize endpoint
            oauthExtension.Extension2.Add(Extension.CreateWithValueUri(context.FhirVersion, "authorize", smartOptions.AuthorizeUrl));
```

Change (lines 102-108) the same way:
```csharp
            // Add token endpoint
            oauthExtension.Extension.Add(new ExtensionJsonNode
            {
                FhirVersion = context.FhirVersion,
                Url = "token",
                ValueUri = smartOptions.TokenUrl,
            });
```
to:
```csharp
            // Add token endpoint
            oauthExtension.Extension2.Add(Extension.CreateWithValueUri(context.FhirVersion, "token", smartOptions.TokenUrl));
```

Change (lines 111-119):
```csharp
            if (!string.IsNullOrEmpty(smartOptions.IntrospectUrl))
            {
                oauthExtension.Extension.Add(new ExtensionJsonNode
                {
                    FhirVersion = context.FhirVersion,
                    Url = "introspect",
                    ValueUri = smartOptions.IntrospectUrl,
                });
            }
```
to:
```csharp
            if (!string.IsNullOrEmpty(smartOptions.IntrospectUrl))
            {
                oauthExtension.Extension2.Add(Extension.CreateWithValueUri(context.FhirVersion, "introspect", smartOptions.IntrospectUrl));
            }
```

Change (lines 122-130):
```csharp
            if (!string.IsNullOrEmpty(smartOptions.RevokeUrl))
            {
                oauthExtension.Extension.Add(new ExtensionJsonNode
                {
                    FhirVersion = context.FhirVersion,
                    Url = "revoke",
                    ValueUri = smartOptions.RevokeUrl,
                });
            }
```
to:
```csharp
            if (!string.IsNullOrEmpty(smartOptions.RevokeUrl))
            {
                oauthExtension.Extension2.Add(Extension.CreateWithValueUri(context.FhirVersion, "revoke", smartOptions.RevokeUrl));
            }
```

Leave line 132 (`security.Extension.Add(oauthExtension);`) unchanged — `security` is a `SecurityComponentJsonNode`, whose own `Extension` property (Step 2) is unaffected by the `Extension2` rename (`SecurityComponentJsonNode` isn't itself named `Extension`).

- [ ] **Step 4: Update `PatientBuilder.cs`**

In `src/Core/Ignixa.FhirFakes/Builders/PatientBuilder.cs`, change the using block (line 14) from `using Ignixa.Serialization.Models;` to `using Ignixa.Models;` (verified: this file uses `Ignixa.Serialization.Models` only for `ExtensionJsonNode`).

Change line 90 from `private readonly List<ExtensionJsonNode> _extensions = [];` to `private readonly List<Extension> _extensions = [];`.

Change line 421 (`public PatientBuilder WithExtension(string url, Action<ExtensionJsonNode> configure)`) to:
```csharp
    public PatientBuilder WithExtension(string url, Action<Ignixa.Models.R4.Extension> configure)
```
(`PatientBuilder` is a test-fixture builder with no existing multi-version concept — defaulting to R4 here matches the codebase-wide single-tenant-defaults-to-R4 convention. This is a public API signature change; `GraphQlQueryTests.cs`, the one known external caller, is updated in Step 6.)

Change line 426 from `var ext = new ExtensionJsonNode();` to `var ext = new Ignixa.Models.R4.Extension();`.

Change line 444 from `var ext = new ExtensionJsonNode();` to `var ext = new Ignixa.Models.R4.Extension();` (the second `WithExtension(string, string)` overload — its own signature doesn't change, only its internal implementation).

Update the doc-comment example at line 417 from:
```csharp
    ///     .WithExtension("http://example.org/ext1", ext => ext.ValueString = "value1")
```
to the same text (unchanged — `ext.ValueString` still works, now against `Ignixa.Models.R4.Extension` instead of the old hand-written type).

- [ ] **Step 5: Write the parity test**

Create `test/Ignixa.Models.Tests/ExtensionFacadeTests.cs`:

```csharp
// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.Models.Tests;

public sealed class ExtensionFacadeTests
{
    [Fact]
    public void GivenExtensionWithUrl_WhenReadBack_ThenValuesRoundTrip()
    {
        var ext = new Extension { Url = "http://example.org/ext1" };

        ext.Url.ShouldBe("http://example.org/ext1");
        ext.MutableNode()["url"]!.GetValue<string>().ShouldBe("http://example.org/ext1");
    }

    [Fact]
    public void GivenExtensionWithNestedExtensions_WhenAddedViaExtension2_ThenBothAreReadable()
    {
        var outer = new Extension { Url = "http://example.org/complex" };

        outer.Extension2.Add(new Ignixa.Models.R4.Extension { Url = "nested1", ValueString = "a" });
        outer.Extension2.Add(new Ignixa.Models.R4.Extension { Url = "nested2", ValueString = "b" });

        outer.Extension2.Count.ShouldBe(2);
        outer.Extension2[0].Url.ShouldBe("nested1");
        outer.Extension2[1].Url.ShouldBe("nested2");
    }

    [Fact]
    public void GivenExistingJsonObject_WhenWrappedAsExtension_ThenAllFieldsAreVisible()
    {
        var node = new JsonObject
        {
            ["url"] = "http://example.org/ext3",
        };

        var ext = new Extension(node);

        ext.Url.ShouldBe("http://example.org/ext3");
    }

    [Fact]
    public void GivenR4Version_WhenCreateWithValueUriCalled_ThenReturnsR4ExtensionWithValueSet()
    {
        var ext = Extension.CreateWithValueUri(FhirVersion.R4, "http://example.org/authorize", "http://example.org/auth-endpoint");

        ext.ShouldBeOfType<Ignixa.Models.R4.Extension>();
        ext.Url.ShouldBe("http://example.org/authorize");
        ((Ignixa.Models.R4.Extension)ext).ValueUri.ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenR5Version_WhenCreateWithValueUriCalled_ThenReturnsR5ExtensionWithValueSet()
    {
        var ext = Extension.CreateWithValueUri(FhirVersion.R5, "http://example.org/authorize", "http://example.org/auth-endpoint");

        ext.ShouldBeOfType<Ignixa.Models.R5.Extension>();
        ((Ignixa.Models.R5.Extension)ext).ValueUri.ShouldBe("http://example.org/auth-endpoint");
    }

    [Fact]
    public void GivenUnsupportedVersion_WhenCreateWithValueUriCalled_ThenThrowsNotSupportedException()
    {
        Should.Throw<NotSupportedException>(() => Extension.CreateWithValueUri(FhirVersion.Stu3, "url", "value"));
    }
}
```

- [ ] **Step 6: Update `GraphQlQueryTests.cs`**

In `test/Ignixa.Api.E2ETests/Operations/GraphQl/GraphQlQueryTests.cs`, change the using block (line 15) from `using Ignixa.Serialization.Models;` to `using Ignixa.Models;` (verified: this file uses `Ignixa.Serialization.Models` only for `ExtensionJsonNode`).

Change lines 472 and 477 from:
```csharp
                    ext.Extension.Add(new ExtensionJsonNode(new JsonObject
```
to:
```csharp
                    ext.Extension2.Add(new Extension(new JsonObject
```
(`ext` is the `Action<Ignixa.Models.R4.Extension>` lambda parameter from `PatientBuilder.WithExtension`, Step 4 — its nested-list member is `Extension2`, same reasoning as `SecurityCapabilitySegment`'s `oauthExtension`.)

- [ ] **Step 7: Build and test**

```bash
dotnet build src/Core/Ignixa.Serialization/Ignixa.Serialization.csproj src/Application/Ignixa.Application/Ignixa.Application.csproj src/Core/Ignixa.FhirFakes/Ignixa.FhirFakes.csproj
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
```
Expected: 0 build errors/warnings; `Ignixa.Models.Tests` includes the 6 new `ExtensionFacadeTests` passing, total count 53 (47 after Task 1 + 6). The E2E test project (`Ignixa.Api.E2ETests`) requires the full API test harness — attempt `dotnet build test/Ignixa.Api.E2ETests/Ignixa.Api.E2ETests.csproj` to confirm it at least compiles; if the harness can't run in this environment to execute `GraphQlQueryTests`, report DONE_WITH_CONCERNS noting the E2E run was skipped, but do not skip the build-compiles check or the `Ignixa.Models.Tests` run.

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
ValueString/ValueUri are deliberately NOT re-added as a hand-written
base partial -- they only exist on the R4/R5 subclasses (real value[x]
divergence by version), and a base-level hand-written version would need
a `new` modifier that silently gives base-typed callers the wrong
(non-choice-clearing) behavior. SecurityCapabilitySegment's runtime-
version-dynamic construction gets a small static factory,
Extension.CreateWithValueUri(FhirVersion?, url, value), instead --
compile-time-known-version callers should construct the specific
subclass directly. The recursive nested-extension-list member is
Extension2 (a member cannot share its enclosing type's name); all call
sites updated. PatientBuilder.WithExtension's Action<T> delegate now
targets Ignixa.Models.R4.Extension (a public signature change, sole
external caller updated in the same commit)."
```

---

### Task 3: Full verification, docs update

**Files:**
- Modify: `docs/features/typed-models/investigations/consolidate-handwritten-facades.md`
- Modify: `docs/features/typed-models/readme.md`

**Interfaces:**
- Consumes: nothing new — this task verifies the whole solution and updates documentation to match what Tasks 1-2 actually shipped.

- [ ] **Step 1: Full solution build**

```bash
dotnet build All.sln
```
Expected: 0 errors, 0 warnings.

- [ ] **Step 2: Full test run for every project touched by Tasks 1-2**

```bash
dotnet test test/Ignixa.Models.Tests/Ignixa.Models.Tests.csproj
dotnet test test/Ignixa.Models.R4.Tests/Ignixa.Models.R4.Tests.csproj
dotnet test test/Ignixa.Serialization.Tests/Ignixa.Serialization.Tests.csproj
dotnet test test/Ignixa.Application.Tests/Ignixa.Application.Tests.csproj
```
Expected: all pass, 0 failures. `Ignixa.Models.Tests` at 53 (see Task 1/2 step-by-step counts). If `Ignixa.Application.Tests` has no tests directly covering `SecurityCapabilitySegment`, that's fine — the E2E path (Task 2 Step 7) is the real coverage for that file; just confirm the project builds and its existing tests aren't broken.

- [ ] **Step 3: Verify current investigation-doc structure before editing**

```bash
grep -n "^## " docs/features/typed-models/investigations/consolidate-handwritten-facades.md
```
Confirm `## Resource-typed and contentReference accessor status (implemented)` (added by Plan A2) is present, immediately followed by `## Verdict`. If the structure doesn't match, stop and report NEEDS_CONTEXT with the actual heading list — do not assume.

- [ ] **Step 4: Add a status section**

Insert a new `##`-level section immediately after `## Resource-typed and contentReference accessor status (implemented)`'s content and before `## Verdict`:

```markdown

## Phase 1 status (in progress): first real merges

`Narrative` and `Extension` are merged — the first two of the 41 hand-written `*JsonNode` facades this
investigation set out to consolidate. `Narrative` needed zero hand-written code (fully generator-covered).
`Extension` needed one small hand-written addition: a static factory method,
`Extension.CreateWithValueUri(FhirVersion?, url, value)`, for the one call site
(`SecurityCapabilitySegment.cs`) that only knows its target FHIR version at runtime (multi-tenant
CapabilityStatement generation) — every other call site constructs the version-specific subclass
directly at compile time.

**Decision recorded:** `ValueString`/`ValueUri` are deliberately *not* re-added as hand-written instance
members on `Extension`'s shared base, even though the old hand-written `ExtensionJsonNode` had them. They
only exist on the R4/R5 subclasses today (the classifier excludes `value[x]` from the base — its
choice-type union genuinely differs by version, confirmed empirically during Plan A2). A base-level
hand-written version would need a `new` modifier to avoid a build error, and `new` is
compile-time-dispatched: any code holding a base-typed `Extension` reference — the common case after a
merge — would silently get a simpler, non-choice-clearing implementation instead of the version-correct
one the generated subclass already provides. This establishes the pattern for every future merge in this
effort: **when a hand-written member's semantics only make sense for a specific version, express that as
a version-specific accessor (or a version-dispatching static factory, if the caller doesn't know its
version until runtime) — never as a same-named hand-written member on the shared base that could silently
shadow the correct generated behavior.**

Remaining Phase 1 datatypes: `Identifier`, `Reference`, `Meta` (see the Phased plan section above).
`Identifier`/`Reference` were previously blocked on the generator's `Reference`-typed-element fallback gap
— resolved by Plan A. `Meta` needs its own plan: its deltas are semantic, not structural (hand
`LastUpdated` is `DateTimeOffset?` vs. generated `string?`; hand `Tags`/`Security` are spec-incorrect
`MutablePrimitiveList<string>` vs. generated spec-correct `Coding`-typed lists) — plus `ResourceJsonNode.Meta`
being the `Meta` property on every resource in the codebase makes this a full-suite-regression-review
change, not a contained one.
```

- [ ] **Step 5: Update the readme's Investigations table**

In `docs/features/typed-models/readme.md`, find the `consolidate-handwritten-facades` row and change the status cell from `Proposed (Phase 0 + Phase 0b implemented; Phase 1a/1b/2/3/4 not started)` to `Proposed (Phase 0 + Phase 0b implemented; Phase 1 in progress — Narrative/Extension merged)`.

- [ ] **Step 6: Commit**

```bash
git add docs/features/typed-models/investigations/consolidate-handwritten-facades.md docs/features/typed-models/readme.md
git commit -m "docs(typed-models): record Narrative/Extension merges, the version-dispatch factory pattern"
```

---

## Explicitly out of scope (future plans)

- **`Identifier`/`Reference` merges** — fully unblocked by Plan A/A2, not yet scheduled as their own plan. Lower call-site count than Extension (verified earlier this session: `Identifier` has zero external references, `Reference` has one).
- **`Meta` merge** — needs its own plan per the semantic-delta reasoning in Task 3's doc update.
- **Phases 2-4** (`Provenance`/`SearchParameter`/`StructureDefinition`/`StructureMap`/`ConceptMap`/`Composition`, then `OperationOutcome`/`Parameters`/`Bundle`) — scoped in detail by prior research this session, not started.
