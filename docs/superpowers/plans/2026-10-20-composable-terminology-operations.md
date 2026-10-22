# Composable Terminology Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace whole-call terminology routing (`HybridTerminologyService`) with a per-code-system composing service. Every operation must say explicitly when it cannot determine an answer, and the public experimental API must cover lookup, validate-code, expand, translate, subsumes, and find-matches with R4-correct semantics.

**Architecture:** `ComposingTerminologyService` (in `Ignixa.Validation`) implements `ITerminologyService` by routing each canonical to one or more `ITerminologySource`s (SQL, package, and later remote). It combines their three-state answers (`Determination.True`/`False`/`Unknown` + `UnknownReason`). Contract changes to `ITerminologyService` are staged one operation per phase. Operations not yet migrated keep whole-call routing through the transitional `ILegacyValueSetOperations` interface, which is removed in Phase 5.

**Tech Stack:** .NET (C#), ASP.NET Core Minimal APIs, Medino CQRS, Autofac, raw ADO.NET over SQL Server, xUnit + Shouldly + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-10-20-composable-terminology-operations-design.md` (background: `docs/adr/adr-2610-composable-terminology-operations.md`, `docs/features/terminology-services/investigations/composable-operations.md`).

## Global Constraints

- Breaking changes to `ITerminologyService` land one operation at a time, per spec §3.2: Phase 1 covers lookup, subsumes, and CodeSystem validate-code; Phase 2 covers ValueSet validate-code; Phase 4 covers expand; Phase 5 covers translate.
- `ValidateBindingAsync` output must remain exactly preserved; the existing guards in `test/Ignixa.Validation.Tests` must pass unchanged.
- Sources report facts; severity policy belongs to `BindingCheck`/`ValidateBindingAsync` and the API boundary (spec §3.2).
- A source exception is never converted to `Unknown`; it propagates (spec §4.4).
- R4 `$expand` never returns a partial expansion. Any `UnknownReason` produces 422 (spec §4.2).
- `$subsumes` never answers `not-subsumed` for an unknown code or system. It returns 400 (spec §4.2).
- Composition never makes HTTP calls to Ignixa's own endpoints (ADR-2610).
- Terminology endpoints stay behind the experimental flag. Tenant `0` is never exposed.
- Do not modify `MergeResources` or its TVPs.
- Do not reference `Hl7.Fhir.*` from Application or DataLayer.
- Use file-scoped namespaces, `_camelCase` fields, primary constructors where natural, `ArgumentNullException.ThrowIfNull`, a `CancellationToken` named `cancellationToken`, no new `#region`, and one top-level type per file. Nested closed hierarchies such as `SubsumesOutcome.Related` are allowed.
- Name tests `GivenContext_WhenAction_ThenResult`, use Arrange/Act/Assert, and use Shouldly assertions. Call a test a *regression* only if it fails at the base commit.
- Build with `dotnet build All.sln` (warnings are errors). Run focused tests with `dotnet test <project> --filter "FullyQualifiedName~<Name>"`.
- Every commit carries the trailers:
  ```
  Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>
  Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041
  ```

## Plan Fidelity

Phases 0–1 are execution-ready: exact files, code, and commands. Phases 2–7 are specified at task level, with concrete behaviour, files, and tests. Their code depends on Phase 1 types landing, so **run a short writing-plans refinement pass for each later phase before executing it**. That pass expands the listed tests into full code against the merged Phase 1 API. Every later-phase task already names its tests and acceptance behaviour, so refinement adds code; it does not decide scope.

## File Structure

New, in `src/Core/Ignixa.Validation/Abstractions/Terminology/` (namespace `Ignixa.Validation.Abstractions.Terminology`):

| File | Responsibility |
|---|---|
| `Determination.cs` | `enum Determination { True, False, Unknown }` |
| `UnknownKind.cs` | Why an answer is unknown |
| `UnknownReason.cs` | `(Kind, Subject, Detail)` with non-empty invariants |
| `CodeRef.cs` | `(System, Code, Version?)`; non-empty system and code |
| `CanonicalRef.cs` | `(Url, Version?)`; `Parse("url\|version")` |
| `CanonicalKind.cs` | `CodeSystem`, `ValueSet`, `ConceptMap` |
| `SourceCoverage.cs` | `Authoritative`, `Partial`, `None` |
| `ITerminologyAnswer.cs` | `UnknownReason? Unknown` — what the router needs from any answer |
| `MembershipResult.cs` | Three-state membership answer |
| `SubsumptionRelation.cs` + `SubsumptionRelationCodes.cs` | Relation enum and FHIR code mapping |
| `SubsumesOutcome.cs` | `Related(SubsumptionRelation)` or `Undetermined(UnknownReason)` |
| `LookupRequest.cs` | `(CodeRef Code)` |
| `ValidateCodeRequest.cs` | `(CodeRef Code, string? Display)` |
| `ValidateCodeResult.cs` + `TerminologyIssue.cs` | Three-state validate-code answer and issues |
| `ITerminologySource.cs` | Per-store contract (spec §3.1), grown per phase |
| `ILegacyValueSetOperations.cs` | Transitional whole-call ValueSet/ConceptMap operations; deleted in Phase 5 |

New, in `src/Core/Ignixa.Validation/Services/Terminology/` (namespace `Ignixa.Validation.Services.Terminology`):

| File | Responsibility |
|---|---|
| `SourceRouter.cs` | Coverage-based routing (spec §4.1) and legacy routing |
| `ComposingTerminologyService.cs` | The single `ITerminologyService` implementation |
| `PackageTerminologySource.cs` | Today's `InMemoryTerminologyService` logic, as a source |

Modified:

| File | Change |
|---|---|
| `src/Core/Ignixa.Validation/Abstractions/ITerminologyService.cs` | Phase 1 contract |
| `src/Core/Ignixa.Validation/Abstractions/LookupResult.cs` | `Found` becomes `Determination`; adds `Unknown` |
| `src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs` | Becomes a thin `ComposingTerminologyService` over one package source |
| `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs` | Implements `ITerminologySource` + `ILegacyValueSetOperations`; no longer `ITerminologyService`. Keeps its name, so the many integration tests constructing it are not churned; the spec's "SqlTerminologySource" is this class. |
| `src/Application/Ignixa.Api/Registrations/ValidationServicesRegistration.cs` | Composes SQL + package sources |
| `src/Application/Ignixa.Application/Features/Experimental/Terminology/Subsumes/*` | Maps `SubsumesOutcome` |
| `src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs` | Undetermined subsumes → 400 |

Deleted:

| File | Why |
|---|---|
| `src/Application/Ignixa.Application.Operations/Features/Terminology/**` | Unreferenced stale copies of the live experimental handlers |
| `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/HybridTerminologyService.cs` | Replaced by the composer |
| `test/Ignixa.DataLayer.SqlServer.Tests/Features/Terminology/HybridTerminologyServiceRoutingTests.cs` | Ported to `ComposingTerminologyServiceRoutingTests` |

---

## Phase 0 — Groundwork (additive; solution stays green)

### Task 1: Delete stale `Application.Operations` terminology handlers

**Files:**
- Delete: `src/Application/Ignixa.Application.Operations/Features/Terminology/Expand/ExpandValueSetHandler.cs`, `.../Expand/ExpandValueSetQuery.cs`, `.../Subsumes/SubsumesHandler.cs`, `.../Subsumes/SubsumesQuery.cs`, `.../Translate/TranslateCodeHandler.cs`, `.../Translate/TranslateCodeCommand.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: only one copy of each terminology handler remains, in `src/Application/Ignixa.Application/Features/Experimental/Terminology/`.

- [ ] **Step 1: Prove they are unreferenced**

Run: `rg -n "Ignixa\.Application\.Operations\.Features\.Terminology" --glob "*.cs"`
Expected: matches only inside `src/Application/Ignixa.Application.Operations/Features/Terminology/` itself. `Features.Transform` is a different namespace and must stay.

- [ ] **Step 2: Delete the folder**

```powershell
git rm -r src/Application/Ignixa.Application.Operations/Features/Terminology
```

- [ ] **Step 3: Build and run the affected suites**

Run: `dotnet build All.sln`
Expected: Build succeeded, 0 warnings.
Run: `dotnet test test/Ignixa.Application.Tests` and `dotnet test test/Ignixa.Application.Experimental.Tests`
Expected: PASS. This is the same pass count as before; the handlers had no tests of their own.

- [ ] **Step 4: Commit**

```powershell
git commit -m "Remove stale duplicate terminology handlers" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 2: Terminology value types

**Files:**
- Create: the 16 files in `src/Core/Ignixa.Validation/Abstractions/Terminology/` listed in File Structure, except `ITerminologySource.cs` and `ILegacyValueSetOperations.cs` (Task 3), and `LookupRequest.cs`, `ValidateCodeRequest.cs`, `ValidateCodeResult.cs`, `TerminologyIssue.cs` (Task 4).
- Test: `test/Ignixa.Validation.Tests/Terminology/TerminologyValueTypeTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces, in namespace `Ignixa.Validation.Abstractions.Terminology`:
  - `enum Determination { True, False, Unknown }`
  - `enum UnknownKind { SystemNotAvailable, SystemVersionNotAvailable, CodeNotInSystem, ValueSetNotFound, UnsupportedFilter, NonEnumerableSystem, BudgetExceeded, SourceFailed }`
  - `sealed record UnknownReason(UnknownKind Kind, string Subject, string Detail)`
  - `sealed record CodeRef(string System, string Code, string? Version = null)` with `CanonicalRef CodeSystem`
  - `sealed record CanonicalRef(string Url, string? Version = null)` with `static CanonicalRef Parse(string)`
  - `enum CanonicalKind { CodeSystem, ValueSet, ConceptMap }`, `enum SourceCoverage { None, Partial, Authoritative }`
  - `interface ITerminologyAnswer { UnknownReason? Unknown { get; } }`
  - `sealed record MembershipResult` with `Member`, `NotMember`, `Undetermined(UnknownReason)`, `Result`, `Unknown`
  - `enum SubsumptionRelation { Equivalent, Subsumes, SubsumedBy, NotSubsumed }`, `static class SubsumptionRelationCodes { string ToFhirCode(this SubsumptionRelation) }`
  - `abstract record SubsumesOutcome` with nested `Related(SubsumptionRelation Relation)` and `Undetermined(UnknownReason Reason)`, plus `SubsumptionRelation? KnownRelation` and `UnknownReason? Unknown`

`UnknownKind.CodeNotInSystem` extends the spec §3.2 list. Task 9 records it in the spec. An unknown code in an authoritative system is a fact, but `$subsumes` must still refuse to answer, and the router needs a reason value to carry.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class TerminologyValueTypeTests
{
    [Theory]
    [InlineData("http://loinc.org", "http://loinc.org", null)]
    [InlineData("http://loinc.org|2.77", "http://loinc.org", "2.77")]
    [InlineData("http://loinc.org|", "http://loinc.org", null)]
    [InlineData("http://example.org/a|b|1.0", "http://example.org/a|b", "1.0")]
    public void GivenCanonicalString_WhenParsed_ThenUrlAndVersionAreSplitOnLastBar(string input, string url, string? version)
    {
        var canonical = CanonicalRef.Parse(input);

        canonical.Url.ShouldBe(url);
        canonical.Version.ShouldBe(version);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("|1.0")]
    public void GivenBlankUrl_WhenCanonicalCreated_ThenThrows(string input)
    {
        Should.Throw<ArgumentException>(() => CanonicalRef.Parse(input));
    }

    [Theory]
    [InlineData("", "a")]
    [InlineData("http://s", "")]
    [InlineData(" ", " ")]
    public void GivenBlankSystemOrCode_WhenCodeRefCreated_ThenThrows(string system, string code)
    {
        Should.Throw<ArgumentException>(() => new CodeRef(system, code));
    }

    [Fact]
    public void GivenCodeRefWithVersion_WhenAskingForItsCodeSystem_ThenCanonicalCarriesTheVersion()
    {
        var code = new CodeRef("http://snomed.info/sct", "22298006", "http://snomed.info/sct/900000000000207008");

        code.CodeSystem.ShouldBe(new CanonicalRef("http://snomed.info/sct", "http://snomed.info/sct/900000000000207008"));
    }

    [Fact]
    public void GivenBlankVersion_WhenCodeRefCreated_ThenVersionIsNull()
    {
        new CodeRef("http://s", "a", " ").Version.ShouldBeNull();
    }

    [Fact]
    public void GivenUndeterminedMembership_WhenInspected_ThenResultIsUnknownAndReasonIsKept()
    {
        var reason = new UnknownReason(UnknownKind.SystemNotAvailable, "http://s", "not loaded");

        var membership = MembershipResult.Undetermined(reason);

        membership.Result.ShouldBe(Determination.Unknown);
        membership.Unknown.ShouldBe(reason);
        MembershipResult.Member.Unknown.ShouldBeNull();
        MembershipResult.NotMember.Result.ShouldBe(Determination.False);
    }

    [Theory]
    [InlineData(SubsumptionRelation.Equivalent, "equivalent")]
    [InlineData(SubsumptionRelation.Subsumes, "subsumes")]
    [InlineData(SubsumptionRelation.SubsumedBy, "subsumed-by")]
    [InlineData(SubsumptionRelation.NotSubsumed, "not-subsumed")]
    public void GivenRelation_WhenMappedToFhir_ThenR4CodeIsReturned(SubsumptionRelation relation, string code)
    {
        relation.ToFhirCode().ShouldBe(code);
    }

    [Fact]
    public void GivenSubsumesOutcomes_WhenInspected_ThenExactlyOneOfRelationOrUnknownIsSet()
    {
        var reason = new UnknownReason(UnknownKind.CodeNotInSystem, "http://s#x", "no such code");

        SubsumesOutcome related = new SubsumesOutcome.Related(SubsumptionRelation.Subsumes);
        SubsumesOutcome undetermined = new SubsumesOutcome.Undetermined(reason);

        related.KnownRelation.ShouldBe(SubsumptionRelation.Subsumes);
        related.Unknown.ShouldBeNull();
        undetermined.KnownRelation.ShouldBeNull();
        undetermined.Unknown.ShouldBe(reason);
    }

    [Theory]
    [InlineData("", "detail")]
    [InlineData("subject", "")]
    public void GivenBlankSubjectOrDetail_WhenUnknownReasonCreated_ThenThrows(string subject, string detail)
    {
        Should.Throw<ArgumentException>(() => new UnknownReason(UnknownKind.SourceFailed, subject, detail));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~TerminologyValueTypeTests"`
Expected: build FAIL — `The type or namespace name 'CanonicalRef' could not be found`.

- [ ] **Step 3: Implement the types (one file each)**

Each file starts with the repository's license header, as in the neighbouring `Abstractions/*.cs` files, then `namespace Ignixa.Validation.Abstractions.Terminology;`.

```csharp
// Determination.cs
public enum Determination { True, False, Unknown }

// UnknownKind.cs
public enum UnknownKind
{
    SystemNotAvailable,
    SystemVersionNotAvailable,
    CodeNotInSystem,
    ValueSetNotFound,
    UnsupportedFilter,
    NonEnumerableSystem,
    BudgetExceeded,
    SourceFailed,
}

// UnknownReason.cs
public sealed record UnknownReason
{
    public UnknownReason(UnknownKind kind, string subject, string detail)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(detail);
        Kind = kind;
        Subject = subject;
        Detail = detail;
    }

    public UnknownKind Kind { get; }

    public string Subject { get; }

    public string Detail { get; }
}

// CanonicalRef.cs
public sealed record CanonicalRef
{
    public CanonicalRef(string url, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        Url = url;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public string Url { get; }

    public string? Version { get; }

    public static CanonicalRef Parse(string canonical)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(canonical);
        int bar = canonical.LastIndexOf('|');
        return bar < 0
            ? new CanonicalRef(canonical)
            : new CanonicalRef(canonical[..bar], canonical[(bar + 1)..]);
    }

    public override string ToString() => Version is null ? Url : $"{Url}|{Version}";
}

// CodeRef.cs
public sealed record CodeRef
{
    public CodeRef(string system, string code, string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(system);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        System = system;
        Code = code;
        Version = string.IsNullOrWhiteSpace(version) ? null : version;
    }

    public string System { get; }

    public string Code { get; }

    public string? Version { get; }

    public CanonicalRef CodeSystem => new(System, Version);

    public override string ToString() => $"{CodeSystem}#{Code}";
}

// CanonicalKind.cs
public enum CanonicalKind { CodeSystem, ValueSet, ConceptMap }

// SourceCoverage.cs
public enum SourceCoverage { None, Partial, Authoritative }

// ITerminologyAnswer.cs
/// <summary>An answer that may be undetermined. <see cref="Unknown"/> is null exactly when the answer is determined.</summary>
public interface ITerminologyAnswer
{
    UnknownReason? Unknown { get; }
}

// MembershipResult.cs
public sealed record MembershipResult : ITerminologyAnswer
{
    private MembershipResult(Determination result, UnknownReason? unknown)
    {
        Result = result;
        Unknown = unknown;
    }

    public static MembershipResult Member { get; } = new(Determination.True, null);

    public static MembershipResult NotMember { get; } = new(Determination.False, null);

    public Determination Result { get; }

    public UnknownReason? Unknown { get; }

    public static MembershipResult Undetermined(UnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(Determination.Unknown, reason);
    }
}

// SubsumptionRelation.cs
public enum SubsumptionRelation { Equivalent, Subsumes, SubsumedBy, NotSubsumed }

// SubsumptionRelationCodes.cs
public static class SubsumptionRelationCodes
{
    public static string ToFhirCode(this SubsumptionRelation relation) => relation switch
    {
        SubsumptionRelation.Equivalent => "equivalent",
        SubsumptionRelation.Subsumes => "subsumes",
        SubsumptionRelation.SubsumedBy => "subsumed-by",
        SubsumptionRelation.NotSubsumed => "not-subsumed",
        _ => throw new ArgumentOutOfRangeException(nameof(relation), relation, null),
    };
}

// SubsumesOutcome.cs
public abstract record SubsumesOutcome : ITerminologyAnswer
{
    private SubsumesOutcome()
    {
    }

    public SubsumptionRelation? KnownRelation => this is Related related ? related.Relation : null;

    public UnknownReason? Unknown => this is Undetermined undetermined ? undetermined.Reason : null;

    public sealed record Related(SubsumptionRelation Relation) : SubsumesOutcome;

    public sealed record Undetermined(UnknownReason Reason) : SubsumesOutcome;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~TerminologyValueTypeTests"`
Expected: PASS (all cases).

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.Validation/Abstractions/Terminology test/Ignixa.Validation.Tests/Terminology
git commit -m "Add terminology determination value types" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 3: `ITerminologySource`, `ILegacyValueSetOperations`, and `SourceRouter`

**Files:**
- Create: `src/Core/Ignixa.Validation/Abstractions/Terminology/ITerminologySource.cs`
- Create: `src/Core/Ignixa.Validation/Abstractions/Terminology/ILegacyValueSetOperations.cs`
- Create: `src/Core/Ignixa.Validation/Services/Terminology/SourceRouter.cs`
- Create: `src/Core/Ignixa.Validation/Services/Terminology/RoutedAnswer.cs`
- Test: `test/Ignixa.Validation.Tests/Terminology/SourceRouterTests.cs`

**Interfaces:**
- Consumes (Task 2): `CanonicalKind`, `CanonicalRef`, `SourceCoverage`, `ITerminologyAnswer`, `UnknownReason`, `UnknownKind`, `MembershipResult` (tests only).
- Produces:
  - `public interface ITerminologySource { string Name { get; } ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken); }`. Phase 1 adds `LookupAsync` and `SubsumesAsync`; later phases add more (spec §3.1).
  - `public interface ILegacyValueSetOperations` with today's four whole-call signatures: `ValidateCodeAsync`, `ValidateBindingAsync`, `ExpandValueSetAsync`, `TranslateCodeAsync`.
  - `internal sealed class SourceRouter(IReadOnlyList<ITerminologySource> sources)` with:
    - `Task<RoutedAnswer<T>> AskAsync<T>(CanonicalKind, CanonicalRef, Func<ITerminologySource, CancellationToken, Task<T>> ask, Func<UnknownReason, T> undetermined, CancellationToken) where T : ITerminologyAnswer`
    - `Task<ILegacyValueSetOperations> SelectLegacyAsync(CanonicalRef valueSet, CancellationToken)`
    - `ILegacyValueSetOperations FirstLegacy { get; }`
  - `public sealed record RoutedAnswer<T>(T Answer, IReadOnlyList<UnknownReason> Unknowns)`

Routing rules (spec §4.1):
1. Ask every source for coverage, in registration order.
2. The **first** `Authoritative` source answers alone. Its unknown, if any, is the only entry in `Unknowns`.
3. Otherwise ask the `Partial` sources in order. The first answer whose `Unknown` is null wins, with empty `Unknowns`.
4. If every partial answer is unknown, return the first partial answer, and `Unknowns` holds every partial's reason in order.
5. If no source covers the canonical, return `undetermined(new UnknownReason(SystemNotAvailable or ValueSetNotFound, canonical.ToString(), …))`. Use `ValueSetNotFound` for `CanonicalKind.ValueSet`; otherwise use `SystemNotAvailable`.
6. Exceptions propagate unchanged (spec §4.4).

Legacy rules preserve `HybridTerminologyService` exactly:
- `SelectLegacyAsync` returns the first legacy-capable source whose ValueSet coverage is `Authoritative`; otherwise it returns the **last** legacy-capable source, which is the fallback.
- `FirstLegacy` returns the first legacy-capable source (used by translate, which Hybrid always sent to SQL).
- With no legacy-capable source, both throw `InvalidOperationException`.
- Callers build the canonical with `new CanonicalRef(rawUrl)`, **not** `Parse`, so a `url|version` string reaches the import-status query unchanged, exactly as Hybrid passed it.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class SourceRouterTests
{
    private static readonly CanonicalRef System = new("http://example.org/cs");
    private static readonly UnknownReason ReasonA = new(UnknownKind.NonEnumerableSystem, "http://example.org/cs", "a");
    private static readonly UnknownReason ReasonB = new(UnknownKind.SystemNotAvailable, "http://example.org/cs", "b");

    private static ITerminologySource Source(string name, SourceCoverage coverage, MembershipResult answer)
    {
        var source = Substitute.For<ITerminologySource, ILegacyValueSetOperations>();
        source.Name.Returns(name);
        source.GetCoverageAsync(Arg.Any<CanonicalKind>(), Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(coverage));
        Answers[source] = answer;
        return source;
    }

    private static readonly Dictionary<ITerminologySource, MembershipResult> Answers = new(ReferenceEqualityComparer.Instance);

    private static Task<RoutedAnswer<MembershipResult>> AskAsync(SourceRouter router, List<string> asked) =>
        router.AskAsync(
            CanonicalKind.CodeSystem,
            System,
            (source, _) =>
            {
                asked.Add(source.Name);
                return Task.FromResult(Answers[source]);
            },
            MembershipResult.Undetermined,
            CancellationToken.None);

    [Fact]
    public async Task GivenAuthoritativeSourceAfterPartial_WhenAsked_ThenOnlyAuthoritativeAnswers()
    {
        var asked = new List<string>();
        var router = new SourceRouter([
            Source("package", SourceCoverage.Partial, MembershipResult.Member),
            Source("sql", SourceCoverage.Authoritative, MembershipResult.NotMember)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.ShouldBe(MembershipResult.NotMember);
        routed.Unknowns.ShouldBeEmpty();
        asked.ShouldBe(["sql"]);
    }

    [Fact]
    public async Task GivenPartialUnknownThenPartialFalse_WhenAsked_ThenFirstDeterminedAnswerWins()
    {
        var asked = new List<string>();
        var router = new SourceRouter([
            Source("first", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonA)),
            Source("second", SourceCoverage.Partial, MembershipResult.NotMember)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.Result.ShouldBe(Determination.False);
        routed.Unknowns.ShouldBeEmpty();
        asked.ShouldBe(["first", "second"]);
    }

    [Fact]
    public async Task GivenEveryPartialUnknown_WhenAsked_ThenFirstAnswerReturnedWithAllReasons()
    {
        var router = new SourceRouter([
            Source("first", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonA)),
            Source("second", SourceCoverage.Partial, MembershipResult.Undetermined(ReasonB))]);

        var routed = await AskAsync(router, []);

        routed.Answer.Unknown.ShouldBe(ReasonA);
        routed.Unknowns.ShouldBe([ReasonA, ReasonB]);
    }

    [Fact]
    public async Task GivenNoCoveringSource_WhenAsked_ThenSystemNotAvailableForTheCanonical()
    {
        var asked = new List<string>();
        var router = new SourceRouter([Source("sql", SourceCoverage.None, MembershipResult.Member)]);

        var routed = await AskAsync(router, asked);

        routed.Answer.Result.ShouldBe(Determination.Unknown);
        routed.Answer.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
        routed.Answer.Unknown.Subject.ShouldBe("http://example.org/cs");
        asked.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenSourceThrows_WhenAsked_ThenExceptionPropagates()
    {
        var router = new SourceRouter([Source("sql", SourceCoverage.Authoritative, MembershipResult.Member)]);

        await Should.ThrowAsync<InvalidOperationException>(() => router.AskAsync<MembershipResult>(
            CanonicalKind.CodeSystem,
            System,
            (_, _) => throw new InvalidOperationException("db down"),
            MembershipResult.Undetermined,
            CancellationToken.None));
    }

    [Fact]
    public async Task GivenAuthoritativeValueSetSource_WhenSelectingLegacy_ThenItIsChosen()
    {
        var sql = Source("sql", SourceCoverage.Authoritative, MembershipResult.Member);
        var package = Source("package", SourceCoverage.Authoritative, MembershipResult.Member);

        var chosen = await new SourceRouter([sql, package]).SelectLegacyAsync(new CanonicalRef("http://vs|1"), CancellationToken.None);

        chosen.ShouldBeSameAs(sql);
        await sql.Received(1).GetCoverageAsync(CanonicalKind.ValueSet, new CanonicalRef("http://vs|1"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoAuthoritativeValueSetSource_WhenSelectingLegacy_ThenLastLegacySourceIsTheFallback()
    {
        var sql = Source("sql", SourceCoverage.None, MembershipResult.Member);
        var package = Source("package", SourceCoverage.None, MembershipResult.Member);

        var chosen = await new SourceRouter([sql, package]).SelectLegacyAsync(new CanonicalRef("http://vs"), CancellationToken.None);

        chosen.ShouldBeSameAs(package);
    }

    [Fact]
    public void GivenSources_WhenAskingForFirstLegacy_ThenRegistrationOrderWins()
    {
        var sql = Source("sql", SourceCoverage.None, MembershipResult.Member);

        new SourceRouter([sql, Source("package", SourceCoverage.None, MembershipResult.Member)]).FirstLegacy.ShouldBeSameAs(sql);
    }

    [Fact]
    public async Task GivenNoLegacyCapableSource_WhenSelectingLegacy_ThenThrows()
    {
        var plain = Substitute.For<ITerminologySource>();

        await Should.ThrowAsync<InvalidOperationException>(() => new SourceRouter([plain]).SelectLegacyAsync(new CanonicalRef("http://vs"), CancellationToken.None));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~SourceRouterTests"`
Expected: build FAIL — `The type or namespace name 'ITerminologySource' could not be found`.

- [ ] **Step 3: Implement**

```csharp
// Abstractions/Terminology/ITerminologySource.cs
namespace Ignixa.Validation.Abstractions.Terminology;

/// <summary>
/// One terminology store (SQL, package content, later a remote server). A source reports facts about
/// the canonicals it holds; it never decides severity and never answers for content it does not hold.
/// </summary>
public interface ITerminologySource
{
    string Name { get; }

    ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken);
}
```

```csharp
// Abstractions/Terminology/ILegacyValueSetOperations.cs
namespace Ignixa.Validation.Abstractions.Terminology;

/// <summary>
/// Transitional: whole-call ValueSet and ConceptMap operations that are routed by canonical until their
/// composed replacements land (validate/binding in Phase 2, expand in Phase 4, translate in Phase 5).
/// Deleted in Phase 5.
/// </summary>
public interface ILegacyValueSetOperations
{
    Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken);

    Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken);

    Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken);

    Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken);
}
```

```csharp
// Services/Terminology/RoutedAnswer.cs
namespace Ignixa.Validation.Services.Terminology;

/// <summary>A routed answer plus every reason a consulted source could not decide.</summary>
public sealed record RoutedAnswer<T>(T Answer, IReadOnlyList<UnknownReason> Unknowns)
    where T : ITerminologyAnswer;
```

```csharp
// Services/Terminology/SourceRouter.cs
using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

internal sealed class SourceRouter
{
    private readonly IReadOnlyList<ITerminologySource> _sources;
    private readonly IReadOnlyList<ILegacyValueSetOperations> _legacy;

    public SourceRouter(IReadOnlyList<ITerminologySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        _sources = sources;
        _legacy = sources.OfType<ILegacyValueSetOperations>().ToList();
    }

    public ILegacyValueSetOperations FirstLegacy =>
        _legacy.Count > 0 ? _legacy[0] : throw NoLegacySource();

    public async Task<RoutedAnswer<T>> AskAsync<T>(
        CanonicalKind kind,
        CanonicalRef canonical,
        Func<ITerminologySource, CancellationToken, Task<T>> ask,
        Func<UnknownReason, T> undetermined,
        CancellationToken cancellationToken)
        where T : ITerminologyAnswer
    {
        var partial = new List<ITerminologySource>();
        foreach (var source in _sources)
        {
            var coverage = await source.GetCoverageAsync(kind, canonical, cancellationToken);
            if (coverage == SourceCoverage.Authoritative)
            {
                var answer = await ask(source, cancellationToken);
                return new RoutedAnswer<T>(answer, answer.Unknown is null ? [] : [answer.Unknown]);
            }

            if (coverage == SourceCoverage.Partial)
            {
                partial.Add(source);
            }
        }

        return partial.Count == 0
            ? Uncovered(kind, canonical, undetermined)
            : await AskPartialAsync(partial, ask, cancellationToken);
    }

    public async Task<ILegacyValueSetOperations> SelectLegacyAsync(CanonicalRef valueSet, CancellationToken cancellationToken)
    {
        if (_legacy.Count == 0)
        {
            throw NoLegacySource();
        }

        foreach (var legacy in _legacy)
        {
            var coverage = await ((ITerminologySource)legacy).GetCoverageAsync(CanonicalKind.ValueSet, valueSet, cancellationToken);
            if (coverage == SourceCoverage.Authoritative)
            {
                return legacy;
            }
        }

        return _legacy[^1];
    }

    private static async Task<RoutedAnswer<T>> AskPartialAsync<T>(
        List<ITerminologySource> partial,
        Func<ITerminologySource, CancellationToken, Task<T>> ask,
        CancellationToken cancellationToken)
        where T : ITerminologyAnswer
    {
        var unknowns = new List<UnknownReason>();
        T? first = default;
        foreach (var source in partial)
        {
            var answer = await ask(source, cancellationToken);
            if (answer.Unknown is null)
            {
                return new RoutedAnswer<T>(answer, []);
            }

            first ??= answer;
            unknowns.Add(answer.Unknown);
        }

        return new RoutedAnswer<T>(first!, unknowns);
    }

    private static RoutedAnswer<T> Uncovered<T>(CanonicalKind kind, CanonicalRef canonical, Func<UnknownReason, T> undetermined)
        where T : ITerminologyAnswer
    {
        var reason = kind == CanonicalKind.ValueSet
            ? new UnknownReason(UnknownKind.ValueSetNotFound, canonical.ToString(), $"No terminology source holds ValueSet '{canonical}'")
            : new UnknownReason(UnknownKind.SystemNotAvailable, canonical.ToString(), $"No terminology source holds {kind} '{canonical}'");
        return new RoutedAnswer<T>(undetermined(reason), [reason]);
    }

    private static InvalidOperationException NoLegacySource() =>
        new("No terminology source implements ILegacyValueSetOperations; at least one is required until Phase 5.");
}
```

`ITerminologySource` instances that implement `ILegacyValueSetOperations` are always the same object, so the cast in `SelectLegacyAsync` is safe by construction. `_legacy` is built from `_sources` with `OfType`.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~SourceRouterTests|FullyQualifiedName~TerminologyValueTypeTests"`
Expected: PASS.

- [ ] **Step 5: Build the solution (Phase 0 must stay green)**

Run: `dotnet build All.sln`
Expected: Build succeeded, 0 warnings. Nothing outside the new files changed.

- [ ] **Step 6: Commit**

```powershell
git add src/Core/Ignixa.Validation test/Ignixa.Validation.Tests/Terminology
git commit -m "Add terminology source abstraction and coverage router" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

---

## Phase 1 — Lookup, subsumes, and CodeSystem validate-code over sources; Hybrid deleted

Tasks 4–6 are additive, so the solution stays green. Task 7 is the single atomic contract switch, because `ITerminologyService` cannot change in pieces without breaking the build. Task 8 covers docs and the spec sync.

### Task 4: Phase 1 request and answer types; source lookup and subsumes members

**Files:**
- Create in `src/Core/Ignixa.Validation/Abstractions/Terminology/`:
  - `ConceptDetails.cs`, `ConceptLookup.cs`
  - `LookupRequest.cs`, `SubsumesRequest.cs`, `ValidateCodeRequest.cs`
  - `TerminologyIssueKind.cs`, `TerminologyIssue.cs`, `ValidateCodeResult.cs`
- Modify: `src/Core/Ignixa.Validation/Abstractions/Terminology/ITerminologySource.cs`
- Test: `test/Ignixa.Validation.Tests/Terminology/TerminologyRequestTypeTests.cs`

**Interfaces:**
- Consumes: Task 2 types; `PropertyValue` and `Designation` from `Ignixa.Validation.Abstractions` (currently declared in `LookupResult.cs`).
- Produces:
  - `public sealed record ConceptDetails(string? Name, string? Version, string? Display, string? Definition, IReadOnlyList<PropertyValue>? Properties, IReadOnlyList<Designation>? Designations)`
  - `public sealed record ConceptLookup : ITerminologyAnswer` with `static ConceptLookup Found(ConceptDetails)`, `static ConceptLookup NotFound`, `static ConceptLookup Undetermined(UnknownReason)`, `Determination Result`, `ConceptDetails? Concept`, `UnknownReason? Unknown`. This replaces `LookupResult` in Task 7; the spec's `LookupResult(Determination Found, …)` is renamed to avoid a same-named type with a different shape.
  - `public sealed record LookupRequest(CodeRef Code)`
  - `public sealed record SubsumesRequest`, built as `new SubsumesRequest(CodeRef codeA, CodeRef codeB)`. It throws `ArgumentException` unless both refs share `System` and `Version`, and exposes `CodeA`, `CodeB`, and `CanonicalRef CodeSystem`.
  - `public sealed record ValidateCodeRequest(CodeRef Code, string? Display = null)`
  - `public enum TerminologyIssueKind { CodeInvalid, DisplayMismatch }`
  - `public sealed record TerminologyIssue(IssueSeverity Severity, TerminologyIssueKind Kind, string Message)`
  - `public sealed record ValidateCodeResult(Determination Result, string? Display, IReadOnlyList<TerminologyIssue> Issues, IReadOnlyList<UnknownReason> Unknowns)`
  - `ITerminologySource` gains:
    - `Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)`
    - `Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)`

`LookupOptions` (spec §3.1) is deferred to Phase 3, when the `property` input parameter needs it. `ContainsAsync` is dropped, because `LookupAsync` already answers CodeSystem membership three-ways. Task 8 records both in the spec.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class TerminologyRequestTypeTests
{
    [Fact]
    public void GivenCodesFromDifferentSystems_WhenSubsumesRequestCreated_ThenThrows()
    {
        Should.Throw<ArgumentException>(() => new SubsumesRequest(new CodeRef("http://a", "x"), new CodeRef("http://b", "y")));
    }

    [Fact]
    public void GivenCodesWithDifferentVersions_WhenSubsumesRequestCreated_ThenThrows()
    {
        Should.Throw<ArgumentException>(() => new SubsumesRequest(new CodeRef("http://a", "x", "1"), new CodeRef("http://a", "y", "2")));
    }

    [Fact]
    public void GivenCodesFromOneSystemVersion_WhenSubsumesRequestCreated_ThenCodeSystemIsShared()
    {
        var request = new SubsumesRequest(new CodeRef("http://a", "x", "1"), new CodeRef("http://a", "y", "1"));

        request.CodeSystem.ShouldBe(new CanonicalRef("http://a", "1"));
    }

    [Fact]
    public void GivenLookupFactories_WhenInspected_ThenResultConceptAndUnknownAgree()
    {
        var reason = new UnknownReason(UnknownKind.SystemNotAvailable, "http://a", "not loaded");
        var details = new ConceptDetails(null, "1", "Car", null, null, null);

        ConceptLookup.Found(details).Result.ShouldBe(Determination.True);
        ConceptLookup.Found(details).Concept.ShouldBe(details);
        ConceptLookup.NotFound.Result.ShouldBe(Determination.False);
        ConceptLookup.NotFound.Concept.ShouldBeNull();
        ConceptLookup.Undetermined(reason).Result.ShouldBe(Determination.Unknown);
        ConceptLookup.Undetermined(reason).Unknown.ShouldBe(reason);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~TerminologyRequestTypeTests"`
Expected: build FAIL — `SubsumesRequest` not found.

- [ ] **Step 3: Implement**

```csharp
// ConceptDetails.cs
using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record ConceptDetails(
    string? Name,
    string? Version,
    string? Display,
    string? Definition,
    IReadOnlyList<PropertyValue>? Properties,
    IReadOnlyList<Designation>? Designations);

// ConceptLookup.cs
public sealed record ConceptLookup : ITerminologyAnswer
{
    private ConceptLookup(Determination result, ConceptDetails? concept, UnknownReason? unknown)
    {
        Result = result;
        Concept = concept;
        Unknown = unknown;
    }

    public static ConceptLookup NotFound { get; } = new(Determination.False, null, null);

    public Determination Result { get; }

    public ConceptDetails? Concept { get; }

    public UnknownReason? Unknown { get; }

    public static ConceptLookup Found(ConceptDetails concept)
    {
        ArgumentNullException.ThrowIfNull(concept);
        return new(Determination.True, concept, null);
    }

    public static ConceptLookup Undetermined(UnknownReason reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new(Determination.Unknown, null, reason);
    }
}

// LookupRequest.cs
public sealed record LookupRequest(CodeRef Code);

// SubsumesRequest.cs
public sealed record SubsumesRequest
{
    public SubsumesRequest(CodeRef codeA, CodeRef codeB)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);
        if (codeA.CodeSystem != codeB.CodeSystem)
        {
            throw new ArgumentException($"$subsumes compares codes from one code system; got '{codeA.CodeSystem}' and '{codeB.CodeSystem}'.", nameof(codeB));
        }

        CodeA = codeA;
        CodeB = codeB;
    }

    public CodeRef CodeA { get; }

    public CodeRef CodeB { get; }

    public CanonicalRef CodeSystem => CodeA.CodeSystem;
}

// ValidateCodeRequest.cs
public sealed record ValidateCodeRequest(CodeRef Code, string? Display = null);

// TerminologyIssueKind.cs
public enum TerminologyIssueKind { CodeInvalid, DisplayMismatch }

// TerminologyIssue.cs
using Ignixa.Validation;

public sealed record TerminologyIssue(IssueSeverity Severity, TerminologyIssueKind Kind, string Message);

// ValidateCodeResult.cs
public sealed record ValidateCodeResult(
    Determination Result,
    string? Display,
    IReadOnlyList<TerminologyIssue> Issues,
    IReadOnlyList<UnknownReason> Unknowns);
```

Add the two members to `ITerminologySource`:

```csharp
    Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken);

    Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken);
```

`IssueSeverity` lives in namespace `Ignixa.Validation`; confirm with `rg -n "enum IssueSeverity" src/Core`. The `SourceRouterTests` substitutes need no change, because NSubstitute stubs the new members.

- [ ] **Step 4: Run the tests to verify they pass, then build**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~Terminology"` then `dotnet build All.sln`
Expected: PASS; Build succeeded.

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.Validation/Abstractions/Terminology test/Ignixa.Validation.Tests/Terminology
git commit -m "Add lookup, subsumes and validate-code request types" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 5: `PackageTerminologySource` (from `InMemoryTerminologyService`)

**Files:**
- Create: `src/Core/Ignixa.Validation/Services/Terminology/PackageTerminologySource.cs`
- Create: `src/Core/Ignixa.Validation/Services/Terminology/LayeredValueSetProvider.cs` (moved out of `InMemoryTerminologyService` and made `internal sealed`)
- Create: `src/Core/Ignixa.Validation/Services/Terminology/ValueSetMembership.cs` (moved out of `InMemoryTerminologyService` and made `internal sealed`)
- Modify: `src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs`. It delegates every member to a private `PackageTerminologySource`, so it is still `ITerminologyService` under the *old* contract until Task 7.
- Test: `test/Ignixa.Validation.Tests/Terminology/PackageTerminologySourceTests.cs`

**Interfaces:**
- Consumes: Task 3 `ITerminologySource` and `ILegacyValueSetOperations`; Task 4 `ConceptLookup`, `ConceptDetails`, and `SubsumesOutcome`; `IValueSetProvider` and `ICodeSystemProvider` (namespace `Ignixa.Abstractions`).
- Produces: `public sealed class PackageTerminologySource : ITerminologySource, ILegacyValueSetOperations` with:
  - constructors `(IValueSetProvider valueSetProvider, ICodeSystemProvider? codeSystemProvider = null)` and `(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional, ICodeSystemProvider? codeSystemProvider = null)`
  - `Name => "package"`

Behaviour (spec §7):
- **Coverage.** CodeSystem → `Partial` always. ValueSet → `Authoritative` when `IsKnownValueSet(canonical.Url)`, else `None`. ConceptMap → `None`.
- **`LookupAsync`.** Keyed on `ICodeSystemProvider.ContainsCode(system, code)`:
  - `true` → `Found(new ConceptDetails(null, null, GetDisplay(system, code), null, null, null))`
  - `false` and the system is in `NonEnumerableSystems` → `Undetermined(NonEnumerableSystem)`
  - `false` → `NotFound`
  - `null`, or no provider → `Undetermined(SystemNotAvailable)`

  Version is ignored, exactly as today.
- **`SubsumesAsync`.** Always `Undetermined(UnsupportedFilter, system, "Package terminology content carries no concept hierarchy")`. This replaces the false `not-subsumed`.
- **Legacy members.** The bodies of today's `InMemoryTerminologyService.ValidateCodeAsync`, `ValidateBindingAsync`, `ExpandValueSetAsync`, and `TranslateCodeAsync` move **verbatim**, together with the private `DetermineSeverity`, `Verified`, `_valueSets` cache, and the `NonEnumerableSystems` set.

- [ ] **Step 1: Write the failing tests**

```csharp
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class PackageTerminologySourceTests
{
    private const string Local = "http://example.org/cs";
    private const string Snomed = "http://snomed.info/sct";

    private static PackageTerminologySource Create(ICodeSystemProvider? codeSystems = null, IValueSetProvider? valueSets = null) =>
        new(valueSets ?? Substitute.For<IValueSetProvider>(), codeSystems);

    [Fact]
    public async Task GivenEnumeratedCode_WhenLookedUp_ThenFoundWithProviderDisplay()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "car").Returns(true);
        codeSystems.GetDisplay(Local, "car").Returns("Car");

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
        lookup.Concept!.Display.ShouldBe("Car");
    }

    [Fact]
    public async Task GivenEnumerableSystemWithoutTheCode_WhenLookedUp_ThenNotFound()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Local, "boat").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "boat"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public async Task GivenNonEnumerableSystemMiss_WhenLookedUp_ThenUnknownNonEnumerable()
    {
        var codeSystems = Substitute.For<ICodeSystemProvider>();
        codeSystems.ContainsCode(Snomed, "123").Returns(false);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Snomed, "123"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.Unknown);
        lookup.Unknown!.Kind.ShouldBe(UnknownKind.NonEnumerableSystem);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task GivenSystemUnknownToProvider_WhenLookedUp_ThenUnknownSystemNotAvailable(bool hasProvider)
    {
        var codeSystems = hasProvider ? Substitute.For<ICodeSystemProvider>() : null;
        codeSystems?.ContainsCode(Local, "car").Returns((bool?)null);

        var lookup = await Create(codeSystems).LookupAsync(new CodeRef(Local, "car"), CancellationToken.None);

        lookup.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
    }

    [Fact]
    public async Task GivenAnyCodes_WhenSubsumesAsked_ThenUndeterminedNeverNotSubsumed()
    {
        var outcome = await Create().SubsumesAsync(new CodeRef(Local, "a"), new CodeRef(Local, "b"), CancellationToken.None);

        outcome.ShouldBeOfType<SubsumesOutcome.Undetermined>();
    }

    [Theory]
    [InlineData(CanonicalKind.CodeSystem, false, SourceCoverage.Partial)]
    [InlineData(CanonicalKind.ValueSet, true, SourceCoverage.Authoritative)]
    [InlineData(CanonicalKind.ValueSet, false, SourceCoverage.None)]
    [InlineData(CanonicalKind.ConceptMap, true, SourceCoverage.None)]
    public async Task GivenCanonicalKind_WhenCoverageAsked_ThenSpecRuleApplies(CanonicalKind kind, bool knownValueSet, SourceCoverage expected)
    {
        var valueSets = Substitute.For<IValueSetProvider>();
        valueSets.IsKnownValueSet("http://example.org/x").Returns(knownValueSet);

        var coverage = await Create(valueSets: valueSets).GetCoverageAsync(kind, new CanonicalRef("http://example.org/x"), CancellationToken.None);

        coverage.ShouldBe(expected);
    }
}
```

The legacy members are already pinned by `InMemoryTerminologyService` tests in `test/Ignixa.Validation.Tests`, which keep exercising them through delegation. List them with `rg -l "InMemoryTerminologyService" test/Ignixa.Validation.Tests` before Step 3, and run them in Step 4 unchanged.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.Validation.Tests --filter "FullyQualifiedName~PackageTerminologySourceTests"`
Expected: build FAIL — `PackageTerminologySource` not found.

- [ ] **Step 3: Implement**

`PackageTerminologySource`, with the moved legacy bodies abbreviated as `/* moved verbatim */`. They are moved, not rewritten, so copy them character-for-character from `InMemoryTerminologyService.cs` (the `ValidateCodeAsync` body at lines ~150–225, `ValidateBindingAsync`, `ExpandValueSetAsync`, `TranslateCodeAsync`, `Verified`, and `DetermineSeverity`):

```csharp
using System.Collections.Concurrent;
using System.Collections.Frozen;
using Ignixa.Abstractions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// Terminology answered from loaded package content: base-spec and IG ValueSets plus optional CodeSystem
/// content. Never authoritative for a CodeSystem, because package content may be a subset of the real system.
/// </summary>
public sealed class PackageTerminologySource : ITerminologySource, ILegacyValueSetOperations
{
    /// <summary>
    /// Code systems that cannot be completely enumerated from local content (large external terminologies).
    /// A miss there is undecidable offline, not an absence.
    /// </summary>
    internal static readonly FrozenSet<string> NonEnumerableSystems = new[]
    {
        "http://snomed.info/sct",
        "http://loinc.org",
        "http://unitsofmeasure.org",
        "http://www.nlm.nih.gov/research/umls/rxnorm",
        "http://hl7.org/fhir/sid/icd-10",
        "http://hl7.org/fhir/sid/icd-10-cm",
        "http://hl7.org/fhir/sid/icd-9-cm",
        "http://hl7.org/fhir/sid/cvx",
        "http://www.ama-assn.org/go/cpt",
    }.ToFrozenSet(StringComparer.Ordinal);

    private readonly ConcurrentDictionary<string, ValueSetMembership> _valueSets = new(StringComparer.Ordinal);
    private readonly IValueSetProvider _valueSetProvider;
    private readonly ICodeSystemProvider? _codeSystemProvider;

    public PackageTerminologySource(IValueSetProvider valueSetProvider, ICodeSystemProvider? codeSystemProvider = null)
    {
        ArgumentNullException.ThrowIfNull(valueSetProvider);
        _valueSetProvider = valueSetProvider;
        _codeSystemProvider = codeSystemProvider;
    }

    public PackageTerminologySource(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional, ICodeSystemProvider? codeSystemProvider = null)
        : this(new LayeredValueSetProvider(primary, additional), codeSystemProvider)
    {
    }

    public string Name => "package";

    public ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var coverage = kind switch
        {
            CanonicalKind.CodeSystem => SourceCoverage.Partial,
            CanonicalKind.ValueSet when _valueSetProvider.IsKnownValueSet(canonical.Url) => SourceCoverage.Authoritative,
            _ => SourceCoverage.None,
        };
        return ValueTask.FromResult(coverage);
    }

    public Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);
        var lookup = _codeSystemProvider?.ContainsCode(code.System, code.Code) switch
        {
            true => ConceptLookup.Found(new ConceptDetails(null, null, _codeSystemProvider!.GetDisplay(code.System, code.Code), null, null, null)),
            false when NonEnumerableSystems.Contains(code.System) => ConceptLookup.Undetermined(new UnknownReason(
                UnknownKind.NonEnumerableSystem, code.System, $"'{code.System}' cannot be enumerated from package content")),
            false => ConceptLookup.NotFound,
            null => ConceptLookup.Undetermined(new UnknownReason(
                UnknownKind.SystemNotAvailable, code.System, $"Package content does not include CodeSystem '{code.System}'")),
        };
        return Task.FromResult(lookup);
    }

    public Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);
        SubsumesOutcome outcome = new SubsumesOutcome.Undetermined(new UnknownReason(
            UnknownKind.UnsupportedFilter, codeA.System, "Package terminology content carries no concept hierarchy"));
        return Task.FromResult(outcome);
    }

    public Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken)
    {
        /* moved verbatim from InMemoryTerminologyService.ValidateCodeAsync */
    }

    public async Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken)
    {
        /* moved verbatim */
    }

    public Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken)
        => Task.FromResult<ExpandResult?>(null);

    public Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken)
        => Task.FromResult(new TranslateResult(
            Result: false,
            Message: "ConceptMap translation not supported in in-memory terminology service",
            Matches: Array.Empty<TranslateMatch>()));

    /* Verified() and DetermineSeverity(...) moved verbatim as private static members */
}
```

`InMemoryTerminologyService` becomes a delegator under the old contract. Its constructors keep their exact signatures and build `_source = new PackageTerminologySource(...)`. `ValidateCodeAsync`, `ValidateBindingAsync`, `ExpandValueSetAsync`, and `TranslateCodeAsync` forward to `_source`. Old `LookupCodeAsync` maps the new answer:

```csharp
    public async Task<LookupResult> LookupCodeAsync(string system, string code, string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(system) || string.IsNullOrEmpty(code))
        {
            return new LookupResult(false, null, null, null, null, null, null);
        }

        var lookup = await _source.LookupAsync(new CodeRef(system, code, version), cancellationToken);
        return lookup.Concept is { } concept
            ? new LookupResult(true, concept.Name, concept.Version, concept.Display, concept.Definition, concept.Properties, concept.Designations)
            : new LookupResult(false, null, null, null, null, null, null);
    }
```

`SubsumesAsync` keeps returning `new SubsumesResult("not-subsumed")` until Task 7 changes the contract.

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test test/Ignixa.Validation.Tests` and `dotnet test test/Ignixa.PackageManagement.Tests`
Expected: PASS. This is the same count as before plus the new `PackageTerminologySourceTests`. Unchanged `InMemoryTerminologyService` tests prove the legacy move is verbatim.

- [ ] **Step 5: Commit**

```powershell
git add src/Core/Ignixa.Validation test/Ignixa.Validation.Tests/Terminology
git commit -m "Extract package terminology source from in-memory service" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 6: `SqlServerTerminologyService` becomes a terminology source

**Files:**
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs` (class declaration ~line 27–31, `LookupCodeAsync` ~86–122, `SubsumesAsync` ~443–485)
- Test: `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/SqlTerminologySourceTests.cs`

**Interfaces:**
- Consumes: Task 3 and Task 4 interfaces; existing private helpers `ResolveSystemIdAsync`, `ReadConceptAsync`, `ParsePropertiesJson`, `FindConceptIdAsync`, `IsDescendantOfAsync`, and `GetImportStatusAsync`.
- Produces: `SqlServerTerminologyService : ITerminologyService, ITerminologySource, ILegacyValueSetOperations, ITerminologyImportStatusProvider`. Task 7 removes `ITerminologyService`. It has:
  - `public string Name => "sql";`
  - `public async ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken)`: returns `Authoritative` when `GetImportStatusAsync(canonical.Url) == TerminologyImportStatus.Completed`, else `None`, for every kind. This is the same predicate Hybrid used.
  - `public async Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)`
  - `public async Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)`

The name stays `SqlServerTerminologyService`. Renaming it to the spec's `SqlTerminologySource` would churn ~20 integration-test constructions for no behavioural gain; Task 8 records the mapping in the spec.

Behaviour:
- **`LookupAsync`.** This is the existing body.
  - Unresolvable system → `Undetermined(new UnknownReason(UnknownKind.SystemNotAvailable, code.System, "CodeSystem is not loaded in the SQL terminology store"))`. Coverage normally prevents this, but a CodeSystem can be replaced between the coverage and lookup queries.
  - No row in either match mode → `NotFound`.
  - Row → `Found(new ConceptDetails(null, row.Version, row.Display, row.Definition, properties, designations))`.
- **`SubsumesAsync`.** This is the existing body, using `codeA.Version`.
  - Unresolvable system → `Undetermined(SystemNotAvailable)`.
  - Either concept unresolved → `Undetermined(new UnknownReason(UnknownKind.CodeNotInSystem, missing.ToString(), $"Code '{missing.Code}' is not defined in '{missing.CodeSystem}'"))`.
  - Otherwise → `Related(Equivalent | Subsumes | SubsumedBy | NotSubsumed)`, exactly as today.
- **Old members remain as adapters** until Task 7.
  - `LookupCodeAsync(system, code, version, ct)` maps `Concept` to the old `LookupResult`, or `Found: false`.
  - `SubsumesAsync(SubsumesParameters, ct)` returns `new SubsumesResult(outcome.KnownRelation?.ToFhirCode() ?? "not-subsumed")`, keeping today's wire answer until Task 7.
  - `ValidateBindingAsync`'s internal lookup calls `LookupAsync(new CodeRef(system, code, version), ct)` and tests `lookup.Result == Determination.True` in place of `lookup.Found`. Its existing null/empty guards ensure `system` and `code` are non-blank before the `CodeRef` is built.

- [ ] **Step 1: Write the failing integration tests**

```csharp
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Validation.Abstractions.Terminology;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class SqlTerminologySourceTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/source-vehicles";

    private TerminologyTestFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyTestFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private async Task ImportAsync()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyTestFixture.HierarchicalCodeSystemJson(SystemUrl));
        await _fixture.CreateSqlServerImporter().ImportCodeSystemAsync(_fixture.SystemPartitionId, packageResource, CancellationToken.None);
    }

    [Fact]
    public async Task GivenImportedCodeSystem_WhenCoverageAsked_ThenAuthoritative()
    {
        await ImportAsync();

        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef(SystemUrl), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.Authoritative);
    }

    [Fact]
    public async Task GivenNeverImportedCodeSystem_WhenCoverageAsked_ThenNone()
    {
        var coverage = await _fixture.CreateTerminologyService()
            .GetCoverageAsync(CanonicalKind.CodeSystem, new CanonicalRef("http://example.org/never"), CancellationToken.None);

        coverage.ShouldBe(SourceCoverage.None);
    }

    [Fact]
    public async Task GivenImportedConcept_WhenLookedUp_ThenFoundWithDisplay()
    {
        await ImportAsync();

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(new CodeRef(SystemUrl, "car"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.True);
        lookup.Concept!.Display.ShouldNotBeNullOrEmpty();
    }

    [Fact]
    public async Task GivenImportedSystemWithoutCode_WhenLookedUp_ThenNotFound()
    {
        await ImportAsync();

        var lookup = await _fixture.CreateTerminologyService().LookupAsync(new CodeRef(SystemUrl, "boat"), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
    }

    [Fact]
    public async Task GivenUnknownCode_WhenSubsumesAsked_ThenUndeterminedCodeNotInSystem()
    {
        // Regression: this answered "not-subsumed", a false negative presented as a valid FHIR result.
        await ImportAsync();

        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(new CodeRef(SystemUrl, "vehicle"), new CodeRef(SystemUrl, "boat"), CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.CodeNotInSystem);
        outcome.Unknown.Subject.ShouldBe($"{SystemUrl}#boat");
    }

    [Fact]
    public async Task GivenUnknownSystem_WhenSubsumesAsked_ThenUndeterminedSystemNotAvailable()
    {
        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(new CodeRef("http://example.org/never", "a"), new CodeRef("http://example.org/never", "b"), CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
    }

    [Theory]
    [InlineData("vehicle", "car", SubsumptionRelation.Subsumes)]
    [InlineData("car", "vehicle", SubsumptionRelation.SubsumedBy)]
    [InlineData("car", "truck", SubsumptionRelation.NotSubsumed)]
    [InlineData("car", "car", SubsumptionRelation.Equivalent)]
    public async Task GivenKnownCodes_WhenSubsumesAsked_ThenRelationIsDetermined(string a, string b, SubsumptionRelation expected)
    {
        await ImportAsync();

        var outcome = await _fixture.CreateTerminologyService()
            .SubsumesAsync(new CodeRef(SystemUrl, a), new CodeRef(SystemUrl, b), CancellationToken.None);

        outcome.KnownRelation.ShouldBe(expected);
    }
}
```

These are SQL integration tests and need the test SQL instance used by the rest of `Ignixa.DataLayer.SqlServer.IntegrationTests`; run them the same way that suite is run locally. The `GivenUnknownCode…` test is the spec §4.2 regression, so confirm that it fails at the base commit by its assertion, not by the build. Copy it into a base-commit worktree in its old form, `SubsumesAsync(new SubsumesParameters("vehicle", "boat", SystemUrl, null), …).Outcome.ShouldNotBe("not-subsumed")`, and observe the failure.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~SqlTerminologySourceTests"`
Expected: build FAIL — `SqlServerTerminologyService` does not contain `GetCoverageAsync`.

- [ ] **Step 3: Implement**

Class declaration:

```csharp
public sealed class SqlServerTerminologyService(
    ISqlExecutionService sqlExecutionService,
    int systemPartitionId,
    ILogger<SqlServerTerminologyService> logger)
    : ITerminologyService, ITerminologySource, ILegacyValueSetOperations, ITerminologyImportStatusProvider
```

New members:

```csharp
    public string Name => "sql";

    public async ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(canonical);
        var status = await GetImportStatusAsync(canonical.Url, cancellationToken);
        return status == TerminologyImportStatus.Completed ? SourceCoverage.Authoritative : SourceCoverage.None;
    }

    public async Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(code);

        var systemId = await ResolveSystemIdAsync(code.System, cancellationToken);
        if (systemId is null)
        {
            return ConceptLookup.Undetermined(NotLoaded(code.System));
        }

        var rows = await ReadConceptAsync(systemId.Value, code.Code, code.Version, MatchMode.CaseSensitive, cancellationToken);
        if (rows.Count == 0)
        {
            rows = await ReadConceptAsync(systemId.Value, code.Code, code.Version, MatchMode.CaseInsensitiveFallback, cancellationToken);
        }

        if (rows.Count == 0)
        {
            return ConceptLookup.NotFound;
        }

        var row = rows[0];
        var (properties, designations) = ParsePropertiesJson(row.PropertiesJson);

        // Name stays null: TermCodeSystem carries no name column.
        return ConceptLookup.Found(new ConceptDetails(null, row.Version, row.Display, row.Definition, properties, designations));
    }

    public async Task<SubsumesOutcome> SubsumesAsync(CodeRef codeA, CodeRef codeB, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codeA);
        ArgumentNullException.ThrowIfNull(codeB);

        var systemId = await ResolveSystemIdAsync(codeA.System, cancellationToken);
        if (systemId is null)
        {
            return new SubsumesOutcome.Undetermined(NotLoaded(codeA.System));
        }

        // One resolution per code, each matching exactly the way the other read paths do.
        var conceptA = await FindConceptIdAsync(systemId.Value, codeA.Code, codeA.Version, cancellationToken);
        if (conceptA is null)
        {
            return new SubsumesOutcome.Undetermined(NotInSystem(codeA));
        }

        var conceptB = await FindConceptIdAsync(systemId.Value, codeB.Code, codeA.Version, cancellationToken);
        if (conceptB is null)
        {
            return new SubsumesOutcome.Undetermined(NotInSystem(codeB));
        }

        // Codes differing only by case in a caseSensitive=false CodeSystem resolve to one concept.
        if (conceptA == conceptB)
        {
            return new SubsumesOutcome.Related(SubsumptionRelation.Equivalent);
        }

        if (await IsDescendantOfAsync(conceptB.Value, conceptA.Value, cancellationToken))
        {
            return new SubsumesOutcome.Related(SubsumptionRelation.Subsumes);
        }

        return await IsDescendantOfAsync(conceptA.Value, conceptB.Value, cancellationToken)
            ? new SubsumesOutcome.Related(SubsumptionRelation.SubsumedBy)
            : new SubsumesOutcome.Related(SubsumptionRelation.NotSubsumed);
    }

    private static UnknownReason NotLoaded(string system) =>
        new(UnknownKind.SystemNotAvailable, system, "CodeSystem is not loaded in the SQL terminology store");

    private static UnknownReason NotInSystem(CodeRef code) =>
        new(UnknownKind.CodeNotInSystem, code.ToString(), $"Code '{code.Code}' is not defined in '{code.CodeSystem}'");
```

Old members become adapters:

```csharp
    public async Task<LookupResult> LookupCodeAsync(string system, string code, string? version, CancellationToken cancellationToken)
    {
        var lookup = await LookupAsync(new CodeRef(system, code, version), cancellationToken);
        return lookup.Concept is { } c
            ? new LookupResult(true, c.Name, c.Version, c.Display, c.Definition, c.Properties, c.Designations)
            : new LookupResult(false, null, null, null, null, null, null);
    }

    public async Task<SubsumesResult> SubsumesAsync(SubsumesParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var outcome = await SubsumesAsync(
            new CodeRef(parameters.System, parameters.CodeA, parameters.Version),
            new CodeRef(parameters.System, parameters.CodeB, parameters.Version),
            cancellationToken);
        return new SubsumesResult(outcome.KnownRelation?.ToFhirCode() ?? "not-subsumed");
    }
```

`new CodeRef(...)` throws `ArgumentException` for a blank system or code, which matches the `ThrowIfNullOrWhiteSpace` guards it replaces.

If the class is currently `public class` rather than `public sealed class`, keep its existing modifier. Only the interface list changes. Check with `rg -n "class SqlServerTerminologyService" src`.

- [ ] **Step 4: Run the tests to verify they pass, then run the existing terminology suites unchanged**

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~Terminology"`
Expected: PASS, including every pre-existing terminology test unchanged, since the adapters preserve old answers.

- [ ] **Step 5: Commit**

```powershell
git add src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/SqlTerminologySourceTests.cs
git commit -m "Expose SQL terminology store as a terminology source" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 7: Contract switch — `ComposingTerminologyService` replaces `HybridTerminologyService`

This is the one deliberately atomic task. Do the steps in order; the build is red between Step 3 and Step 9.

**Files:**
- Modify: `src/Core/Ignixa.Validation/Abstractions/ITerminologyService.cs`
- Delete: `src/Core/Ignixa.Validation/Abstractions/LookupResult.cs`, `SubsumesParameters.cs`, `SubsumesResult.cs`
- Create: `src/Core/Ignixa.Validation/Abstractions/PropertyValue.cs`, `Designation.cs` (moved out of `LookupResult.cs` unchanged)
- Create: `src/Core/Ignixa.Validation/Services/Terminology/ComposingTerminologyService.cs`
- Modify: `src/Core/Ignixa.Validation/Services/InMemoryTerminologyService.cs`
- Modify: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerTerminologyService.cs` (drop `ITerminologyService` and the two Task 6 adapters)
- Delete: `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/HybridTerminologyService.cs`
- Delete: `test/Ignixa.DataLayer.SqlServer.Tests/Features/Terminology/HybridTerminologyServiceRoutingTests.cs`
- Modify: `src/Application/Ignixa.Api/Registrations/ValidationServicesRegistration.cs:95-133`
- Modify: `src/Application/Ignixa.Application/Features/Experimental/Terminology/Subsumes/SubsumesHandler.cs`
- Test (new): `test/Ignixa.Validation.Tests/Terminology/ComposingTerminologyServiceTests.cs`, `test/Ignixa.Application.Experimental.Tests/Features/Terminology/SubsumesHandlerTests.cs`
- Test (port): every file listed by `rg -l "LookupCodeAsync|SubsumesParameters|SubsumesResult|LookupResult|HybridTerminologyService" test src`. At plan time that is:
  - `test/Ignixa.DataLayer.SqlServer.IntegrationTests/`: `Fixtures/TerminologyTestFixture.cs`, `PopulatedTerminologySchemaUpgradeTests.cs`, and `Features/Terminology/{CanonicalTerminologyReplacementTests,SqlServerCodeSystemImporterTests,SqlServerValueSetImporterTests,TerminologyCanonicalIdentityTests,TerminologyCaseSensitivityTests,TerminologyReplacementFreshnessTests}.cs`
  - `test/Ignixa.PackageManagement.Tests/Validation/PackageBackedValidatorTests.cs`
  - `test/Ignixa.Validation.Tests/Conformance/ValidatorConformanceRunner.cs`
  - `test/Ignixa.Api.Tests/Registrations/ValidationServicesRegistrationTerminologyTests.cs`
  - the `ITerminologyService` substitutes in `test/Ignixa.Validation.Tests/BindingCheckTests.cs`, `ValidationBehaviorResourceScopeTests`, `ValidateResourceHandlerTests`, `TransformResourceHandlerTests`, and `ConceptMapResolverServiceTests` (only if they stub the changed members)
- Modify (comments only): `src/Application/Ignixa.Domain/Terminology/TerminologyImportResult.cs:104`, `src/Application/Ignixa.Application.BackgroundOperations/Terminology/Activities/ImportTerminologyResourceActivity.cs:27`, `src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/SqlServerCodeSystemImporter.cs:223`

**Interfaces:**
- Consumes: Tasks 2–6.
- Produces the Phase 1 `ITerminologyService`. Members with `// legacy` keep their exact signatures and are replaced in the phase named:

```csharp
public interface ITerminologyService
{
    Task<ConceptLookup> LookupAsync(LookupRequest request, CancellationToken cancellationToken);

    Task<ValidateCodeResult> ValidateCodeInCodeSystemAsync(ValidateCodeRequest request, CancellationToken cancellationToken);

    Task<SubsumesOutcome> SubsumesAsync(SubsumesRequest request, CancellationToken cancellationToken);

    // legacy until Phase 2 (becomes ValidateCodeInValueSetAsync)
    Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken);

    // kept; output preserved exactly (Phase 2 re-implements it over composed membership)
    Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken);

    // legacy until Phase 4
    Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken);

    // legacy until Phase 5
    Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken);
}
```

Keep the existing XML doc comments on the legacy members. Give each new member a one-line `<summary>` that states it never answers for content no source holds. `TerminologyValidationResult` stays in `ITerminologyService.cs`, as today.

- `public sealed class ComposingTerminologyService(IEnumerable<ITerminologySource> sources) : ITerminologyService`
- `InMemoryTerminologyService` keeps its two public constructors and remains `ITerminologyService`.

`ComposingTerminologyService` behaviour:
- **Lookup and subsumes.** Routed with `CanonicalKind.CodeSystem` on the request's code system.
- **`ValidateCodeInCodeSystemAsync`.** Built on a routed lookup:
  - `True` → `Result=True`, `Display` = the source display. When `request.Display` is non-null and differs ordinally from a non-empty source display, add `TerminologyIssue(Warning, DisplayMismatch, "Display '{given}' does not match expected '{actual}'")`. The result stays `True`; the R4 wire mapping is decided in Phase 3.
  - `False` → `Result=False` with `TerminologyIssue(Error, CodeInvalid, "Code '{code}' is not defined in '{system}'")`.
  - `Unknown` → `Result=Unknown`, no issues, `Unknowns` = the routed reasons.
- **Legacy members.** Reproduce Hybrid exactly:
  - `ValidateCodeAsync` and `ValidateBindingAsync` return Hybrid's `"ValueSet URL is required"` error for a null or empty URL, without consulting any source.
  - Otherwise they and `ExpandValueSetAsync` call `SelectLegacyAsync(new CanonicalRef(url))`.
  - `TranslateCodeAsync` calls `FirstLegacy`.
- **Constructor.** Throws `ArgumentException` when given zero sources.

- [ ] **Step 1: Write the failing composer tests (ported Hybrid routing cases plus the new operations)**

```csharp
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Terminology;

public class ComposingTerminologyServiceTests
{
    private const string System = "http://example.org/cs";
    private const string ValueSet = "http://example.org/vs";

    private readonly ITerminologySource _sql = Source("sql");
    private readonly ITerminologySource _package = Source("package");

    private static ITerminologySource Source(string name)
    {
        var source = Substitute.For<ITerminologySource, ILegacyValueSetOperations>();
        source.Name.Returns(name);
        source.GetCoverageAsync(Arg.Any<CanonicalKind>(), Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(SourceCoverage.None));
        return source;
    }

    private static void Covers(ITerminologySource source, CanonicalKind kind, SourceCoverage coverage) =>
        source.GetCoverageAsync(kind, Arg.Any<CanonicalRef>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<SourceCoverage>(coverage));

    private static ILegacyValueSetOperations Legacy(ITerminologySource source) => (ILegacyValueSetOperations)source;

    private ComposingTerminologyService Create() => new([_sql, _package]);

    [Fact]
    public async Task GivenImportedCodeSystem_WhenLookingUp_ThenSqlAnswersAndPackageIsNotAsked()
    {
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.NotFound);

        var lookup = await Create().LookupAsync(new LookupRequest(new CodeRef(System, "x")), CancellationToken.None);

        lookup.Result.ShouldBe(Determination.False);
        await _package.DidNotReceive().LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenUnimportedCodeSystem_WhenLookingUp_ThenPackageAnswers()
    {
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        var details = new ConceptDetails(null, null, "Car", null, null, null);
        _package.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.Found(details));

        var lookup = await Create().LookupAsync(new LookupRequest(new CodeRef(System, "car")), CancellationToken.None);

        lookup.Concept.ShouldBe(details);
        await _sql.DidNotReceive().LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenNoSourceHoldsTheSystem_WhenSubsumesAsked_ThenUndeterminedNeverNotSubsumed()
    {
        // Regression: Hybrid sent every $subsumes to SQL, which answered "not-subsumed" for an unknown system.
        var request = new SubsumesRequest(new CodeRef(System, "a"), new CodeRef(System, "b"));

        var outcome = await Create().SubsumesAsync(request, CancellationToken.None);

        outcome.Unknown!.Kind.ShouldBe(UnknownKind.SystemNotAvailable);
        outcome.KnownRelation.ShouldBeNull();
    }

    [Fact]
    public async Task GivenUnknownCode_WhenValidatingInCodeSystem_ThenResultIsUnknownWithReasons()
    {
        var reason = new UnknownReason(UnknownKind.NonEnumerableSystem, System, "subset");
        Covers(_package, CanonicalKind.CodeSystem, SourceCoverage.Partial);
        _package.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.Undetermined(reason));

        var result = await Create().ValidateCodeInCodeSystemAsync(new ValidateCodeRequest(new CodeRef(System, "x")), CancellationToken.None);

        result.Result.ShouldBe(Determination.Unknown);
        result.Unknowns.ShouldBe([reason]);
        result.Issues.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAbsentCode_WhenValidatingInCodeSystem_ThenFalseWithCodeInvalidError()
    {
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>()).Returns(ConceptLookup.NotFound);

        var result = await Create().ValidateCodeInCodeSystemAsync(new ValidateCodeRequest(new CodeRef(System, "x")), CancellationToken.None);

        result.Result.ShouldBe(Determination.False);
        result.Issues.ShouldHaveSingleItem().Kind.ShouldBe(TerminologyIssueKind.CodeInvalid);
    }

    [Fact]
    public async Task GivenWrongDisplay_WhenValidatingInCodeSystem_ThenTrueWithDisplayWarning()
    {
        Covers(_sql, CanonicalKind.CodeSystem, SourceCoverage.Authoritative);
        _sql.LookupAsync(Arg.Any<CodeRef>(), Arg.Any<CancellationToken>())
            .Returns(ConceptLookup.Found(new ConceptDetails(null, null, "Car", null, null, null)));

        var result = await Create().ValidateCodeInCodeSystemAsync(new ValidateCodeRequest(new CodeRef(System, "car"), "Automobile"), CancellationToken.None);

        result.Result.ShouldBe(Determination.True);
        result.Display.ShouldBe("Car");
        var issue = result.Issues.ShouldHaveSingleItem();
        issue.Kind.ShouldBe(TerminologyIssueKind.DisplayMismatch);
        issue.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Fact]
    public async Task GivenImportedValueSet_WhenValidatingABinding_ThenSqlLegacyIsUsed()
    {
        Covers(_sql, CanonicalKind.ValueSet, SourceCoverage.Authoritative);

        await Create().ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        await Legacy(_sql).Received(1).ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, Arg.Any<CancellationToken>());
        await Legacy(_package).DidNotReceiveWithAnyArgs().ValidateBindingAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GivenUnimportedValueSet_WhenValidatingABinding_ThenPackageLegacyIsTheFallback()
    {
        await Create().ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        await Legacy(_package).Received(1).ValidateBindingAsync(ValueSet, BindingStrength.Required, System, "x", null, null, Arg.Any<CancellationToken>());
        await Legacy(_sql).DidNotReceiveWithAnyArgs().ValidateBindingAsync(default!, default, default, default, default, default, default);
    }

    [Fact]
    public async Task GivenUnimportedValueSet_WhenValidatingACode_ThenPackageLegacyIsTheFallback()
    {
        await Create().ValidateCodeAsync(System, "x", null, ValueSet, CancellationToken.None);

        await Legacy(_package).Received(1).ValidateCodeAsync(System, "x", null, ValueSet, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenImportedValueSet_WhenExpanding_ThenSqlLegacyIsUsed()
    {
        Covers(_sql, CanonicalKind.ValueSet, SourceCoverage.Authoritative);
        var parameters = new ExpansionParameters(ValueSet);

        await Create().ExpandValueSetAsync(parameters, CancellationToken.None);

        await Legacy(_sql).Received(1).ExpandValueSetAsync(parameters, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task GivenNoValueSetUrl_WhenValidating_ThenErrorWithoutConsultingSources(string? url)
    {
        var code = await Create().ValidateCodeAsync(System, "x", null, url, CancellationToken.None);
        var binding = await Create().ValidateBindingAsync(url!, BindingStrength.Required, System, "x", null, null, CancellationToken.None);

        code.ShouldBe(new TerminologyValidationResult(false, IssueSeverity.Error, "ValueSet URL is required"));
        binding.ShouldBe(new BindingValidationResult(false, BindingStrength.Required, IssueSeverity.Error, "ValueSet URL is required", null));
        await _sql.DidNotReceiveWithAnyArgs().GetCoverageAsync(default, default!, default);
    }

    [Fact]
    public async Task GivenAnyImportState_WhenTranslating_ThenFirstLegacySourceIsUsedWithoutCoverageQuery()
    {
        var parameters = new TranslateParameters(null, null, "x", System, null, null, null, null);

        await Create().TranslateCodeAsync(parameters, CancellationToken.None);

        await Legacy(_sql).Received(1).TranslateCodeAsync(parameters, Arg.Any<CancellationToken>());
        await _sql.DidNotReceiveWithAnyArgs().GetCoverageAsync(default, default!, default);
    }

    [Fact]
    public void GivenNoSources_WhenConstructed_ThenThrows()
    {
        Should.Throw<ArgumentException>(() => new ComposingTerminologyService([]));
    }
}
```


- [ ] **Step 2: Write the failing handler test (the 400 regression)**

```csharp
using Ignixa.Application.Features.Experimental.Terminology.Subsumes;
using Ignixa.Domain.Exceptions;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Experimental.Tests.Features.Terminology;

public class SubsumesHandlerTests
{
    private const string System = "http://example.org/cs";

    private readonly ITerminologyService _terminologyService = Substitute.For<ITerminologyService>();

    private SubsumesHandler CreateHandler() => new(_terminologyService, NullLogger<SubsumesHandler>.Instance);

    [Fact]
    public async Task GivenUndeterminedOutcome_WhenHandled_ThenBadRequestNamesTheReason()
    {
        // Regression: an unknown code reached the client as a well-formed "not-subsumed".
        _terminologyService.SubsumesAsync(Arg.Any<SubsumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesOutcome.Undetermined(new UnknownReason(UnknownKind.CodeNotInSystem, $"{System}#b", "Code 'b' is not defined")));

        var ex = await Should.ThrowAsync<BadRequestException>(
            () => CreateHandler().HandleAsync(new SubsumesQuery(1, "a", "b", System, null), CancellationToken.None));

        ex.Message.ShouldContain("Code 'b' is not defined");
    }

    [Theory]
    [InlineData(SubsumptionRelation.Subsumes, "subsumes")]
    [InlineData(SubsumptionRelation.NotSubsumed, "not-subsumed")]
    public async Task GivenKnownRelation_WhenHandled_ThenOutcomeCodeIsReturned(SubsumptionRelation relation, string code)
    {
        _terminologyService.SubsumesAsync(Arg.Any<SubsumesRequest>(), Arg.Any<CancellationToken>())
            .Returns(new SubsumesOutcome.Related(relation));

        var result = await CreateHandler().HandleAsync(new SubsumesQuery(1, "a", "b", System, null), CancellationToken.None);

        result.ParametersResource["parameter"]![0]!["valueCode"]!.GetValue<string>().ShouldBe(code);
    }
}
```

`SubsumesQuery` is `(TenantId, CodeA, CodeB, System, Version)`, and `SubsumesQueryResult` is `(JsonNode ParametersResource)`. Confirm the regression at the base commit: the old handler returns `not-subsumed` for an unknown code, so `ThrowAsync` fails.

- [ ] **Step 3: Change the contract and delete the old types**

1. Replace the `ITerminologyService` interface body with the Phase 1 contract above, and add `using Ignixa.Validation.Abstractions.Terminology;`.
2. Move `PropertyValue` and `Designation` out of `LookupResult.cs` into their own files, unchanged.
3. `git rm` `LookupResult.cs`, `SubsumesParameters.cs`, and `SubsumesResult.cs`.

- [ ] **Step 4: Implement `ComposingTerminologyService`**

```csharp
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>
/// The terminology service: routes each canonical to the sources that hold it and combines their
/// three-state answers. Never answers for content that no source holds.
/// </summary>
public sealed class ComposingTerminologyService : ITerminologyService
{
    private const string ValueSetUrlRequired = "ValueSet URL is required";

    private readonly SourceRouter _router;

    public ComposingTerminologyService(IEnumerable<ITerminologySource> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);
        var list = sources.ToList();
        if (list.Count == 0)
        {
            throw new ArgumentException("At least one terminology source is required.", nameof(sources));
        }

        _router = new SourceRouter(list);
    }

    public async Task<ConceptLookup> LookupAsync(LookupRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return (await RoutedLookupAsync(request.Code, cancellationToken)).Answer;
    }

    public async Task<ValidateCodeResult> ValidateCodeInCodeSystemAsync(ValidateCodeRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routed = await RoutedLookupAsync(request.Code, cancellationToken);
        return routed.Answer.Result switch
        {
            Determination.True => Valid(request, routed.Answer.Concept!.Display),
            Determination.False => new ValidateCodeResult(
                Determination.False,
                null,
                [new TerminologyIssue(IssueSeverity.Error, TerminologyIssueKind.CodeInvalid, $"Code '{request.Code.Code}' is not defined in '{request.Code.CodeSystem}'")],
                []),
            _ => new ValidateCodeResult(Determination.Unknown, null, [], routed.Unknowns),
        };
    }

    public async Task<SubsumesOutcome> SubsumesAsync(SubsumesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var routed = await _router.AskAsync<SubsumesOutcome>(
            CanonicalKind.CodeSystem,
            request.CodeSystem,
            (source, ct) => source.SubsumesAsync(request.CodeA, request.CodeB, ct),
            reason => new SubsumesOutcome.Undetermined(reason),
            cancellationToken);
        return routed.Answer;
    }

    public async Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new TerminologyValidationResult(false, IssueSeverity.Error, ValueSetUrlRequired);
        }

        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(valueSetUrl), cancellationToken);
        return await legacy.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);
    }

    public async Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(valueSetUrl))
        {
            return new BindingValidationResult(false, strength, IssueSeverity.Error, ValueSetUrlRequired, null);
        }

        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(valueSetUrl), cancellationToken);
        return await legacy.ValidateBindingAsync(valueSetUrl, strength, system, code, display, version, cancellationToken);
    }

    public async Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        var legacy = await _router.SelectLegacyAsync(new CanonicalRef(parameters.Url), cancellationToken);
        return await legacy.ExpandValueSetAsync(parameters, cancellationToken);
    }

    public Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return _router.FirstLegacy.TranslateCodeAsync(parameters, cancellationToken);
    }

    private Task<RoutedAnswer<ConceptLookup>> RoutedLookupAsync(CodeRef code, CancellationToken cancellationToken) =>
        _router.AskAsync(
            CanonicalKind.CodeSystem,
            code.CodeSystem,
            (source, ct) => source.LookupAsync(code, ct),
            ConceptLookup.Undetermined,
            cancellationToken);

    private static ValidateCodeResult Valid(ValidateCodeRequest request, string? display)
    {
        var mismatch = request.Display is not null
            && !string.IsNullOrEmpty(display)
            && !string.Equals(request.Display, display, StringComparison.Ordinal);
        IReadOnlyList<TerminologyIssue> issues = mismatch
            ? [new TerminologyIssue(IssueSeverity.Warning, TerminologyIssueKind.DisplayMismatch, $"Display '{request.Display}' does not match expected '{display}'")]
            : [];
        return new ValidateCodeResult(Determination.True, display, issues, []);
    }
}
```

`ExpansionParameters.Url` is non-nullable. `new CanonicalRef` throws `ArgumentException` on a blank URL, which is what Hybrid did via `GetImportStatusAsync`'s `ThrowIfNullOrWhiteSpace`.

- [ ] **Step 5: Re-point `InMemoryTerminologyService`**

```csharp
public class InMemoryTerminologyService : ITerminologyService
{
    private readonly PackageTerminologySource _source;
    private readonly ComposingTerminologyService _composer;

    public InMemoryTerminologyService(IValueSetProvider valueSetProvider, ICodeSystemProvider? codeSystemProvider = null)
        : this(new PackageTerminologySource(valueSetProvider, codeSystemProvider))
    {
    }

    public InMemoryTerminologyService(IValueSetProvider primary, IEnumerable<IValueSetProvider> additional, ICodeSystemProvider? codeSystemProvider = null)
        : this(new PackageTerminologySource(primary, additional, codeSystemProvider))
    {
    }

    private InMemoryTerminologyService(PackageTerminologySource source)
    {
        _source = source;
        _composer = new ComposingTerminologyService([source]);
    }

    public Task<ConceptLookup> LookupAsync(LookupRequest request, CancellationToken cancellationToken) => _composer.LookupAsync(request, cancellationToken);

    public Task<ValidateCodeResult> ValidateCodeInCodeSystemAsync(ValidateCodeRequest request, CancellationToken cancellationToken) => _composer.ValidateCodeInCodeSystemAsync(request, cancellationToken);

    public Task<SubsumesOutcome> SubsumesAsync(SubsumesRequest request, CancellationToken cancellationToken) => _composer.SubsumesAsync(request, cancellationToken);

    // Straight to the source, not the composer: the composer's empty-URL error is Hybrid's server
    // behaviour, while the standalone validator has always warned and skipped instead.
    public Task<TerminologyValidationResult> ValidateCodeAsync(string? system, string? code, string? display, string? valueSetUrl, CancellationToken cancellationToken) =>
        _source.ValidateCodeAsync(system, code, display, valueSetUrl, cancellationToken);

    public Task<BindingValidationResult> ValidateBindingAsync(string valueSetUrl, BindingStrength strength, string? system, string? code, string? display, string? version, CancellationToken cancellationToken) =>
        _source.ValidateBindingAsync(valueSetUrl, strength, system, code, display, version, cancellationToken);

    public Task<ExpandResult?> ExpandValueSetAsync(ExpansionParameters parameters, CancellationToken cancellationToken) => _source.ExpandValueSetAsync(parameters, cancellationToken);

    public Task<TranslateResult> TranslateCodeAsync(TranslateParameters parameters, CancellationToken cancellationToken) => _source.TranslateCodeAsync(parameters, cancellationToken);
}
```

Keep the class XML summary, and update its wording to say it is the package source behind the composing service for standalone (serverless) validation.

- [ ] **Step 6: Strip the SQL adapters and Hybrid**

In `SqlServerTerminologyService`:
1. Remove `ITerminologyService` from the interface list, and delete the Task 6 `LookupCodeAsync` and `SubsumesAsync(SubsumesParameters…)` adapters.
2. `git rm src/DataLayer/Ignixa.DataLayer.SqlServer/Features/Terminology/HybridTerminologyService.cs test/Ignixa.DataLayer.SqlServer.Tests/Features/Terminology/HybridTerminologyServiceRoutingTests.cs`.
3. Every Hybrid routing case now lives in `ComposingTerminologyServiceTests`, except the import-status delegation case. The composer is no status provider, and `ValidationServicesRegistrationTerminologyTests` already asserts that `ITerminologyImportStatusProvider` resolves to `SqlServerTerminologyService`.

- [ ] **Step 7: Rewire DI** (`ValidationServicesRegistration.RegisterTerminologyServices`)

```csharp
    private static void RegisterTerminologyServices(ContainerBuilder builder)
    {
        // Package content for the request's FHIR version and tenant. Needs a valid request context.
        builder.Register(c =>
        {
            var versionContext = c.Resolve<IFhirVersionContext>();
            var requestContext = c.Resolve<IFhirRequestContextAccessor>().RequestContext
                ?? throw new InvalidOperationException(
                    "FHIR request context not available. PackageTerminologySource requires " +
                    "a valid request context with FhirVersion and TenantId for terminology resolution.");
            var schemaProvider = versionContext.GetSchemaProvider(requestContext.FhirVersion, requestContext.TenantId);
            return new PackageTerminologySource(schemaProvider.ValueSetProvider);
        })
        .AsSelf()
        .InstancePerLifetimeScope();

        // SqlServerTerminologyService: terminology is server-wide, so it reads the system partition.
        builder.Register(c => new SqlServerTerminologyService(
                c.Resolve<ISqlExecutionService>(),
                SystemConstants.SystemPartitionId,
                c.Resolve<ILogger<SqlServerTerminologyService>>()))
            .AsSelf()
            .As<ITerminologyImportStatusProvider>()
            .InstancePerLifetimeScope();

        // Source order is routing order: SQL first (authoritative for imported content), package second.
        builder.Register<ITerminologyService>(c => new ComposingTerminologyService(
            [c.Resolve<SqlServerTerminologyService>(), c.Resolve<PackageTerminologySource>()]))
            .InstancePerLifetimeScope();
    }
```

First run `rg -n "Resolve<InMemoryTerminologyService>|InMemoryTerminologyService>" src test`. If anything else resolves `InMemoryTerminologyService` from the container, keep its registration as well. Otherwise it is removed here, because the composer replaces it.

- [ ] **Step 8: Port `SubsumesHandler`**

```csharp
    public async Task<SubsumesQueryResult> HandleAsync(SubsumesQuery request, CancellationToken cancellationToken)
    {
        var subsumes = new SubsumesRequest(
            new CodeRef(request.System, request.CodeA, request.Version),
            new CodeRef(request.System, request.CodeB, request.Version));

        var outcome = await _terminologyService.SubsumesAsync(subsumes, cancellationToken);
        if (outcome is SubsumesOutcome.Undetermined undetermined)
        {
            throw new BadRequestException($"Cannot determine subsumption: {undetermined.Reason.Detail}");
        }

        var parametersResponse = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray(new JsonObject
            {
                ["name"] = "outcome",
                ["valueCode"] = outcome.KnownRelation!.Value.ToFhirCode(),
            }),
        };

        return new SubsumesQueryResult(parametersResponse);
    }
```

`BadRequestException` (namespace `Ignixa.Domain.Exceptions`) is mapped to 400 with an `invalid` issue by the existing exception handling. `BulkDeleteRequestParser` documents and relies on this. Building `JsonObject` directly replaces the serialize-then-parse round trip. Blank `CodeA`/`CodeB`/`System` now throw `ArgumentException` from `CodeRef`. Check how the endpoint validates those inputs before dispatching (`rg -n "SubsumesCodes" -A 40 src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs`). If it does not reject blanks first, add a `BadRequestException` guard at the top of `HandleAsync` naming the missing parameter, so a blank input stays a 400 and not a 500.

- [ ] **Step 9: Port the remaining callers mechanically**

| Old | New |
|---|---|
| `service.LookupCodeAsync(s, c, v, ct)` | `service.LookupAsync(new CodeRef(s, c, v), ct)` on `SqlServerTerminologyService`, or `service.LookupAsync(new LookupRequest(new CodeRef(s, c, v)), ct)` on `ITerminologyService` |
| `.Found.ShouldBeTrue()` / `.Found.ShouldBeFalse()` | `.Result.ShouldBe(Determination.True)` / `.Result.ShouldBe(Determination.False)`. Where the old test pinned *unknown system → not found*, assert `Determination.Unknown`, since that case is now honestly unknown. |
| `lookup.Display` / `.Version` / `.Properties` / `.Designations` / `.Definition` | `lookup.Concept!.Display`, and so on |
| `service.SubsumesAsync(new SubsumesParameters(a, b, s, v), ct)` | `service.SubsumesAsync(new CodeRef(s, a, v), new CodeRef(s, b, v), ct)` on the SQL source |
| `.Outcome.ShouldBe("subsumes")` | `.KnownRelation.ShouldBe(SubsumptionRelation.Subsumes)` (same for each code) |
| `.Outcome.ShouldBe("not-subsumed")` where a code or system is **unknown** | `.Unknown.ShouldNotBeNull()`. This is the intended behaviour change, so note it in the commit body. |
| `ShouldBeOfType<HybridTerminologyService>()` | `ShouldBeOfType<ComposingTerminologyService>()`, and rename the test to `…ThenTheComposingServiceIsReturned` |

`TerminologyTestFixture.CreateTerminologyService()` keeps returning `SqlServerTerminologyService`. Add `using Ignixa.Validation.Abstractions.Terminology;` where needed. Update the three Hybrid-mentioning comments: each now names `ComposingTerminologyService` / SQL source coverage and keeps the same meaning ("anything other than Completed is not covered by the SQL source").

- [ ] **Step 10: Build and run everything this touches**

Run: `dotnet build All.sln`
Expected: Build succeeded, 0 warnings. `rg -n "HybridTerminologyService|LookupCodeAsync|SubsumesParameters|SubsumesResult" src test` returns nothing.

Run: `dotnet test test/Ignixa.Validation.Tests`, `dotnet test test/Ignixa.Application.Experimental.Tests`, `dotnet test test/Ignixa.Api.Tests`, `dotnet test test/Ignixa.PackageManagement.Tests`, and `dotnet test test/Ignixa.DataLayer.SqlServer.Tests`
Expected: PASS. In particular, `BindingCheckTests` and every `ValidateBindingAsync` test are **unchanged and green**, which guards the spec's exact-preservation constraint.

Run: `dotnet test test/Ignixa.DataLayer.SqlServer.IntegrationTests --filter "FullyQualifiedName~Terminology"`
Expected: PASS.

- [ ] **Step 11: Mutation-check the two guards**

1. In `ComposingTerminologyService.ValidateBindingAsync`, temporarily change `SelectLegacyAsync(...)` to `_router.FirstLegacy`. Run `ComposingTerminologyServiceTests`; expect `GivenUnimportedValueSet_WhenValidatingABinding_ThenPackageLegacyIsTheFallback` to fail. Revert.
2. In `SourceRouter.AskAsync`, temporarily ask partial sources before authoritative ones. Run `SourceRouterTests`; expect `GivenAuthoritativeSourceAfterPartial…` to fail. Revert.

- [ ] **Step 12: Commit**

```powershell
git add -A src test
git commit -m "Route terminology through composing service; remove Hybrid" -m "Lookup, subsumes and CodeSystem validate-code now combine per-source three-state answers. Subsumes with an unknown code or system is undetermined (HTTP 400) instead of not-subsumed. ValidateBinding, ValueSet validate-code, expand and translate keep Hybrid's whole-call routing through ILegacyValueSetOperations." -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

### Task 8: Phase 1 documentation and spec sync

**Files:**
- Modify: `docs/site/docs/core-sdk/validation.md:389-434` (the `CustomTerminologyService` example)
- Modify: `src/Core/Ignixa.Validation/README.md:39`
- Modify: `docs/site/docs/server/fhir/operations.md` (the `$subsumes (CodeSystem)` section, ~line 551)
- Modify: `docs/superpowers/specs/2026-10-20-composable-terminology-operations-design.md` (§3.1, §3.2, §8)

**Interfaces:**
- Consumes: the Task 7 `ITerminologyService`.
- Produces: none (documentation only).

- [ ] **Step 1: Replace the SDK custom-service example**

The example must compile against the Phase 1 contract. Replace the class with a custom **source**, the extension point consumers should now use, composed with the package source:

```csharp
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Abstractions.Terminology;
using Ignixa.Validation.Services.Terminology;

public sealed class MyLocalCodesSource : ITerminologySource
{
    private const string System = "http://example.org/codes";
    private static readonly Dictionary<string, string> Codes = new() { ["a"] = "Alpha", ["b"] = "Beta" };

    public string Name => "my-local-codes";

    public ValueTask<SourceCoverage> GetCoverageAsync(CanonicalKind kind, CanonicalRef canonical, CancellationToken cancellationToken) =>
        ValueTask.FromResult(kind == CanonicalKind.CodeSystem && canonical.Url == System
            ? SourceCoverage.Authoritative
            : SourceCoverage.None);

    public Task<ConceptLookup> LookupAsync(CodeRef code, CancellationToken cancellationToken) =>
        Task.FromResult(Codes.TryGetValue(code.Code, out var display)
            ? ConceptLookup.Found(new ConceptDetails(null, null, display, null, null, null))
            : ConceptLookup.NotFound);

    public Task<SubsumesOutcome> SubsumesAsync(CodeRef a, CodeRef b, CancellationToken cancellationToken) =>
        Task.FromResult<SubsumesOutcome>(a.Code == b.Code
            ? new SubsumesOutcome.Related(SubsumptionRelation.Equivalent)
            : new SubsumesOutcome.Related(SubsumptionRelation.NotSubsumed));
}

// Compose it with package content; the first source listed is consulted first.
ITerminologyService terminology = new ComposingTerminologyService(
[
    new MyLocalCodesSource(),
    new PackageTerminologySource(valueSetProvider),
]);
```

Check the `ConceptDetails` positional order in `ConceptDetails.cs` (Task 4) and make the example match it exactly. In the prose above the example:
- Say that ValueSet validation, binding, expand, and translate still go through `ILegacyValueSetOperations` until later releases, so a source without that interface does not take part in them.
- Say that at least one source (normally `PackageTerminologySource`) must implement it.
- Add a short "Breaking changes" note: `LookupCodeAsync` becomes `LookupAsync(LookupRequest)` returning `ConceptLookup`; `SubsumesAsync(SubsumesParameters)` becomes `SubsumesAsync(SubsumesRequest)` returning `SubsumesOutcome`; `HybridTerminologyService` is replaced by `ComposingTerminologyService`.

- [ ] **Step 2: Update the package README and operations page**

- `src/Core/Ignixa.Validation/README.md:39`: keep the `InMemoryTerminologyService` construction (its constructors did not change). Add one sentence saying that it is a composing service over a single `PackageTerminologySource`.
- `operations.md` `$subsumes` section: add "If either code, or the code system, is not known to the server, the operation returns 400 with an OperationOutcome naming the reason. It never answers `not-subsumed` for unknown content."

- [ ] **Step 3: Sync the spec to the decisions this phase made**

Edit the spec in place:
1. **§3.1.** Phase 1 adds `LookupAsync(CodeRef, CancellationToken)` without `LookupOptions`; options arrive with the Phase 3 `property`/`displayLanguage` parameters. `ContainsAsync` is dropped, because a three-state `LookupAsync` answers containment. CodeSystem validate-code uses lookup.
2. **§3.2.** Add `CodeNotInSystem` to `UnknownKind`; it is used when a source holds the system but not the code, as in subsumes with an unknown code. In the target-shape table, the lookup row now reads `LookupAsync(LookupRequest)` → `ConceptLookup` (`Found(ConceptDetails)` / `NotFound` / `Undetermined(UnknownReason)`).
3. **§4.2.** CodeSystem `$validate-code` is now "route(system) → `LookupAsync`; display compared from the same lookup."
4. **§8.** The SQL source keeps the class name `SqlServerTerminologyService` and implements `ITerminologySource`; no separate `SqlTerminologySource` adapter exists.

- [ ] **Step 4: Verify and commit**

Run: `git diff --check`
Expected: no output.

```powershell
git add docs src/Core/Ignixa.Validation/README.md
git commit -m "Document composing terminology service and Phase 1 contract changes" -m "Co-authored-by: Copilot App <223556219+Copilot@users.noreply.github.com>" -m "Copilot-Session: 256080b3-ea2b-4623-b9d7-30c08c033041"
```

---

## Phase 2 — ValueSet `$validate-code`, evaluated by targeted composition

Depends on Phase 1. Refine into code first (see Plan Fidelity).

### Task 9: ValueSet definition types and source members

**Files:**
- Create in `Abstractions/Terminology/`: `ComposeInclude.cs`, `ComposeFilter.cs`, `ValueSetCompose.cs`, `ValueSetDefinition.cs`, `ValidateCodeInValueSetRequest.cs`
- Modify: `ITerminologySource.cs`, `PackageTerminologySource.cs`, `SqlServerTerminologyService.cs`
- Test: `test/Ignixa.Validation.Tests/Terminology/ComposeIncludeTests.cs`, `PackageTerminologySourceTests.cs`; `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/SqlTerminologySourceTests.cs`

**Interfaces (produced):**
- `ComposeFilter(string Property, string Op, string Value)`.
- `ComposeInclude(string? System, string? Version, IReadOnlyList<string> Concepts, IReadOnlyList<ComposeFilter> Filters, IReadOnlyList<CanonicalRef> ValueSets)`. It throws when it has neither a system nor a ValueSet, and when concepts or filters are given without a system. These are the R4 constraints vsd-1 and vsd-2.
- `ValueSetCompose(IReadOnlyList<ComposeInclude> Include, IReadOnlyList<ComposeInclude> Exclude, bool? Inactive)`.
- `abstract record ValueSetDefinition`, with nested `Composed(CanonicalRef Canonical, ValueSetCompose Compose)` and `Enumerated(CanonicalRef Canonical, IReadOnlyList<CodeRef> Codes)`.
- `ValidateCodeInValueSetRequest(CanonicalRef ValueSet, IReadOnlyList<ValidateCodeRequest> Codings)`. One coding is a Coding; several form a CodeableConcept.
- `ITerminologySource` gains:
  - `Task<MembershipResult> MatchesIncludeAsync(CodeRef code, ComposeInclude include, CancellationToken)`, which is only called with an include whose `System` equals `code.System`;
  - `Task<ValueSetDefinition?> ResolveValueSetAsync(CanonicalRef canonical, CancellationToken)`.

**Behaviour:**
- **Package source.**
  - `ResolveValueSetAsync` returns `Enumerated` from `IValueSetProvider.GetCodes` when `IsKnownValueSet`, else null.
  - `MatchesIncludeAsync`, explicit concepts: membership by list.
  - `MatchesIncludeAsync`, filters: `Undetermined(UnsupportedFilter)`.
  - `MatchesIncludeAsync`, whole-system include (no concepts, no filters): delegate to `LookupAsync`.
- **SQL source.**
  - `ResolveValueSetAsync` reads the stored compose of a Completed ValueSet, else null.
  - `MatchesIncludeAsync` tests one code without materialising the include:
    - concept list: an exact match;
    - `is-a` / `descendent-of`: an ancestry walk over `ParentConceptId` from the code upwards;
    - `=` on a property: a property row lookup;
    - any other operator: `Undetermined(UnsupportedFilter)`;
    - code absent from the system: `NotMember`.
  - Reuse `SqlServerValueSetComposer`'s filter SQL where it is per-code. Otherwise add a new parameterized query over the existing `Term*` tables. Do not change the schema.

**Tests (named):**
- `GivenIncludeWithoutSystemOrValueSet_WhenConstructed_ThenThrows`
- `GivenKnownValueSet_WhenResolvedByPackage_ThenEnumeratedDefinition`
- `GivenFilteredInclude_WhenPackageMatches_ThenUnsupportedFilter`
- SQL: `GivenIsAFilter_WhenDescendantMatched_ThenMember`, `GivenIsAFilter_WhenUnrelatedCodeMatched_ThenNotMember`, `GivenRegexFilter_WhenMatched_ThenUndeterminedUnsupportedFilter`, `GivenImportedValueSet_WhenResolved_ThenComposedDefinitionRoundTrips`

**Commit:** `Add ValueSet definitions and targeted include matching to terminology sources`.

### Task 10: Composer `ValidateCodeInValueSetAsync`; legacy ValueSet validate removed

**Files:**
- Create: `src/Core/Ignixa.Validation/Services/Terminology/ValueSetMembershipEvaluator.cs` (internal)
- Create: `src/Core/Ignixa.Validation/Abstractions/Terminology/TerminologyBudget.cs`
- Modify: `ComposingTerminologyService.cs`, `ITerminologyService.cs`, `ILegacyValueSetOperations.cs` (remove `ValidateCodeAsync`), `InMemoryTerminologyService.cs`, both sources' legacy implementations
- Modify: callers of `ITerminologyService.ValidateCodeAsync` (find them with `rg -n "\.ValidateCodeAsync\(" src test`)
- Test: `test/Ignixa.Validation.Tests/Terminology/ValueSetMembershipEvaluatorTests.cs`, `ComposingTerminologyServiceTests.cs`; `test/Ignixa.DataLayer.SqlServer.IntegrationTests/Features/Terminology/MixedSourceValueSetValidationTests.cs`

**Interfaces (produced):**
- `TerminologyBudget(int MaxImportDepth = 10, int MaxEnumeratedConcepts = 100_000)`.
- `ITerminologyService.ValidateCodeInValueSetAsync(ValidateCodeInValueSetRequest, CancellationToken)` → `ValidateCodeResult`. It replaces `ValidateCodeAsync(system, code, display, valueSetUrl)`.

**Behaviour (spec §4.2 and §4.3):**
- Resolve the ValueSet through `ResolveValueSetAsync` on the routed source. If no source resolves it, the answer is `Unknown(ValueSetNotFound)`.
- `Enumerated`: direct membership.
- `Composed`, includes:
  - ValueSet imports: recurse, keeping a visited set keyed by `CanonicalRef`. A revisit gives `Unknown(ValueSetNotFound, detail "import cycle")`. Exceeding `MaxImportDepth` gives `Unknown(BudgetExceeded)`.
  - System includes: call `MatchesIncludeAsync` on `route(include.System)`.
  - An include that names both a system and ValueSets is the intersection of the two.
  - The include result is True if any include is True. Otherwise it is Unknown if any include is Unknown. Otherwise it is False.
- Excludes, evaluated only when the include result is True:
  - any exclude True → `False`;
  - otherwise any exclude Unknown → `Unknown`;
  - otherwise → `True`.
- CodeableConcept: the result is True if any coding is True. Otherwise it is Unknown if any coding is Unknown. Otherwise it is False. Issues are reported per coding.
- Display check: only for a True coding with a supplied display, reusing Task 7's `Valid` logic. Move it into `ValueSetMembershipEvaluator` as a shared internal static.
- **`ValidateBindingAsync`** is now composed: `ValidateCodeInValueSetAsync`, then strength mapping. Its output is preserved exactly:
  - True gives today's valid result plus the display suggestion;
  - False gives today's per-strength severity and message;
  - Unknown gives today's "unable to verify" Warning text.

  Before writing the mapping, copy the existing message strings from `PackageTerminologySource.ValidateBindingAsync` and `SqlServerTerminologyService.ValidateBindingAsync`. The binding guard tests decide which text is correct.
- Remove `ValidateBindingAsync` from `ILegacyValueSetOperations` as well. Keep the empty-URL Error guard in the composer, and keep InMemory's Warning-and-skip guard (Task 7, Step 5 comment).

**Tests (named):**
- **`GivenValueSetIncludingSqlSystemAndPackageSystem_WhenValidatingPackageCode_ThenTrue`.** This is the **mixed-source regression**. A ValueSet stored in SQL includes an imported system plus one only the package knows. A package-system code was rejected at the base commit, because whole-call routing sent it to SQL. Verify it fails at the base in a temporary worktree.
- `GivenImportCycle_WhenValidating_ThenUnknownNotStackOverflow`
- `GivenCodeInIncludeAndExclude_WhenValidating_ThenFalse`
- `GivenExcludeUndetermined_WhenIncludeTrue_ThenUnknown`
- `GivenCodeableConceptWithOneUnknownOneFalse_WhenValidating_ThenUnknown`
- `GivenImportDeeperThanBudget_WhenValidating_ThenBudgetExceeded`
- All existing `BindingCheckTests` / binding tests are unchanged and green (guards).

Mutation-check the exclude precedence: swap the True/Unknown exclude order, see `GivenExcludeUndetermined…` fail, then revert. Measure `MatchesIncludeAsync` `is-a` on the SNOMED fixture if one is available (spec §11), and record the timing in the PR.

**Commit:** `Compose ValueSet validate-code from targeted include matches`, with a release-note line on the signature change.

---

## Phase 3 — Public `$lookup` and `$validate-code`; FHIR Parameters in and out

Depends on Phase 2.

### Task 11: `TerminologyParametersReader` and `TerminologyParametersWriter`

**Files:**
- Create: `src/Application/Ignixa.Api/Endpoints/Experimental/Terminology/TerminologyParametersReader.cs`, `TerminologyParametersWriter.cs`
- Create: `src/Core/Ignixa.Validation/Abstractions/Terminology/LookupOptions.cs`
- Test: `test/Ignixa.Api.Tests/Endpoints/Terminology/TerminologyParametersReaderTests.cs`, `TerminologyParametersWriterTests.cs`

**Behaviour:**
- **Reader.** One `TerminologyParameters` view over either an `IQueryCollection` or a POST `Parameters` `JsonNode`.
  - Each FHIR primitive is read with its declared type; `coding` and `codeableConcept` are read as complex values.
  - Errors throw `BadRequestException`, naming the parameter: a repeated scalar, a wrong type, a body that is not `Parameters`, or a `parameter` that is not an array.
  - It produces `LookupRequest`, `ValidateCodeRequest`, `ValidateCodeInValueSetRequest`, and `SubsumesRequest`.
  - `coding` and `system`+`code` are mutually exclusive; supplying both is a 400.
- **`LookupOptions`.**
  - Shape: `LookupOptions(IReadOnlyList<string> Properties, string? DisplayLanguage)`.
  - Added to `LookupRequest`, defaulting to empty. The source `LookupAsync` signature does not change; filtering happens in the composer.
  - Update the spec §3.1 note from Task 8 to say that options landed here.
- **Writer.** R4 `Parameters` output:
  - `$lookup` → `name`, `version`, `display`, `designation` parts, `property` parts;
  - `$validate-code` → `result`, `message`, `display`.
  - For `$validate-code` with `Unknown`, `result=false` and `message` joins the reasons' `Detail`.
  - For `False`, `message` comes from the issues.

**Tests (named):**
- `GivenGetAndPostWithSameInputs_WhenRead_ThenRequestsAreEqual`
- `GivenCodingAndSystemTogether_WhenRead_ThenBadRequest`
- `GivenRepeatedCode_WhenRead_ThenBadRequest`
- `GivenNonParametersBody_WhenRead_ThenBadRequest`
- `GivenUnknownValidateCode_WhenWritten_ThenResultFalseWithReasonMessage`

**Commit:** `Add terminology Parameters reader and writer`.

### Task 12: `$lookup` and `$validate-code` endpoints; JSON DTOs removed

**Files:**
- Modify: `src/Application/Ignixa.Api/Endpoints/Experimental/TerminologyEndpoints.cs`
- Create (Application): `Features/Experimental/Terminology/Lookup/{LookupQuery,LookupHandler}.cs`, `ValidateCode/{ValidateCodeQuery,ValidateCodeHandler}.cs`
- Delete: `TranslateRequestDto`, `SubsumesRequestDto`. Port `$translate` and `$subsumes` POST to the reader, and add the GET forms of `$subsumes` and `$translate`.
- Modify: `docs/site/docs/server/fhir/operations.md` (new sections, plus a breaking-change note on the DTO removal)
- Test: `test/Ignixa.Api.Tests/Endpoints/TerminologyEndpointsTests.cs`, extending the existing `TerminologyEndpointErrorSerializationTests`

**Routes (spec §5):**
- `CodeSystem/$lookup` and `CodeSystem/{id}/$lookup`, GET and POST.
- `CodeSystem/$validate-code` and `CodeSystem/{id}/$validate-code`, and the same for `ValueSet/…`.
- `$subsumes` GET plus `codingA`/`codingB`.
- `$translate` GET.

Each route has the unqualified form and the `/tenant/{tenantId}` form, registered exactly like the existing three. Instance routes resolve `{id}` to the canonical URL through the resource store. An unknown id gives 404.

**Status mapping:**
- 400: malformed input.
- 404: `$lookup` with an `Unknown(SystemNotAvailable)`, or a `ValueSetNotFound` on an instance route.
- 200: `$validate-code` Unknown, with `result=false`.

**Tests (named; each `[Theory]` runs tenant-agnostic and `/tenant/1`):**
- `GivenKnownCode_WhenLookupGet_ThenDisplayReturned`
- `GivenUnknownSystem_WhenLookup_ThenNotFoundOutcome`
- `GivenCodeOutsideValueSet_WhenValidateCode_ThenResultFalse`
- `GivenInstanceRoute_WhenValidateCode_ThenUrlBoundFromResource`
- `GivenLegacyJsonDtoBody_WhenTranslatePost_ThenBadRequest`
- `GivenTenantZero_WhenAnyTerminologyRoute_ThenNotExposed`

**Commit:** `Add $lookup and $validate-code endpoints; read FHIR Parameters for all terminology operations`.

---

## Phase 4 — `$expand` that is complete or fails

Depends on Phase 1. Independent of Phases 2–3, except that it reuses Task 9's definition types when they have landed. If Phase 4 runs first, Task 13 creates them.

### Task 13: `SelectAsync` and `TryComposeWholeAsync` on sources

**Interfaces (produced):**
- `SelectOptions(TerminologyBudget Budget, bool IncludeDesignations, bool ActiveOnly)`.
- `ConceptSet(IReadOnlyList<ExpandedConcept> Concepts, IReadOnlyList<UnknownReason> Unknowns)`.
- `ITerminologySource.SelectAsync(ComposeInclude, SelectOptions, CancellationToken)` → `ConceptSet`.
- `ITerminologySource.TryComposeWholeAsync(ValueSetDefinition, SelectOptions, CancellationToken)` → `ConceptSet?`. Null means the source cannot do it.

**Behaviour:**
- **SQL.**
  - `SelectAsync` enumerates one include through the composer's per-include SQL.
  - `TryComposeWholeAsync` calls `SqlServerValueSetComposer`. Its `_externalSystems`, `_missingValueSets`, `_unknownSystemVersions`, and `_unsupportedFilters` become `UnknownReason`s.
- **Package.**
  - `SelectAsync` supports only explicit concepts or an `Enumerated` ValueSet; any filter gives `UnsupportedFilter`.
  - `TryComposeWholeAsync` returns null.

**Tests (named):**
- SQL: `GivenComposerTrackedExternalSystem_WhenComposedWhole_ThenSystemNotAvailableReason`, `GivenUnsupportedFilter_WhenComposedWhole_ThenUnsupportedFilterReason`
- Package: `GivenFilterInclude_WhenSelected_ThenUnsupportedFilter`

**Commit:** `Add include selection and whole-ValueSet pushdown to terminology sources`.

### Task 14: Composer `ExpandAsync`; `Incomplete` removed; 422 on undeterminable expansion

**Interfaces (produced):**
- `ExpandRequest(CanonicalRef? ValueSet, ValueSetDefinition? Inline, string? Filter, int? Count, int? Offset, bool IncludeDesignations)`. Exactly one of `ValueSet` and `Inline` must be set.
- `abstract record ExpandOutcome`, with nested `Expanded(ExpandResult)` and `NotExpandable(IReadOnlyList<UnknownReason>)`.
- `ITerminologyService.ExpandAsync(ExpandRequest, CancellationToken)` replaces `ExpandValueSetAsync`.
- `ExpandResult.Incomplete` is deleted.

**Behaviour:**
- Pushdown happens when one source is `Authoritative` for every include system and every import. Otherwise selection is per include, recursing through imports with Task 10's cycle and budget rules, and the composer applies union, intersect, and exclude.
- `filter`, `count`, and `offset` are applied after composition. `total` is exact.
- Any reason gives `NotExpandable`.
- Exceeding `MaxEnumeratedConcepts` gives `BudgetExceeded`.

**API:**
- `NotExpandable` → 422 with OperationOutcome issues:
  - `not-found` for `SystemNotAvailable` / `ValueSetNotFound`;
  - `not-supported` for `UnsupportedFilter` / `NonEnumerableSystem`;
  - `too-costly` for `BudgetExceeded`.
- POST accepts an inline `valueSet` and `valueSetVersion`.
- Add the instance routes.
- Remove the `incomplete` projection from `ExpandValueSetHandler`, and delete `ExpandValueSetHandlerTests`' `Incomplete` cases. They pinned the behaviour being removed.
- Remove `ExpandValueSetAsync` from `ILegacyValueSetOperations`.
- Update the "Package-backed terminology imports" prose in `operations.md` that describes incomplete results (~lines 465–487).

**Tests (named):**
- **`GivenValueSetIncludingUnresolvedSystem_WhenExpanded_Then422NotPartialContents`.** Regression: at the base commit the response was 200 with partial contents. Verify it at the base.
- `GivenNestedImports_WhenExpanded_ThenUnionMinusExcludes`
- `GivenCountAndOffset_WhenExpanded_ThenTotalIsExactAndPageIsSliced`
- `GivenInlineValueSet_WhenExpandPost_ThenExpanded`
- `GivenExpansionOverBudget_WhenExpanded_ThenTooCostly`
- `GivenImportCycle_WhenExpanded_Then422Invalid`

**Commit:** `Expand ValueSets completely or fail with 422; remove Incomplete`.

After this phase merges, use the accept-adr workflow to move ADR-2610 to Accepted (spec §12).

---

## Phase 5 — `$translate` honours every scoping parameter

Depends on Phase 1.

### Task 15: `FindConceptMapsAsync`, mapping evaluation, and removal of the legacy interface

**Interfaces (produced):**
- `TranslateRequest(CodeRef? Code, IReadOnlyList<CodeRef> Codings, CanonicalRef? ConceptMap, CanonicalRef? SourceScope, CanonicalRef? TargetScope, string? TargetSystem, bool Reverse)`.
- `ConceptMapRelationship { RelatedTo, Equivalent, SourceIsNarrowerThanTarget, SourceIsBroaderThanTarget, NotRelatedTo }`, mapped from the R4 `equivalence` codes at the SQL boundary.
- `TranslateMatch(CodeRef Concept, ConceptMapRelationship Relationship, CanonicalRef Source)`.
- `TranslateResult(IReadOnlyList<TranslateMatch> Matches, IReadOnlyList<UnknownReason> Unknowns)`.
- `ITerminologySource.FindConceptMapsAsync(TranslateScope, CancellationToken)` → `IReadOnlyList<ConceptMapCandidate>`.
- `ITerminologyService.TranslateAsync(TranslateRequest, CancellationToken)`.
- `ILegacyValueSetOperations` and `SourceRouter.SelectLegacyAsync`/`FirstLegacy` are **deleted**.
- `ConceptMapResolverService` is ported to `TranslateAsync`.

**Behaviour:**
- Candidates are filtered by:
  - `url` + `conceptMapVersion`;
  - `source`/`target` ValueSet scope;
  - `system` + `version`, matched against the group's source and sourceVersion;
  - `targetsystem`;
  - `reverse`, which matches the target side.
- `dependsOn` is matched only by exact equality; anything else gives `UnsupportedFilter`.
- FHIR `result` is true iff some match has a relationship other than `NotRelatedTo`.

**Tests (named; each regression fails at the base commit):**
- `GivenTwoMapsDifferingBySource_WhenTranslatedWithSource_ThenOnlyScopedMapUsed`
- `GivenTargetScope_WhenTranslated_ThenOtherTargetsExcluded`
- `GivenSystemVersion_WhenTranslated_ThenOtherVersionGroupIgnored`
- `GivenReverse_WhenTranslated_ThenSourceReturned`
- `GivenNoMatch_WhenTranslated_ThenResultFalse`
- `GivenUnsupportedDependsOn_WhenTranslated_ThenUnknownNotMatch`
- `ConceptMapResolverServiceTests` are ported and green.

**Commit:** `Translate with full ConceptMap scoping; remove legacy terminology operations`.

---

## Phase 6 — `$find-matches` (exact)

Depends on Phases 3 and 4.

### Task 16: Exact-mode `$find-matches`

**Behaviour:**
- `CodeSystem/$find-matches` (GET/POST) with `system`, `version`, `property` (code + value parts), and `exact=true`.
- It is implemented as `SelectAsync` with a synthetic `ComposeInclude(system, version, [], [ComposeFilter(property, "=", value)…], [])` and returns `match` parts with `code`.
- `exact` absent or false → 422 `not-supported`.

**Tests (named):**
- `GivenPropertyEquality_WhenFindMatchesExact_ThenMatchingCodesReturned`
- `GivenExactFalse_WhenFindMatches_Then422NotSupported`

**Commit:** `Add exact-mode CodeSystem $find-matches`.

---

## Phase 7 — R5 wire mapping

Depends on Phases 3–5.

### Task 17: R5 aliases and outputs in the reader and writer

**Behaviour:**
- The reader, when the request's FHIR version is R5, accepts R5 input names:
  - `$translate`: `sourceCode`, `sourceCoding`, `sourceCodeableConcept`, `targetCode`, `targetCoding`, `targetCodeableConcept`, `system`, `targetSystem`;
  - `$validate-code`: `lenient-display-validation`.
- The writer, for R5:
  - emits `match.relationship` instead of `equivalence`;
  - adds an `issues` OperationOutcome part to `$validate-code`;
  - when `lenient-display-validation=false`, raises the display mismatch to error and sets `result=false`.
- The composer does not change. Check that with `git diff --stat src/Core` (expect no changes).

**Tests (named; R5 round-trip per operation):**
- `GivenR5Request_WhenTranslate_ThenRelationshipEmitted`
- `GivenR5StrictDisplay_WhenDisplayMismatch_ThenResultFalseWithIssue`
- `GivenR4Request_WhenTranslate_ThenEquivalenceEmitted` (guard)

**Commit:** `Map terminology operations to the R5 wire format`.

---

## Self-Review Record

- **Spec coverage.** Each section maps to tasks:
  - §3.1 source contract: Tasks 3, 4, 9, 13, 15.
  - §3.2 outcomes: Tasks 2, 4, 7, 10, 14, 15.
  - §4.1 routing: Task 3.
  - §4.2 decompositions: Tasks 7 (lookup, subsumes, CodeSystem validate), 10 (ValueSet validate, Coding/CodeableConcept), 14 (expand), 15 (translate), 16 (find-matches).
  - §4.3 budgets and cycles: Tasks 10 and 14.
  - §4.4 failure semantics: Tasks 3 (exceptions propagate) and 14 (OperationOutcome issue codes).
  - §5 API: Tasks 11, 12, 14, 16.
  - §6 R5: Task 17.
  - §7 package: Tasks 5, 9, 13.
  - §8 SQL: Tasks 6, 9, 13, 15.
  - §11 risks: the binding guards (Tasks 7 and 10) and the SNOMED measurement (Task 10).
  - §12 docs: Tasks 8, 12, 14.
- **Deliberate deviations,** recorded in the spec by Task 8: `CodeNotInSystem`; `ConceptLookup` naming; `LookupOptions` moved to Phase 3; `ContainsAsync` dropped; the SQL class name kept.
- **Type names.** Names used across tasks:
  - `CodeRef`, `CanonicalRef`, `ConceptLookup`, `SubsumesOutcome.Related`/`.Undetermined`;
  - `ValidateCodeResult`, `RoutedAnswer<T>`, `SelectLegacyAsync`/`FirstLegacy`;
  - `ComposeInclude`, `ValueSetDefinition.Composed`/`.Enumerated`;
  - `ExpandOutcome.Expanded`/`.NotExpandable`, `TranslateMatch`.
