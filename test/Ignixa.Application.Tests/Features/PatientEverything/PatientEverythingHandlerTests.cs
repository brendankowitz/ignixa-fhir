// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using System.Runtime.CompilerServices;
using Shouldly;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Operations.Features.PatientEverything;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Expressions;
using Ignixa.Search.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace Ignixa.Application.Tests.Features.PatientEverything;

/// <summary>
/// Unit tests for PatientEverythingHandler.
/// Tests the FHIR Patient $everything operation handler logic, including expression creation,
/// search options configuration, partition resolution, and result generation.
/// </summary>
public class PatientEverythingHandlerTests
{
    private readonly IPartitionStrategy _partitionStrategy;
    private readonly IQueryExecutionStrategy _executionStrategy;
    private readonly IFhirRequestContextAccessor _contextAccessor;
    private readonly ILogger<PatientEverythingHandler> _logger;
    private readonly PatientEverythingHandler _handler;

    public PatientEverythingHandlerTests()
    {
        _partitionStrategy = Substitute.For<IPartitionStrategy>();
        _executionStrategy = Substitute.For<IQueryExecutionStrategy>();
        _contextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        _logger = NullLogger<PatientEverythingHandler>.Instance;
        _handler = new PatientEverythingHandler(
            _partitionStrategy,
            _executionStrategy,
            _contextAccessor,
            _logger);
    }

    #region Setup

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

        // Setup IFhirRequestContextAccessor to return valid context
        var mockContext = Substitute.For<IFhirRequestContext>();
        mockContext.TenantId.Returns(1);
        mockContext.TenantConfiguration.Returns(tenantConfig);
        _contextAccessor.RequestContext.Returns(mockContext);

        // Setup IPartitionStrategy
        _partitionStrategy.DetermineReadPartition(
            Arg.Any<PartitionResolutionContext>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>())
            .Returns(new RequestPartition { PartitionIds = SinglePartitionArray, Mode = PartitionMode.Isolated });

        // Setup IQueryExecutionStrategy to return empty async enumerable
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

    #endregion

    #region Request Context Tests

    [Fact]
    public async Task GivenMissingRequestContext_WhenHandling_ThenThrowsInvalidOperationException()
    {
        // Arrange
        _contextAccessor.RequestContext.Returns((IFhirRequestContext)null);
        var query = new PatientEverythingQuery("patient-123");

        // Act & Assert
        var act = async () => await _handler.HandleAsync(query, CancellationToken.None);
        (await Should.ThrowAsync<InvalidOperationException>(act))
            .Message.ShouldContain("request context not available");
    }

    [Fact]
    public async Task GivenValidRequestContext_WhenHandling_ThenUsesContextTenantId()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        _partitionStrategy.Received(1).DetermineReadPartition(
            Arg.Is<PartitionResolutionContext>(ctx => ctx.TenantId == 1),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    #endregion

    #region Expression Creation Tests

    [Fact]
    public async Task GivenPatientId_WhenHandling_ThenCreatesExpressionWithPatientId()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-456");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.PatientIds.ShouldHaveSingleItem();
        expression.PatientIds[0].ShouldBe("patient-456");
    }

    [Fact]
    public async Task GivenDateFilters_WhenHandling_ThenCreatesExpressionWithDates()
    {
        // Arrange
        SetupDefaultMocks();
        var startDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var query = new PatientEverythingQuery(
            "patient-789",
            Start: startDate,
            End: endDate);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.StartDate.ShouldBe(startDate);
        expression.EndDate.ShouldBe(endDate);
    }

    [Fact]
    public async Task GivenTypeFilter_WhenHandling_ThenCreatesExpressionWithTypes()
    {
        // Arrange
        SetupDefaultMocks();
        var types = new HashSet<string> { "Observation", "Condition", "MedicationRequest" };
        var query = new PatientEverythingQuery(
            "patient-123",
            Types: types);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.FilteredResourceTypes.ShouldBe(types);
        // When _type filter is specified, referenced resources should NOT be included
        expression.IncludeReferencedResources.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenSinceParameter_WhenHandling_ThenCreatesExpressionWithSince()
    {
        // Arrange
        SetupDefaultMocks();
        var sinceDate = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var query = new PatientEverythingQuery(
            "patient-123",
            Since: sinceDate);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.SinceDate.ShouldBe(sinceDate);
    }

    [Fact]
    public async Task GivenAllFilters_WhenHandling_ThenCreatesExpressionWithAllParameters()
    {
        // Arrange
        SetupDefaultMocks();
        var startDate = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2023, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var sinceDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var types = new HashSet<string> { "Observation" };
        var query = new PatientEverythingQuery(
            "patient-999",
            Start: startDate,
            End: endDate,
            Since: sinceDate,
            Types: types,
            Count: 100);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.PatientIds.ShouldHaveSingleItem();
        expression.PatientIds[0].ShouldBe("patient-999");
        expression.StartDate.ShouldBe(startDate);
        expression.EndDate.ShouldBe(endDate);
        expression.SinceDate.ShouldBe(sinceDate);
        expression.FilteredResourceTypes.ShouldBe(types);
        // When _type filter is specified, referenced resources should NOT be included
        expression.IncludeReferencedResources.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenNoTypeFilter_WhenHandling_ThenIncludesReferencedResources()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
        var expression = (PatientEverythingExpression)result.SearchOptions.Expression;
        expression.IncludeReferencedResources.ShouldBeTrue();
    }

    #endregion

    #region SearchOptions Tests

    [Fact]
    public async Task GivenCountParameter_WhenHandling_ThenSetsMaxItemCount()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123", Count: 100);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.MaxItemCount.ShouldBe(100);
    }

    [Fact]
    public async Task GivenNoCount_WhenHandling_ThenDefaultsTo50()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.MaxItemCount.ShouldBe(50);
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenResourceTypeIsNull()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.ResourceType.ShouldBeNull();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenSortIsEmpty()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Sort.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenIncludeAndRevIncludeAreEmpty()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Include.ShouldBeEmpty();
        result.SearchOptions.RevInclude.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenTotalIsNone()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Total.ShouldBe(TotalType.None);
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenSummaryIsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Summary.ShouldBe(Ignixa.Search.Models.SummaryType.False);
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenElementsIsEmpty()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.Elements.ShouldBeEmpty();
    }

    #endregion

    #region Partition Resolution Tests

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenCallsPartitionStrategyWithPatientResourceType()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        _partitionStrategy.Received(1).DetermineReadPartition(
            Arg.Any<PartitionResolutionContext>(),
            "Patient",
            Arg.Any<IReadOnlyDictionary<string, string>>());
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenCallsExecutionStrategyWithPartitionResult()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert - verify partition strategy was called correctly
        _executionStrategy.Received(1).SearchStreamAsync(
            Arg.Is<RequestPartition>(pr => pr.PartitionIds.Contains(1) && pr.Mode == PartitionMode.Isolated),
            Arg.Any<SearchOptions>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Result Tests

    [Fact]
    public async Task GivenValidRequest_WhenHandling_ThenReturnsSearchResourcesResult()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<SearchResourcesResult>();
        result.Resources.ShouldNotBeNull();
    }

    [Fact]
    public async Task GivenValidRequest_WhenHandling_ThenIncludesSearchOptionsInResult()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123", Count: 75);

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.SearchOptions.ShouldNotBeNull();
        result.SearchOptions.MaxItemCount.ShouldBe(75);
        result.SearchOptions.Expression.ShouldBeOfType<PatientEverythingExpression>();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenTotalIsNull()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.Total.ShouldBeNull();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenContinuationTokenIsNull()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.ContinuationToken.ShouldBeNull();
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenHasMoreIsFalse()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        result.ShouldNotBeNull();
        result.HasMore.ShouldBeFalse();
    }

    #endregion

    #region Execution Strategy Tests

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenCallsSearchStreamAsync()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");

        // Act
        var result = await _handler.HandleAsync(query, CancellationToken.None);

        // Assert
        _executionStrategy.Received(1).SearchStreamAsync(
            Arg.Any<RequestPartition>(),
            Arg.Any<SearchOptions>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GivenRequest_WhenHandling_ThenPassesCancellationToken()
    {
        // Arrange
        SetupDefaultMocks();
        var query = new PatientEverythingQuery("patient-123");
        var cts = new CancellationTokenSource();

        // Act
        var result = await _handler.HandleAsync(query, cts.Token);

        // Assert
        _executionStrategy.Received(1).SearchStreamAsync(
            Arg.Any<RequestPartition>(),
            Arg.Any<SearchOptions>(),
            cts.Token);
    }

    #endregion
}
