// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Resolvers;
using Ignixa.Abstractions;
using FhirISchema = Ignixa.Abstractions.ISchema;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class SearchResolverTests
{
    private static readonly byte[] PatientJson1 = Encoding.UTF8.GetBytes(
        """{"resourceType":"Patient","id":"p1"}""");
    private static readonly byte[] PatientJson2 = Encoding.UTF8.GetBytes(
        """{"resourceType":"Patient","id":"p2"}""");
    private static readonly string[] SortDateName = ["-date", "name"];

    private static SearchEntryResult MakeEntry(string id, byte[] json)
        => new SearchEntryResult("Patient", id, "1", DateTimeOffset.UtcNow, json);

    private static async IAsyncEnumerable<SearchEntryResult> ToAsyncEnumerable(
        IEnumerable<SearchEntryResult> items,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return item;
            await Task.Yield();
        }
    }

    private static (IMediator mediator, ISearchOptionsBuilderFactory builderFactory, ISearchOptionsBuilder builder, IFhirRequestContextAccessor contextAccessor, IResolverContext resolverContext) CreateMocks(
        SearchOptions? returnOptions = null)
    {
        var mediator = Substitute.For<IMediator>();
        var builderFactory = Substitute.For<ISearchOptionsBuilderFactory>();
        var builder = Substitute.For<ISearchOptionsBuilder>();
        var contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        var resolverContext = Substitute.For<IResolverContext>();

        var options = returnOptions ?? new SearchOptions { ResourceType = "Patient", MaxItemCount = 10 };
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<FhirISchema?>())
            .Returns(options);
        builderFactory.Create(Arg.Any<FhirVersion>(), Arg.Any<int?>())
            .Returns(builder);

        contextAccessor.RequestContext.Returns((IFhirRequestContext?)null);

        resolverContext.ArgumentOptional<int?>("_count").Returns(new Optional<int?>());
        resolverContext.ArgumentOptional<string?>("_cursor").Returns(new Optional<string?>());
        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("_sort").Returns(new Optional<IReadOnlyList<string>?>());
        resolverContext.ArgumentOptional<string?>("_total").Returns(new Optional<string?>());

        return (mediator, builderFactory, builder, contextAccessor, resolverContext);
    }

    private static IOptions<ExperimentalOptions> DefaultOptions(
        int defaultPageSize = 10,
        int maxPageSize = 1000)
    {
        var graphQlOptions = new GraphQlExperimentalOptions
        {
            DefaultPageSize = defaultPageSize,
            MaxPageSize = maxPageSize,
        };
        var experimentalOptions = new ExperimentalOptions();
        experimentalOptions.Features.GraphQl = graphQlOptions;
        return Options.Create(experimentalOptions);
    }

    private static SearchResolver CreateResolver(
        IMediator mediator,
        ISearchOptionsBuilderFactory builderFactory,
        IFhirRequestContextAccessor contextAccessor,
        IOptions<ExperimentalOptions>? experimentalOptions = null)
        => new SearchResolver(
            mediator,
            builderFactory,
            contextAccessor,
            experimentalOptions ?? DefaultOptions(),
            NullLogger<SearchResolver>.Instance);

    [Fact]
    public async Task GivenSearchQuery_WhenSearching_ThenReturnsConnectionResult()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        var entries = new[] { MakeEntry("p1", PatientJson1), MakeEntry("p2", PatientJson2) };
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable(entries), Total: 2));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Edges.Count.ShouldBe(2);
        result.Count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenSortArgument_WhenSearching_ThenPassesSortToBuilder()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("_sort").Returns(new Optional<IReadOnlyList<string>?>(SortDateName));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_sort" && q.Value == "-date,name")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenCursorArgument_WhenSearching_ThenPassesContinuationTokenToBuilder()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<string?>("_cursor").Returns(new Optional<string?>("opaque-cursor-token"));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "ct" && q.Value == "opaque-cursor-token")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenNextPageToken_WhenSearching_ThenResultHasNextCursorInLinks()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(
                ToAsyncEnumerable([]),
                ContinuationToken: "next-page-token",
                HasMore: true));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.Next.ShouldNotBeNull();
        result.Next.ShouldBe("next-page-token");
    }

    [Fact]
    public async Task GivenTotalArgument_WhenSearching_ThenPassesTotalModeToBuilder()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<string?>("_total").Returns(new Optional<string?>("accurate"));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_total" && q.Value == "accurate")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenCountExceedsMaxPageSize_WhenSearching_ThenCapsAtMaxPageSize()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<int?>("_count").Returns(new Optional<int?>(5000));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor, DefaultOptions(maxPageSize: 100));

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_count" && q.Value == "100")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenNoCountArgument_WhenSearching_ThenUsesDefaultPageSize()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor, DefaultOptions(defaultPageSize: 25));

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_count" && q.Value == "25")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenNegativeCount_WhenSearching_ThenClampsToZero()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<int?>("_count").Returns(new Optional<int?>(-5));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_count" && q.Value == "0")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenListSearch_WhenSearching_ThenReturnsListOfJsonElements()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        var entries = new[] { MakeEntry("p1", PatientJson1), MakeEntry("p2", PatientJson2) };
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable(entries), Total: 2));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchListAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Count.ShouldBe(2);
        result[0].GetProperty("id").GetString().ShouldBe("p1");
    }

    [Fact]
    public async Task GivenDeletedEntries_WhenSearching_ThenExcludesThemFromResult()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        var entries = new[]
        {
            MakeEntry("p1", PatientJson1),
            new SearchEntryResult("Patient", "p2", "1", DateTimeOffset.UtcNow, PatientJson2)
            {
                IsDeleted = true,
            },
        };
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable(entries)));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.Edges.Count.ShouldBe(1);
        result.Edges[0].Resource.GetProperty("id").GetString().ShouldBe("p1");
    }
}
