using DurableTask.Core;
using Ignixa.Application.BackgroundOperations.Terminology.Activities;
using Ignixa.Application.BackgroundOperations.Terminology.Models;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.SqlServer.Features.PackageManagement;
using Ignixa.DataLayer.SqlServer.Features.Terminology;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Terminology;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using Xunit;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests.Features.Terminology;

/// <summary>
/// The terminology import activity end to end against a real database, with no <c>FhirDbContext</c>
/// anywhere in the graph: it resolves <see cref="IPackageResourceRepository"/> and
/// <see cref="ITerminologyImporterFactory"/> and nothing else that touches storage.
/// <para>
/// The double-import test here is the one that matters. The defect lived in this activity rather than in
/// the importer: it stamped <c>InProgress</c> on the package row <i>before</i> calling the importer, which
/// is the status the importer's unchanged-content guard reads, so the guard could never fire and every
/// package load re-imported every terminology resource in full. Testing the importer alone cannot see that
/// — its own tests call it directly, with nothing writing the row in between.
/// </para>
/// </summary>
public class ImportTerminologyResourceActivityTests : IAsyncLifetime
{
    private const string SystemUrl = "http://example.org/fhir/CodeSystem/activity-vehicles";

    private TerminologyOracleFixture _fixture = null!;

    public async Task InitializeAsync() => _fixture = await TerminologyOracleFixture.CreateAsync();

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    private TestableImportTerminologyResourceActivity CreateActivity()
    {
        var services = new ServiceCollection();

        services.AddSingleton<IPackageResourceRepository>(_ => new SqlServerPackageResourceRepository(
            _fixture.SqlExecutionService,
            _fixture.SystemPartitionId,
            NullLogger<SqlServerPackageResourceRepository>.Instance));

        services.AddSingleton<ITerminologyImporterFactory>(_ => new SqlServerTerminologyImporterFactory(
            _fixture.SqlExecutionService,
            _fixture.CacheRegistry,
            _fixture.SystemPartitionId,
            NullLoggerFactory.Instance));

        services.AddSingleton<IFhirRequestContextAccessor, FhirRequestContextAccessor>();

        return new TestableImportTerminologyResourceActivity(
            services.BuildServiceProvider(),
            NullLogger<ImportTerminologyResourceActivity>.Instance);
    }

    private static TaskContext TaskContext =>
        new(new OrchestrationInstance { InstanceId = "terminology-import-test" });

    private Task<int> ConceptCountAsync(string url) => _fixture.ExecuteScalarAsync<int>(
        "SELECT COUNT(*) FROM dbo.TermConcept tc " +
        "JOIN dbo.TermCodeSystem cs ON cs.TermCodeSystemId = tc.TermCodeSystemId " +
        "JOIN dbo.System s ON s.SystemId = cs.SystemId " +
        $"WHERE s.Value = '{url}'", CancellationToken.None);

    private Task<string> StatusAsync(long packageResourceId) => _fixture.ExecuteScalarAsync<string>(
        "SELECT TOP 1 TerminologyImportStatus FROM dbo.PackageResource " +
        $"WHERE PackageResourceId = {packageResourceId}", CancellationToken.None);

    private Task<int> RecordedConceptCountAsync(long packageResourceId) => _fixture.ExecuteScalarAsync<int>(
        "SELECT TOP 1 ISNULL(ImportedConceptCount, -1) FROM dbo.PackageResource " +
        $"WHERE PackageResourceId = {packageResourceId}", CancellationToken.None);

    [Fact]
    public async Task GivenACodeSystemPackageResource_WhenTheActivityRuns_ThenItIsImportedAndTheRowRecordsCompleted()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var output = await CreateActivity().RunExecuteAsync(
            TaskContext,
            new ImportTerminologyResourceInput(_fixture.SystemPartitionId, packageResource.PackageResourceId));

        output.Success.ShouldBeTrue();
        output.Canonical.ShouldBe(SystemUrl);
        output.ResourceType.ShouldBe("CodeSystem");
        output.ConceptCount.ShouldBe(4);
        output.ErrorMessage.ShouldBeNull();

        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);
        (await StatusAsync(packageResource.PackageResourceId)).ShouldBe("Completed");
        (await RecordedConceptCountAsync(packageResource.PackageResourceId)).ShouldBe(4);
    }

    [Fact]
    public async Task GivenAnUnchangedPackage_WhenTheActivityRunsTwice_ThenTheSecondPassDoesNoWorkAndLeavesTheRowCompleted()
    {
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", SystemUrl, TerminologyOracleFixture.HierarchicalCodeSystemJson(SystemUrl));

        var input = new ImportTerminologyResourceInput(
            _fixture.SystemPartitionId, packageResource.PackageResourceId);

        var first = await CreateActivity().RunExecuteAsync(TaskContext, input);

        var importedDateAfterFirst = await _fixture.ExecuteScalarAsync<DateTimeOffset>(
            "SELECT TOP 1 ImportedDate FROM dbo.TermCodeSystem " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        var second = await CreateActivity().RunExecuteAsync(TaskContext, input);

        first.ConceptCount.ShouldBe(4);

        // Nothing was re-imported: no concepts written, and the status the first pass earned is intact.
        second.Success.ShouldBeTrue();
        second.ConceptCount.ShouldBe(0);
        (await StatusAsync(packageResource.PackageResourceId)).ShouldBe("Completed");

        // The recorded count is not clobbered with the skip's zero, which is what made a re-loaded package
        // look like it had imported nothing.
        (await RecordedConceptCountAsync(packageResource.PackageResourceId)).ShouldBe(4);

        (await ConceptCountAsync(SystemUrl)).ShouldBe(4);

        // The strongest evidence that the second pass never reached the import procedure: it replaces the
        // dbo.TermCodeSystem row, so a re-import would move this timestamp.
        var importedDateAfterSecond = await _fixture.ExecuteScalarAsync<DateTimeOffset>(
            "SELECT TOP 1 ImportedDate FROM dbo.TermCodeSystem " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}", CancellationToken.None);

        importedDateAfterSecond.ShouldBe(importedDateAfterFirst);
    }

    [Fact]
    public async Task GivenChangedContent_WhenTheActivityRunsAgain_ThenItStillReimports()
    {
        // The guard must not be so eager that a genuinely updated package is ignored.
        var url = "http://example.org/fhir/CodeSystem/activity-changed";

        var packageResource = await _fixture.SeedPackageResourceAsync(
            "CodeSystem", url, TerminologyOracleFixture.HierarchicalCodeSystemJson(url));

        var activity = CreateActivity();
        await activity.RunExecuteAsync(
            TaskContext, new ImportTerminologyResourceInput(_fixture.SystemPartitionId, packageResource.PackageResourceId));

        var changed = TerminologyOracleFixture.FlatCodeSystemJson(url, 7);
        await _fixture.ExecuteNonQueryAsync(
            $"UPDATE dbo.PackageResource SET ResourceJson = '{changed.Replace("'", "''", StringComparison.Ordinal)}' " +
            $"WHERE PackageResourceId = {packageResource.PackageResourceId}",
            CancellationToken.None);

        var second = await activity.RunExecuteAsync(
            TaskContext, new ImportTerminologyResourceInput(_fixture.SystemPartitionId, packageResource.PackageResourceId));

        second.ConceptCount.ShouldBe(7);
        (await ConceptCountAsync(url)).ShouldBe(7);
        (await StatusAsync(packageResource.PackageResourceId)).ShouldBe("Completed");
    }

    [Fact]
    public async Task GivenAPackageResourceIdThatDoesNotExist_WhenTheActivityRuns_ThenItReportsFailureWithoutThrowing()
    {
        // Returned rather than thrown so one bad id does not abort the orchestration's other imports.
        var output = await CreateActivity().RunExecuteAsync(
            TaskContext, new ImportTerminologyResourceInput(_fixture.SystemPartitionId, 987654321));

        output.Success.ShouldBeFalse();
        output.ErrorMessage.ShouldNotBeNull();
        output.ErrorMessage.ShouldContain("987654321");
    }

    [Fact]
    public async Task GivenANonTerminologyResource_WhenTheActivityRuns_ThenItIsRecordedFailedRatherThanLeftPending()
    {
        // The importer is never called for this one, so nothing else can record why it did not import.
        // Failed rather than Skipped: Failed is not terminal, so adding support for the type later still
        // gets a retry, and the row carries a diagnosable message in the meantime.
        var packageResource = await _fixture.SeedPackageResourceAsync(
            "StructureDefinition", "http://example.org/fhir/StructureDefinition/activity-unsupported", "{}");

        var output = await CreateActivity().RunExecuteAsync(
            TaskContext,
            new ImportTerminologyResourceInput(_fixture.SystemPartitionId, packageResource.PackageResourceId));

        output.Success.ShouldBeFalse();
        output.ErrorMessage.ShouldNotBeNull();
        output.ErrorMessage.ShouldContain("StructureDefinition");

        (await StatusAsync(packageResource.PackageResourceId)).ShouldBe("Failed");
    }

    /// <summary>
    /// DurableTask's <see cref="AsyncTaskActivity{TInput,TResult}.ExecuteAsync"/> is protected. This
    /// subclass exposes it so the tests invoke the real production method, following
    /// <c>ImportBatchActivityBaseUriWiringTests</c>.
    /// </summary>
    private sealed class TestableImportTerminologyResourceActivity(
        IServiceProvider serviceProvider,
        ILogger<ImportTerminologyResourceActivity> logger)
        : ImportTerminologyResourceActivity(serviceProvider, logger)
    {
        public Task<ImportTerminologyResourceOutput> RunExecuteAsync(
            TaskContext context, ImportTerminologyResourceInput input) => ExecuteAsync(context, input);
    }
}
