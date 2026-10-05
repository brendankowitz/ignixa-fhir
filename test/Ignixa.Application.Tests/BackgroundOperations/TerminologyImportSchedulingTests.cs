using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Ignixa.Domain.Models;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class TerminologyImportSchedulingTests
{
    [Theory]
    [InlineData("include", "V1", false)]
    [InlineData("exclude", "V1", false)]
    [InlineData("include", "V2", false)]
    [InlineData("include", "v1", false)]
    [InlineData("include", null, false)]
    [InlineData("include", "V1", true)]
    public void GivenVersionQualifiedValueSetReference_WhenPlanning_ThenOnlyExactCanonicalAndVersionAreDependencies(
        string kind, string version, bool wrongUrlCase)
    {
        var reference = "https://example.test/" + (wrongUrlCase ? "source" : "Source") + (version is null ? "" : "|" + version);
        PackageResource[] resources =
        [
            Resource(1, "ValueSet", "https://example.test/Source",
                """{"resourceType":"ValueSet","url":"https://example.test/Source","version":"V1","expansion":{"contains":[]}}"""),
            Resource(2, "ValueSet", "https://example.test/Source",
                """{"resourceType":"ValueSet","url":"https://example.test/Source","version":"V2","expansion":{"contains":[]}}"""),
            Resource(3, "ValueSet", "https://example.test/target", $$$"""
                {"resourceType":"ValueSet","url":"https://example.test/target",
                 "compose":{"{{{kind}}}":[{"valueSet":["{{{reference}}}"]}]}}
                """),
        ];

        var plan = TerminologyImportPlanner.Create(resources);

        long[] expected = wrongUrlCase || version == "v1" ? [] : version == "V1" ? [1] : version == "V2" ? [2] : [1, 2];
        plan.Single(resource => resource.PackageResourceId == 3).DependsOn.ShouldBe(expected);
    }

    [Theory]
    [InlineData("include", "", true, false)]
    [InlineData("exclude", "", false, false)]
    [InlineData("include", ""","concept":[],"filter":[],"valueSet":[]""", true, false)]
    [InlineData("exclude", ""","concept":[],"filter":[],"valueSet":[]""", false, false)]
    [InlineData("include", ""","concept":[{"code":"one"}]""", false, false)]
    [InlineData("exclude", ""","concept":[{"code":"one"}]""", false, false)]
    [InlineData("include", ""","filter":[{"property":"code","op":"=","value":"one"}]""", true, false)]
    [InlineData("exclude", ""","filter":[{"property":"code","op":"=","value":"one"}]""", true, false)]
    [InlineData("include", ""","valueSet":["https://example.test/referenced"]""", false, true)]
    [InlineData("exclude", ""","valueSet":["https://example.test/referenced"]""", false, true)]
    [InlineData("include", ""","valueSet":["https://example.test/referenced"],"filter":[{"property":"code","op":"=","value":"one"}]""", true, true)]
    [InlineData("exclude", ""","valueSet":["https://example.test/referenced"],"filter":[{"property":"code","op":"=","value":"one"}]""", true, true)]
    public void GivenComposeClause_WhenPlanning_ThenOnlyContentReadsCreateDependencies(
        string kind, string details, bool readsCodeSystem, bool readsValueSet)
    {
        PackageResource[] resources =
        [
            Resource(1, "CodeSystem", "https://example.test/cs",
                """{"resourceType":"CodeSystem","url":"https://example.test/cs","content":"complete"}"""),
            Resource(2, "ValueSet", "https://example.test/referenced",
                """{"resourceType":"ValueSet","url":"https://example.test/referenced","expansion":{"contains":[]}}"""),
            Resource(3, "ValueSet", "https://example.test/vs", $$$"""
                {"resourceType":"ValueSet","url":"https://example.test/vs",
                 "compose":{"{{{kind}}}":[{"system":"https://example.test/cs"{{{details}}}}]}}
                """),
        ];

        var plan = TerminologyImportPlanner.Create(resources);

        var expected = new List<long>();
        if (readsCodeSystem)
        {
            expected.Add(1);
        }
        if (readsValueSet)
        {
            expected.Add(2);
        }
        plan.Single(resource => resource.PackageResourceId == 3).DependsOn.ShouldBe(expected);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenFailedExcludedCodeSystem_WhenRunningPlannedJob_ThenOnlyFilteredExclusionIsBlocked(bool filtered)
    {
        var filter = filtered ? ""","filter":[{"property":"code","op":"=","value":"duplicate"}]""" : "";
        PackageResource[] resources =
        [
            Resource(1, "CodeSystem", "https://example.test/bad-cs",
                """{"resourceType":"CodeSystem","url":"https://example.test/bad-cs","content":"complete","concept":[{"code":"duplicate"},{"code":"duplicate"}]}"""),
            Resource(2, "ValueSet", "https://example.test/vs", $$$"""
                {"resourceType":"ValueSet","url":"https://example.test/vs","compose":{
                 "include":[{"system":"https://example.test/good-cs","concept":[{"code":"kept"}]}],
                 "exclude":[{"system":"https://example.test/bad-cs"{{{filter}}}}]}}
                """),
        ];
        var context = Substitute.For<OrchestrationContext>();
        var scheduled = new List<ImportTerminologyResourceInput>();
        context.ScheduleTask<ImportTerminologyResourceOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                var input = (ImportTerminologyResourceInput)call.Arg<object[]>()[0];
                scheduled.Add(input);
                return Output(input.PackageResourceId,
                    input.PackageResourceId == 1 ? "CodeSystem import failed" : input.DependencyFailure);
            });
        var input = new TerminologyImportOrchestrationInput(1, "planned.package", "1",
            [1, 2], TerminologyImportPlanner.Create(resources));

        var result = await new TerminologyImportOrchestration().RunTask(context, input);

        result.Success.ShouldBeFalse();
        result.Results.Single(resource => resource.PackageResourceId == 1).Success.ShouldBeFalse();
        result.Results.Single(resource => resource.PackageResourceId == 2).Success.ShouldBe(!filtered);
        result.FailedCount.ShouldBe(filtered ? 2 : 1);
        var failure = scheduled.Single(resource => resource.PackageResourceId == 2).DependencyFailure;
        if (filtered)
        {
            failure.ShouldContain("dependencies failed: 1");
        }
        else
        {
            failure.ShouldBeNull();
        }
    }

    [Fact]
    public async Task GivenNewJobWithMoreThanFiveResources_WhenRunning_ThenOnlyFiveActivitiesAreActuallyScheduled()
    {
        long[] ids = [1, 2, 3, 4, 5, 6, 7];
        var pending = new ControlledImports(ids);
        var execution = new TerminologyImportOrchestration().RunTask(pending.Context, Input(ids, new Dictionary<long, long[]>()));
        try
        {
            pending.Scheduled.Select(i => i.PackageResourceId).ShouldBe(ids.Take(5));
            foreach (var id in ids.Take(5))
            {
                pending.Complete(id);
            }
            await pending.Started[7].Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending.Scheduled.Count.ShouldBe(7);
        }
        finally
        {
            pending.CompleteAll();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
        (await execution).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenValueSetChainBeforeItsCodeSystem_WhenRunning_ThenDependenciesCompleteBeforeDependentsStart()
    {
        long[] ids = [3, 2, 1];
        var pending = new ControlledImports(ids);
        var execution = new TerminologyImportOrchestration().RunTask(pending.Context,
            Input(ids, new Dictionary<long, long[]> { [3] = [2], [2] = [1] }));
        try
        {
            pending.Scheduled.Select(i => i.PackageResourceId).ShouldBe([1L]);
            pending.Complete(1);
            await pending.Started[2].Task.WaitAsync(TimeSpan.FromSeconds(5));
            pending.Scheduled.Select(i => i.PackageResourceId).ShouldBe([1L, 2L]);
            pending.Complete(2);
            await pending.Started[3].Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            pending.CompleteAll();
            await execution.WaitAsync(TimeSpan.FromSeconds(5));
        }
        (await execution).Results.Select(r => r.PackageResourceId).ShouldBe([1L, 2L, 3L]);
    }

    [Fact]
    public async Task GivenFailedDependency_WhenRunning_ThenDependentIsRecordedFailedInsteadOfImportedPartially()
    {
        var context = Substitute.For<OrchestrationContext>();
        var scheduled = new List<JsonNode>();
        context.ScheduleTask<ImportTerminologyResourceOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                var input = (ImportTerminologyResourceInput)call.Arg<object[]>()[0];
                var json = JsonSerializer.SerializeToNode(input)!;
                scheduled.Add(json);
                var error = input.PackageResourceId == 1
                    ? "CodeSystem import failed"
                    : json["DependencyFailure"]?.GetValue<string>();
                return Output(input.PackageResourceId, error);
            });

        var result = await new TerminologyImportOrchestration().RunTask(context,
            Input([1, 2], new Dictionary<long, long[]> { [2] = [1] }));

        result.Success.ShouldBeFalse();
        result.FailedCount.ShouldBe(2);
        scheduled.Single(i => i["PackageResourceId"]!.GetValue<long>() == 2)["DependencyFailure"]
            .ShouldNotBeNull();
        result.Results.Single(r => r.PackageResourceId == 2).ErrorMessage.ShouldContain("1");
    }

    [Fact]
    public async Task GivenCyclicValueSetDependencies_WhenRunning_ThenNoDependentCanBecomeAnIncompleteSuccess()
    {
        var context = Substitute.For<OrchestrationContext>();
        context.ScheduleTask<ImportTerminologyResourceOutput>(Arg.Any<Type>(), Arg.Any<object[]>())
            .Returns(call =>
            {
                var input = (ImportTerminologyResourceInput)call.Arg<object[]>()[0];
                return Output(input.PackageResourceId,
                    JsonSerializer.SerializeToNode(input)!["DependencyFailure"]?.GetValue<string>());
            });

        var result = await new TerminologyImportOrchestration().RunTask(context,
            Input([1, 2], new Dictionary<long, long[]> { [1] = [2], [2] = [1] }));

        result.Success.ShouldBeFalse();
        result.FailedCount.ShouldBe(2);
        result.Results.ShouldAllBe(r => r.ErrorMessage.Contains("cycle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GivenPersistedInputWithoutDependencyPlan_WhenRunning_ThenLegacyEagerSchedulingIsPreserved()
    {
        long[] ids = [7, 3, 2, 9, 1, 8];
        var pending = new ControlledImports(ids);
        var input = new TerminologyImportOrchestrationInput(1, "legacy.package", "1", ids);

        var execution = new TerminologyImportOrchestration().RunTask(pending.Context, input);

        pending.Scheduled.Select(i => i.PackageResourceId).ShouldBe(ids);
        pending.CompleteAll();
        (await execution.WaitAsync(TimeSpan.FromSeconds(5))).Results.Select(r => r.PackageResourceId).ShouldBe(ids);
    }

    private static TerminologyImportOrchestrationInput Input(
        long[] ids, IReadOnlyDictionary<long, long[]> dependencies)
    {
        var input = JsonSerializer.SerializeToNode(new TerminologyImportOrchestrationInput(1, "planned.package", "1", ids))!;
        input["DependencyPlan"] = new JsonArray(ids.Select(id => (JsonNode)new JsonObject
        {
            ["PackageResourceId"] = id,
            ["Canonical"] = $"http://example.org/terminology/{id}",
            ["ResourceType"] = dependencies.ContainsKey(id) ? "ValueSet" : "CodeSystem",
            ["DependsOn"] = JsonSerializer.SerializeToNode(dependencies.GetValueOrDefault(id) ?? []),
        }).ToArray());
        return input.Deserialize<TerminologyImportOrchestrationInput>()!;
    }

    private static ImportTerminologyResourceOutput Output(long id, string error = null)
        => new(id, $"http://example.org/terminology/{id}", id == 1 ? "CodeSystem" : "ValueSet",
            error == null, error == null ? 3 : 0, error);

    private static PackageResource Resource(long id, string type, string canonical, string json) => new()
    {
        PackageResourceId = id, PackageId = "planned.package", PackageVersion = "1",
        ResourceId = id.ToString(System.Globalization.CultureInfo.InvariantCulture), ResourceType = type,
        Canonical = canonical, ResourceJson = json, FhirVersion = "4.0.1",
    };

    private sealed class ControlledImports
    {
        private readonly IReadOnlyDictionary<long, TaskCompletionSource<ImportTerminologyResourceOutput>> _results;
        public OrchestrationContext Context { get; } = Substitute.For<OrchestrationContext>();
        public ConcurrentQueue<ImportTerminologyResourceInput> Scheduled { get; } = new();
        public IReadOnlyDictionary<long, TaskCompletionSource> Started { get; }

        public ControlledImports(IEnumerable<long> ids)
        {
            _results = ids.ToDictionary(id => id, _ => new TaskCompletionSource<ImportTerminologyResourceOutput>(
                TaskCreationOptions.RunContinuationsAsynchronously));
            Started = ids.ToDictionary(id => id, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Context.ScheduleTask<ImportTerminologyResourceOutput>(Arg.Any<Type>(), Arg.Any<object[]>()).Returns(call =>
            {
                var input = (ImportTerminologyResourceInput)call.Arg<object[]>()[0];
                Scheduled.Enqueue(input);
                Started[input.PackageResourceId].TrySetResult();
                return _results[input.PackageResourceId].Task;
            });
        }

        public void Complete(long id) => _results[id].TrySetResult(Output(id));
        public void CompleteAll()
        {
            foreach (var id in _results.Keys)
            {
                Complete(id);
            }
        }
    }
}
