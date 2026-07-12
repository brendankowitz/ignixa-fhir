# TestScript Assertion Alternatives Evaluator Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `TestScriptEvaluator` actually execute the `assertionAnyOfGroup` and `assertionWhenResponseStatus` extensions that PR #330 parses but never acts on, closing out issue #324's remaining checklist items.

**Architecture:** A shared `EvaluateAssertionMember` primitive layers conditional applicability on top of the existing per-assertion evaluation. Standalone assertions record individually as today (now applicability-aware, mapping "condition didn't match" to a `Skip` outcome). Assertions sharing an `AnyOfGroupId` within one test are intercepted in `ExecuteActionsAsync`, evaluated via the same primitive, and aggregated into one reported result with per-member diagnostics, instead of each recording independently.

**Tech Stack:** .NET (net9.0 + net10.0 multi-target), xUnit (`[Fact]`), Shouldly (`ShouldBe`/`ShouldContain`/etc.), NSubstitute for test doubles.

**Design doc:** `docs/superpowers/specs/2026-07-11-testscript-assertion-alternatives-evaluator-design.md` — read it first if anything below is ambiguous; this plan implements it verbatim.

## Global Constraints

- Extension URLs are fixed values, do not invent alternatives: `http://ignixa.io/testscript/assertionAnyOfGroup`, `http://ignixa.io/testscript/assertionWhenResponseStatus` (both already parsed by PR #330, unchanged by this plan), and `http://ignixa.io/testscript/assertionGroupMember` (new — this plan's own FHIR `TestReport` rendering extension for group member diagnostics).
- Every `dotnet test test/Ignixa.TestScript.Tests` run must report results for **both** `net9.0` and `net10.0` target frameworks, and the existing 264 tests must stay green after every task — no behavior change for ungrouped, unconditional assertions.
- Test stack is xUnit `[Fact]` + Shouldly assertions (`ShouldBe`, `ShouldBeTrue`, `ShouldContain`, `ShouldNotBeNull`) + NSubstitute (`Substitute.For<T>()`, `.Returns(...)`) — match `test/Ignixa.TestScript.Tests/Evaluation/TestScriptEvaluatorTests.cs` and `test/Ignixa.TestScript.Tests/Reporting/TestScriptResultRecorderTests.cs` conventions exactly.
- New small value types (e.g. `AssertionGroupMemberResult`) go in their own file, one record per file — this repo's existing convention (see `src/Core/Ignixa.TestScript/Expressions/ResponseStatusCondition.cs`, `WaitForCondition.cs`).
- `ImplicitUsings` is enabled project-wide — do not add `using System.Linq;`, `using System;`, etc. explicitly; only add usings for namespaces that aren't implicit (e.g. `Ignixa.TestScript.Reporting`, `Ignixa.TestScript.Parsing`).
- **Known limitation, explicitly out of scope:** `TestScriptContext.ResponseHistory` is not scoped per-test (it threads across the whole run). A `WhenResponseStatus` condition referencing a different test's `responseId` will resolve instead of erroring, contrary to issue #324's "cross-test references are invalid" acceptance criterion. Do not attempt to fix this — it's a pre-existing property of `TestScriptContext` unrelated to this feature. Do not add per-test history scoping as a "nice to have."
- Do not touch `ignixa-lab`'s workaround components (`WarningOnlyStatusAlternativeEnforcer`, etc.) — different repository, separate follow-up, not part of this plan.

---

### Task 1: Report model — group aggregation data shapes

**Files:**
- Modify: `src/Core/Ignixa.TestScript/Reporting/AssertionOutcome.cs`
- Create: `src/Core/Ignixa.TestScript/Reporting/AssertionGroupMemberResult.cs`
- Modify: `src/Core/Ignixa.TestScript/Reporting/ActionResult.cs`
- Modify: `src/Core/Ignixa.TestScript/Reporting/ITestScriptResultRecorder.cs`
- Modify: `src/Core/Ignixa.TestScript/Reporting/TestScriptResultRecorder.cs`
- Test: `test/Ignixa.TestScript.Tests/Reporting/TestScriptResultRecorderTests.cs`

**Interfaces:**
- Produces: `AssertionOutcome(bool Passed, bool WarningOnly, string? Message = null, bool IsError = false, bool Applicable = true)` — one new field, `Applicable`, defaulting to `true` so every existing call site (in `TestScriptEvaluatorTests.cs`, `TestScriptResultRecorderTests.cs`, and `TestScriptEvaluator.cs` itself) keeps compiling unchanged.
- Produces: `AssertionGroupMemberResult(string? Description, bool Applicable, bool Passed, string? Message)`.
- Produces: `ActionResult` gains `string? GroupId = null` and `IReadOnlyList<AssertionGroupMemberResult>? Members = null` — both default to `null`, every existing `new ActionResult(...)` call site keeps compiling unchanged.
- Produces: `ITestScriptResultRecorder.RecordAssertionGroupResult(string groupId, string? label, string? description, AssertionOutcome outcome, IReadOnlyList<AssertionGroupMemberResult> members)`.
- Consumes: nothing from other tasks — this task is the foundation the others build on.

- [ ] **Step 1: Write the failing tests**

Append these to `test/Ignixa.TestScript.Tests/Reporting/TestScriptResultRecorderTests.cs`, inside the existing `TestScriptResultRecorderTests` class, just before the final closing `}`:

```csharp
    [Fact]
    public void GivenInapplicableAssertion_WhenRecording_ThenOutcomeIsSkip()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Test, "Test");
        recorder.RecordAssertionResult("a", "desc",
            new AssertionOutcome(false, WarningOnly: false, Applicable: false));
        recorder.EndPhase();

        var report = recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        report.TestResults[0].Actions[0].Outcome.ShouldBe(TestScriptOutcome.Skip);
    }

    [Fact]
    public void GivenPassingGroupResult_WhenRecording_ThenActionCarriesGroupIdAndMembers()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Test, "Test");

        var members = new List<AssertionGroupMemberResult>
        {
            new("Preferred: 410 Gone", true, false, "Expected response 'gone' but got status 404"),
            new("Alternative: 404 Not Found", true, true, null)
        };
        recorder.RecordAssertionGroupResult("deleted-resource-readback", "grp", "Deleted resource readback",
            new AssertionOutcome(true, WarningOnly: false), members);
        recorder.EndPhase();

        var report = recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        var action = report.TestResults[0].Actions[0];
        action.Outcome.ShouldBe(TestScriptOutcome.Pass);
        action.GroupId.ShouldBe("deleted-resource-readback");
        action.Members.ShouldNotBeNull();
        action.Members!.Count.ShouldBe(2);
        action.Members[1].Passed.ShouldBeTrue();
    }

    [Fact]
    public void GivenErroredGroupResult_WhenRecording_ThenOutcomeIsError()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Test, "Test");

        var members = new List<AssertionGroupMemberResult>
        {
            new("Member A", false, false, null),
            new("Member B", false, false, null)
        };
        recorder.RecordAssertionGroupResult("group-x", null, "Group X",
            new AssertionOutcome(false, WarningOnly: false, Message: "no member was applicable", IsError: true),
            members);
        recorder.EndPhase();

        var report = recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        report.TestResults[0].Actions[0].Outcome.ShouldBe(TestScriptOutcome.Error);
    }

    [Fact]
    public void GivenFailedGroupResult_WhenRecording_ThenOutcomeIsFail()
    {
        var recorder = new TestScriptResultRecorder();
        recorder.BeginPhase(TestPhaseType.Test, "Test");

        var members = new List<AssertionGroupMemberResult>
        {
            new("Member A", true, false, "expected X"),
            new("Member B", true, false, "expected Y")
        };
        recorder.RecordAssertionGroupResult("group-y", null, "Group Y",
            new AssertionOutcome(false, WarningOnly: false, Message: "no alternative matched"),
            members);
        recorder.EndPhase();

        var report = recorder.Build("name", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
        report.TestResults[0].Actions[0].Outcome.ShouldBe(TestScriptOutcome.Fail);
    }
```

- [ ] **Step 2: Run tests to verify they fail to compile**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~TestScriptResultRecorderTests"`
Expected: build errors — `AssertionGroupMemberResult` does not exist, `AssertionOutcome` has no `Applicable` parameter, `RecordAssertionGroupResult` does not exist, `ActionResult` has no `GroupId`/`Members`.

- [ ] **Step 3: Create `AssertionGroupMemberResult.cs`**

```csharp
namespace Ignixa.TestScript.Reporting;

/// <summary>
/// One member's outcome within an <c>assertionAnyOfGroup</c> aggregate. Surfaced as diagnostic detail
/// alongside the group's single top-level <see cref="ActionResult"/> — members are never reported as
/// independent top-level results.
/// </summary>
public sealed record AssertionGroupMemberResult(
    string? Description,
    bool Applicable,
    bool Passed,
    string? Message);
```

- [ ] **Step 4: Update `AssertionOutcome.cs`**

Replace the whole file with:

```csharp
namespace Ignixa.TestScript.Reporting;

public sealed record AssertionOutcome(
    bool Passed,
    bool WarningOnly,
    string? Message = null,
    bool IsError = false,
    bool Applicable = true);
```

- [ ] **Step 5: Update `ActionResult.cs`**

Replace the whole file with:

```csharp
namespace Ignixa.TestScript.Reporting;

public sealed record ActionResult(
    string? Label,
    string? Description,
    TestScriptOutcome Outcome,
    string? Message = null,
    TimeSpan Duration = default,
    TestActionKind Kind = TestActionKind.Assertion,
    HttpExchange? Exchange = null,
    string? GroupId = null,
    IReadOnlyList<AssertionGroupMemberResult>? Members = null);
```

- [ ] **Step 6: Update `ITestScriptResultRecorder.cs`**

Replace the whole file with:

```csharp
namespace Ignixa.TestScript.Reporting;

public interface ITestScriptResultRecorder
{
    TestScriptOutcome? SetupOutcome { get; }

    void RecordOperationResult(string? label, string? description, OperationOutcome outcome);
    void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome);
    void RecordAssertionGroupResult(
        string groupId,
        string? label,
        string? description,
        AssertionOutcome outcome,
        IReadOnlyList<AssertionGroupMemberResult> members);
    void BeginPhase(TestPhaseType phase, string? name = null, string? description = null);
    void EndPhase();
    void RecordSkippedTest(string name, string? description, string reason);
    TestScriptReport Build(string testScriptName, DateTimeOffset startTime, DateTimeOffset endTime);
}
```

- [ ] **Step 7: Update `TestScriptResultRecorder.cs`**

Replace the existing `RecordAssertionResult` method (currently lines 53-71) with:

```csharp
    public void RecordAssertionResult(string? label, string? description, AssertionOutcome outcome)
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("RecordAssertionResult called without an open phase. Call BeginPhase first.");

        _currentActions.Add(new ActionResult(label, description, DetermineOutcome(outcome), outcome.Message));
    }

    public void RecordAssertionGroupResult(
        string groupId,
        string? label,
        string? description,
        AssertionOutcome outcome,
        IReadOnlyList<AssertionGroupMemberResult> members)
    {
        if (_isBuilt)
            throw new InvalidOperationException("Cannot record results after Build() has been called.");
        if (!_inPhase)
            throw new InvalidOperationException("RecordAssertionGroupResult called without an open phase. Call BeginPhase first.");

        _currentActions.Add(new ActionResult(
            label, description, DetermineOutcome(outcome), outcome.Message,
            GroupId: groupId, Members: members));
    }

    private static TestScriptOutcome DetermineOutcome(AssertionOutcome outcome)
    {
        if (!outcome.Applicable)
            return TestScriptOutcome.Skip;
        if (outcome.IsError)
            return TestScriptOutcome.Error;
        if (outcome.Passed)
            return TestScriptOutcome.Pass;
        if (outcome.WarningOnly)
            return TestScriptOutcome.Warning;
        return TestScriptOutcome.Fail;
    }
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~TestScriptResultRecorderTests"`
Expected: all pass, including the 4 new tests, on both `net9.0` and `net10.0`.

- [ ] **Step 9: Run the full test project to confirm no regression**

Run: `dotnet test test/Ignixa.TestScript.Tests`
Expected: all 264 existing tests plus the 4 new ones pass (268 total × 2 target frameworks).

- [ ] **Step 10: Commit**

```bash
git add src/Core/Ignixa.TestScript/Reporting/AssertionOutcome.cs \
        src/Core/Ignixa.TestScript/Reporting/AssertionGroupMemberResult.cs \
        src/Core/Ignixa.TestScript/Reporting/ActionResult.cs \
        src/Core/Ignixa.TestScript/Reporting/ITestScriptResultRecorder.cs \
        src/Core/Ignixa.TestScript/Reporting/TestScriptResultRecorder.cs \
        test/Ignixa.TestScript.Tests/Reporting/TestScriptResultRecorderTests.cs
git commit -m "feat(testscript): add group-aggregate reporting shapes to the recorder"
```

---

### Task 2: Evaluator execution — conditional applicability and OR-group aggregation

**Files:**
- Modify: `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`
- Test: Create `test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesTests.cs`

**Interfaces:**
- Consumes (from Task 1): `AssertionOutcome(bool Passed, bool WarningOnly, string? Message = null, bool IsError = false, bool Applicable = true)`, `AssertionGroupMemberResult(string? Description, bool Applicable, bool Passed, string? Message)`, `ITestScriptResultRecorder.RecordAssertionGroupResult(string groupId, string? label, string? description, AssertionOutcome outcome, IReadOnlyList<AssertionGroupMemberResult> members)`.
- Consumes (already exists): `AssertExpression.AnyOfGroupId` (`string?`), `AssertExpression.WhenResponseStatus` (`ResponseStatusCondition?`), `ResponseStatusCondition(string SourceId, IReadOnlyList<int> Statuses)`, `TestScriptContext.ResponseHistory` (`ImmutableDictionary<string, TestResponse>`), `TestResponse.StatusCode` (`int`).
- Produces: nothing new for later tasks — Task 3 only needs Task 1's `ActionResult.Members` shape, not anything from this task.

- [ ] **Step 1: Write the failing tests**

Create `test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesTests.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Expressions;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Model;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class AssertionAlternativesTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public AssertionAlternativesTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    private static TestScriptDefinition SingleTestDefinition(string name, params ActionExpression[] actions) =>
        new()
        {
            Metadata = new TestScriptMetadata { Name = name },
            Tests = [new TestPhaseDefinition { Name = "t", Actions = actions }]
        };

    [Fact]
    public async Task GivenGroupWherePreferredMemberPasses_WhenExecuting_ThenAggregatePassesAndCarriesBothMembers()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 410 });

        var definition = SingleTestDefinition("GroupPreferred",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults[0].Actions.Count.ShouldBe(2);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Pass);
        groupAction.GroupId.ShouldBe("deleted-resource-readback");
        groupAction.Members!.Count.ShouldBe(2);
        groupAction.Members[0].Passed.ShouldBeTrue();
        groupAction.Members[1].Passed.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenGroupWhereOnlyFallbackMemberPasses_WhenExecuting_ThenAggregatePassesWithoutWarning()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 404 });

        var definition = SingleTestDefinition("GroupFallback",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Pass);
        groupAction.Members![0].Passed.ShouldBeFalse();
        groupAction.Members[1].Passed.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenGroupWhereNoMemberPasses_WhenExecuting_ThenAggregateFails()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 500 });

        var definition = SingleTestDefinition("GroupNoneMatch",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Preferred: 410 Gone"
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "deleted-resource-readback",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Fail);
        report.TestResults[0].Actions[1].Outcome.ShouldBe(TestScriptOutcome.Fail);
    }

    [Fact]
    public async Task GivenGroupWhereNoMemberIsApplicable_WhenExecuting_ThenAggregateErrors()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 200 });

        var definition = SingleTestDefinition("GroupNoneApplicable",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/deleted-id", ResponseId = "delete-response" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                AnyOfGroupId = "conditional-group",
                WarningOnly = true,
                Description = "Only applies if delete returned 202",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "conditional-group",
                WarningOnly = true,
                Description = "Only applies if delete returned 204",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [204])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Error);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        groupAction.Message.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenGroupMemberWithUnresolvableSourceId_WhenExecuting_ThenAggregateErrorsNamingMember()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new TestResponse { StatusCode = 404 });

        var definition = SingleTestDefinition("GroupBadSourceId",
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/deleted-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("gone"),
                AnyOfGroupId = "bad-group",
                WarningOnly = true,
                Description = "Broken conditional member",
                WhenResponseStatus = new ResponseStatusCondition("does-not-exist", [202])
            },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("notFound"),
                AnyOfGroupId = "bad-group",
                WarningOnly = true,
                Description = "Alternative: 404 Not Found"
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Error);
        var groupAction = report.TestResults[0].Actions[1];
        groupAction.Outcome.ShouldBe(TestScriptOutcome.Error);
        groupAction.Message.ShouldContain("Broken conditional member");
    }

    [Fact]
    public async Task GivenStandaloneConditionalAssertionWhoseConditionMatches_WhenExecuting_ThenEvaluatedNormally()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 202 : 200
            });

        var definition = SingleTestDefinition("StandaloneConditionMatches",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/async-id", ResponseId = "delete-response" },
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/async-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                WarningOnly = true,
                Description = "An asynchronous delete may still be readable immediately",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
        report.TestResults[0].Actions[2].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }

    [Fact]
    public async Task GivenStandaloneConditionalAssertionWhoseConditionDoesNotMatch_WhenExecuting_ThenRecordedAsSkip()
    {
        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 204 : 404
            });

        var definition = SingleTestDefinition("StandaloneConditionMismatch",
            new OperationExpression { Type = "delete", Resource = "Patient", Params = "/async-id", ResponseId = "delete-response" },
            new OperationExpression { Type = "read", Resource = "Patient", Params = "/async-id" },
            new AssertExpression
            {
                Criteria = new ResponseStatusCriteria("okay"),
                WarningOnly = true,
                Description = "An asynchronous delete may still be readable immediately",
                WhenResponseStatus = new ResponseStatusCondition("delete-response", [202])
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(definition, CancellationToken.None);

        report.TestResults[0].Actions[2].Outcome.ShouldBe(TestScriptOutcome.Skip);
        report.TestResults[0].Outcome.ShouldBe(TestScriptOutcome.Pass);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~AssertionAlternativesTests"`
Expected: compiles (Task 1 already landed the types this references), but assertions fail — today every assert in a group still records independently, so `Actions.Count` is 3 not 2, group members never match "matched", conditions are silently ignored (no `Skip`, no `Error` for bad sourceId).

- [ ] **Step 3: Add the shared evaluation primitive**

In `src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs`, add this new private method immediately before `EvaluateAssertionWithMessage` (currently at line 575):

```csharp
    private (bool Applicable, bool Passed, string? Message) EvaluateAssertionMember(
        AssertExpression assertion, TestScriptContext context)
    {
        if (assertion.WhenResponseStatus is { } condition)
        {
            if (!context.ResponseHistory.TryGetValue(condition.SourceId, out var response))
                throw new InvalidOperationException(
                    $"assertionWhenResponseStatus sourceId '{condition.SourceId}' refers to no known response");

            if (!condition.Statuses.Contains(response.StatusCode))
                return (false, false, null);
        }

        var (passed, message) = EvaluateAssertionWithMessage(assertion, context);
        return (true, passed, message);
    }
```

- [ ] **Step 4: Update `VisitAssertAsync` for the standalone conditional path**

Replace the existing `VisitAssertAsync` method (currently lines 395-416) with:

```csharp
    public ValueTask<TestScriptContext> VisitAssertAsync(
        AssertExpression expression,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            var (applicable, passed, message) = EvaluateAssertionMember(expression, context);
            context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                new AssertionOutcome(passed, expression.WarningOnly, applicable ? message : null, Applicable: applicable));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Recorder.RecordAssertionResult(expression.Label, expression.Description,
                new AssertionOutcome(false, expression.WarningOnly, ex.Message, IsError: true));
        }
        return ValueTask.FromResult(context);
    }
```

This method now only ever fires for assertions with **no** `AnyOfGroupId` — Step 5 intercepts grouped assertions one level up and they never reach the per-action visitor dispatch.

- [ ] **Step 5: Restructure `ExecuteActionsAsync` to intercept and aggregate groups**

Replace the existing `ExecuteActionsAsync` method (currently lines 302-316) with:

```csharp
    private async Task<TestScriptContext> ExecuteActionsAsync(
        IReadOnlyList<ActionExpression> actions,
        IReadOnlyList<VariableDefinition> variables,
        TestScriptContext context,
        CancellationToken cancellationToken)
    {
        var lastGroupIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < actions.Count; i++)
            if (actions[i] is AssertExpression { AnyOfGroupId: { } gid })
                lastGroupIndex[gid] = i;

        var pendingGroups = new Dictionary<string, List<(AssertExpression Assertion, bool Applicable, bool Passed, string? Message, bool IsError)>>(
            StringComparer.Ordinal);

        for (var i = 0; i < actions.Count; i++)
        {
            var action = actions[i];

            if (action is AssertExpression { AnyOfGroupId: { } groupId } assertion)
            {
                var member = EvaluateGroupMemberSafe(assertion, context);
                if (!pendingGroups.TryGetValue(groupId, out var members))
                    pendingGroups[groupId] = members = [];
                members.Add((assertion, member.Applicable, member.Passed, member.Message, member.IsError));

                if (i == lastGroupIndex[groupId])
                    RecordGroupResult(context, groupId, members);

                continue;
            }

            context = await action.AcceptAsync(this, context, cancellationToken);
            if (action is OperationExpression)
                context = VariableExtractor.ExtractFromResponse(variables, context, schemaProvider);
        }

        return context;
    }

    private (bool Applicable, bool Passed, string? Message, bool IsError) EvaluateGroupMemberSafe(
        AssertExpression assertion, TestScriptContext context)
    {
        try
        {
            var (applicable, passed, message) = EvaluateAssertionMember(assertion, context);
            return (applicable, passed, message, false);
        }
        catch (Exception ex)
        {
            return (false, false, ex.Message, true);
        }
    }

    private static void RecordGroupResult(
        TestScriptContext context,
        string groupId,
        List<(AssertExpression Assertion, bool Applicable, bool Passed, string? Message, bool IsError)> members)
    {
        var reportMembers = members
            .Select(m => new AssertionGroupMemberResult(m.Assertion.Description, m.Applicable, m.Passed, m.Message))
            .ToList();

        var first = members[0].Assertion;
        var errored = members.FirstOrDefault(m => m.IsError);
        var applicableMembers = members.Where(m => m.Applicable).ToList();
        var matched = applicableMembers.FirstOrDefault(m => m.Passed);

        AssertionOutcome outcome;
        if (errored.IsError)
        {
            outcome = new AssertionOutcome(false, WarningOnly: false,
                Message: $"assertionAnyOfGroup '{groupId}': member '{errored.Assertion.Description}' failed to evaluate: {errored.Message}",
                IsError: true);
        }
        else if (applicableMembers.Count == 0)
        {
            outcome = new AssertionOutcome(false, WarningOnly: false,
                Message: $"assertionAnyOfGroup '{groupId}': no member was applicable — condition(s) never matched",
                IsError: true);
        }
        else if (matched.Passed)
        {
            outcome = new AssertionOutcome(true, WarningOnly: false,
                Message: $"assertionAnyOfGroup '{groupId}': matched alternative '{matched.Assertion.Description}'");
        }
        else
        {
            var summary = string.Join("; ", applicableMembers.Select(m => $"{m.Assertion.Description}: {m.Message}"));
            outcome = new AssertionOutcome(false, WarningOnly: false,
                Message: $"assertionAnyOfGroup '{groupId}': no alternative matched ({summary})");
        }

        var label = matched.Passed ? matched.Assertion.Label : first.Label;
        var description = matched.Passed ? matched.Assertion.Description : first.Description;

        context.Recorder.RecordAssertionGroupResult(groupId, label, description, outcome, reportMembers);
    }
```

`EvaluateGroupMemberSafe` and `RecordGroupResult` have no async work and no `CancellationToken` parameter — assertion evaluation here is synchronous (no I/O), matching `EvaluateAssertionWithMessage`'s existing signature. Do not add a `CancellationToken` parameter to either; that would be scope the design doesn't call for.

- [ ] **Step 6: Run the new tests**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~AssertionAlternativesTests"`
Expected: all 7 tests pass on both `net9.0` and `net10.0`.

- [ ] **Step 7: Run the full test project to confirm no regression**

Run: `dotnet test test/Ignixa.TestScript.Tests`
Expected: all 268 previously-passing tests (264 baseline + 4 from Task 1) plus the 7 new ones pass — 275 total × 2 target frameworks. This is the critical regression check: every existing test that has zero grouped/conditional assertions must produce byte-identical results to before this task.

- [ ] **Step 8: Commit**

```bash
git add src/Core/Ignixa.TestScript/Evaluation/TestScriptEvaluator.cs \
        test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesTests.cs
git commit -m "feat(testscript): execute assertionAnyOfGroup and assertionWhenResponseStatus in the evaluator"
```

---

### Task 3: FHIR `TestReport` rendering of group members

**Files:**
- Modify: `src/Core/Ignixa.TestScript/Reporting/TestReportResourceGenerator.cs`
- Test: `test/Ignixa.TestScript.Tests/Reporting/TestReportResourceGeneratorTests.cs`

**Interfaces:**
- Consumes (from Task 1): `ActionResult.GroupId` (`string?`), `ActionResult.Members` (`IReadOnlyList<AssertionGroupMemberResult>?`), `AssertionGroupMemberResult(string? Description, bool Applicable, bool Passed, string? Message)`.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Write the failing tests**

Append these to `test/Ignixa.TestScript.Tests/Reporting/TestReportResourceGeneratorTests.cs`, inside the existing `TestReportResourceGeneratorTests` class, just before the final closing `}`:

```csharp
    [Fact]
    public void GivenGroupActionWithMembers_WhenGenerating_ThenMembersRenderAsChildExtensions()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "GroupReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("DeletedResourceReadback", null, [
                    new ActionResult("grp", "Deleted resource readback", TestScriptOutcome.Pass,
                        "assertionAnyOfGroup 'grp': matched alternative 'Alternative: 404 Not Found'",
                        GroupId: "grp",
                        Members:
                        [
                            new AssertionGroupMemberResult("Preferred: 410 Gone", true, false, "Expected response 'gone' but got status 404"),
                            new AssertionGroupMemberResult("Alternative: 404 Not Found", true, true, null)
                        ])
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var action = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!;
        action["result"]!.GetValue<string>().ShouldBe("pass");
        var extensions = action["extension"]!.AsArray();
        extensions.Count.ShouldBe(2);
        extensions[0]!["url"]!.GetValue<string>().ShouldBe("http://ignixa.io/testscript/assertionGroupMember");
        var firstChildren = extensions[0]!["extension"]!.AsArray();
        firstChildren.Any(c => c!["url"]!.GetValue<string>() == "passed" && c["valueBoolean"]!.GetValue<bool>() == false)
            .ShouldBeTrue();
        extensions[1]!["extension"]!.AsArray()
            .Any(c => c!["url"]!.GetValue<string>() == "passed" && c["valueBoolean"]!.GetValue<bool>() == true)
            .ShouldBeTrue();
    }

    [Fact]
    public void GivenActionWithoutMembers_WhenGenerating_ThenNoExtensionEmitted()
    {
        var report = new TestScriptReport
        {
            TestScriptName = "PlainReport",
            StartTime = DateTimeOffset.UtcNow,
            EndTime = DateTimeOffset.UtcNow,
            TestResults =
            [
                new TestCaseResult("Plain", null, [
                    new ActionResult("a", null, TestScriptOutcome.Pass)
                ], TestScriptOutcome.Pass)
            ]
        };

        var json = TestReportResourceGenerator.Generate(report);

        var action = json["test"]!.AsArray()[0]!["action"]!.AsArray()[0]!;
        action.AsObject().ContainsKey("extension").ShouldBeFalse();
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~TestReportResourceGeneratorTests"`
Expected: the first new test fails (`action["extension"]` is null — nothing renders members today); the second passes already (no regression risk there, but keep it since it locks in the "no `Members`, no `extension` key" behavior going forward).

- [ ] **Step 3: Update `TestReportResourceGenerator.cs`**

Replace the existing `GenerateAction` method (currently lines 59-69) with:

```csharp
    private const string AssertionGroupMemberUrl = "http://ignixa.io/testscript/assertionGroupMember";

    private static JsonObject GenerateAction(ActionResult action)
    {
        var obj = new JsonObject
        {
            ["result"] = MapActionResult(action.Outcome)
        };
        if (action.Label is not null) obj["id"] = action.Label;
        if (action.Message is not null) obj["message"] = action.Message;
        if (action.Description is not null) obj["detail"] = action.Description;

        if (action.Members is { Count: > 0 })
            obj["extension"] = GenerateGroupMemberExtensions(action.Members);

        return obj;
    }

    private static JsonArray GenerateGroupMemberExtensions(IReadOnlyList<AssertionGroupMemberResult> members)
    {
        var array = new JsonArray();
        foreach (var member in members)
        {
            var children = new JsonArray
            {
                new JsonObject { ["url"] = "applicable", ["valueBoolean"] = member.Applicable },
                new JsonObject { ["url"] = "passed", ["valueBoolean"] = member.Passed }
            };
            if (member.Description is not null)
                children.Add(new JsonObject { ["url"] = "description", ["valueString"] = member.Description });
            if (member.Message is not null)
                children.Add(new JsonObject { ["url"] = "message", ["valueString"] = member.Message });

            array.Add(new JsonObject
            {
                ["url"] = AssertionGroupMemberUrl,
                ["extension"] = children
            });
        }
        return array;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~TestReportResourceGeneratorTests"`
Expected: both new tests pass, plus every existing `TestReportResourceGeneratorTests` test still passes.

- [ ] **Step 5: Run the full test project to confirm no regression**

Run: `dotnet test test/Ignixa.TestScript.Tests`
Expected: 277 total tests pass × 2 target frameworks (275 from Task 2 + 2 new).

- [ ] **Step 6: Commit**

```bash
git add src/Core/Ignixa.TestScript/Reporting/TestReportResourceGenerator.cs \
        test/Ignixa.TestScript.Tests/Reporting/TestReportResourceGeneratorTests.cs
git commit -m "feat(testscript): render assertion group members as child extensions in TestReport"
```

---

### Task 4: Worked-example end-to-end fixture

**Files:**
- Test: Create `test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesEndToEndTests.cs`

**Interfaces:**
- Consumes (from Task 2, already merged by the time this runs): the evaluator behavior implemented there.
- Consumes (existing, unchanged): `TestScriptParser.Parse(string json) : ParseResult<TestScriptDefinition>` (`Ignixa.TestScript.Parsing`), `ParseResult<T>.IsSuccess`, `ParseResult<T>.Value`.
- Produces: nothing — this is the final task, a living usage reference exercised through the full parse → evaluate → report pipeline.

This test parses a real TestScript JSON document (not hand-built C# expressions like Task 2's tests) mirroring issue #324's own worked example: a Subscription delete whose status is one of an OR group (`200`/`202`/`204`), followed by an immediate readback whose acceptable statuses depend on which delete status actually occurred.

- [ ] **Step 1: Write the failing test**

Create `test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesEndToEndTests.cs`:

```csharp
using Ignixa.Abstractions;
using Ignixa.TestScript.Client;
using Ignixa.TestScript.Evaluation;
using Ignixa.TestScript.Fixtures;
using Ignixa.TestScript.Parsing;
using Ignixa.TestScript.Reporting;
using NSubstitute;

namespace Ignixa.TestScript.Tests.Evaluation;

public class AssertionAlternativesEndToEndTests
{
    private readonly ITestRequestProvider _mockProvider;
    private readonly IFixtureProvider _fixtureProvider;
    private readonly IFhirSchemaProvider _schema;

    public AssertionAlternativesEndToEndTests()
    {
        _mockProvider = Substitute.For<ITestRequestProvider>();
        _fixtureProvider = new InlineFixtureProvider();
        _schema = Substitute.For<IFhirSchemaProvider>();
    }

    [Fact]
    public async Task GivenSubscriptionDeleteReadbackWorkedExample_WhenExecutingEndToEnd_ThenBothGroupsPassViaMatchedAlternative()
    {
        var json = """
            {
              "resourceType":"TestScript","name":"SubscriptionDeleteReadback","status":"active",
              "test":[{"name":"delete then readback","action":[
                {"operation":{"type":{"code":"delete"},"url":"Subscription/sub-1","responseId":"delete-response"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"200","warningOnly":true,"description":"Completed synchronously"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"202","warningOnly":true,"description":"Accepted asynchronously"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"delete-status"}],
                  "responseCode":"204","warningOnly":true,"description":"Completed with no content"}},
                {"operation":{"type":{"code":"read"},"url":"Subscription/sub-1"}},
                {"assert":{
                  "extension":[
                    {"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"},
                    {"url":"http://ignixa.io/testscript/assertionWhenResponseStatus","extension":[
                      {"url":"sourceId","valueString":"delete-response"},
                      {"url":"status","valueInteger":202}
                    ]}
                  ],
                  "responseCode":"200","warningOnly":true,
                  "description":"An asynchronous delete may still be readable immediately"
                }},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"}],
                  "response":"notFound","warningOnly":true,"description":"404 when tracked as gone"}},
                {"assert":{"extension":[{"url":"http://ignixa.io/testscript/assertionAnyOfGroup","valueString":"readback"}],
                  "response":"gone","warningOnly":true,"description":"410 when tracked as deleted"}}
              ]}]
            }
            """;

        var parseResult = TestScriptParser.Parse(json);
        parseResult.IsSuccess.ShouldBeTrue();

        _mockProvider.ExecuteAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(call => new TestResponse
            {
                StatusCode = call.Arg<TestRequest>().Method == HttpMethod.Delete ? 202 : 200
            });

        var evaluator = new TestScriptEvaluator(_mockProvider, _fixtureProvider, _schema);
        var report = await evaluator.ExecuteAsync(parseResult.Value!, CancellationToken.None);

        report.OverallOutcome.ShouldBe(TestScriptOutcome.Pass);
        var actions = report.TestResults[0].Actions;
        actions.Count.ShouldBe(4);

        var deleteStatusGroup = actions[1];
        deleteStatusGroup.GroupId.ShouldBe("delete-status");
        deleteStatusGroup.Outcome.ShouldBe(TestScriptOutcome.Pass);
        deleteStatusGroup.Members!.Single(m => m.Passed).Description.ShouldBe("Accepted asynchronously");

        var readbackGroup = actions[3];
        readbackGroup.GroupId.ShouldBe("readback");
        readbackGroup.Outcome.ShouldBe(TestScriptOutcome.Pass);
        readbackGroup.Members!.Single(m => m.Passed).Description
            .ShouldBe("An asynchronous delete may still be readable immediately");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test test/Ignixa.TestScript.Tests --filter "FullyQualifiedName~AssertionAlternativesEndToEndTests"`

If Tasks 1-3 are already merged into this branch (they should be, since this task runs after them), this test should **pass immediately** — it exercises only already-implemented behavior through a new entry point (parsed JSON instead of hand-built C# expressions). If it fails, that's a real gap: something about parsing + evaluating together doesn't match hand-built-expression behavior, and must be root-caused before proceeding — do not adjust the test's expectations to make it pass without understanding why.

- [ ] **Step 3: Run the full test project to confirm no regression**

Run: `dotnet test test/Ignixa.TestScript.Tests`
Expected: 278 total tests pass × 2 target frameworks (277 from Task 3 + 1 new).

- [ ] **Step 4: Commit**

```bash
git add test/Ignixa.TestScript.Tests/Evaluation/AssertionAlternativesEndToEndTests.cs
git commit -m "test(testscript): add worked end-to-end example for assertion alternatives"
```
