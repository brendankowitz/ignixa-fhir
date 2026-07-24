// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.BackgroundOperations;

/// <summary>
/// ImportBatchActivity indexes resources with no HTTP request. Before this activity established an
/// ambient <see cref="IFhirRequestContext"/> for its own duration, <see cref="FhirRequestContextBaseUriProvider"/>
/// found no context, fell back to a null tenant, and only recognized the deployment root. An absolute
/// self-reference in tenant-scoped form (e.g. <c>https://host/tenant/1/Patient/p1</c>) then indexed as
/// External here while the request path that first wrote the same reference collapsed it to Internal, so
/// a re-imported row silently dropped out of <c>?subject=Patient/p1</c> searches. This test runs the
/// actual activity (not a hand-constructed context) and asserts it reaches the same classification the
/// request path reaches for the same tenant and the same reference.
/// </summary>
public class ImportBatchActivityBaseUriWiringTests
{
    private const int TenantId = 1;
    private const string ObservationId = "obs1";

    private static readonly Uri ServiceRoot = new("https://fhir.example.org/");

    private static string ObservationJson => $$"""
        {
          "resourceType": "Observation",
          "id": "{{ObservationId}}",
          "status": "final",
          "code": { "coding": [ { "system": "http://loinc.org", "code": "1234-5" } ] },
          "subject": { "reference": "https://fhir.example.org/tenant/1/Patient/p1" }
        }
        """;

    [Fact]
    public async Task GivenAnImportBatchActivity_WhenIndexingAnAbsoluteSelfReference_ThenItAgreesWithTheRequestPath()
    {
        var accessor = new FhirRequestContextAccessor();
        var resolver = new FhirServiceBaseUriResolver(ServiceRoot);
        var baseUriProvider = new FhirRequestContextBaseUriProvider(accessor, resolver, Substitute.For<ITenantConfigurationStore>());
        var versionContext = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            baseUriProvider);

        // Baseline: what FhirRequestContextMiddleware establishes for a request routed via /tenant/1/.
        accessor.RequestContext = new FhirRequestContext
        {
            TenantId = TenantId,
            ServiceBaseUris = resolver.Resolve(requestOrigin: null, TenantId, FhirServiceBaseUriForm.TenantScoped)
        };
        var expectedReference = ExtractSubjectReference(versionContext);
        accessor.RequestContext = null;

        // Actual: the background activity establishing its own context, not one left behind by the test.
        var actualReference = await ExtractSubjectReferenceViaActivityAsync(versionContext, accessor);

        actualReference.Kind.ShouldBe(expectedReference.Kind);
        actualReference.BaseUri.ShouldBe(expectedReference.BaseUri);
        actualReference.Kind.ShouldBe(ReferenceKind.Internal);
    }

    [Fact]
    public async Task GivenAnImportBatchActivity_WhenItCompletes_ThenThePreviousAmbientContextIsRestored()
    {
        var accessor = new FhirRequestContextAccessor();
        var resolver = new FhirServiceBaseUriResolver(ServiceRoot);
        var baseUriProvider = new FhirRequestContextBaseUriProvider(accessor, resolver, Substitute.For<ITenantConfigurationStore>());
        var versionContext = new FhirVersionContext(
            NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(),
            baseUriProvider);

        var callerContext = new FhirRequestContext { TenantId = 42 };
        accessor.RequestContext = callerContext;

        await ExtractSubjectReferenceViaActivityAsync(versionContext, accessor);

        // A pooled thread must not carry this activity's context into whatever runs on it next.
        accessor.RequestContext.ShouldBeSameAs(callerContext);
    }

    private static ReferenceSearchValue ExtractSubjectReference(IFhirVersionContext versionContext)
    {
        var schemaProvider = versionContext.GetSchemaProvider(FhirVersion.R4, TenantId);
        var searchIndexer = versionContext.GetSearchIndexer(FhirVersion.R4, TenantId);

        var element = JsonSourceNodeFactory.Parse(ObservationJson).ToElement(schemaProvider);
        var entries = searchIndexer.Extract((IElement)element);

        return GetSubjectReference(entries);
    }

    private static async Task<ReferenceSearchValue> ExtractSubjectReferenceViaActivityAsync(
        IFhirVersionContext versionContext,
        IFhirRequestContextAccessor accessor)
    {
        IReadOnlyList<object>? capturedSearchIndexes = null;

        var tenantConfigurationStore = Substitute.For<ITenantConfigurationStore>();
        tenantConfigurationStore
            .GetTenantConfigurationAsync(TenantId, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TenantConfiguration?>(new TenantConfiguration
            {
                TenantId = TenantId,
                DisplayName = "Test Tenant",
                FhirVersion = "4.0"
            }));

        var repository = Substitute.For<IFhirRepository>();
        repository.GetNextTransactionIdAsync(Arg.Any<CancellationToken>())
            .Returns(new ValueTask<TransactionId>(new TransactionId(1)));
        repository
            .BatchWriteAsync(
                Arg.Any<TransactionId>(),
                Arg.Do<IReadOnlyList<(string resourceType, string resourceId, ResourceJsonNode resource, IReadOnlyList<object> searchIndexes, string httpMethod, int entryIndex)>>(
                    operations => capturedSearchIndexes = operations[0].searchIndexes),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<ResourceKey>>([new ResourceKey("Observation", ObservationId)]));

        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(TenantId, Arg.Any<CancellationToken>()).Returns(repository);

        var activity = new TestableImportBatchActivity(
            repositoryFactory,
            versionContext,
            tenantConfigurationStore,
            accessor,
            NullLogger<ImportBatchActivity>.Instance);

        var input = new ImportBatchInput
        {
            JobId = "job-1",
            TenantId = TenantId,
            ResourceType = "Observation",
            Resources = [ObservationJson],
            Mode = "IncrementalLoad"
        };

        var taskContext = new TaskContext(new OrchestrationInstance { InstanceId = "test-instance" });

        var output = await activity.RunExecuteAsync(taskContext, input);

        output.ErrorCount.ShouldBe(0);
        capturedSearchIndexes.ShouldNotBeNull();

        return GetSubjectReference(capturedSearchIndexes!.Cast<SearchIndexEntry>());
    }

    private static ReferenceSearchValue GetSubjectReference(IEnumerable<SearchIndexEntry> entries) =>
        (ReferenceSearchValue)entries.Single(entry => entry.SearchParameter.Name == "subject").Value;

    /// <summary>
    /// DurableTask's <see cref="AsyncTaskActivity{TInput,TResult}.ExecuteAsync"/> is protected. This
    /// subclass exposes it so the test invokes the real production method rather than a hand-constructed
    /// substitute for it.
    /// </summary>
    private sealed class TestableImportBatchActivity(
        IFhirRepositoryFactory repositoryFactory,
        IFhirVersionContext fhirVersionContext,
        ITenantConfigurationStore tenantConfigurationStore,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILogger<ImportBatchActivity> logger)
        : ImportBatchActivity(repositoryFactory, fhirVersionContext, tenantConfigurationStore, fhirContextAccessor, logger)
    {
        public Task<ImportBatchOutput> RunExecuteAsync(TaskContext context, ImportBatchInput input) =>
            ExecuteAsync(context, input);
    }
}
