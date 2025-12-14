// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using FluentAssertions;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for quantity search parameters with comparison operators.
/// Tests FHIR quantity search with prefixes (gt, ge, lt, le).
/// </summary>
/// <remarks>
/// FHIR Quantity Search Semantics (http://hl7.org/fhir/search.html#quantity):
/// - gt: the value in the resource is greater than the provided value
/// - ge: the value in the resource is greater or equal to the provided value
/// - lt: the value in the resource is less than the provided value
/// - le: the value in the resource is less or equal to the provided value
///
/// NOTE: Unit/system filtering is NOT fully implemented. These tests focus on
/// numeric comparison only. Tests that require unit filtering are skipped.
///
/// Test Data Setup:
/// - obs[0]: 180 [lb_av] (Body Weight)
/// - obs[1]: 185 [lb_av] (Body Weight)
/// - obs[2]: 190 [lb_av] (Body Weight)
/// - obs[3]: 120 mmHg (Systolic BP)
/// - obs[4]: 185 kg (Body Weight - same numeric value as obs[1], different unit)
///
/// For numeric comparisons:
/// - Distinct numeric values: 120, 180, 185, 185, 190
/// - Count at value 185: 2 (obs[1] [lb_av] and obs[4] kg)
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class QuantitySearchTests : CapabilityDrivenTestBase, IClassFixture<QuantitySearchTestFixture>
{
    private readonly QuantitySearchTestFixture _fixture;

    public QuantitySearchTests(IgnixaApiFixture apiFixture, QuantitySearchTestFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests greater-than comparison operator (gt).
    /// Should match observations with numeric values greater than 185.
    /// </summary>
    /// <remarks>
    /// gt185: matches only values > 185
    /// Expected: obs[2] (190 [lb_av])
    /// Not matched: obs[0] (180), obs[1] (185), obs[3] (120), obs[4] (185)
    /// </remarks>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithGreaterThan_ThenReturnsGreaterValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=gt185");

        // Assert
        results.Should().HaveCount(1, "gt185 should match only values > 185");
        results[0].Id.Should().Be(_fixture.Observations[2].Id, "should match obs[2] with value 190");
    }

    /// <summary>
    /// Tests greater-than-or-equal comparison operator (ge).
    /// Should match observations with numeric values >= 185.
    /// </summary>
    /// <remarks>
    /// ge185: matches values >= 185
    /// Expected: obs[1] (185 [lb_av]), obs[2] (190 [lb_av]), obs[4] (185 kg)
    /// Not matched: obs[0] (180), obs[3] (120)
    /// </remarks>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithGreaterOrEqual_ThenReturnsGreaterOrEqualValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=ge185");

        // Assert - includes both 185 [lb_av] and 185 kg
        results.Should().HaveCount(3, "ge185 should match values >= 185 (185 [lb_av], 185 kg, and 190)");

        var expectedIds = new[]
        {
            _fixture.Observations[1].Id, // 185 [lb_av]
            _fixture.Observations[2].Id, // 190 [lb_av]
            _fixture.Observations[4].Id  // 185 kg
        };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[1] (185 [lb_av]), obs[2] (190), and obs[4] (185 kg)");
    }

    /// <summary>
    /// Tests less-than comparison operator (lt).
    /// Should match observations with numeric values less than 185.
    /// </summary>
    /// <remarks>
    /// lt185: matches values less than 185
    /// Expected: obs[0] (180 [lb_av]), obs[3] (120 mmHg)
    /// Not matched: obs[1] (185), obs[2] (190), obs[4] (185)
    /// </remarks>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithLessThan_ThenReturnsLesserValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=lt185");

        // Assert
        results.Should().HaveCount(2, "lt185 should match values < 185 (120 and 180)");

        var expectedIds = new[]
        {
            _fixture.Observations[0].Id, // 180 [lb_av]
            _fixture.Observations[3].Id  // 120 mmHg
        };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[0] (180) and obs[3] (120)");
    }

    /// <summary>
    /// Tests less-than-or-equal comparison operator (le).
    /// Should match observations with numeric values less than or equal to 185.
    /// </summary>
    /// <remarks>
    /// le185: matches values less than or equal to 185
    /// Expected: obs[0] (180), obs[1] (185 [lb_av]), obs[3] (120), obs[4] (185 kg)
    /// Not matched: obs[2] (190)
    /// </remarks>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithLessOrEqual_ThenReturnsLessOrEqualValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=le185");

        // Assert - includes both 185 values plus lower ones
        results.Should().HaveCount(4, "le185 should match values <= 185 (120, 180, 185, 185)");

        var expectedIds = new[]
        {
            _fixture.Observations[0].Id, // 180 [lb_av]
            _fixture.Observations[1].Id, // 185 [lb_av]
            _fixture.Observations[3].Id, // 120 mmHg
            _fixture.Observations[4].Id  // 185 kg
        };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[0] (180), obs[1] (185), obs[3] (120), and obs[4] (185)");
    }

    /// <summary>
    /// Tests combining multiple quantity search parameters with different operators.
    /// Example: value-quantity=gt180&amp;value-quantity=lt190 creates a range query.
    /// </summary>
    /// <remarks>
    /// Range query: 180 less than x less than 190
    /// Expected: obs[1] (185 [lb_av]), obs[4] (185 kg)
    /// Not matched: obs[0] (180 - not greater), obs[2] (190 - not less), obs[3] (120 - outside range)
    /// </remarks>
    [Fact]
    public async Task GivenQuantities_WhenSearchedWithMultipleComparisons_ThenReturnsInRange()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for values > 180 AND < 190
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=gt180&value-quantity=lt190");

        // Assert
        results.Should().HaveCount(2, "range query (180 < x < 190) should match both 185 values");

        var expectedIds = new[]
        {
            _fixture.Observations[1].Id, // 185 [lb_av]
            _fixture.Observations[4].Id  // 185 kg
        };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[1] (185 [lb_av]) and obs[4] (185 kg)");
    }

    /// <summary>
    /// Tests search for value above all data.
    /// </summary>
    [Fact]
    public async Task GivenQuantities_WhenSearchedAboveAllValues_ThenReturnsEmpty()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=gt200");

        // Assert
        results.Should().BeEmpty("gt200 should return no results when maximum value is 190");
    }

    /// <summary>
    /// Tests search for value below all data.
    /// </summary>
    [Fact]
    public async Task GivenQuantities_WhenSearchedBelowAllValues_ThenReturnsEmpty()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=lt100");

        // Assert
        results.Should().BeEmpty("lt100 should return no results when minimum value is 120");
    }

    /// <summary>
    /// Tests that the quantity search parameter is recognized.
    /// Basic capability verification.
    /// </summary>
    [Fact]
    public async Task GivenQuantities_WhenSearchedWithValueOnlyBoundary_ThenReturnsCorrectCount()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - le190 should return all 5 observations
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=le190");

        // Assert
        results.Should().HaveCount(5, "le190 should match all 5 observations");
    }

    #region Skipped Tests - Unit/System Filtering Not Implemented

    /// <summary>
    /// Tests quantity search with explicit system and unit.
    /// Format: value|system|unit
    /// </summary>
    /// <remarks>
    /// SKIPPED: Unit/system filtering is not implemented.
    /// When implemented, this should match only 185 [lb_av], not 185 kg.
    /// </remarks>
    [Fact(Skip = "Unit/system filtering not implemented")]
    public async Task GivenQuantity_WhenSearchedWithSystemAndUnit_ThenReturnsMatchingSystemAndUnit()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for 185 [lb_av] from UCUM system
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=185|http://unitsofmeasure.org|[lb_av]");

        // Assert
        results.Should().HaveCount(1, "should match only 185 with UCUM [lb_av]");
        results[0].Id.Should().Be(_fixture.Observations[1].Id,
            "should match obs[1] (185 [lb_av]), not obs[4] (185 kg)");
    }

    /// <summary>
    /// Tests quantity search with unit only (empty system).
    /// Format: value||unit
    /// </summary>
    /// <remarks>
    /// SKIPPED: Unit filtering is not implemented.
    /// </remarks>
    [Fact(Skip = "Unit filtering not implemented")]
    public async Task GivenQuantity_WhenSearchedWithUnitOnly_ThenReturnsMatchingUnit()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for 185 with [lb_av] unit, any system
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=185||[lb_av]");

        // Assert
        results.Should().HaveCount(1, "should match 185 [lb_av] regardless of system");
        results[0].Id.Should().Be(_fixture.Observations[1].Id,
            "should match obs[1] with [lb_av] unit");
    }

    /// <summary>
    /// Tests quantity search differentiating by unit type.
    /// </summary>
    /// <remarks>
    /// SKIPPED: Unit filtering is not implemented.
    /// </remarks>
    [Fact(Skip = "Unit filtering not implemented")]
    public async Task GivenSameValueDifferentUnits_WhenSearchedByUnit_ThenReturnsCorrectUnit()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for 185 kg (not 185 [lb_av])
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=185|http://unitsofmeasure.org|kg");

        // Assert
        results.Should().HaveCount(1, "should match only 185 kg, not 185 [lb_av]");
        results[0].Id.Should().Be(_fixture.Observations[4].Id,
            "should match obs[4] (185 kg)");
    }

    #endregion
}
