// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Features.Bundle.Serialization;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class BundleProcessorTests
{
    [Fact]
    public async Task GivenMissingBatchWriteResult_WhenCheckingCreationStatus_ThenFailsFast()
    {
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        repositoryFactory.GetRepositoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Substitute.For<IFhirRepository>()));
        var requestContextAccessor = new FhirRequestContextAccessor
        {
            RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1)
        };
        var coordinator = await DeferredWriteCoordinator.CreateAsync(
            channelCapacity: 1,
            repositoryFactory,
            Substitute.For<IPartitionStrategy>(),
            requestContextAccessor,
            NullLogger<DeferredWriteCoordinator>.Instance);

        var exception = Should.Throw<InvalidOperationException>(() => coordinator.IsCreated(42));

        exception.Message.ShouldBe("No write result recorded for entry 42.");
    }

    [Fact]
    public async Task GivenTransactionAboveEntryLimit_WhenProcessed_ThenRequestTooCostlyAndNoEntryExecutes()
    {
        var harness = new ProcessorHarness();
        var entriesPulled = 0;
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Transaction,
            MaxTransactionEntries = 3
        };

        var exception = await Should.ThrowAsync<RequestTooCostlyException>(() =>
            harness.Processor.ProcessAsync(
                CountedEntries(
                    [
                        CreateEntry(0, "GET"),
                        CreateEntry(1, "DELETE"),
                        CreateEntry(2, "GET"),
                        CreateEntry(3, "GET")
                    ],
                    () => Interlocked.Increment(ref entriesPulled)),
                options,
                CancellationToken.None));

        exception.Message.ShouldContain("3");
        exception.Message.ShouldContain("Bundle:MaxTransactionEntries");
        Volatile.Read(ref entriesPulled).ShouldBeLessThanOrEqualTo(4);
        harness.ExecutedEntries.ShouldBe(0);
        await harness.RepositoryFactory.DidNotReceiveWithAnyArgs().GetRepositoryAsync(default);
    }

    [Fact]
    public async Task GivenTransactionAtEntryLimit_WhenProcessed_ThenAllEntriesExecute()
    {
        var harness = new ProcessorHarness();
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Transaction,
            MaxTransactionEntries = 3
        };

        var result = await harness.Processor.ProcessAsync(
            CountedEntries([CreateEntry(0), CreateEntry(1), CreateEntry(2)], static () => { }),
            options,
            CancellationToken.None);

        result.Entry.Count.ShouldBe(3);
        harness.ExecutedEntries.ShouldBe(3);
    }

    [Fact]
    public async Task GivenBatchAboveTransactionEntryLimit_WhenProcessed_ThenEveryEntryReceivesAResponse()
    {
        var harness = new ProcessorHarness();
        var options = new BundleProcessingOptions
        {
            Type = BundleType.Batch,
            MaxTransactionEntries = 3
        };

        var result = await harness.Processor.ProcessAsync(
            CountedEntries([CreateEntry(0), CreateEntry(1), CreateEntry(2), CreateEntry(3)], static () => { }),
            options,
            CancellationToken.None);

        result.Entry.Count.ShouldBe(4);
        harness.ExecutedEntries.ShouldBe(4);
    }

    [Fact]
    public async Task GivenTransactionWithTokenAboveParserCeiling_WhenProcessed_ThenNoEntryIsExecuted()
    {
        var parser = new StreamingBundleParser(NullLogger<StreamingBundleParser>.Instance, maxTokenBytes: 8_192);
        var harness = new ProcessorHarness();
        var context = await parser.ParseStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "resourceType": "Bundle",
              "type": "transaction",
              "entry": [
                { "request": { "method": "GET", "url": "Patient/0" } },
                { "request": { "method": "GET", "url": "Patient/1" } },
                {
                  "resource": { "resourceType": "Binary", "data": "
            """ + new string('A', 9_000) + """
            " },
                  "request": { "method": "PUT", "url": "Binary/2" }
                }
              ]
            }
            """)));

        await Should.ThrowAsync<RequestNotValidException>(() => harness.Processor.ProcessAsync(
            context.Entries,
            new BundleProcessingOptions { Type = BundleType.Transaction },
            CancellationToken.None));

        harness.ExecutedEntries.ShouldBe(0);
    }

    [Fact]
    public async Task GivenTransactionWithMalformedTrailingContent_WhenProcessed_ThenNoEntryIsExecuted()
    {
        var parser = new StreamingBundleParser(NullLogger<StreamingBundleParser>.Instance);
        var harness = new ProcessorHarness();
        var context = await parser.ParseStreamAsync(new MemoryStream(Encoding.UTF8.GetBytes(
            """
            {
              "resourceType": "Bundle",
              "type": "transaction",
              "entry": [{ "request": { "method": "GET", "url": "Patient/0" } }]
            }
            trailing
            """)));

        await Should.ThrowAsync<JsonException>(() => harness.Processor.ProcessAsync(
            context.Entries,
            new BundleProcessingOptions { Type = BundleType.Transaction },
            CancellationToken.None));

        harness.ExecutedEntries.ShouldBe(0);
    }

    [Fact]
    public async Task GivenTransactionEnumerationThrowsBodyLimitException_WhenProcessed_ThenNoEntryIsExecuted()
    {
        var harness = new ProcessorHarness();
        var bodyLimitException = new BadHttpRequestException(
            "Request body too large.",
            StatusCodes.Status413PayloadTooLarge);

        var act = () => harness.Processor.ProcessAsync(
            ThrowAfterFirstEntry(bodyLimitException),
            new BundleProcessingOptions { Type = BundleType.Transaction },
            CancellationToken.None);

        (await act.ShouldThrowAsync<BadHttpRequestException>()).ShouldBeSameAs(bodyLimitException);
        harness.ExecutedEntries.ShouldBe(0);
    }

    private static BundleEntryContext CreateEntry(int index, string method = "GET") =>
        new()
        {
            Index = index,
            HttpVerb = method,
            ResourceType = "Patient",
            ResourceId = index.ToString(),
            RequestUrl = $"Patient/{index}",
            Resource = null,
            FullUrl = null
        };

    private static async IAsyncEnumerable<BundleEntryContext> CountedEntries(
        IEnumerable<BundleEntryContext> entries,
        Action entryPulled,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            entryPulled();
            yield return entry;
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<BundleEntryContext> ThrowAfterFirstEntry(
        Exception exception,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield return CreateEntry(0);
        await Task.Yield();
        throw exception;
    }

    private sealed class ProcessorHarness
    {
        private int _executedEntries;

        public ProcessorHarness()
        {
            RepositoryFactory = Substitute.For<IFhirRepositoryFactory>();
            RepositoryFactory.GetRepositoryAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(Substitute.For<IFhirRepository>()));

            var pipelineExecutor = Substitute.For<IPipelineExecutor>();
            pipelineExecutor.ExecuteAsync(Arg.Any<HttpContext>())
                .Returns(callInfo =>
                {
                    Interlocked.Increment(ref _executedEntries);
                    var context = callInfo.Arg<HttpContext>();
                    return context.Response.Body.WriteAsync(
                        Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"test"}"""),
                        context.RequestAborted).AsTask();
                });

            var requestContextAccessor = new FhirRequestContextAccessor
            {
                RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1)
            };
            var httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = new DefaultHttpContext()
            };
            var entryExecutor = new BundleEntryExecutor(
                pipelineExecutor,
                httpContextAccessor,
                requestContextAccessor,
                new RecyclableMemoryStreamManager(),
                NullLogger<BundleEntryExecutor>.Instance);
            var channelExecutor = new BundleChannelExecutor(
                entryExecutor,
                NullLogger<BundleChannelExecutor>.Instance);
            var versionContext = Substitute.For<IFhirVersionContext>();
            versionContext.GetSchemaProvider(Arg.Any<FhirVersion>(), Arg.Any<int?>())
                .Returns(FhirVersion.R4.GetSchemaProvider());

            Processor = new BundleProcessor(
                new BundleReferencePreProcessor(NullLogger<BundleReferencePreProcessor>.Instance),
                channelExecutor,
                new BundleResponseBuilder(NullLogger<BundleResponseBuilder>.Instance),
                RepositoryFactory,
                Substitute.For<IPartitionStrategy>(),
                requestContextAccessor,
                NullLoggerFactory.Instance,
                NullLogger<BundleProcessor>.Instance,
                versionContext);
        }

        public BundleProcessor Processor { get; }
        public IFhirRepositoryFactory RepositoryFactory { get; }
        public int ExecutedEntries => Volatile.Read(ref _executedEntries);
    }
}
