// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using GreenDonut;
using Ignixa.Application.Features.Experimental.GraphQl.DataLoaders;
using Ignixa.Application.Features.Experimental.GraphQl.Models;
using Ignixa.Application.Features.Resource;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Medino;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class ResourceDataLoaderTests
{
    private sealed class TestableResourceDataLoader(IMediator mediator)
        : ResourceDataLoader(mediator, AutoBatchScheduler.Default, new DataLoaderOptions())
    {
        public Task<IReadOnlyDictionary<ResourceKey, JsonElement?>> LoadBatchPublicAsync(
            IReadOnlyList<ResourceKey> keys,
            CancellationToken cancellationToken)
            => LoadBatchAsync(keys, cancellationToken);
    }

    private static SearchEntryResult MakeResult(string resourceType, string id, string json, bool isDeleted = false)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        return new SearchEntryResult(resourceType, id, "1", DateTimeOffset.UtcNow, bytes)
        {
            IsDeleted = isDeleted,
        };
    }

    [Fact]
    public async Task GivenBatchOfKeys_WhenLoading_ThenReturnsAllResults()
    {
        var mediator = Substitute.For<IMediator>();
        var key1 = new ResourceKey("Patient", "123");
        var key2 = new ResourceKey("Observation", "456");

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Patient" && q.Id == "123"), Arg.Any<CancellationToken>())
            .Returns(MakeResult("Patient", "123", """{"resourceType":"Patient","id":"123"}"""));

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Observation" && q.Id == "456"), Arg.Any<CancellationToken>())
            .Returns((SearchEntryResult?)null);

        var loader = new TestableResourceDataLoader(mediator);

        var result = await loader.LoadBatchPublicAsync([key1, key2], CancellationToken.None);

        result.ShouldContainKey(key1);
        result.ShouldContainKey(key2);
        result[key1].ShouldNotBeNull();
        result[key2].ShouldBeNull();
    }

    [Fact]
    public async Task GivenDeletedResource_WhenLoading_ThenReturnsNullForKey()
    {
        var mediator = Substitute.For<IMediator>();
        var key = new ResourceKey("Patient", "deleted-99");

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Patient" && q.Id == "deleted-99"), Arg.Any<CancellationToken>())
            .Returns(MakeResult("Patient", "deleted-99", """{"resourceType":"Patient","id":"deleted-99"}""", isDeleted: true));

        var loader = new TestableResourceDataLoader(mediator);

        var result = await loader.LoadBatchPublicAsync([key], CancellationToken.None);

        result.ShouldContainKey(key);
        result[key].ShouldBeNull();
    }

    [Fact]
    public async Task GivenValidResource_WhenLoading_ThenParsesJsonElementCorrectly()
    {
        var mediator = Substitute.For<IMediator>();
        var key = new ResourceKey("Patient", "789");
        const string json = """{"resourceType":"Patient","id":"789","birthDate":"1990-01-01"}""";

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Patient" && q.Id == "789"), Arg.Any<CancellationToken>())
            .Returns(MakeResult("Patient", "789", json));

        var loader = new TestableResourceDataLoader(mediator);

        var result = await loader.LoadBatchPublicAsync([key], CancellationToken.None);

        result[key].ShouldNotBeNull();
        result[key]!.Value.GetProperty("id").GetString().ShouldBe("789");
        result[key]!.Value.GetProperty("birthDate").GetString().ShouldBe("1990-01-01");
    }

    [Fact]
    public async Task GivenFaultingKey_WhenLoading_ThenPropagatesOriginalExceptionUnwrapped()
    {
        // The loader must not wrap exceptions in InvalidOperationException; doing so
        // discards FhirException semantics and contaminates the shared batch.
        var mediator = Substitute.For<IMediator>();
        var goodKey = new ResourceKey("Patient", "good");
        var badKey = new ResourceKey("Observation", "bad");

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Patient" && q.Id == "good"), Arg.Any<CancellationToken>())
            .Returns(MakeResult("Patient", "good", """{"resourceType":"Patient","id":"good"}"""));

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Observation" && q.Id == "bad"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("corrupt resource bytes"));

        var loader = new TestableResourceDataLoader(mediator);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => loader.LoadBatchPublicAsync([goodKey, badKey], CancellationToken.None));

        // Original exception is surfaced directly, not re-wrapped.
        ex.Message.ShouldBe("corrupt resource bytes");
        ex.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task GivenFhirException_WhenLoading_ThenPropagatesFhirExceptionType()
    {
        // FhirException semantics must survive batch loading so the error filter can map them.
        var mediator = Substitute.For<IMediator>();
        var key = new ResourceKey("Patient", "forbidden");

        mediator
            .SendAsync(Arg.Is<GetResourceQuery>(q => q.ResourceType == "Patient" && q.Id == "forbidden"), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ForbiddenException("Access denied"));

        var loader = new TestableResourceDataLoader(mediator);

        await Should.ThrowAsync<ForbiddenException>(
            () => loader.LoadBatchPublicAsync([key], CancellationToken.None));
    }
}
