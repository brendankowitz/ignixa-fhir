// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Runtime.CompilerServices;
using Ignixa.Application.Features.Compartment;
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
/// They used to hand-copy a subset of the properties, silently dropping nine — among them
/// <c>AccessConstraints</c> and <c>AllowedResourceTypes</c>, which the compiler enforces structurally and
/// cannot enforce if they never arrive. No production caller populates those two yet, so this pins the
/// forwarding before one does.
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

        // The one property this handler deliberately varies: it asks for a probe row rather than inflating
        // the page size, so the data layer still knows which rows are genuinely on the page.
        executed.MaxItemCount.ShouldBe(options.MaxItemCount);
        executed.ProbeExtraRow.ShouldBeTrue();
        options.ProbeExtraRow.ShouldBeFalse();

        // Total=Accurate also runs a COUNT. It must carry the same constraints, or the count leaks the
        // cardinality of resources the caller may not read.
        await _executionStrategy.Received(1).CountAsync(
            Arg.Any<RequestPartition>(),
            Arg.Is<SearchOptions>(o =>
                o.AccessConstraints.Count == options.AccessConstraints.Count
                && o.AllowedResourceTypes.Count == options.AllowedResourceTypes.Count),
            Arg.Any<CancellationToken>());
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

        // The properties this handler deliberately varies: it widens the page to reach the includes, so it
        // must not inherit the caller's page boundary or ask for a total. Both includes cursors are
        // consumed here, so they must not travel downstream either.
        executed.ContinuationToken.ShouldBeNull();
        executed.Total.ShouldBe(TotalType.None);
        executed.MaxItemCount.ShouldBe(options.MaxItemCount * 10);
        executed.IncludesContinuationToken.ShouldBeNull();
        executed.IncludesMaxItemCount.ShouldBeNull();

        // $includes wants every row of its widened match budget on the page: it is mining those rows for
        // their includes, and a row treated as a probe contributes none.
        executed.ProbeExtraRow.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenALargePageSize_WhenFetchingIncludes_ThenTheWidenedPageIsCapped()
    {
        // Arrange
        SearchOptions options = ConstrainedOptions();
        options.MaxItemCount = 5000;

        var handler = new IncludesResourceHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            NullLogger<IncludesResourceHandler>.Instance);

        // Act
        await handler.HandleAsync(new IncludesResourceQuery("Patient", options), CancellationToken.None);

        // Assert: 5000 * 10 exceeds the 10000 ceiling, so the multiplier is capped rather than applied.
        CapturedOptions().MaxItemCount.ShouldBe(10000);
    }

    [Fact]
    public async Task GivenAWildcardCompartmentSearch_WhenHandled_ThenTheCallersOptionsAreNotMutated()
    {
        // Arrange: the compartment handler narrows the request, and used to do so by writing through the
        // caller's instance — the leak the copy constructor exists to prevent.
        SearchOptions options = ConstrainedOptions();
        options.Expression = null;

        var handler = new SearchCompartmentHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            NullLogger<SearchCompartmentHandler>.Instance);

        // Act
        await handler.HandleAsync(
            new SearchCompartmentQuery("Patient", "123", "*", options),
            CancellationToken.None);

        // Assert
        options.Expression.ShouldBeNull();
        options.ResourceType.ShouldBe("Patient");

        SearchOptions executed = CapturedOptions();
        executed.ShouldNotBeSameAs(options);
        executed.Expression.ShouldBeOfType<CompartmentSearchExpression>();
        executed.ResourceType.ShouldBeNull();
        executed.AccessConstraints.ShouldBe(options.AccessConstraints);
        executed.AllowedResourceTypes.ShouldBe(options.AllowedResourceTypes);
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
        IncludesMaxItemCount = 25,
        IncludesContinuationToken = IncludesContinuationToken.Encode(includesOffset: 5, pageSize: 25),
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
