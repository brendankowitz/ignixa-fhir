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
/// E2E tests for FHIR number search parameters with comparison operators.
/// Tests number search using Patient.multipleBirth field with prefixes (eq, gt, ge, lt, le).
/// Validates Proposal 1 from E2E test gap analysis: MultipleBirth support in PatientBuilder.
/// </summary>
/// <remarks>
/// FHIR Number Search Semantics (http://hl7.org/fhir/search.html#number):
/// - Number search parameters target numeric values (integer, decimal)
/// - Comparison operators:
///   - eq (or no prefix): exact match (default)
///   - gt: greater than
///   - ge: greater than or equal
///   - lt: less than
///   - le: less than or equal
/// - Multiple constraints using the same parameter create AND logic
///
/// Patient.multipleBirth Field:
/// - Can be multipleBirthInteger (birth order: 1, 2, 3, etc.)
/// - Can be multipleBirthBoolean (true/false indicator)
/// - Number searches ONLY match integer values, not boolean
///
/// Test Data Setup (see NumberSearchTestFixture):
/// - Patients[0]: multipleBirthInteger = 1
/// - Patients[1]: multipleBirthInteger = 2
/// - Patients[2]: multipleBirthInteger = 3
/// - Patients[3]: multipleBirthInteger = 4
/// - Patients[4]: multipleBirthBoolean = true
/// - Patients[5]: multipleBirthBoolean = false
/// - Patients[6]: no multipleBirth value
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class NumberSearchTests : CapabilityDrivenTestBase, IClassFixture<NumberSearchTestFixture>
{
    private readonly NumberSearchTestFixture _fixture;

    public NumberSearchTests(IgnixaApiFixture apiFixture, NumberSearchTestFixture fixture)
        : base(apiFixture)
    {
        _fixture = fixture ?? throw new ArgumentNullException(nameof(fixture));
    }

    /// <summary>
    /// Tests exact match number search (implicit eq prefix).
    /// Should match patient with multipleBirthInteger exactly equal to 3.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithExactValue_ThenReturnsExactMatch()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=3");

        // Assert
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[2].Id,
            "exact match 'multiplebirth=3' should return only patient with value 3");
    }

    /// <summary>
    /// Tests explicit eq prefix for exact match.
    /// Should behave identically to no prefix.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithEqPrefix_ThenReturnsExactMatch()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=eq3");

        // Assert
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[2].Id,
            "explicit eq prefix should match exact value 3");
    }

    /// <summary>
    /// Tests less-than-or-equal comparison operator (le).
    /// Should match patients with multipleBirthInteger less than or equal to 3.
    /// </summary>
    /// <remarks>
    /// Expected matches: Patients[0] (1), Patients[1] (2), Patients[2] (3)
    /// Not matched: Patients[3] (4), Patients[4-6] (boolean or null)
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithLessOrEqual_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=le3");

        // Assert
        results.Should().HaveCount(3, "le3 should match values 1, 2, and 3");

        var expectedIds = new[]
        {
            _fixture.Patients[0].Id, // 1
            _fixture.Patients[1].Id, // 2
            _fixture.Patients[2].Id  // 3
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 1, 2, and 3");
    }

    /// <summary>
    /// Tests less-than comparison operator (lt).
    /// Should match patients with multipleBirthInteger strictly less than 3.
    /// </summary>
    /// <remarks>
    /// Expected matches: Patients[0] (1), Patients[1] (2)
    /// Not matched: Patients[2] (3), Patients[3] (4), Patients[4-6] (boolean or null)
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithLessThan_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=lt3");

        // Assert
        results.Should().HaveCount(2, "lt3 should match values 1 and 2 only");

        var expectedIds = new[]
        {
            _fixture.Patients[0].Id, // 1
            _fixture.Patients[1].Id  // 2
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 1 and 2");
    }

    /// <summary>
    /// Tests greater-than-or-equal comparison operator (ge).
    /// Should match patients with multipleBirthInteger greater than or equal to 2.
    /// </summary>
    /// <remarks>
    /// Expected matches: Patients[1] (2), Patients[2] (3), Patients[3] (4)
    /// Not matched: Patients[0] (1), Patients[4-6] (boolean or null)
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithGreaterOrEqual_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=ge2");

        // Assert
        results.Should().HaveCount(3, "ge2 should match values 2, 3, and 4");

        var expectedIds = new[]
        {
            _fixture.Patients[1].Id, // 2
            _fixture.Patients[2].Id, // 3
            _fixture.Patients[3].Id  // 4
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 2, 3, and 4");
    }

    /// <summary>
    /// Tests greater-than comparison operator (gt).
    /// Should match patients with multipleBirthInteger strictly greater than 1.
    /// </summary>
    /// <remarks>
    /// Expected matches: Patients[1] (2), Patients[2] (3), Patients[3] (4)
    /// Not matched: Patients[0] (1), Patients[4-6] (boolean or null)
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithGreaterThan_ThenReturnsMatching()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=gt1");

        // Assert
        results.Should().HaveCount(3, "gt1 should match values 2, 3, and 4");

        var expectedIds = new[]
        {
            _fixture.Patients[1].Id, // 2
            _fixture.Patients[2].Id, // 3
            _fixture.Patients[3].Id  // 4
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 2, 3, and 4");
    }

    /// <summary>
    /// Tests that number search does NOT match boolean multipleBirth values.
    /// Only integer values should be matched by number search parameters.
    /// </summary>
    /// <remarks>
    /// Number searches should ignore:
    /// - Patients[4]: multipleBirthBoolean = true
    /// - Patients[5]: multipleBirthBoolean = false
    /// - Patients[6]: no multipleBirth value
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithBooleanMultipleBirth_WhenSearchedByNumber_ThenDoesNotMatch()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act - search for any integer value
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=ge1");

        // Assert - should only match integer values (1, 2, 3, 4)
        results.Should().HaveCount(4, "number search should only match integer values, not boolean");

        var expectedIds = new[]
        {
            _fixture.Patients[0].Id, // 1
            _fixture.Patients[1].Id, // 2
            _fixture.Patients[2].Id, // 3
            _fixture.Patients[3].Id  // 4
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match only patients with integer multipleBirth, not boolean or null");
    }

    /// <summary>
    /// Tests combining multiple number search parameters to create a range query.
    /// Multiple constraints on the same parameter create AND logic.
    /// </summary>
    /// <remarks>
    /// Query: multiplebirth=gt1 AND multiplebirth=lt4
    /// Expected matches: Patients[1] (2), Patients[2] (3)
    /// Range: 1 less than x less than 4 (i.e., x in {2, 3})
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithRange_ThenReturnsInRange()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act - search for values > 1 AND < 4
        var results = await Harness.SearchAsync("Patient",
            $"_tag={_fixture.Tag}&multiplebirth=gt1&multiplebirth=lt4");

        // Assert
        results.Should().HaveCount(2, "range query (1 < x < 4) should match 2 and 3");

        var expectedIds = new[]
        {
            _fixture.Patients[1].Id, // 2
            _fixture.Patients[2].Id  // 3
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 2 and 3");
    }

    /// <summary>
    /// Tests boundary condition: search for minimum value in dataset.
    /// Should match only the patient with multipleBirthInteger = 1.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedForMinimum_ThenReturnsMinimumOnly()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=1");

        // Assert
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[0].Id,
            "search for minimum value 1 should match only first patient");
    }

    /// <summary>
    /// Tests boundary condition: search for maximum value in dataset.
    /// Should match only the patient with multipleBirthInteger = 4.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedForMaximum_ThenReturnsMaximumOnly()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=4");

        // Assert
        results.Should().ContainSingle(r => r.Id == _fixture.Patients[3].Id,
            "search for maximum value 4 should match only fourth patient");
    }

    /// <summary>
    /// Tests search for value that doesn't exist in the dataset.
    /// Should return empty results.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedForNonExistentValue_ThenReturnsEmpty()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=99");

        // Assert
        results.Should().BeEmpty("search for non-existent value should return no results");
    }

    /// <summary>
    /// Tests that greater-than search for maximum value returns empty results.
    /// Edge case: gt4 when 4 is the maximum value in dataset.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedGreaterThanMaximum_ThenReturnsEmpty()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=gt4");

        // Assert
        results.Should().BeEmpty("gt4 should return no results when 4 is the maximum value");
    }

    /// <summary>
    /// Tests that less-than search for minimum value returns empty results.
    /// Edge case: lt1 when 1 is the minimum value in dataset.
    /// </summary>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedLessThanMinimum_ThenReturnsEmpty()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=lt1");

        // Assert
        results.Should().BeEmpty("lt1 should return no results when 1 is the minimum value");
    }

    /// <summary>
    /// Tests multiple value search with OR logic using comma separator.
    /// Should match patients with multipleBirthInteger equal to 1 OR 3.
    /// </summary>
    /// <remarks>
    /// FHIR spec: Comma-separated values create OR logic
    /// Query: multiplebirth=1,3
    /// Expected matches: Patients[0] (1), Patients[2] (3)
    /// </remarks>
    [Fact]
    public async Task GivenPatientsWithMultipleBirth_WhenSearchedWithMultipleValues_ThenReturnsAnyMatch()
    {
        // Capability check
        RequireSearchParameter("Patient", "multiplebirth");

        // Act - search for 1 OR 3
        var results = await Harness.SearchAsync("Patient", $"_tag={_fixture.Tag}&multiplebirth=1,3");

        // Assert
        results.Should().HaveCount(2, "comma-separated values should match either 1 OR 3");

        var expectedIds = new[]
        {
            _fixture.Patients[0].Id, // 1
            _fixture.Patients[2].Id  // 3
        };

        results.Select(r => r.Id).Should().BeEquivalentTo(expectedIds,
            "should match patients with multipleBirth 1 or 3");
    }
}
