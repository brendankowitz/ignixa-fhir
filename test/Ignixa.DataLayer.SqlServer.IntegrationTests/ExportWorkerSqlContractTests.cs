using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Autofac;
using DurableTask.Core;
using DurableTask.Core.History;
using Ignixa.Abstractions;
using Ignixa.Api.Endpoints;
using Ignixa.Api.Infrastructure;
using Ignixa.Api.Middleware;
using Ignixa.Application.BackgroundOperations.Export;
using Ignixa.Application.BackgroundOperations.Export.Activities;
using Ignixa.Application.BackgroundOperations.Export.Models;
using Ignixa.Application.BackgroundOperations.Export.Orchestrations;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.DataLayer.BlobStorage;
using Ignixa.DataLayer.BlobStorage.Features.BackgroundJobs;
using Ignixa.DataLayer.BlobStorage.Infrastructure;
using Ignixa.DataLayer.FileSystem.DurableTask;
using Ignixa.DataLayer.SqlServer.Compression;
using Ignixa.DataLayer.SqlServer.Indexing;
using Ignixa.DataLayer.SqlServer.IntegrationTests.Fixtures;
using Ignixa.DataLayer.SqlServer.Search;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Search.Sql.Ast;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Medino;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Data.SqlClient;
using Microsoft.IO;
using Shouldly;

namespace Ignixa.DataLayer.SqlServer.IntegrationTests;

#pragma warning disable CA1001
public sealed class ExportWorkerSqlContractTests : IAsyncLifetime
#pragma warning restore CA1001
{
    private readonly string _directory = Path.GetFullPath(Path.Combine("export-contract-blobs", Guid.NewGuid().ToString("N")));
    private TestTenantDatabase _database = null!;
    private SqlServerSearchIndexReferenceDataCache _cache = null!;
    private FhirVersionContext _versions = null!;
    private ServiceProvider _services = null!;
    private RecordingSearchService _search = null!;
    private InMemoryOrchestrationService _runtime = null!;
    private InMemoryBackgroundJobRepository<ExportJobDefinition> _jobs = null!;
    private IFhirSchemaProvider _schema = null!;
    private readonly FhirRequestContextAccessor _contextAccessor = new();
    private FhirServiceBaseUriResolver _baseUriResolver = null!;
    private FetchBoundaryExecution _execution = null!;

    public async Task InitializeAsync()
    {
        _database = await TestTenantDatabase.CreateSqlServerFhirRepositoryAsync();
        var tenants = new TenantStore();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Fhir:BaseUri"] = "https://fhir.example/" }).Build();
        _baseUriResolver = new FhirServiceBaseUriResolver(new Uri(configuration["Fhir:BaseUri"]!));
        var baseUris = new FhirRequestContextBaseUriProvider(
            _contextAccessor, _baseUriResolver, tenants, NullLogger<FhirRequestContextBaseUriProvider>.Instance);
        _versions = new FhirVersionContext(NullLoggerFactory.Instance, new SearchParameterResolutionOptions(),
            baseUris);
        _schema = _versions.GetSchemaProvider(FhirVersion.R4, 1);
        var definitions = _versions.GetSearchParameterDefinitionManager(FhirVersion.R4);
        _cache = new SqlServerSearchIndexReferenceDataCache(
            _database.SqlExecutionService, 1, NullLogger<SqlServerSearchIndexReferenceDataCache>.Instance);
        await _cache.SyncSearchParametersToDatabaseAsync(
            definitions.AllSearchParameters.Select(p => p.Url.ToString()).ToList(), definitions, CancellationToken.None);
        await _cache.PreloadResourceTypesAsync(CancellationToken.None);
        _execution = new FetchBoundaryExecution(_database.SqlExecutionService);
        _search = new RecordingSearchService(new SqlServerCompiledSearchService(
            _execution, 1, new SqlServerSymbolResolver(_cache),
            _versions.GetCompartmentDefinitionManager(FhirVersion.R4), definitions,
            new GzipResourceCompressor(new RecyclableMemoryStreamManager()), NullLogger.Instance));
        var references = new ReferenceSearchValueParser(_schema, baseUris);
        var builder = new SearchOptionsBuilder(new ExpressionParser(
            () => definitions, new SearchParameterExpressionParser(references, _schema), _schema), definitions);
        _jobs = new InMemoryBackgroundJobRepository<ExportJobDefinition>(
            tenants, NullLogger<InMemoryBackgroundJobRepository<ExportJobDefinition>>.Instance);
        _runtime = new InMemoryOrchestrationService(NullLogger<InMemoryOrchestrationService>.Instance);
        var blobs = new LocalFileBlobClient(Options.Create(new LocalFileBlobStorageOptions { RootDirectory = _directory }),
            NullLogger<LocalFileBlobClient>.Instance);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFhirVersionContext>(_versions);
        services.AddSingleton<ITenantConfigurationStore>(tenants);
        services.AddSingleton<IFhirBaseUriProvider>(baseUris);
        services.AddSingleton<IFhirRequestContextAccessor>(_contextAccessor);
        services.AddSingleton<IFhirRepositoryFactory>(new RepositoryFactory(_database.Repository));
        services.AddSingleton<ISearchServiceFactory>(new SearchFactory(_search));
        services.AddSingleton<IQueryParameterParser, QueryParameterParser>();
        services.AddSingleton<ISearchOptionsBuilder>(builder);
        services.AddSingleton<ISearchOptionsBuilderFactory, SearchOptionsBuilderFactory>();
        services.AddSingleton<IBlobStorageClient>(blobs);
        services.AddSingleton<IExportStreamWriterFactory>(new BlobStorageExportStreamWriterFactory(blobs, NullLoggerFactory.Instance));
        services.AddTransient<ViewDefinitionLoader>();
        services.AddSingleton(new TaskHubClient(_runtime));
        services.AddSingleton<IBackgroundJobRepository<ExportJobDefinition>>(_jobs);
        services.AddTransient<CreateExportJobHandler>();
        services.AddTransient<ExportWorkerActivity>();
        // Discover this new dependency so the same tests compile and execute against the archived baseline.
        var groupResolver = typeof(ExportWorkerActivity).Assembly.GetType(
            "Ignixa.Application.BackgroundOperations.Export.ExportGroupResolver");
        if (groupResolver is not null)
        {
            services.AddTransient(groupResolver);
        }
        _services = services.BuildServiceProvider();
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        _cache.Dispose();
        _versions.Dispose();
        await _database.DisposeAsync();
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public async Task GivenTenantRouting_WhenPostingGroupExportOverHttp_ThenTheResolvedTenantExportsOnlyMemberClinicalData(
        bool multipleTenants, bool qualifiedRoute, bool absoluteMembers)
    {
        await RunGroupHttpAsync(multipleTenants, qualifiedRoute,
            absoluteMembers ? "https://fhir.example/tenant/1/Patient/member" : null,
            multipleTenants && !qualifiedRoute ? HttpStatusCode.BadRequest : HttpStatusCode.Accepted);
    }

    [Theory]
    [InlineData("https://external.example/Patient/member")]
    [InlineData("https://fhir.example/tenant/2/Patient/member")]
    public async Task GivenANonlocalGroupReference_WhenInvokedOverHttpAndInBackground_ThenBothRejectIt(string reference)
    {
        await RunGroupHttpAsync(true, true, reference, HttpStatusCode.BadRequest);
        var exception = await Should.ThrowAsync<DurableTask.Core.Exceptions.TaskFailureException>(
            () => ExportAsync("Observation", group: "cohort"));
        exception.Message.ShouldContain("members must reference local");
        _contextAccessor.RequestContext.ShouldBeNull();
    }

    private async Task RunGroupHttpAsync(
        bool multipleTenants, bool qualifiedRoute, string? reference, HttpStatusCode expectedStatus)
    {
        await SeedGroupAsync();
        if (reference is not null)
        {
            await WriteAsync($$$"""{"resourceType":"Group","id":"cohort","type":"person","actual":true,"member":[{"entity":{"reference":"{{{reference}}}"}}]}""");
        }
        var mediatorBuilder = new ContainerBuilder();
        mediatorBuilder.RegisterInstance(_services.GetRequiredService<CreateExportJobHandler>())
            .As<IRequestHandler<CreateExportJobCommand, CreateExportJobResult>>().ExternallyOwned();
        using var mediatorContainer = mediatorBuilder.Build();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddSingleton<IMediator>(new Mediator(new AutofacMediatorServiceProvider(mediatorContainer)));
        builder.Services.AddSingleton<ITenantConfigurationStore>(new TenantStore(multipleTenants));
        builder.Services.AddSingleton<IFhirVersionContext>(_versions);
        builder.Services.AddSingleton<IFhirRequestContextAccessor>(_contextAccessor);
        builder.Services.AddSingleton(_baseUriResolver);
        await using var app = builder.Build();
        app.UseRouting();
        app.UseFhirExceptionHandler();
        app.UseMiddleware<TenantResolutionMiddleware>();
        app.UseMiddleware<FhirRequestContextMiddleware>();
        int? resolvedTenant = null;
        int? fhirTenant = null;
        string? routeTenant = null;
        app.Use(async (context, next) =>
        {
            resolvedTenant = context.Items["TenantId"] as int?;
            fhirTenant = context.RequestServices.GetRequiredService<IFhirRequestContextAccessor>().RequestContext?.TenantId;
            routeTenant = context.Request.RouteValues["tenantId"]?.ToString();
            await next(context);
        });
        app.MapExportEndpoints();
        await app.StartAsync(CancellationToken.None);
        try
        {
            using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
            using var request = new HttpRequestMessage(HttpMethod.Post,
                $"{(qualifiedRoute ? "/tenant/1" : "")}/Group/cohort/$export?_type=Observation");
            request.Headers.Add("Accept", "application/fhir+json");
            request.Headers.Add("Prefer", "respond-async");
            using var response = await client.SendAsync(request, CancellationToken.None);
            var body = await response.Content.ReadAsStringAsync(CancellationToken.None);
            response.StatusCode.ShouldBe(expectedStatus, body);
            if (expectedStatus == HttpStatusCode.BadRequest)
            {
                if (!qualifiedRoute)
                {
                    resolvedTenant.ShouldBeNull();
                }
                return;
            }

            response.StatusCode.ShouldBe(HttpStatusCode.Accepted, body);
            resolvedTenant.ShouldBe(1);
            fhirTenant.ShouldBe(1);
            routeTenant.ShouldBe(qualifiedRoute ? "1" : null);
            using var result = JsonDocument.Parse(body);
            var jobId = result.RootElement.GetProperty("jobId").GetString()!;
            response.Content.Headers.ContentLocation.ShouldBe(new Uri(client.BaseAddress!, $"/tenant/1/_export/{jobId}"));
            await _runtime.StartAsync();
            var work = await _runtime.LockNextTaskOrchestrationWorkItemAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
            var started = work!.NewMessages.Single().Event.ShouldBeOfType<ExecutionStartedEvent>();
            var input = JsonSerializer.Deserialize<ExportCoordinatorInput>(started.Input)!;
            input.TenantId.ShouldBe(1);
            input.GroupId.ShouldBe("cohort");
            input.ResourceTypes.ShouldBe(["Observation"]);
            _contextAccessor.RequestContext.ShouldBeNull();
            var (_, ids) = await ExportAsync(input.ResourceTypes.Single(), group: input.GroupId,
                since: input.Since, filters: input.TypeFilters, tenantId: input.TenantId, jobId: input.JobId);
            ids.ShouldBe(["Observation/member-observation"]);
            _contextAccessor.RequestContext.ShouldBeNull();
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(2005, 500, 1)]
    [InlineData(2005, 1000, 1)]
    [InlineData(2005, 1001, 1)]
    [InlineData(1001, 1001, 1)]
    [InlineData(2005, 2005, 1)]
    [InlineData(2005, 1, 1000)]
    public async Task GivenSelectedRowsDeletedBeforeFetch_WhenExporting_ThenEveryLaterUnchangedRowIsRetained(
        int rowCount, int firstDeleted, int deleteCount)
    {
        const long Start = 5_110_000_000_000_000_000;
        await _database.ExecuteNonQueryAsync($$"""
            INSERT dbo.Resource(ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RawResource)
            SELECT (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name='Patient'),
                'loss-' + CONVERT(varchar(10), n), 1, 0, {{Start}} + n, 0,
                COMPRESS(CONVERT(varchar(max), '{"resourceType":"Patient","id":"loss-' + CONVERT(varchar(10), n) + '"}'))
            FROM (SELECT TOP ({{rowCount}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers;
            """);
        _execution.TargetSurrogateId = Start + firstDeleted;
        _execution.BeforeFetch = () => _database.ExecuteNonQueryAsync($"""
            DELETE FROM dbo.Resource
            WHERE ResourceSurrogateId BETWEEN {Start + firstDeleted} AND {Start + firstDeleted + deleteCount - 1}
            """);

        var (result, ids) = await ExportAsync("Patient", range: (Start + 1, Start + rowCount));

        _execution.FaultApplied.ShouldBeTrue();
        result.ResourcesExported.ShouldBe(rowCount - deleteCount);
        ids.ShouldBe(Enumerable.Range(1, rowCount)
            .Where(id => id < firstDeleted || id >= firstDeleted + deleteCount)
            .Select(id => $"Patient/loss-{id}"), ignoreOrder: true);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(ids.Count);
        _search.Pages.ShouldAllBe(page => page.StartSurrogateId == Start + 1 &&
            page.EndSurrogateId == Start + rowCount && page.MaxItemCount == 1000 && page.ProbeExtraRow);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenTwoPatientCompartments_WhenExportingAGroup_ThenOnlyMemberResourcesAreWritten(bool mixedTypes)
    {
        await SeedGroupAsync();
        string[] types = mixedTypes ? ["Patient", "Observation", "Condition", "AllergyIntolerance", "Organization"] : ["Observation"];
        var actual = new List<string>();
        foreach (var type in types)
        {
            var (_, ids) = await ExportAsync(type, group: "cohort");
            actual.AddRange(ids);
        }

        string[] expected = mixedTypes
            ? ["Patient/member", "Observation/member-observation", "Condition/member-condition", "AllergyIntolerance/member-allergy"]
            : ["Observation/member-observation"];
        actual.ShouldBe(expected, ignoreOrder: true);
    }

    [Fact]
    public async Task GivenNestedAndRepeatedGroupMembership_WhenExporting_ThenClinicalResourcesAreUnique()
    {
        await SeedGroupAsync();
        await WriteAsync("""{"resourceType":"Group","id":"nested","type":"person","actual":true,"member":[{"entity":{"reference":"Group/cohort"}},{"entity":{"reference":"Patient/member"}},{"entity":{"reference":"Group/nested"}}]}""");
        var (_, ids) = await ExportAsync("Observation", group: "nested");
        ids.ShouldBe(["Observation/member-observation"]);
    }

    [Fact]
    public async Task GivenAnEmptyGroup_WhenExportingClinicalData_ThenTheExportIsEmpty()
    {
        await SeedGroupAsync();
        await WriteAsync("""{"resourceType":"Group","id":"empty","type":"person","actual":true}""");
        var (result, ids) = await ExportAsync("Observation", group: "empty");
        result.ResourcesExported.ShouldBe(0);
        ids.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("missing", null)]
    [InlineData("not-actual", """{"resourceType":"Group","id":"not-actual","type":"person","actual":false}""")]
    public async Task GivenAnInvalidGroup_WhenStartingOrExecutingAnExport_ThenItCannotReturnGlobalData(string group, string? json)
    {
        await SeedGroupAsync();
        if (json is not null)
        {
            await WriteAsync(json);
        }

        await Should.ThrowAsync<Exception>(() => ExportAsync("Observation", group: group));
        await Should.ThrowAsync<Exception>(() => CreateJobAsync(["Observation"], group));
    }

    [Fact]
    public async Task GivenOldNewAndBoundaryResources_WhenSinceAndGroupAndTypeFiltersIntersect_ThenTheExactInclusiveSetIsWritten()
    {
        await SeedGroupAsync();
        await WriteAsync("""{"resourceType":"Observation","id":"old","status":"final","subject":{"reference":"Patient/member"}}""");
        await WriteAsync("""{"resourceType":"Observation","id":"boundary","status":"final","subject":{"reference":"Patient/member"}}""");
        await WriteAsync("""{"resourceType":"Observation","id":"new","status":"final","subject":{"reference":"Patient/member"}}""");
        await WriteAsync("""{"resourceType":"Observation","id":"filtered","status":"preliminary","subject":{"reference":"Patient/member"}}""");
        var cutoff = new DateTimeOffset(2024, 2, 1, 0, 0, 0, TimeSpan.Zero);
        var floor = cutoff.UtcTicks << 3;
        // Re-key resources AND reference/token indexes to make the actual SQL timestamps deterministic.
        await _database.ExecuteNonQueryAsync($"""
            DECLARE @timestamps TABLE (OldId bigint, NewId bigint);
            INSERT @timestamps
            SELECT ResourceSurrogateId, {floor} + CASE ResourceId
                WHEN 'old' THEN -80000 WHEN 'boundary' THEN 7 ELSE 80000 + Version END
            FROM dbo.Resource WHERE ResourceTypeId = (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name = 'Observation')
                AND ResourceId IN ('old','boundary','new');
            UPDATE p SET ResourceSurrogateId = t.NewId FROM dbo.ReferenceSearchParam p JOIN @timestamps t ON p.ResourceSurrogateId=t.OldId;
            UPDATE p SET ResourceSurrogateId = t.NewId FROM dbo.TokenSearchParam p JOIN @timestamps t ON p.ResourceSurrogateId=t.OldId;
            UPDATE r SET ResourceSurrogateId = t.NewId,
                RawResource = COMPRESS(CONVERT(varchar(max), JSON_MODIFY(CONVERT(varchar(max), DECOMPRESS(r.RawResource)),
                    '$.meta.lastUpdated', CASE r.ResourceId
                        WHEN 'old' THEN '2024-01-31T23:59:59.999Z'
                        WHEN 'boundary' THEN '2024-02-01T00:00:00.000Z'
                        ELSE '2024-02-01T00:00:00.001Z' END)))
            FROM dbo.Resource r JOIN @timestamps t ON r.ResourceSurrogateId=t.OldId;
            """);
        var old = (await _database.Repository.GetAsync(new ResourceKey("Observation", "old")))!;
        JsonSourceNodeFactory.Parse<ResourceJsonNode>(old.ResourceBytes).Meta.LastUpdatedOffset
            .ShouldBe(cutoff.AddMilliseconds(-1));
        var boundary = (await _database.Repository.GetAsync(new ResourceKey("Observation", "boundary")))!;
        JsonSourceNodeFactory.Parse<ResourceJsonNode>(boundary.ResourceBytes).Meta.LastUpdatedOffset.ShouldBe(cutoff);
        var filters = new Dictionary<string, string> { ["Observation"] = "status=final&_id=old,boundary,new,filtered,nonmember-observation" };

        var (_, ids) = await ExportAsync("Observation", group: "cohort", since: cutoff, filters: filters);

        ids.ShouldBe(["Observation/boundary", "Observation/new"], ignoreOrder: true);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GivenNewJobTypeSelection_WhenPersistingInput_ThenTheCompleteApplicableTypesOrExplicitSubsetAreSnapshotted(bool explicitTypes)
    {
        await WriteAsync("""{"resourceType":"AllergyIntolerance","id":"allergy","patient":{"reference":"Patient/member"}}""");
        await WriteAsync("""{"resourceType":"Organization","id":"organization","name":"Export me"}""");
        var result = await CreateJobAsync(explicitTypes ? ["AllergyIntolerance"] : []);
        var job = (await _jobs.GetAsync(result.JobId, 1, CancellationToken.None))!;
        await _runtime.StartAsync();
        var work = await _runtime.LockNextTaskOrchestrationWorkItemAsync(TimeSpan.FromSeconds(1), CancellationToken.None);
        var started = work!.NewMessages.Single().Event.ShouldBeOfType<ExecutionStartedEvent>();
        var input = JsonSerializer.Deserialize<ExportCoordinatorInput>(started.Input)!;
        var expected = explicitTypes ? ["AllergyIntolerance"] : _schema.ResourceTypeNames
            .Where(type => _schema.GetTypeDefinition(type) is { Info.IsAbstract: false }).Order(StringComparer.Ordinal).ToArray();
        input.ResourceTypes.ShouldBe(expected);
        job.Definition.ResourceTypes.ShouldBe(expected);
        var exported = new List<string>();
        foreach (var type in input.ResourceTypes)
        {
            var (_, ids) = await ExportAsync(type);
            exported.AddRange(ids);
        }
        exported.ShouldBe(explicitTypes ? ["AllergyIntolerance/allergy"] :
            ["AllergyIntolerance/allergy", "Organization/organization"], ignoreOrder: true);
    }

    [Fact]
    public async Task GivenAnOlderOutlierAndMoreThan50000RowsInOnePartition_WhenExportingAllRanges_ThenCountsAndUniqueOutputAreExhaustive()
    {
        const int DenseCount = 50_123;
        const long OldId = 5_100_000_000_000_000_000;
        const long DenseStart = 5_110_000_000_000_000_000;
        await _database.ExecuteNonQueryAsync($$"""
            INSERT dbo.Resource(ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RawResource)
            SELECT (SELECT ResourceTypeId FROM dbo.ResourceType WHERE Name='Patient'),
                'dense-' + CONVERT(varchar(10), n), 1, 0, {{DenseStart}} + n, 0,
                COMPRESS(CONVERT(varchar(max), '{"resourceType":"Patient","id":"dense-' + CONVERT(varchar(10), n) + '"}'))
            FROM (SELECT TOP ({{DenseCount}}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) n
                FROM sys.all_objects a CROSS JOIN sys.all_objects b) numbers;
            INSERT dbo.Resource(ResourceTypeId,ResourceId,Version,IsHistory,ResourceSurrogateId,IsDeleted,RawResource)
            SELECT ResourceTypeId,'outlier',1,0,{{OldId}},0,COMPRESS(CONVERT(varchar(max),'{"resourceType":"Patient","id":"outlier"}'))
            FROM dbo.ResourceType WHERE Name='Patient';
            """);
        var ranges = await _search.GetExportRangesAsync("Patient", 6, CancellationToken.None);
        ranges.Count.ShouldBe(6);
        var denseRange = ranges.Single(r => r.StartId <= DenseStart + 1 && r.EndId >= DenseStart + DenseCount);
        var measured = await _database.ExecuteScalarAsync<int>($"""
            SELECT COUNT(*) FROM dbo.Resource
            WHERE ResourceSurrogateId BETWEEN {denseRange.StartId} AND {denseRange.EndId}
            """);
        measured.ShouldBe(DenseCount);
        var ids = new List<string>();
        long total = 0;
        foreach (var range in ranges)
        {
            _search.Pages.Clear();
            var (result, partitionIds) = await ExportAsync("Patient", range: range);
            result.StartSurrogateId.ShouldBe(range.StartId);
            result.EndSurrogateId.ShouldBe(range.EndId);
            total += result.ResourcesExported;
            ids.AddRange(partitionIds);
            if (range == denseRange)
            {
                result.ResourcesExported.ShouldBe(DenseCount);
                _search.Pages.Count.ShouldBeGreaterThan(50);
                _search.Pages.ShouldAllBe(page => page.MaxItemCount <= 1000);
                _search.Pages.Skip(1).Select(page =>
                {
                    KeysetContinuationToken.TryDecode(page.ContinuationToken, out var position).ShouldBeTrue();
                    return position!.BoundarySurrogateId;
                }).ShouldBe(Enumerable.Range(1, DenseCount / 1000).Select(page => DenseStart + page * 1000));
            }
            _search.Pages.ShouldAllBe(page => page.StartSurrogateId == range.StartId &&
                page.EndSurrogateId == range.EndId && page.ProbeExtraRow);
        }
        total.ShouldBe(DenseCount + 1);
        ids.Count.ShouldBe(DenseCount + 1);
        ids.Distinct(StringComparer.Ordinal).Count().ShouldBe(DenseCount + 1);
        ids.ToHashSet(StringComparer.Ordinal).SetEquals(
            Enumerable.Range(1, DenseCount).Select(i => $"Patient/dense-{i}").Append("Patient/outlier")).ShouldBeTrue();
    }

    private Task<CreateExportJobResult> CreateJobAsync(string[] types, string? group = null) =>
        _services.GetRequiredService<CreateExportJobHandler>().HandleAsync(new CreateExportJobCommand
        {
            TenantId = 1, ResourceTypes = types, TypeFilters = new Dictionary<string, string>(), GroupId = group
        }, CancellationToken.None);

    private async Task<(ExportWorkerOutput Result, List<string> Ids)> ExportAsync(
        string type, string? group = null, DateTimeOffset? since = null,
        IReadOnlyDictionary<string, string>? filters = null, (long StartId, long EndId)? range = null,
        int tenantId = 1, string? jobId = null)
    {
        var path = $"{Guid.NewGuid():N}.ndjson";
        var input = new ExportWorkerInput(jobId ?? "test", tenantId, type, range?.StartId ?? 0, range?.EndId ?? long.MaxValue,
            path, since, filters, GroupId: group);
        var json = await _services.GetRequiredService<ExportWorkerActivity>().RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "test" }), JsonSerializer.Serialize(new[] { input }));
        var result = JsonSerializer.Deserialize<ExportWorkerOutput>(json)!;
        var ids = new List<string>();
        if (File.Exists(Path.Combine(_directory, path)))
        {
            foreach (var line in await File.ReadAllLinesAsync(Path.Combine(_directory, path)))
            {
                using var resource = JsonDocument.Parse(line);
                ids.Add($"{resource.RootElement.GetProperty("resourceType").GetString()}/{resource.RootElement.GetProperty("id").GetString()}");
            }
        }
        result.ResourcesExported.ShouldBe(ids.Count);
        return (result, ids);
    }

    private async Task SeedGroupAsync()
    {
        await WriteAsync("""{"resourceType":"Patient","id":"member"}""");
        await WriteAsync("""{"resourceType":"Patient","id":"nonmember"}""");
        await WriteAsync("""{"resourceType":"Group","id":"cohort","type":"person","actual":true,"member":[{"entity":{"reference":"Patient/member"}}]}""");
        foreach (var (type, suffix, reference) in new[]
        {
            ("Observation", "observation", "subject"), ("Condition", "condition", "subject"),
            ("AllergyIntolerance", "allergy", "patient")
        })
        {
            foreach (var patient in new[] { "member", "nonmember" })
            {
                await WriteAsync($$$"""{"resourceType":"{{{type}}}","id":"{{{patient}}}-{{{suffix}}}","status":"final","{{{reference}}}":{"reference":"Patient/{{{patient}}}"}}""");
            }
        }
        await WriteAsync("""{"resourceType":"Organization","id":"unrelated"}""");
    }

    private async Task WriteAsync(string json)
    {
        var node = ResourceJsonNode.Parse(json);
        var indexes = _versions.GetSearchIndexer(FhirVersion.R4).Extract(node.ToElement(_schema)).Cast<object>().ToList();
        await _database.Repository.CreateOrUpdateAsync(new ResourceWrapper(node.ResourceType, node.Id!, "1",
            DateTimeOffset.UtcNow, node, new ResourceRequest("PUT", $"{node.ResourceType}/{node.Id}"))
        {
            SearchIndices = indexes
        }, CancellationToken.None);
    }

    private sealed class RepositoryFactory(IFhirRepository repository) : IFhirRepositoryFactory
    {
        public Task<IFhirRepository> GetRepositoryAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(repository);
    }

    private sealed class SearchFactory(ISearchService service) : ISearchServiceFactory
    {
        public Task<ISearchService> GetSearchServiceAsync(int tenantId, CancellationToken ct = default) => Task.FromResult(service);
    }

    private sealed class TenantStore(bool multipleTenants = false) : ITenantConfigurationStore
    {
        private readonly TenantConfiguration _tenant = new() { TenantId = 1, DisplayName = "Export contract", FhirVersion = "4.0" };
        private readonly TenantConfiguration _otherTenant = new() { TenantId = 2, DisplayName = "Other tenant", FhirVersion = "4.0" };
        public TenantMode Mode => TenantMode.Isolated;
        public ValueTask<TenantConfiguration?> GetTenantConfigurationAsync(int tenantId, CancellationToken ct = default) =>
            new(tenantId == 1 ? _tenant : multipleTenants && tenantId == 2 ? _otherTenant : null);
        public ValueTask<IReadOnlyList<TenantConfiguration>> GetAllTenantsAsync(CancellationToken ct = default) =>
            new(multipleTenants ? new[] { _tenant, _otherTenant } : new[] { _tenant });
        public ValueTask<TenantConfiguration?> ResolveByHostAsync(string host, CancellationToken cancellationToken = default) => new((TenantConfiguration?)null);
    }

    private sealed class RecordingSearchService(ISearchService inner) : ISearchService
    {
        public List<SearchOptions> Pages { get; } = [];
        public async IAsyncEnumerable<SearchEntryResult> SearchStreamAsync<TSearchOptions>(
            TSearchOptions searchOptions, [EnumeratorCancellation] CancellationToken cancellationToken = default)
            where TSearchOptions : class
        {
            if (searchOptions is not SearchOptions options)
            {
                throw new ArgumentException("Expected SearchOptions.", nameof(searchOptions));
            }
            Pages.Add(new SearchOptions(options));
            await foreach (var entry in inner.SearchStreamAsync(searchOptions, cancellationToken))
            {
                yield return entry;
            }
        }
        public ValueTask<int> CountAsync<TSearchOptions>(TSearchOptions options, CancellationToken cancellationToken = default)
            where TSearchOptions : class => inner.CountAsync(options, cancellationToken);
        public Task<IReadOnlyList<(long StartId, long EndId)>> GetExportRangesAsync(
            string resourceType, int numberOfRanges, CancellationToken cancellationToken = default) =>
            inner.GetExportRangesAsync(resourceType, numberOfRanges, cancellationToken);
    }

    private sealed class FetchBoundaryExecution(ISqlExecutionService inner) : ISqlExecutionService
    {
        public long? TargetSurrogateId { get; set; }
        public Func<Task>? BeforeFetch { get; set; }
        public bool FaultApplied { get; private set; }

        public async Task<IReadOnlyList<TResult>> ExecuteReaderAsync<TResult>(
            int tenantId, SqlCommand command, Func<SqlDataReader, TResult> readRow,
            CancellationToken cancellationToken, SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent)
        {
            if (BeforeFetch is { } fault && command.CommandText.Contains("FROM (VALUES", StringComparison.Ordinal) &&
                command.Parameters.Cast<SqlParameter>().Any(parameter =>
                    parameter.ParameterName.StartsWith("@Sid", StringComparison.Ordinal) &&
                    parameter.Value is long id && id == TargetSurrogateId))
            {
                BeforeFetch = null;
                await fault();
                FaultApplied = true;
            }
            return await inner.ExecuteReaderAsync(tenantId, command, readRow, cancellationToken, idempotency);
        }

        public Task<int> ExecuteNonQueryAsync(int tenantId, SqlCommand command, CancellationToken cancellationToken,
            SqlCommandIdempotency idempotency = SqlCommandIdempotency.Idempotent) =>
            inner.ExecuteNonQueryAsync(tenantId, command, cancellationToken, idempotency);
        public Task<TResult> ExecuteInTransactionAsync<TResult>(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task<TResult>> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
        public Task ExecuteInTransactionAsync(int tenantId,
            Func<ISqlTransactionContext, CancellationToken, Task> work, CancellationToken cancellationToken) =>
            inner.ExecuteInTransactionAsync(tenantId, work, cancellationToken);
    }
}
