// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Text;
using System.Text.Json;
using DurableTask.Core;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.Import.Activities;
using Ignixa.Application.BackgroundOperations.Import.Models;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Indexing;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.BackgroundOperations;

public class BulkImportElementOrderingTests
{
    [Fact]
    public async Task GivenOrderingEnabled_WhenImportingUnorderedResource_ThenWrittenResourceIsSpecOrdered()
    {
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(
            new TenantConfiguration { TenantId = 1, DisplayName = "Bulk import test tenant", FhirVersion = "4.0" });
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(FhirVersion.R4, 1).Returns(new R4CoreSchemaProvider());
        versions.GetSearchIndexer(FhirVersion.R4, 1).Returns(Substitute.For<ISearchIndexer>());
        ResourceJsonNode? written = null;
        var repository = Substitute.For<IFhirRepository>();
        repository.GetNextTransactionIdAsync(Arg.Any<CancellationToken>()).Returns(new TransactionId(1));
        repository.BatchWriteAsync(
                Arg.Any<TransactionId>(),
                Arg.Do<IReadOnlyList<(string, string, ResourceJsonNode resource, IReadOnlyList<object>, string, int)>>(operations => written = operations[0].resource),
                Arg.Any<CancellationToken>())
            .Returns(new[] { new ResourceKey("Patient", "p1") });
        var factory = Substitute.For<IFhirRepositoryFactory>();
        factory.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(repository);
        var blobs = Substitute.For<IBlobStorageClient>();
        blobs.ReadBlobAsync("input.ndjson", Arg.Any<CancellationToken>())
            .Returns(new MemoryStream(Encoding.UTF8.GetBytes("{\"active\":true,\"id\":\"p1\",\"resourceType\":\"Patient\"}\n")));
        var activity = new StreamingImportFileActivity(factory, versions, tenants, blobs,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Import:ConsumerCount"] = "1" }).Build(),
            new FhirRequestContextAccessor(), new ResourceElementOrdering(Options.Create(new SerializationOptions { OrderElementsOnWrite = true })),
            NullLogger<StreamingImportFileActivity>.Instance);
        var input = new StreamingImportFileInput
        {
            JobId = "job", TenantId = 1, FileUrl = "input.ndjson", ResourceType = "Patient",
            Mode = "IncrementalLoad", BatchSize = 1, ChannelCapacity = 1
        };

        await activity.RunAsync(new TaskContext(new OrchestrationInstance { InstanceId = "job" }), JsonSerializer.Serialize(new[] { input }));

        written.ShouldNotBeNull();
        System.Text.Json.Nodes.JsonNode.Parse(written.SerializeToString())!.AsObject()
            .Select(property => property.Key)
            .ShouldBe(["resourceType", "id", "meta", "active"]);
    }
}
