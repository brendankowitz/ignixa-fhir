using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology;
using Ignixa.Application.BackgroundOperations.Terminology.Activities;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.BackgroundOperations.Terminology.Orchestrations;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Domain.Terminology;
using Ignixa.Validation.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

public class TerminologyDependencyImportTests(TerminologyReplacementFixture fixture)
    : IClassFixture<TerminologyReplacementFixture>
{
    private TerminologyTestFixture Database => fixture.Database;

    [Fact]
    public async Task GivenVersionQualifiedValueSetChain_WhenOrchestrated_ThenFreshImportsHonorPinnedDependencies()
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var resources = await StoreAsync(
            Resource("ValueSet", root + "/parent", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/parent","status":"active",
                 "compose":{"include":[{"valueSet":["{{{root}}}/leaf|V1"]}]}}
                """),
            Resource("ValueSet", root + "/leaf", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/leaf","version":"V1","status":"active",
                 "compose":{"include":[{"system":"{{{root}}}/cs","version":"C1"}]}}
                """),
            Resource("CodeSystem", root + "/cs", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/cs","version":"C1","content":"complete",
                 "concept":[{"code":"one"},{"code":"two"}]}
                """));

        var output = await RunAsync(resources);

        output.Success.ShouldBeTrue(output.ErrorMessage);
        output.TotalConceptsImported.ShouldBe(6);
        var expansion = await Database.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(root + "/parent"), CancellationToken.None);
        expansion.ShouldNotBeNull();
        expansion.Contains.Select(concept => concept.Code).Order().ShouldBe(["one", "two"]);
        expansion.Contains.ShouldAllBe(concept => concept.Version == "C1");
        expansion.Incomplete.ShouldBeFalse();
        foreach (var resource in resources)
        {
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT ContentHash FROM dbo.PackageResource WHERE PackageResourceId = {resource.PackageResourceId}"))
                .ShouldBe(resource.ComputeContentHash());
        }
    }

    [Fact]
    public async Task GivenFreshPackageWithDependentIdsFirst_WhenOrchestrated_ThenEveryValueSetExpandsWithoutReload()
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var resources = await StoreAsync(
            Resource("ValueSet", root + "/parent", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/parent","status":"active",
                 "compose":{"include":[{"valueSet":["{{{root}}}/leaf"]}]}}
                """),
            Resource("ValueSet", root + "/leaf", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/leaf","status":"active",
                 "compose":{"include":[{"system":"{{{root}}}/cs"}]}}
                """),
            Resource("CodeSystem", root + "/cs", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/cs","status":"active","content":"complete",
                 "concept":[{"code":"one"},{"code":"two"},{"code":"three"}]}
                """));

        var output = await RunAsync(resources);

        output.Success.ShouldBeTrue(output.ErrorMessage);
        output.TotalConceptsImported.ShouldBe(9);
        foreach (var canonical in new[] { root + "/leaf", root + "/parent" })
        {
            var expansion = await Database.CreateTerminologyService().ExpandValueSetAsync(
                new ExpansionParameters(canonical), CancellationToken.None);
            expansion.ShouldNotBeNull();
            expansion.Total.ShouldBe(3);
            expansion.Incomplete.ShouldBeFalse();
            expansion.Contains.Select(concept => concept.Code).Order().ShouldBe(["one", "three", "two"]);
        }
        (await Repository().ListPendingTerminologyImportsAsync(resources[0].PackageId, "1.0.0", CancellationToken.None))
            .ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenFailedInPackageCodeSystem_WhenOrchestrated_ThenDependentValueSetIsFailedAndRetryable()
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var resources = await StoreAsync(
            Resource("ValueSet", root + "/vs", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/vs","status":"active",
                 "compose":{"include":[{"system":"{{{root}}}/cs"}]}}
                """),
            Resource("CodeSystem", root + "/cs", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/cs","content":"complete",
                 "concept":[{"code":"duplicate"},{"code":"duplicate"}]}
                """));

        var output = await RunAsync(resources);

        output.Success.ShouldBeFalse();
        output.FailedCount.ShouldBe(2);
        var blocked = output.Results.Single(result => result.PackageResourceId == resources[0].PackageResourceId);
        blocked.ErrorMessage.ShouldNotBeNull();
        blocked.ErrorMessage.ShouldContain("dependencies failed");
        (await Database.ExecuteScalarAsync<string>(
            $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {resources[0].PackageResourceId}"))
            .ShouldBe("Failed");
        (await Database.ExecuteScalarAsync<int>(
            $"SELECT COUNT(*) FROM dbo.TermValueSet WHERE PackageResourceId = {resources[0].PackageResourceId}"))
            .ShouldBe(0);
        (await Repository().ListPendingTerminologyImportsAsync(resources[0].PackageId, "1.0.0", CancellationToken.None))
            .Single().PackageResourceIds.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenFailedExcludedCodeSystem_WhenOrchestrated_ThenOnlyFilteredExclusionIsBlocked(bool filtered)
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var filter = filtered ? ""","filter":[{"property":"code","op":"=","value":"duplicate"}]""" : "";
        var resources = await StoreAsync(
            Resource("CodeSystem", root + "/bad-cs", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/bad-cs","content":"complete",
                 "concept":[{"code":"duplicate"},{"code":"duplicate"}]}
                """),
            Resource("ValueSet", root + "/vs", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/vs","status":"active","compose":{
                 "include":[{"system":"{{{root}}}/good-cs","version":"V1","concept":[{"code":"kept","display":"Kept"}]},
                            {"system":"{{{root}}}/bad-cs","concept":[{"code":"removed","display":"Removed"}]}],
                 "exclude":[{"system":"{{{root}}}/bad-cs"{{{filter}}}}]}}
                """));

        var output = await RunAsync(resources);

        output.Success.ShouldBeFalse();
        var failedCodeSystem = output.Results.Single(result => result.PackageResourceId == resources[0].PackageResourceId);
        failedCodeSystem.Success.ShouldBeFalse();
        failedCodeSystem.ErrorMessage.ShouldNotBeNullOrEmpty();
        (await Database.ExecuteScalarAsync<string>(
            $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {resources[0].PackageResourceId}"))
            .ShouldBe("Failed");
        var valueSet = output.Results.Single(result => result.PackageResourceId == resources[1].PackageResourceId);
        valueSet.Success.ShouldBe(!filtered);
        output.FailedCount.ShouldBe(filtered ? 2 : 1);
        var pending = (await Repository().ListPendingTerminologyImportsAsync(
            resources[0].PackageId, "1.0.0", CancellationToken.None)).Single().PackageResourceIds;
        if (filtered)
        {
            valueSet.ErrorMessage.ShouldNotBeNull();
            valueSet.ErrorMessage.ShouldContain("dependencies failed");
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {resources[1].PackageResourceId}"))
                .ShouldBe("Failed");
            (await Database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.TermValueSet WHERE PackageResourceId = {resources[1].PackageResourceId}"))
                .ShouldBe(0);
            (await Database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.PackageResource WHERE PackageResourceId = {resources[1].PackageResourceId} AND ContentHash IS NOT NULL"))
                .ShouldBe(0);
            pending.Order().ShouldBe(resources.Select(resource => resource.PackageResourceId).Order());
        }
        else
        {
            valueSet.ErrorMessage.ShouldBeNull();
            valueSet.ConceptCount.ShouldBe(1);
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {resources[1].PackageResourceId}"))
                .ShouldBe("Completed");
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT ContentHash FROM dbo.PackageResource WHERE PackageResourceId = {resources[1].PackageResourceId}"))
                .ShouldBe(resources[1].ComputeContentHash());
            var expansion = await Database.CreateTerminologyService().ExpandValueSetAsync(
                new ExpansionParameters(root + "/vs"), CancellationToken.None);
            expansion.ShouldNotBeNull();
            expansion.Total.ShouldBe(1);
            expansion.Incomplete.ShouldBeFalse();
            var concept = expansion.Contains.ShouldHaveSingleItem();
            concept.System.ShouldBe(root + "/good-cs");
            concept.Code.ShouldBe("kept");
            concept.Display.ShouldBe("Kept");
            concept.Version.ShouldBe("V1");
            pending.ShouldBe([resources[0].PackageResourceId]);
        }
    }

    [Fact]
    public async Task GivenExcludedValueSetWithFailedDependency_WhenOrchestrated_ThenExcludingValueSetRemainsBlocked()
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var resources = await StoreAsync(
            Resource("ValueSet", root + "/vs", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/vs","status":"active","compose":{
                 "include":[{"system":"{{{root}}}/good-cs","concept":[{"code":"kept"}]}],
                 "exclude":[{"valueSet":["{{{root}}}/excluded-vs"]}]}}
                """),
            Resource("ValueSet", root + "/excluded-vs", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/excluded-vs","status":"active",
                 "compose":{"include":[{"system":"{{{root}}}/bad-cs"}]}}
                """),
            Resource("CodeSystem", root + "/bad-cs", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/bad-cs","content":"complete",
                 "concept":[{"code":"duplicate"},{"code":"duplicate"}]}
                """));

        var output = await RunAsync(resources);

        output.Success.ShouldBeFalse();
        output.FailedCount.ShouldBe(3);
        foreach (var resource in resources.Take(2))
        {
            var error = output.Results.Single(result => result.PackageResourceId == resource.PackageResourceId).ErrorMessage;
            error.ShouldNotBeNull();
            error.ShouldContain("dependencies failed");
            (await Database.ExecuteScalarAsync<string>(
                $"SELECT TerminologyImportStatus FROM dbo.PackageResource WHERE PackageResourceId = {resource.PackageResourceId}"))
                .ShouldBe("Failed");
            (await Database.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM dbo.TermValueSet WHERE PackageResourceId = {resource.PackageResourceId}"))
                .ShouldBe(0);
        }
        (await Repository().ListPendingTerminologyImportsAsync(resources[0].PackageId, "1.0.0", CancellationToken.None))
            .Single().PackageResourceIds.Order().ShouldBe(resources.Select(resource => resource.PackageResourceId).Order());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenExternalOrDeclaredNotPresentSystem_WhenOrchestrated_ThenDeclaredPartialSemanticsRemain(bool inPackageNotPresent)
    {
        var root = $"https://terminology-order.example/{Guid.NewGuid():N}";
        var resources = new List<PackageResource>
        {
            Resource("ValueSet", root + "/vs", $$$"""
                {"resourceType":"ValueSet","url":"{{{root}}}/vs","status":"active",
                 "compose":{"include":[{"system":"{{{root}}}/external"}]}}
                """),
        };
        if (inPackageNotPresent)
        {
            resources.Add(Resource("CodeSystem", root + "/external", $$"""
                {"resourceType":"CodeSystem","url":"{{root}}/external","content":"not-present"}
                """));
        }
        var stored = await StoreAsync(resources.ToArray());

        var output = await RunAsync(stored);

        output.Success.ShouldBeTrue(output.ErrorMessage);
        var expansion = await Database.CreateTerminologyService().ExpandValueSetAsync(
            new ExpansionParameters(root + "/vs"), CancellationToken.None);
        expansion.ShouldNotBeNull();
        expansion.Total.ShouldBe(0);
        expansion.Incomplete.ShouldBeTrue();
    }

    private SqlServerPackageResourceRepository Repository()
        => new(Database.SqlExecutionService, 1, NullLogger<SqlServerPackageResourceRepository>.Instance);

    private async Task<PackageResource[]> StoreAsync(params PackageResource[] resources)
    {
        var packageId = $"terminology.order.{Guid.NewGuid():N}";
        foreach (var resource in resources)
        {
            resource.PackageId = packageId;
            await Repository().UpsertAsync(resource, CancellationToken.None);
        }
        var stored = new List<PackageResource>();
        foreach (var resource in resources)
        {
            stored.Add((await Repository().GetFromPackageAsync(packageId, "1.0.0", resource.Canonical))!);
        }
        return stored.ToArray();
    }

    private async Task<TerminologyImportOrchestrationOutput> RunAsync(IReadOnlyList<PackageResource> resources)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPackageResourceRepository>(Repository());
        services.AddSingleton<ITerminologyImporterFactory>(new SqlServerTerminologyImporterFactory(
            Database.SqlExecutionService, Database.CacheRegistry, 0, NullLoggerFactory.Instance));
        services.AddSingleton<IFhirRequestContextAccessor, FhirRequestContextAccessor>();
        await using var provider = services.BuildServiceProvider();
        var activity = new ImportActivity(provider);
        var context = new ExecutingContext(activity);
        var input = new TerminologyImportOrchestrationInput(1, resources[0].PackageId, "1.0.0",
            resources.Select(resource => resource.PackageResourceId).ToArray(), TerminologyImportPlanner.Create(resources));
        return await new TerminologyImportOrchestration().RunTask(context, input);
    }

    private static PackageResource Resource(string type, string canonical, string json) => new()
    {
        PackageId = "assigned-before-write", PackageVersion = "1.0.0", ResourceType = type,
        Canonical = canonical, ResourceId = Guid.NewGuid().ToString("N"), ResourceJson = json, FhirVersion = "4.0.1",
    };

    private sealed class ImportActivity(IServiceProvider services)
        : ImportTerminologyResourceActivity(services, NullLogger<ImportTerminologyResourceActivity>.Instance)
    {
        public Task<ImportTerminologyResourceOutput> RunAsync(ImportTerminologyResourceInput input)
            => ExecuteAsync(new TaskContext(new OrchestrationInstance { InstanceId = "terminology-order" }), input);
    }

    private sealed class ExecutingContext(ImportActivity activity) : OrchestrationContext
    {
        public override async Task<T> ScheduleTask<T>(string name, string version, params object[] parameters)
        {
            name.ShouldBe(typeof(ImportTerminologyResourceActivity).FullName);
            typeof(T).ShouldBe(typeof(ImportTerminologyResourceOutput));
            return (T)(object)await activity.RunAsync((ImportTerminologyResourceInput)parameters.Single());
        }

        public override Task<T> CreateTimer<T>(DateTime fireAt, T state) => throw new NotSupportedException();
        public override Task<T> CreateTimer<T>(DateTime fireAt, T state, CancellationToken cancellationToken) => throw new NotSupportedException();
        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, object input) => throw new NotSupportedException();
        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input) => throw new NotSupportedException();
        public override Task<T> CreateSubOrchestrationInstance<T>(string name, string version, string instanceId, object input,
            IDictionary<string, string> tags) => throw new NotSupportedException();
        public override void SendEvent(OrchestrationInstance orchestrationInstance, string eventName, object eventData) => throw new NotSupportedException();
        public override void ContinueAsNew(object input) => throw new NotSupportedException();
        public override void ContinueAsNew(string newVersion, object input) => throw new NotSupportedException();
    }
}
