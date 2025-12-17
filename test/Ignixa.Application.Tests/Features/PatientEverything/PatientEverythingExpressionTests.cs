// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable disable

using Shouldly;
using Ignixa.Search.Expressions;
using Xunit;

namespace Ignixa.Application.Tests.Features.PatientEverything;

/// <summary>
/// Comprehensive unit tests for PatientEverythingExpression.
/// Tests construction, property validation, equality, and string formatting.
/// </summary>
public class PatientEverythingExpressionTests
{
    #region Constructor Tests

    [Fact]
    public void GivenSinglePatientId_WhenCreatingExpression_ThenPatientIdsContainsOneElement()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var expression = new PatientEverythingExpression(patientId);

        // Assert
        expression.PatientIds.ShouldNotBeNull();
        expression.PatientIds.Count.ShouldBe(1);
        expression.PatientIds[0].ShouldBe(patientId);
    }

    [Fact]
    public void GivenMultiplePatientIds_WhenCreatingExpression_ThenPatientIdsContainsAll()
    {
        // Arrange
        var patientIds = new List<string> { "patient-1", "patient-2", "patient-3" };

        // Act
        var expression = new PatientEverythingExpression(patientIds);

        // Assert
        expression.PatientIds.ShouldNotBeNull();
        expression.PatientIds.Count.ShouldBe(3);
        expression.PatientIds.ShouldBe(patientIds);
    }

    [Fact]
    public void GivenNullPatientIds_WhenCreatingExpression_ThenThrowsArgumentNullException()
    {
        // Arrange
        IReadOnlyList<string> patientIds = null;

        // Act
        var act = () => new PatientEverythingExpression(patientIds);

        // Assert
        Should.Throw<ArgumentNullException>(act)
            .ParamName.ShouldBe("patientIds");
    }

    [Fact]
    public void GivenEmptyPatientIds_WhenCreatingExpression_ThenThrowsArgumentException()
    {
        // Arrange
        var patientIds = Array.Empty<string>();

        // Act
        var act = () => new PatientEverythingExpression(patientIds);

        // Assert
        Should.Throw<ArgumentException>(act)
            .ParamName.ShouldBe("patientIds");
    }

    #endregion

    #region Property Tests

    [Fact]
    public void GivenDateFilters_WhenCreatingExpression_ThenStartAndEndDateSet()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);

        // Act
        var expression = new PatientEverythingExpression(
            patientId,
            startDate: startDate,
            endDate: endDate);

        // Assert
        expression.StartDate.ShouldBe(startDate);
        expression.EndDate.ShouldBe(endDate);
    }

    [Fact]
    public void GivenSinceDate_WhenCreatingExpression_ThenSinceDateSet()
    {
        // Arrange
        var patientId = "patient-123";
        var sinceDate = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);

        // Act
        var expression = new PatientEverythingExpression(
            patientId,
            sinceDate: sinceDate);

        // Assert
        expression.SinceDate.ShouldBe(sinceDate);
    }

    [Fact]
    public void GivenTypeFilter_WhenCreatingExpression_ThenFilteredResourceTypesSet()
    {
        // Arrange
        var patientId = "patient-123";
        var filteredTypes = new HashSet<string> { "Observation", "Condition", "MedicationRequest" };

        // Act
        var expression = new PatientEverythingExpression(
            patientId,
            filteredResourceTypes: filteredTypes);

        // Assert
        expression.FilteredResourceTypes.ShouldNotBeNull();
        expression.FilteredResourceTypes.ShouldBe(filteredTypes);
    }

    [Fact]
    public void GivenNullTypeFilter_WhenCreatingExpression_ThenDefaultsToEmptyHashSet()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var expression = new PatientEverythingExpression(patientId, filteredResourceTypes: null);

        // Assert
        expression.FilteredResourceTypes.ShouldNotBeNull();
        expression.FilteredResourceTypes.ShouldBeEmpty();
    }

    [Fact]
    public void GivenIncludeReferencedTrue_WhenCreatingExpression_ThenPropertyIsTrue()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var expression = new PatientEverythingExpression(
            patientId,
            includeReferencedResources: true);

        // Assert
        expression.IncludeReferencedResources.ShouldBeTrue();
    }

    [Fact]
    public void GivenIncludeReferencedFalse_WhenCreatingExpression_ThenPropertyIsFalse()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var expression = new PatientEverythingExpression(
            patientId,
            includeReferencedResources: false);

        // Assert
        expression.IncludeReferencedResources.ShouldBeFalse();
    }

    [Fact]
    public void GivenDefaultIncludeReferenced_WhenCreatingExpression_ThenDefaultsToTrue()
    {
        // Arrange
        var patientId = "patient-123";

        // Act
        var expression = new PatientEverythingExpression(patientId);

        // Assert
        expression.IncludeReferencedResources.ShouldBeTrue();
    }

    #endregion

    #region Equality Tests

    [Fact]
    public void GivenTwoIdenticalExpressions_WhenComparingValueInsensitive_ThenReturnsTrue()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expression1 = new PatientEverythingExpression(
            patientId,
            startDate: startDate,
            includeReferencedResources: true);
        var expression2 = new PatientEverythingExpression(
            patientId,
            startDate: startDate,
            includeReferencedResources: true);

        // Act
        var result = expression1.ValueInsensitiveEquals(expression2);

        // Assert
        result.ShouldBeTrue();
    }

    [Fact]
    public void GivenDifferentPatientCounts_WhenComparingValueInsensitive_ThenReturnsFalse()
    {
        // Arrange
        var expression1 = new PatientEverythingExpression("patient-1");
        var patientIds = new List<string> { "patient-1", "patient-2" };
        var expression2 = new PatientEverythingExpression(patientIds);

        // Act
        var result = expression1.ValueInsensitiveEquals(expression2);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenDifferentDatePresence_WhenComparingValueInsensitive_ThenReturnsFalse()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var expression1 = new PatientEverythingExpression(patientId, startDate: startDate);
        var expression2 = new PatientEverythingExpression(patientId);

        // Act
        var result = expression1.ValueInsensitiveEquals(expression2);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenDifferentIncludeReferenced_WhenComparingValueInsensitive_ThenReturnsFalse()
    {
        // Arrange
        var patientId = "patient-123";
        var expression1 = new PatientEverythingExpression(patientId, includeReferencedResources: true);
        var expression2 = new PatientEverythingExpression(patientId, includeReferencedResources: false);

        // Act
        var result = expression1.ValueInsensitiveEquals(expression2);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenNullExpression_WhenComparingValueInsensitive_ThenReturnsFalse()
    {
        // Arrange
        var expression = new PatientEverythingExpression("patient-123");

        // Act
        var result = expression.ValueInsensitiveEquals(null);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void GivenDifferentExpressionType_WhenComparingValueInsensitive_ThenReturnsFalse()
    {
        // Arrange
        var expression = new PatientEverythingExpression("patient-123");
        var otherExpression = new DummyExpression();

        // Act
        var result = expression.ValueInsensitiveEquals(otherExpression);

        // Assert
        result.ShouldBeFalse();
    }

    #endregion

    #region ToString Tests

    [Fact]
    public void GivenSinglePatient_WhenCallingToString_ThenFormatsWithQuotes()
    {
        // Arrange
        var patientId = "patient-123";
        var expression = new PatientEverythingExpression(patientId);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("'patient-123'");
        result.ShouldStartWith("(PatientEverything");
        result.ShouldEndWith(")");
    }

    [Fact]
    public void GivenMultiplePatients_WhenCallingToString_ThenFormatsAsList()
    {
        // Arrange
        var patientIds = new List<string> { "patient-1", "patient-2", "patient-3" };
        var expression = new PatientEverythingExpression(patientIds);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("['patient-1', 'patient-2', 'patient-3']");
        result.ShouldStartWith("(PatientEverything");
        result.ShouldEndWith(")");
    }

    [Fact]
    public void GivenStartDate_WhenCallingToString_ThenIncludesStartFilter()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateTimeOffset(2024, 1, 15, 0, 0, 0, TimeSpan.Zero);
        var expression = new PatientEverythingExpression(patientId, startDate: startDate);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("start=2024-01-15");
    }

    [Fact]
    public void GivenEndDate_WhenCallingToString_ThenIncludesEndFilter()
    {
        // Arrange
        var patientId = "patient-123";
        var endDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var expression = new PatientEverythingExpression(patientId, endDate: endDate);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("end=2024-12-31");
    }

    [Fact]
    public void GivenSinceDate_WhenCallingToString_ThenIncludesSinceFilter()
    {
        // Arrange
        var patientId = "patient-123";
        var sinceDate = new DateTimeOffset(2024, 6, 1, 12, 30, 45, TimeSpan.Zero);
        var expression = new PatientEverythingExpression(patientId, sinceDate: sinceDate);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("_since=");
        result.ShouldContain("2024-06-01");
    }

    [Fact]
    public void GivenTypeFilter_WhenCallingToString_ThenIncludesTypeFilter()
    {
        // Arrange
        var patientId = "patient-123";
        var filteredTypes = new HashSet<string> { "Observation", "Condition" };
        var expression = new PatientEverythingExpression(patientId, filteredResourceTypes: filteredTypes);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("_type=");
        (result.Contains("Observation") || result.Contains("Condition")).ShouldBeTrue();
    }

    [Fact]
    public void GivenMultipleFilters_WhenCallingToString_ThenIncludesAllFilters()
    {
        // Arrange
        var patientId = "patient-123";
        var startDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var endDate = new DateTimeOffset(2024, 12, 31, 23, 59, 59, TimeSpan.Zero);
        var sinceDate = new DateTimeOffset(2024, 6, 1, 0, 0, 0, TimeSpan.Zero);
        var filteredTypes = new HashSet<string> { "Observation" };
        var expression = new PatientEverythingExpression(
            patientId,
            startDate: startDate,
            endDate: endDate,
            sinceDate: sinceDate,
            filteredResourceTypes: filteredTypes);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldContain("start=2024-01-01");
        result.ShouldContain("end=2024-12-31");
        result.ShouldContain("_since=");
        result.ShouldContain("_type=Observation");
    }

    [Fact]
    public void GivenNoFilters_WhenCallingToString_ThenOnlyShowsPatientId()
    {
        // Arrange
        var patientId = "patient-123";
        var expression = new PatientEverythingExpression(patientId);

        // Act
        var result = expression.ToString();

        // Assert
        result.ShouldBe("(PatientEverything 'patient-123')");
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Dummy expression for testing type comparison.
    /// </summary>
    private class DummyExpression : Expression
    {
        public override TOutput AcceptVisitor<TContext, TOutput>(IExpressionVisitor<TContext, TOutput> visitor, TContext context)
        {
            throw new NotImplementedException();
        }

        public override void AddValueInsensitiveHashCode(ref HashCode hashCode)
        {
            hashCode.Add(typeof(DummyExpression));
        }

        public override bool ValueInsensitiveEquals(Expression other)
        {
            return other is DummyExpression;
        }

        public override string ToString()
        {
            return "(Dummy)";
        }
    }

    #endregion
}
