// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HotChocolate;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using Ignixa.Abstractions;
using FhirISchema = Ignixa.Abstractions.ISchema;
using Ignixa.Application.Features.Experimental.Configuration;
using Ignixa.Application.Features.Experimental.GraphQl.Resolvers;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Medino;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.GraphQl;

public class SearchResolverTests
{
    private static readonly byte[] PatientJson1 = Encoding.UTF8.GetBytes(
        """{"resourceType":"Patient","id":"p1"}""");
    private static readonly byte[] PatientJson2 = Encoding.UTF8.GetBytes(
        """{"resourceType":"Patient","id":"p2"}""");
    private static readonly string[] SortDateName = ["-date", "name"];

    private static readonly DateTimeOffset FixedTimestamp = new(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

    private static SearchEntryResult MakeEntry(string id, byte[] json)
        => new SearchEntryResult("Patient", id, "1", FixedTimestamp, json);

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

        // Set up default empty Selection.Field.Arguments chain
        SetupFieldArguments(resolverContext, []);

        return (mediator, builderFactory, builder, contextAccessor, resolverContext);
    }

    private static void SetupFieldArguments(IResolverContext resolverContext, IInputField[] arguments)
    {
        var argumentsCollection = Substitute.For<IFieldCollection<IInputField>>();
        argumentsCollection.GetEnumerator().Returns(_ => ((IEnumerable<IInputField>)arguments).GetEnumerator());

        var objectField = Substitute.For<IObjectField>();
        objectField.Arguments.Returns(argumentsCollection);

        var selection = Substitute.For<ISelection>();
        selection.Field.Returns(objectField);

        resolverContext.Selection.Returns(selection);
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
    public async Task GivenMoreResultsThanPageSize_WhenSearching_ThenResultHasEncodedNextCursor()
    {
        // Arrange
        var searchOptions = new SearchOptions { ResourceType = "Patient", MaxItemCount = 2 };
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks(searchOptions);

        var entries = new[]
        {
            MakeEntry("p1", PatientJson1),
            MakeEntry("p2", PatientJson2),
            MakeEntry("p3", PatientJson1),
        };
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable(entries)));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.Edges.Count.ShouldBe(2);
        result.Next.ShouldNotBeNull();
        ContinuationToken.TryDecode(result.Next!, out var offset, out var count).ShouldBeTrue();
        offset.ShouldBe(2);
        count.ShouldBe(2);
    }

    [Fact]
    public async Task GivenResultsFitInOnePage_WhenSearching_ThenNextCursorIsNull()
    {
        // Arrange
        var searchOptions = new SearchOptions { ResourceType = "Patient", MaxItemCount = 10 };
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks(searchOptions);

        var entries = new[] { MakeEntry("p1", PatientJson1), MakeEntry("p2", PatientJson2) };
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable(entries)));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.Edges.Count.ShouldBe(2);
        result.Next.ShouldBeNull();
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
    public async Task GivenNegativeCount_WhenSearching_ThenClampsToOne()
    {
        // Arrange — the lower clamp bound is 1 to avoid a degenerate empty-but-not-last page
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
                p.Any(q => q.Name == "_count" && q.Value == "1")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenZeroCount_WhenSearching_ThenClampsToOne()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<int?>("_count").Returns(new Optional<int?>(0));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_count" && q.Value == "1")),
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
            new SearchEntryResult("Patient", "p2", "1", FixedTimestamp, PatientJson2)
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

    [Fact]
    public async Task GivenCustomSearchParameter_WhenSearching_ThenForwardsToBuilder()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

        // Set up a custom "name" argument on the field
        var nameArg = Substitute.For<IInputField>();
        nameArg.Name.Returns("name");

        var birthDateArg = Substitute.For<IInputField>();
        birthDateArg.Name.Returns("birth_date");

        SetupFieldArguments(resolverContext, [nameArg, birthDateArg]);

        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("name").Returns(new Optional<IReadOnlyList<string>?>(["Smith"]));
        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("birth_date").Returns(new Optional<IReadOnlyList<string>?>(["gt2000"]));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "name" && q.Value == "Smith") &&
                p.Any(q => q.Name == "birth-date" && q.Value == "gt2000")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenCustomSearchParameterWithNoValue_WhenSearching_ThenDoesNotForward()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

        var nameArg = Substitute.For<IInputField>();
        nameArg.Name.Returns("name");

        SetupFieldArguments(resolverContext, [nameArg]);

        // Argument exists but has no value provided
        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("name").Returns(new Optional<IReadOnlyList<string>?>());

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.All(q => q.Name != "name")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenArraySearchParameter_WhenSearching_ThenJoinsValuesWithComma()
    {
        // Arrange
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

        var idArg = Substitute.For<IInputField>();
        idArg.Name.Returns("_id");

        SetupFieldArguments(resolverContext, [idArg]);

        resolverContext.ArgumentOptional<IReadOnlyList<string>?>("_id").Returns(new Optional<IReadOnlyList<string>?>(["p1", "p2", "p3"]));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Any(q => q.Name == "_id" && q.Value == "p1,p2,p3")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenTenantContext_WhenSearching_ThenCreatesBuilderWithTenantId()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        var requestContext = new FhirRequestContext
        {
            TenantId = 7,
            FhirVersion = FhirVersion.R4,
        };
        contextAccessor.RequestContext.Returns(requestContext);

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builderFactory.Received(1).Create(FhirVersion.R4, 7);
    }

    [Fact]
    public async Task GivenTenantContext_WhenListSearching_ThenCreatesBuilderWithTenantId()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();

        var requestContext = new FhirRequestContext
        {
            TenantId = 42,
            FhirVersion = FhirVersion.R5,
        };
        contextAccessor.RequestContext.Returns(requestContext);

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchListAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builderFactory.Received(1).Create(FhirVersion.R5, 42);
    }

    [Fact]
    public async Task GivenMalformedContinuationToken_WhenSearching_ThenOffsetDefaultsToZero()
    {
        // Arrange
        var searchOptions = new SearchOptions
        {
            ResourceType = "Patient",
            MaxItemCount = 10,
            ContinuationToken = "this-is-not-a-valid-token",
        };
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks(searchOptions);

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        var result = await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        result.Offset.ShouldBe(0);
    }

    [Fact]
    public async Task GivenConnectionSearchWithoutTotalArgument_WhenSearching_ThenRequestsAccurateTotal()
    {
        // Arrange — the connection path must request an accurate total so count is populated
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

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
    public async Task GivenListSearchWithoutTotalArgument_WhenSearching_ThenDoesNotRequestTotal()
    {
        // Arrange — the list path should NOT inject _total
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchListAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.All(q => q.Name != "_total")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenExplicitTotalArgument_WhenConnectionSearching_ThenDoesNotOverrideUserTotal()
    {
        // Arrange — a user-supplied _total wins over the connection default
        var (mediator, builderFactory, builder, contextAccessor, resolverContext) = CreateMocks();
        resolverContext.ArgumentOptional<string?>("_total").Returns(new Optional<string?>("none"));

        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(ToAsyncEnumerable([])));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act
        await resolver.SearchAsync("Patient", resolverContext, CancellationToken.None);

        // Assert
        builder.Received(1).Build(
            "Patient",
            Arg.Is<IReadOnlyList<QueryParameter>>(p =>
                p.Count(q => q.Name == "_total") == 1 &&
                p.Any(q => q.Name == "_total" && q.Value == "none")),
            Arg.Any<FhirISchema?>());
    }

    [Fact]
    public async Task GivenFhirException_WhenSearching_ThenThrowsCodedGraphQLException()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new BadRequestException("Unsupported search parameter"));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.SearchAsync("Patient", resolverContext, CancellationToken.None));
        ex.Errors[0].Code.ShouldBe("INVALID_RESOURCE");
        ex.Errors[0].Message.ShouldBe("Unsupported search parameter");
    }

    [Fact]
    public async Task GivenFhirException_WhenListSearching_ThenThrowsCodedGraphQLException()
    {
        // Arrange
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ResourceNotSupportedException("Patient"));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act & Assert
        var ex = await Should.ThrowAsync<GraphQLException>(
            () => resolver.SearchListAsync("Patient", resolverContext, CancellationToken.None));
        ex.Errors[0].Code.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenNonFhirException_WhenSearching_ThenPropagatesUncaught()
    {
        // Arrange — non-FHIR exceptions are left for the error filter to log and mask
        var (mediator, builderFactory, _, contextAccessor, resolverContext) = CreateMocks();
        mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("database offline"));

        var resolver = CreateResolver(mediator, builderFactory, contextAccessor);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(
            () => resolver.SearchAsync("Patient", resolverContext, CancellationToken.None));
    }
}
