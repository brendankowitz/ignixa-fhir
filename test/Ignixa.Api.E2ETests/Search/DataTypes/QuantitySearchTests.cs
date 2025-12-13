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
/// Tests FHIR quantity search with prefixes (eq, gt, ge, lt, le) and system/unit parameters.
/// Covers exact match, comparison operators, system+unit combinations, and unit-only searches.
/// </summary>
/// <remarks>
/// FHIR Quantity Search Semantics (http://hl7.org/fhir/search.html#quantity):
/// - eq (or no prefix): the value in the resource is equal to the provided value (default +/-5%)
/// - gt: the value in the resource is greater than the provided value
/// - ge: the value in the resource is greater or equal to the provided value
/// - lt: the value in the resource is less than the provided value
/// - le: the value in the resource is less or equal to the provided value
///
/// Quantity Parameter Format:
/// - Simple value: value-quantity=185
/// - With prefix: value-quantity=ge185
/// - With system and unit: value-quantity=185|http://unitsofmeasure.org|[lb_av]
/// - Unit only (empty system): value-quantity=185||[lb_av]
///
/// Test Data Setup:
/// - obs[0]: 180 [lb_av] (Body Weight - below test value)
/// - obs[1]: 185 [lb_av] (Body Weight - exact match)
/// - obs[2]: 190 [lb_av] (Body Weight - above test value)
/// - obs[3]: 120 mmHg (Systolic BP - different unit)
/// - obs[4]: 185 kg (Body Weight - same value, different unit)
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
    /// Tests exact match quantity search (implicit eq prefix).
    /// Should match observation with value exactly 185 [lb_av].
    /// </summary>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithExactValue_ThenReturnsExactMatch()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=185");

        // Assert
        results.Should().HaveCount(1, "exact match should return only the 185 [lb_av] observation");
        results[0].Id.Should().Be(_fixture.Observations[1].Id, "should match obs[1] with value 185");
    }

    /// <summary>
    /// Tests greater-than comparison operator (gt).
    /// Should match observations with values greater than 185.
    /// </summary>
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
    /// Should match observations with values greater than or equal to 185.
    /// </summary>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithGreaterOrEqual_ThenReturnsGreaterOrEqualValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=ge185");

        // Assert
        results.Should().HaveCount(2, "ge185 should match values >= 185 (185 and 190)");

        var expectedIds = new[] { _fixture.Observations[1].Id, _fixture.Observations[2].Id };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[1] (185) and obs[2] (190)");
    }

    /// <summary>
    /// Tests less-than comparison operator (lt).
    /// Should match observations with values less than 185.
    /// </summary>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithLessThan_ThenReturnsLesserValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=lt185");

        // Assert
        results.Should().HaveCount(1, "lt185 should match only values < 185");
        results[0].Id.Should().Be(_fixture.Observations[0].Id, "should match obs[0] with value 180");
    }

    /// <summary>
    /// Tests less-than-or-equal comparison operator (le).
    /// Should match observations with values less than or equal to 185.
    /// </summary>
    [Fact]
    public async Task GivenObservationsWithQuantities_WhenSearchedWithLessOrEqual_ThenReturnsLessOrEqualValues()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act
        var results = await Harness.SearchAsync("Observation", $"_tag={_fixture.Tag}&value-quantity=le185");

        // Assert
        results.Should().HaveCount(2, "le185 should match values <= 185 (180 and 185)");

        var expectedIds = new[] { _fixture.Observations[0].Id, _fixture.Observations[1].Id };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[0] (180) and obs[1] (185)");
    }

    /// <summary>
    /// Tests quantity search with explicit system and unit.
    /// Format: value|system|unit
    /// Should match only observations with the exact value, system, and unit.
    /// </summary>
    [Fact]
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
    /// Should match observations with the value and unit, regardless of system.
    /// </summary>
    [Fact]
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
    /// Tests quantity search with different unit using the same code system.
    /// Verifies that different units are correctly distinguished.
    /// </summary>
    [Fact]
    public async Task GivenQuantity_WhenSearchedWithDifferentUnit_ThenReturnsDifferentUnitOnly()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for 120 mmHg (blood pressure observation)
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=120|http://unitsofmeasure.org|mmHg");

        // Assert
        results.Should().HaveCount(1, "should match only the blood pressure observation");
        results[0].Id.Should().Be(_fixture.Observations[3].Id,
            "should match obs[3] (120 mmHg)");
    }

    /// <summary>
    /// Tests combining multiple quantity search parameters with different operators.
    /// Example: value-quantity=gt180&amp;value-quantity=lt190 creates a range query.
    /// </summary>
    [Fact]
    public async Task GivenQuantities_WhenSearchedWithMultipleComparisons_ThenReturnsInRange()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for values > 180 AND < 190
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=gt180&value-quantity=lt190");

        // Assert
        results.Should().HaveCount(1, "range query (180 < x < 190) should match only 185");
        results[0].Id.Should().Be(_fixture.Observations[1].Id,
            "should match obs[1] with value 185");
    }

    /// <summary>
    /// Tests that quantity search correctly filters by unit type.
    /// Different units with the same numeric value should not match.
    /// </summary>
    [Fact]
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

    /// <summary>
    /// Tests that greater-than-or-equal works correctly with system and unit.
    /// Combines comparison operator with explicit system/unit specification.
    /// </summary>
    [Fact]
    public async Task GivenQuantity_WhenSearchedWithComparisonAndSystemUnit_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Observation", "value-quantity");

        // Act - search for >= 185 [lb_av] from UCUM
        var results = await Harness.SearchAsync("Observation",
            $"_tag={_fixture.Tag}&value-quantity=ge185|http://unitsofmeasure.org|[lb_av]");

        // Assert
        results.Should().HaveCount(2, "ge185 [lb_av] should match 185 and 190");

        var expectedIds = new[] { _fixture.Observations[1].Id, _fixture.Observations[2].Id };
        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match obs[1] (185 [lb_av]) and obs[2] (190 [lb_av])");
    }
}
