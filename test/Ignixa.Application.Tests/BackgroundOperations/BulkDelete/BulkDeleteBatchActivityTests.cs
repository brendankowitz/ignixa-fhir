using System.Text;
using System.Text.Json;
using DurableTask.Core;
using DurableTask.Core.Serializing;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete.Activities;
using Ignixa.Application.BackgroundOperations.BulkDelete.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.BackgroundOperations.BulkDelete;

public sealed class BulkDeleteBatchActivityTests : IAsyncLifetime, IDisposable
{
    private const int TenantId = 1;
    private const string JobId = "job";
    private static readonly JsonSerializerOptions WebOptions = new(JsonSerializerDefaults.Web);
    private readonly FhirVersionContext _versions = new(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(),
        NullFhirBaseUriProvider.Instance);
    private readonly SearchOptionsBuilderFactory _builders;

    /// <summary>
    /// One lease shared by the search-option builders and the activity, as the production singleton is.
    /// </summary>
    private readonly ConformanceLease _lease = TestConformanceLease.NotHeld();
    private bool _leaseHeld = true;
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private readonly InMemoryBackgroundJobRepository<BulkDeleteJobDefinition> _jobs;
    private readonly ISearchService _search = Substitute.For<ISearchService>();
    private readonly IFhirRepository _repository = Substitute.For<IFhirRepository>();
    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IFhirRequestContextAccessor _accessor = Substitute.For<IFhirRequestContextAccessor>();
    private IFhirRequestContext? _currentContext;

    /// <summary>
    /// The service base the activity's reference parser recognizes as "this server". Only
    /// <c>_remove-references</c> reads it, so it stays separate from the <see cref="NullFhirBaseUriProvider"/>
    /// the search-option builders use.
    /// </summary>
    private IFhirBaseUriProvider _baseUris = NullFhirBaseUriProvider.Instance;
    private readonly List<SearchOptions> _searches = [];
    private readonly List<string> _operations = [];
    private Func<SearchOptions, IEnumerable<SearchEntryResult>> _results = _ => [];

    public BulkDeleteBatchActivityTests()
    {
        _builders = new SearchOptionsBuilderFactory(
            _versions,
            NullFhirBaseUriProvider.Instance,
            new HttpContextAccessor(),
            _accessor,
            _lease);
        _tenants.Mode.Returns(TenantMode.Isolated);
        _tenants.GetTenantConfigurationAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = TenantId, DisplayName = "Bulk delete", FhirVersion = "4.0" });
        _jobs = new(_tenants, NullLogger<InMemoryBackgroundJobRepository<BulkDeleteJobDefinition>>.Instance);
        // A plain slot rather than the AsyncLocal accessor, so the test observes what the activity leaves behind.
        _accessor.RequestContext.Returns(_ => _currentContext);
        _accessor.When(accessor => accessor.RequestContext = Arg.Any<IFhirRequestContext>())
            .Do(call => _currentContext = call.Arg<IFhirRequestContext>());
        _search.SearchStreamAsync(Arg.Any<SearchOptions>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var options = call.Arg<SearchOptions>();
            _searches.Add(new SearchOptions(options));
            return Stream(_results(options));
        });
        _repository.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _operations.Add($"hard {Describe(call.Arg<ResourceKey>())}");
            return true;
        });
        _repository.PurgeHistoryAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            _operations.Add($"purge {Describe(call.Arg<ResourceKey>())}");
            return 0;
        });
        _mediator.SendAsync(Arg.Any<DeleteResourceCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<DeleteResourceCommand>();
            _operations.Add($"soft {command.ResourceType}/{command.Id}");
            _currentContext!.TenantId.ShouldBe(TenantId);
            _currentContext.IsBackgroundTask.ShouldBeTrue();
            _currentContext.ResourceType.ShouldBe("Patient");
            return command.Id != "gone";
        });
    }

    public Task InitializeAsync() => _jobs.CreateAsync(new BackgroundJob<BulkDeleteJobDefinition>
    {
        JobId = JobId,
        JobType = (int)BackgroundJobType.BulkDelete,
        Status = "Queued",
        Definition = new BulkDeleteJobDefinition
        {
            TenantId = TenantId, ResourceTypes = ["Patient"], SearchQuery = string.Empty, Mode = BulkDeleteMode.HardDelete,
            ExcludedResourceTypes = [],
        },
    }, CancellationToken.None);

    [Theory]
    [InlineData("Completed")]
    [InlineData("Failed")]
    [InlineData("Cancelled")]
    public async Task GivenATerminalJob_WhenABatchRuns_ThenNothingIsSearchedOrDeletedAndTheBatchIsSuperseded(string status)
    {
        await SetStatusAsync(status);
        _results = _ => [Match("Patient", "p1")];

        var output = await RunAsync(Input(BulkDeleteMode.HardDelete));

        output.Superseded.ShouldBeTrue();
        output.DeletedCounts.ShouldBeEmpty();
        _searches.ShouldBeEmpty();
        _operations.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(BulkDeleteMode.SoftDelete)]
    [InlineData(BulkDeleteMode.HardDelete)]
    [InlineData(BulkDeleteMode.PurgeHistory)]
    public async Task GivenTheConformanceLeaseIsNotHeld_WhenABatchRuns_ThenNothingIsSearchedOrDeletedAndTheBatchAsksToWait(BulkDeleteMode mode)
    {
        _leaseHeld = false;
        _results = _ => [Match("Patient", "p1")];

        var output = await RunAsync(Input(mode));

        _searches.ShouldBeEmpty();
        _operations.ShouldBeEmpty();
        output.DeletedCounts.ShouldBeEmpty();
        output.ConformanceStale.ShouldBeTrue();
        output.Superseded.ShouldBeFalse();
        (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!.Status.ShouldBe("Queued");
    }

    [Fact]
    public async Task GivenMatchesAndIncludes_WhenHardDeleting_ThenIncludesAreDeletedFirstOnceEachAndExcludedTypesAreKept()
    {
        _results = _ =>
        [
            Include("Observation", "o1"),
            Match("Patient", "p1"),
            Include("Group", "g1"),
            Include("Observation", "o1"),
            Include("Patient", "p2"),
            Match("Patient", "p2"),
        ];

        var output = await RunAsync(Input(BulkDeleteMode.HardDelete, "_revinclude=Observation:subject", excluded: ["group"]));

        _operations.ShouldBe(["hard Observation/o1", "hard Patient/p1", "hard Patient/p2"]);
        output.DeletedCounts.ShouldBe(new Dictionary<string, long> { ["Observation"] = 1, ["Patient"] = 2 }, ignoreOrder: true);
        output.FirstMatchKey.ShouldBe("Patient/p1");
        output.HasMore.ShouldBeFalse();
        output.Superseded.ShouldBeFalse();
        var search = _searches.ShouldHaveSingleItem();
        search.ResourceType.ShouldBe("Patient");
        search.MaxItemCount.ShouldBe(3);
        search.ProbeExtraRow.ShouldBeTrue();
        search.ContinuationToken.ShouldBeNull();
        search.UseExportContinuation.ShouldBeFalse();
        search.RevInclude.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GivenASoftDelete_WhenABatchRuns_ThenEachResourceIsDeletedThroughTheMediatorInTheTenantContextAndMissesAreNotCounted()
    {
        var previous = FhirRequestContextFactory.CreateBackgroundContext(2);
        _currentContext = previous;
        _results = _ => [Match("Patient", "p1"), Match("Patient", "gone")];

        var output = await RunAsync(Input(BulkDeleteMode.SoftDelete));

        _operations.ShouldBe(["soft Patient/p1", "soft Patient/gone"]);
        output.DeletedCounts.ShouldBe(new Dictionary<string, long> { ["Patient"] = 1 });
        _currentContext.ShouldBeSameAs(previous);
        await _repository.DidNotReceiveWithAnyArgs().HardDeleteAsync(default!, default);
    }

    [Fact]
    public async Task GivenMoreMatchesThanTheBatchSize_WhenRestartModeRuns_ThenItReportsMoreWithoutACursor()
    {
        _results = _ => [Match("Patient", "p1"), Probe(continuation: null)];

        var output = await RunAsync(Input(BulkDeleteMode.HardDelete));

        output.HasMore.ShouldBeTrue();
        output.NextContinuationToken.ShouldBeNull();
        output.FirstMatchKey.ShouldBe("Patient/p1");
        _operations.ShouldBe(["hard Patient/p1"]);
    }

    [Fact]
    public async Task GivenAPurgeWithoutIncludes_WhenABatchRuns_ThenItPagesWithTheProviderCursorAndCountsEveryProcessedResource()
    {
        _results = _ => [Match("Patient", "p1"), Match("Patient", "p2"), Probe("cursor-2")];

        var output = await RunAsync(Input(BulkDeleteMode.PurgeHistory, continuation: "cursor-1"));

        var search = _searches.ShouldHaveSingleItem();
        search.UseExportContinuation.ShouldBeTrue();
        search.ContinuationToken.ShouldBe("cursor-1");
        output.HasMore.ShouldBeTrue();
        output.NextContinuationToken.ShouldBe("cursor-2");
        _operations.ShouldBe(["purge Patient/p1", "purge Patient/p2"]);
        output.DeletedCounts.ShouldBe(new Dictionary<string, long> { ["Patient"] = 2 });
    }

    [Fact]
    public async Task GivenAPurgeWithIncludes_WhenABatchRuns_ThenMatchesPageByKeysetAndTheCascadeIsReadForThatPageOnly()
    {
        _results = options => options.ProbeExtraRow
            ? [Match("Patient", "p1"), Match("Patient", "p2"), Probe("cursor-2")]
            : [Match("Patient", "p1"), Include("Observation", "o1"), Include("Group", "g1"), Include("Patient", "p2"), Match("Patient", "p2")];

        var output = await RunAsync(Input(
            BulkDeleteMode.PurgeHistory, "_revinclude=Observation:subject", excluded: ["Group"], continuation: "cursor-1"));

        _searches.Count.ShouldBe(2);
        var page = _searches[0];
        page.UseExportContinuation.ShouldBeTrue();
        page.ContinuationToken.ShouldBe("cursor-1");
        page.Include.ShouldBeEmpty();
        page.RevInclude.ShouldBeEmpty();
        var cascade = _searches[1];
        cascade.ProbeExtraRow.ShouldBeFalse();
        cascade.UseExportContinuation.ShouldBeFalse();
        cascade.ContinuationToken.ShouldBeNull();
        cascade.MaxItemCount.ShouldBe(2);
        cascade.RevInclude.ShouldHaveSingleItem();
        cascade.Expression.ToString().ShouldContain("p1");
        cascade.Expression.ToString().ShouldContain("p2");
        _operations.ShouldBe(["purge Observation/o1", "purge Patient/p1", "purge Patient/p2"]);
        output.DeletedCounts.ShouldBe(new Dictionary<string, long> { ["Observation"] = 1, ["Patient"] = 2 }, ignoreOrder: true);
        output.NextContinuationToken.ShouldBe("cursor-2");
        output.HasMore.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenAPurgePageLargerThanTheCascadeChunk_WhenABatchRuns_ThenTheCascadeIsReadInChunks()
    {
        var matches = Enumerable.Range(0, 250).Select(index => Match("Patient", $"p{index}")).ToList();
        _results = options => options.ProbeExtraRow ? matches : [];

        await RunAsync(Input(BulkDeleteMode.PurgeHistory, "_include=Patient:organization", batchSize: 250));

        _searches.Skip(1).Select(search => search.MaxItemCount).ShouldBe([100, 100, 50]);
        _searches.Skip(1).ShouldAllBe(search => search.Include.Count == 1);
        _operations.Count.ShouldBe(250);
    }

    [Fact]
    public async Task GivenAnEmptyPurgePage_WhenABatchRuns_ThenNoCascadeIsRead()
    {
        _results = _ => [];

        var output = await RunAsync(Input(BulkDeleteMode.PurgeHistory, "_revinclude=Observation:subject"));

        _searches.ShouldHaveSingleItem();
        output.HasMore.ShouldBeFalse();
        output.FirstMatchKey.ShouldBeNull();
    }

    [Theory]
    [InlineData(BulkDeleteMode.SoftDelete)]
    [InlineData(BulkDeleteMode.PurgeHistory)]
    public async Task GivenRemoveReferencesWithoutHardDelete_WhenABatchRuns_ThenItFailsBeforeTouchingAnything(BulkDeleteMode mode)
    {
        _results = _ => [Match("Patient", "p1")];

        var failure = await Should.ThrowAsync<Exception>(() => RunAsync(Input(mode, removeReferences: true)));

        failure.ToString().ShouldContain("only when hard deleting");
        _searches.ShouldBeEmpty();
        _operations.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAPurgeProbeWithoutACursor_WhenABatchRuns_ThenTheBatchFailsInsteadOfRestarting()
    {
        _results = _ => [Match("Patient", "p1"), Probe(continuation: null)];

        var failure = await Should.ThrowAsync<Exception>(() => RunAsync(Input(BulkDeleteMode.PurgeHistory)));

        failure.ToString().ShouldContain("without a continuation");
    }

    [Fact]
    public async Task GivenAnUnreadableMatch_WhenABatchRuns_ThenItFailsWithoutDeletingAndRestoresTheContext()
    {
        var previous = FhirRequestContextFactory.CreateBackgroundContext(2);
        _currentContext = previous;
        _results = _ => [Match("Patient", "p1"), Match("OperationOutcome", "skipped") with { SearchMode = SearchEntryMode.Outcome }];

        var failure = await Should.ThrowAsync<Exception>(() => RunAsync(Input(BulkDeleteMode.HardDelete)));

        failure.ToString().ShouldContain("unreadable");
        _operations.ShouldBeEmpty();
        _currentContext.ShouldBeSameAs(previous);
    }

    [Fact]
    public async Task GivenEarlierBatches_WhenABatchCompletes_ThenCumulativeProgressIsPersisted()
    {
        _results = _ => [Match("Patient", "p1"), Match("Patient", "p2")];

        await RunAsync(Input(BulkDeleteMode.HardDelete, cumulative: new() { ["Patient"] = 3, ["Observation"] = 1 }));

        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status.ShouldBe("Running");
        job.StartDate.ShouldNotBeNull();
        var progress = job.Progress.Deserialize<BulkDeleteJobProgress>(WebOptions)!;
        progress.ResourceDeletedCount.ShouldBe(new Dictionary<string, long> { ["Patient"] = 5, ["Observation"] = 1 }, ignoreOrder: true);
    }

    [Fact]
    public async Task GivenTheJobIsCancelledDuringTheBatch_WhenRecordingProgress_ThenTheBatchIsSupersededAndCancellationStands()
    {
        _results = _ => [Match("Patient", "p1")];
        _repository.HardDeleteAsync(Arg.Any<ResourceKey>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await SetStatusAsync("Cancelled");
            return true;
        });

        var output = await RunAsync(Input(BulkDeleteMode.HardDelete));

        output.Superseded.ShouldBeTrue();
        output.DeletedCounts.ShouldBe(new Dictionary<string, long> { ["Patient"] = 1 });
        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status.ShouldBe("Cancelled");
        job.Progress.ShouldBeNull();
    }

    [Fact]
    public async Task GivenRemoveReferences_WhenHardDeleting_ThenReferrersOutsideTheDeleteSetAreRewrittenOnceBeforeDeletion()
    {
        var encounter = Include("Encounter", "e1", """
            {"resourceType":"Encounter","id":"e1","status":"finished","class":{"code":"AMB"},
             "subject":{"reference":"Patient/p1"},"reasonReference":[{"reference":"Observation/o1"}],
             "basedOn":[{"reference":"ServiceRequest/s1"}]}
            """) with { VersionId = "3" };
        _results = options => options switch
        {
            { ProbeExtraRow: true } => [Include("Observation", "o1"), Match("Patient", "p1")],
            { ResourceType: "Patient" } => [Match("Patient", "p1"), Include("Observation", "o1"), encounter],
            { ResourceType: "Observation" } => [Match("Observation", "o1"), encounter],
            _ => throw new InvalidOperationException("Unexpected search"),
        };
        var updates = new List<CreateOrUpdateResourceCommand>();
        _mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<CreateOrUpdateResourceCommand>();
            updates.Add(command);
            _operations.Add($"update {command.ResourceType}/{command.Id}");
            return new UpdateResult(new ResourceKey(command.ResourceType, command.Id, "4"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch);
        });

        await RunAsync(Input(BulkDeleteMode.HardDelete, "_revinclude=Observation:subject", removeReferences: true));

        _operations.ShouldBe(["update Encounter/e1", "hard Observation/o1", "hard Patient/p1"]);
        var update = updates.ShouldHaveSingleItem();
        update.IfMatch.ShouldBe("3");
        update.HttpMethod.ShouldBe(HttpMethod.Put);
        var body = update.JsonNode.SerializeToString();
        body.ShouldNotContain("Patient/p1");
        body.ShouldNotContain("Observation/o1");
        body.ShouldContain("ServiceRequest/s1");
        body.ShouldContain("Referenced resource deleted");
        _searches.Where(search => !search.ProbeExtraRow).ShouldAllBe(search =>
            search.RevInclude.Count == 1 && search.RevInclude[0].WildCard);
    }

    /// <summary>
    /// The referrers handed to removal come from the reference search index, which stores a normalized
    /// (type, id) pair: a <c>/_history/{version}</c> suffix is dropped and an absolute URL under one of this
    /// server's bases collapses onto the relative form. So the cascade reports a referrer written in any of
    /// those forms, and removal has to rewrite all of them -- otherwise the referrer survives, still
    /// pointing at a resource this batch then hard deletes.
    /// </summary>
    [Theory]
    [InlineData("Patient/p1")]
    [InlineData("Patient/p1/_history/2")]
    [InlineData("https://fhir.example.org/Patient/p1")]
    [InlineData("https://fhir.example.org/Patient/p1/_history/2")]
    public async Task GivenARemovableReferenceForm_WhenHardDeletingWithRemoveReferences_ThenTheReferrerIsRewritten(
        string reference)
    {
        _baseUris = new StubBaseUriProvider("https://fhir.example.org/");
        var encounter = Include("Encounter", "e1", Referrer(reference)) with { VersionId = "3" };
        _results = options => options switch
        {
            { ProbeExtraRow: true } => [Match("Patient", "p1")],
            _ => [Match("Patient", "p1"), encounter],
        };
        var updates = new List<CreateOrUpdateResourceCommand>();
        _mediator.SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var command = call.Arg<CreateOrUpdateResourceCommand>();
            updates.Add(command);
            _operations.Add($"update {command.ResourceType}/{command.Id}");
            return new UpdateResult(new ResourceKey(command.ResourceType, command.Id, "4"), ReadOnlyMemory<byte>.Empty, DateTimeOffset.UnixEpoch);
        });

        await RunAsync(Input(BulkDeleteMode.HardDelete, removeReferences: true));

        _operations.ShouldBe(["update Encounter/e1", "hard Patient/p1"]);
        var body = updates.ShouldHaveSingleItem().JsonNode.SerializeToString();
        body.ShouldNotContain(reference);
        body.ShouldContain("Referenced resource deleted");
    }

    /// <summary>
    /// A base URI that is not this server's names a different resource; the index keeps the base attached
    /// and the cascade does not report it. Removal must not rewrite it even though the type and id match.
    /// </summary>
    [Fact]
    public async Task GivenAReferenceToAnotherServer_WhenHardDeletingWithRemoveReferences_ThenTheReferrerIsNotRewritten()
    {
        _baseUris = new StubBaseUriProvider("https://fhir.example.org/");
        var encounter = Include("Encounter", "e1", Referrer("https://other.example.org/fhir/Patient/p1"))
            with { VersionId = "3" };
        _results = options => options switch
        {
            { ProbeExtraRow: true } => [Match("Patient", "p1")],
            _ => [Match("Patient", "p1"), encounter],
        };

        await RunAsync(Input(BulkDeleteMode.HardDelete, removeReferences: true));

        _operations.ShouldBe(["hard Patient/p1"]);
        await _mediator.DidNotReceive().SendAsync(Arg.Any<CreateOrUpdateResourceCommand>(), Arg.Any<CancellationToken>());
    }

    /// <summary>An Encounter whose only link is <paramref name="reference"/>, written verbatim.</summary>
    private static string Referrer(string reference) =>
        """{"resourceType":"Encounter","id":"e1","status":"finished","subject":{"reference":"""
        + JsonSerializer.Serialize(reference) + "}}";

    private sealed class StubBaseUriProvider(string baseUri) : IFhirBaseUriProvider
    {
        public Uri? GetBaseUri() => new(baseUri);
    }

    private async Task<BulkDeleteBatchOutput> RunAsync(BulkDeleteBatchInput input)
    {
        if (_leaseHeld)
        {
            _lease.Renew(_lease.CaptureStart());
        }

        var repositories = Substitute.For<IFhirRepositoryFactory>();
        repositories.GetRepositoryAsync(TenantId, Arg.Any<CancellationToken>()).Returns(_repository);
        var searches = Substitute.For<ISearchServiceFactory>();
        searches.GetSearchServiceAsync(TenantId, Arg.Any<CancellationToken>()).Returns(_search);
        var lifetime = Substitute.For<IHostApplicationLifetime>();
        lifetime.ApplicationStopping.Returns(CancellationToken.None);
        var activity = new BulkDeleteBatchActivity(_jobs, _tenants, searches, repositories, new QueryParameterParser(), _builders,
            _versions, _baseUris, _mediator, _accessor, _lease, lifetime, NullLogger<BulkDeleteBatchActivity>.Instance);
        var json = await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = JobId }),
            JsonSerializer.Serialize(new[] { input }));
        return JsonDataConverter.Default.Deserialize<BulkDeleteBatchOutput>(json);
    }

    private static BulkDeleteBatchInput Input(
        BulkDeleteMode mode,
        string query = "",
        string[]? excluded = null,
        string? continuation = null,
        bool removeReferences = false,
        Dictionary<string, long>? cumulative = null,
        int batchSize = 3) =>
        new(JobId, TenantId, "Patient", query, mode, excluded ?? [], removeReferences, batchSize, continuation, cumulative ?? []);

    private async Task SetStatusAsync(string status)
    {
        var job = (await _jobs.GetAsync(JobId, TenantId, CancellationToken.None))!;
        job.Status = status;
        await _jobs.UpdateAsync(job, TenantId, CancellationToken.None);
    }

    private static SearchEntryResult Match(string type, string id) =>
        new(type, id, "1", DateTimeOffset.UnixEpoch, Encoding.UTF8.GetBytes($$"""{"resourceType":"{{type}}","id":"{{id}}"}"""));

    private static SearchEntryResult Include(string type, string id, string? json = null) =>
        new(type, id, "1", DateTimeOffset.UnixEpoch,
            Encoding.UTF8.GetBytes(json ?? $$"""{"resourceType":"{{type}}","id":"{{id}}"}"""))
        {
            SearchMode = SearchEntryMode.Include,
        };

    private static SearchEntryResult Probe(string? continuation) =>
        Match("Patient", "probe") with { IsPagingProbe = true, ResourceBytes = ReadOnlyMemory<byte>.Empty, ContinuationToken = continuation };

    private static string Describe(ResourceKey key)
    {
        key.TenantId.ShouldBe(TenantId);
        return $"{key.ResourceType}/{key.Id}";
    }

    private static async IAsyncEnumerable<SearchEntryResult> Stream(IEnumerable<SearchEntryResult> results)
    {
        await Task.CompletedTask;
        foreach (var result in results)
        {
            yield return result;
        }
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public void Dispose()
    {
        _builders.Dispose();
        _versions.Dispose();
    }
}
