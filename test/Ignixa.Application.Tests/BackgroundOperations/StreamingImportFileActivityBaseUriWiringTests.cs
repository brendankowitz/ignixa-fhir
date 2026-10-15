// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.BackgroundOperations;

/// <summary>
/// Bulk import indexes resources with no HTTP request. Unless the activity establishes an ambient
/// <see cref="IFhirRequestContext"/> for its own duration, <see cref="FhirRequestContextBaseUriProvider"/>
/// finds no context, falls back to a null tenant, and only recognizes the deployment root. An absolute
/// self-reference in tenant-scoped form (e.g. <c>https://host/tenant/1/Patient/p1</c>) then indexes as
/// External here while the request path that first wrote the same reference collapses it to Internal, so
/// a re-imported row silently drops out of <c>?subject=Patient/p1</c> searches. This test runs the
/// actual activity and asserts it reaches the same classification the request path reaches for the same
/// tenant and the same reference.
/// </summary>
public class StreamingImportFileActivityBaseUriWiringTests
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
    public async Task GivenAStreamingImport_WhenIndexingAnAbsoluteSelfReference_ThenItAgreesWithTheRequestPath()
    {
        var accessor = new FhirRequestContextAccessor();
        var resolver = new FhirServiceBaseUriResolver(ServiceRoot);
        var baseUriProvider = new FhirRequestContextBaseUriProvider(accessor, resolver, Substitute.For<ITenantConfigurationStore>());
        using var versionContext = new FhirVersionContext(
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

        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes(JsonNode.Parse(ObservationJson)!.ToJsonString() + "\n")));

        var activity = new StreamingImportFileActivity(
            repositoryFactory,
            versionContext,
            tenantConfigurationStore,
            blobs,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" }).Build(),
            accessor,
            NullLogger<StreamingImportFileActivity>.Instance);

        var input = new StreamingImportFileInput
        {
            JobId = "job-1",
            TenantId = TenantId,
            FileUrl = "input.ndjson",
            ResourceType = "Observation",
            Mode = "IncrementalLoad",
            BatchSize = 1,
            ChannelCapacity = 1
        };

        var json = await activity.RunAsync(
            new TaskContext(new OrchestrationInstance { InstanceId = "test-instance" }),
            JsonSerializer.Serialize(new[] { input }));

        JsonSerializer.Deserialize<StreamingImportFileOutput>(json)!.ErrorCount.ShouldBe(0);
        capturedSearchIndexes.ShouldNotBeNull();

        return GetSubjectReference(capturedSearchIndexes!.Cast<SearchIndexEntry>());
    }

    private static ReferenceSearchValue GetSubjectReference(IEnumerable<SearchIndexEntry> entries) =>
        (ReferenceSearchValue)entries.Single(entry => entry.SearchParameter.Name == "subject").Value;
}
