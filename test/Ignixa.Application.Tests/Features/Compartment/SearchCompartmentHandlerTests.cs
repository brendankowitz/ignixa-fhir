// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Runtime.CompilerServices;
using Shouldly;
using Ignixa.Application.Features.Compartment;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.Compartment;

/// <summary>
/// Unit tests for SearchCompartmentHandler.
/// Tests composition of the CompartmentSearchExpression with the caller's existing SearchOptions.Expression.
/// </summary>
public class SearchCompartmentHandlerTests
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<SearchCompartmentHandler> _logger;
    private readonly SearchCompartmentHandler _handler;

    public SearchCompartmentHandlerTests()
    {
        _partitionStrategy = Substitute.For<IPartitionStrategy>();
        _executionStrategy = Substitute.For<IQueryExecutionStrategy>();
        _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _logger = NullLogger<SearchCompartmentHandler>.Instance;
        _handler = new SearchCompartmentHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            _logger);
    }

    private static readonly int[] SinglePartitionArray = new[] { 1 };

    private void SetupDefaultMocks()
    {
        var tenantConfig = new TenantConfiguration
        {
            TenantId = 1,
            DisplayName = "Test Tenant",
            FhirVersion = "R4",
            ValidationDepth = "Spec"
        };

        var mockContext = Substitute.For<IFhirRequestContext>();
        mockContext.TenantId.Returns(1);
        mockContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(mockContext);

        _partitionStrategy.DetermineReadPartition(
            Arg.Any<PartitionResolutionContext>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(new RequestPartition { PartitionIds = SinglePartitionArray, Mode = PartitionMode.Isolated });

        _executionStrategy.SearchStreamAsync(
            Arg.Any<RequestPartition>(),
            Arg.Any<SearchOptions>(),
            Arg.Any<CancellationToken>())
            .Returns(CreateEmptyAsyncEnumerable<SearchEntryResult>());
    }

    private static async IAsyncEnumerable<T> CreateEmptyAsyncEnumerable<T>([EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await Task.CompletedTask;
        yield break;
    }

    [Fact]
    public async Task GivenACompartmentSearchWithMultipleOrdinaryParameters_WhenComposed_ThenSplicesIntoTheExistingAndInsteadOfNesting()
    {
        // Arrange -- mirrors GET /Patient/123/Observation?_id=obs-1&category=laboratory: SearchOptions.Expression
        // is already a flat And of 2 ordinary params before the handler composes it with the compartment expression.
        SetupDefaultMocks();

        var idParam = new SearchParameterInfo("_id", "_id", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Resource-id"));
        var categoryParam = new SearchParameterInfo("category", "category", SearchParamType.Token, new Uri("http://hl7.org/fhir/SearchParameter/Observation-category"));
        var idExpression = new SearchParameterExpression(
            idParam,
            new SearchParameterPredicateExpression(idParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "obs-1", text: null)));
        var categoryExpression = new SearchParameterPredicateExpression(
            categoryParam, SearchComparator.Eq, modifier: null, new TokenSearchValue(system: null, code: "laboratory", text: null));
        var existingAnd = new MultiaryExpression(MultiaryOperator.And, [idExpression, categoryExpression]);

        var query = new SearchCompartmentQuery(
            "Patient",
            "123",
            "Observation",
            new SearchOptions { Expression = existingAnd });

        // Act
        await _handler.HandleAsync(query, CancellationToken.None);

        // Assert -- the composed Expression is a SINGLE flat MultiaryExpression{And} containing 3 children
        // (compartment + the 2 original params), not a MultiaryExpression{And} containing 2 children where
        // one child is itself a nested MultiaryExpression{And}.
        var composed = query.SearchOptions.Expression.ShouldBeOfType<MultiaryExpression>();
        composed.MultiaryOperation.ShouldBe(MultiaryOperator.And);
        composed.Expressions.Count.ShouldBe(3);
        composed.Expressions.ShouldContain(idExpression);
        composed.Expressions.ShouldContain(categoryExpression);
        composed.Expressions.ShouldContain(e => e is CompartmentSearchExpression);
        bool IsNotNestedAnd(Expression e) => e is not MultiaryExpression multiary || multiary.MultiaryOperation != MultiaryOperator.And;
        composed.Expressions.All(IsNotNestedAnd).ShouldBeTrue();
    }
}
