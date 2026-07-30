// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Runtime.CompilerServices;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Features.Resource;

/// <summary>
/// Both handlers vary a caller's <see cref="SearchOptions"/> before handing it to the execution strategy.
/// They used to do that by hand-copying a subset of the properties, which silently dropped
/// <c>AccessConstraints</c> and <c>AllowedResourceTypes</c> — a fail-open authorization gap, since the
/// compiler enforces both structurally and cannot enforce what it never receives. These tests pin the
/// forwarding to the options the caller actually supplied.
/// </summary>
public class SearchOptionsForwardingTests
{
    private static readonly int[] SinglePartition = [1];

    private readonly IPartitionStrategy _partitionStrategy = Substitute.For<IPartitionStrategy>();
    private readonly IQueryExecutionStrategy _executionStrategy = Substitute.For<IQueryExecutionStrategy>();
    private readonly IFhirRequestContextAccessor _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();

    public SearchOptionsForwardingTests()
    {
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.TenantConfiguration.Returns(new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec",
        });
        _contextAccessor.RequestContext.Returns(context);

        _partitionStrategy.DetermineReadPartition(
                Arg.Any<PartitionResolutionContext>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(new RequestPartition { PartitionIds = SinglePartition, Mode = PartitionMode.Isolated });

        _executionStrategy.SearchStreamAsync(
                Arg.Any<RequestPartition>(),
                Arg.Any<SearchOptions>(),
                Arg.Any<CancellationToken>())
            .Returns(EmptyStream<SearchEntryResult>());
    }

    [Fact]
    public async Task GivenConstrainedSearchOptions_WhenSearching_ThenTheExecutedOptionsKeepEveryConstraint()
    {
        // Arrange
        SearchOptions options = ConstrainedOptions();

        var handler = new SearchResourcesHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            NullLogger<SearchResourcesHandler>.Instance);

        // Act
        await handler.HandleAsync(new SearchResourcesQuery("Patient", options), CancellationToken.None);

        // Assert
        SearchOptions executed = CapturedOptions();
        ShouldKeepConstraints(executed, options);

        // The one property this handler deliberately varies: pageSize + 1 for has-more detection.
        executed.MaxItemCount.ShouldBe(options.MaxItemCount + 1);
    }

    [Fact]
    public async Task GivenConstrainedSearchOptions_WhenFetchingIncludes_ThenTheExecutedOptionsKeepEveryConstraint()
    {
        // Arrange
        SearchOptions options = ConstrainedOptions();

        var handler = new IncludesResourceHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            NullLogger<IncludesResourceHandler>.Instance);

        // Act
        await handler.HandleAsync(new IncludesResourceQuery("Patient", options), CancellationToken.None);

        // Assert
        SearchOptions executed = CapturedOptions();
        ShouldKeepConstraints(executed, options);

        // The properties this handler deliberately varies: it re-runs the search unpaged to reach the
        // includes, so it must not inherit the caller's page boundary or ask for a total.
        executed.ContinuationToken.ShouldBeNull();
        executed.Total.ShouldBe(TotalType.None);
        executed.MaxItemCount.ShouldBeGreaterThan(options.MaxItemCount);
    }

    private static SearchOptions ConstrainedOptions() => new()
    {
        MaxItemCount = 10,
        ResourceType = "Patient",
        ContinuationToken = "page-2",
        Total = TotalType.Accurate,
        AllowedResourceTypes = ["Patient", "Observation"],
        AccessConstraints =
        [
            new AccessConstraint(
                "Patient",
                new StringExpression(StringOperator.Equals, FieldName.String, componentIndex: null, "constrained", ignoreCase: false)),
        ],
        ResourceVersionTypes = ResourceVersionTypes.Latest | ResourceVersionTypes.History,
        StartSurrogateId = 100,
        EndSurrogateId = 200,
        Elements = new HashSet<string> { "id" },
        Summary = SummaryType.Text,
    };

    private static void ShouldKeepConstraints(SearchOptions executed, SearchOptions requested)
    {
        executed.ShouldNotBeSameAs(requested);

        executed.AllowedResourceTypes.ShouldBe(requested.AllowedResourceTypes);
        executed.AccessConstraints.ShouldBe(requested.AccessConstraints);
        executed.ResourceVersionTypes.ShouldBe(requested.ResourceVersionTypes);
        executed.StartSurrogateId.ShouldBe(requested.StartSurrogateId);
        executed.EndSurrogateId.ShouldBe(requested.EndSurrogateId);
        executed.Elements.ShouldBe(requested.Elements);
        executed.Summary.ShouldBe(requested.Summary);
        executed.ResourceType.ShouldBe(requested.ResourceType);
    }

    private SearchOptions CapturedOptions()
        => (SearchOptions)_executionStrategy.ReceivedCalls()
            .Single(call => call.GetMethodInfo().Name == nameof(IQueryExecutionStrategy.SearchStreamAsync))
            .GetArguments()[1];

    private static async IAsyncEnumerable<T> EmptyStream<T>([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }
}
