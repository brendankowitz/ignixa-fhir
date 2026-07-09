// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Shouldly;
using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Api.E2ETests._Infrastructure.Base;
using Ignixa.Api.E2ETests._Infrastructure.Collections;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.Search.DataTypes;

/// <summary>
/// E2E tests for enhanced date precision support in the birthdate search parameter.
/// Tests that the server correctly handles year-only and month-only date searches.
/// </summary>
/// <remarks>
/// FHIR Date Precision Semantics:
/// - "1982" = year-only: matches all dates from 1982-01-01T00:00:00 to 1982-12-31T23:59:59.9999999
/// - "1982-01" = month-only: matches all dates from 1982-01-01T00:00:00 to 1982-01-31T23:59:59.9999999
/// - "1982-01-15" = day precision: matches all dates from 1982-01-15T00:00:00 to 1982-01-15T23:59:59.9999999
///
/// Search operators work with date ranges:
/// - eq: search value fully contains target value
/// - gt: target end is after search value end
/// - ge: target end is >= search value start
/// - lt: target start is before search value start
/// - le: target start is <= search value end
///
/// Gap Analysis Reference:
/// From fhir-candle gap analysis - enhanced date precision support needed for:
/// - Year-only searches: birthdate=1982
/// - Month-only searches: birthdate=1982-01
/// - Comparison operators with reduced precision: birthdate=gt1982, birthdate=ge1982-01
/// - OR logic with reduced precision: birthdate=1982,1985
/// </remarks>
[Collection(E2ETestCollection.Name)]
public class DatePrecisionTests : CapabilityDrivenTestBase
{
    private readonly string _testTag;

    public DatePrecisionTests(IgnixaApiFixture apiFixture)
        : base(apiFixture)
    {
        _testTag = Guid.NewGuid().ToString();
    }

    #region Year-Only Precision Tests

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingByYearOnly_ThenReturnsAllPatientsInThatYear()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Create patients with various birthdates in 1982
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1, 15).WithTag(_testTag).Build(),  // Jan 15
            CreatePatient().WithBirthDate(1982, 6, 20).WithTag(_testTag).Build(),  // Jun 20
            CreatePatient().WithBirthDate(1982, 12, 31).WithTag(_testTag).Build(), // Dec 31
            CreatePatient().WithBirthDate(1980, 5, 10).WithTag(_testTag).Build(),  // Different year
            CreatePatient().WithBirthDate(1985, 3, 25).WithTag(_testTag).Build()   // Different year
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982");

        // Assert
        results.Length.ShouldBe(3, "Should return all patients born in 1982");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[3].Id, "Should not match 1980");
        results.ShouldNotContain(r => r.Id == created[4].Id, "Should not match 1985");
    }

    [Fact]
    public async Task GivenPatientsWithYearOnlyBirthdates_WhenSearchingByYearOnly_ThenMatchesCorrectly()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Create patients with year-only precision birthdates
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),  // Year-only: 1982
            CreatePatient().WithBirthDate(1980).WithTag(_testTag).Build(),  // Year-only: 1980
            CreatePatient().WithBirthDate(1985).WithTag(_testTag).Build()   // Year-only: 1985
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982");

        // Assert
        results.Length.ShouldBe(1, "Should return only the patient born in 1982");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    [Fact]
    public async Task GivenPatientsWithMixedPrecisions_WhenSearchingByYearOnly_ThenMatchesAllPrecisionsInThatYear()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Mix of precisions all in 1982
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),         // Year-only
            CreatePatient().WithBirthDate(1982, 5).WithTag(_testTag).Build(),      // Month-only
            CreatePatient().WithBirthDate(1982, 5, 15).WithTag(_testTag).Build(),  // Full date
            CreatePatient().WithBirthDate(1983, 1, 1).WithTag(_testTag).Build()    // Different year
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982");

        // Assert
        results.Length.ShouldBe(3, "Should match year-only, month-only, and full date in 1982");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[3].Id);
    }

    #endregion

    #region Month-Only Precision Tests

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingByMonthOnly_ThenReturnsAllPatientsInThatMonth()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Create patients with various birthdates in January 1982
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1, 1).WithTag(_testTag).Build(),   // Jan 1
            CreatePatient().WithBirthDate(1982, 1, 15).WithTag(_testTag).Build(),  // Jan 15
            CreatePatient().WithBirthDate(1982, 1, 31).WithTag(_testTag).Build(),  // Jan 31
            CreatePatient().WithBirthDate(1982, 2, 1).WithTag(_testTag).Build(),   // Feb 1 (different month)
            CreatePatient().WithBirthDate(1983, 1, 15).WithTag(_testTag).Build()   // Jan 1983 (different year)
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-01");

        // Assert
        results.Length.ShouldBe(3, "Should return all patients born in January 1982");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[3].Id, "Should not match February");
        results.ShouldNotContain(r => r.Id == created[4].Id, "Should not match different year");
    }

    [Fact]
    public async Task GivenPatientsWithMonthOnlyBirthdates_WhenSearchingByMonthOnly_ThenMatchesCorrectly()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Create patients with month-only precision birthdates
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1).WithTag(_testTag).Build(),  // Month-only: Jan 1982
            CreatePatient().WithBirthDate(1982, 2).WithTag(_testTag).Build(),  // Month-only: Feb 1982
            CreatePatient().WithBirthDate(1982, 12).WithTag(_testTag).Build()  // Month-only: Dec 1982
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-01");

        // Assert
        results.Length.ShouldBe(1, "Should return only the patient born in January 1982");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    [Fact]
    public async Task GivenPatientsWithMixedPrecisions_WhenSearchingByMonthOnly_ThenMatchesAllPrecisionsInThatMonth()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Mix of precisions all in January 1982
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),         // Year-only (contains Jan)
            CreatePatient().WithBirthDate(1982, 1).WithTag(_testTag).Build(),      // Month-only: Jan
            CreatePatient().WithBirthDate(1982, 1, 15).WithTag(_testTag).Build(),  // Full date: Jan 15
            CreatePatient().WithBirthDate(1982, 2, 1).WithTag(_testTag).Build()    // Different month
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-01");

        // Assert
        results.Length.ShouldBe(3, "Should match year-only, month-only, and full date containing January 1982");
        results.ShouldContain(r => r.Id == created[0].Id, "Year 1982 contains January");
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[3].Id);
    }

    #endregion

    #region Boundary Tests

    [Fact]
    public async Task GivenPatientsAtYearBoundary_WhenSearchingByYear_ThenDoesNotMatchAdjacentYears()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Patients at year boundaries
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1981, 12, 31).WithTag(_testTag).Build(), // Last day of 1981
            CreatePatient().WithBirthDate(1982, 1, 1).WithTag(_testTag).Build(),   // First day of 1982
            CreatePatient().WithBirthDate(1982, 12, 31).WithTag(_testTag).Build(), // Last day of 1982
            CreatePatient().WithBirthDate(1983, 1, 1).WithTag(_testTag).Build()    // First day of 1983
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982");

        // Assert
        results.Length.ShouldBe(2, "Should match only 1982-01-01 and 1982-12-31");
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[0].Id, "1981-12-31 should not match 1982");
        results.ShouldNotContain(r => r.Id == created[3].Id, "1983-01-01 should not match 1982");
    }

    [Fact]
    public async Task GivenPatientsAtMonthBoundary_WhenSearchingByMonth_ThenDoesNotMatchAdjacentMonths()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Patients at month boundaries
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 4, 30).WithTag(_testTag).Build(),  // Last day of April
            CreatePatient().WithBirthDate(1982, 5, 1).WithTag(_testTag).Build(),   // First day of May
            CreatePatient().WithBirthDate(1982, 5, 31).WithTag(_testTag).Build(),  // Last day of May
            CreatePatient().WithBirthDate(1982, 6, 1).WithTag(_testTag).Build()    // First day of June
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-05");

        // Assert
        results.Length.ShouldBe(2, "Should match only May 1 and May 31");
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[0].Id, "April 30 should not match May");
        results.ShouldNotContain(r => r.Id == created[3].Id, "June 1 should not match May");
    }

    [Fact]
    public async Task GivenPatientsInLeapYearFebruary_WhenSearchingByMonth_ThenMatchesAllDaysIncluding29th()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange - Patients born in February of leap year 2000
        var patients = new[]
        {
            CreatePatient().WithBirthDate(2000, 2, 1).WithTag(_testTag).Build(),   // Feb 1
            CreatePatient().WithBirthDate(2000, 2, 28).WithTag(_testTag).Build(),  // Feb 28
            CreatePatient().WithBirthDate(2000, 2, 29).WithTag(_testTag).Build(),  // Feb 29 (leap year)
            CreatePatient().WithBirthDate(2000, 3, 1).WithTag(_testTag).Build()    // March 1
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=2000-02");

        // Assert
        results.Length.ShouldBe(3, "Should match all three days in February including leap day");
        results.ShouldContain(r => r.Id == created[0].Id);
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[3].Id);
    }

    #endregion

    #region Comparison Operator Tests

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingGreaterThanYear_ThenReturnsPatientsAfterThatYear()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1980).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1985).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - gt1982 means after end of 1982 (after 1982-12-31T23:59:59.9999999)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=gt1982");

        // Assert
        results.Length.ShouldBe(1, "Should return only patients born after 1982");
        results.ShouldContain(r => r.Id == created[2].Id, "1985 is after 1982");
        results.ShouldNotContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[1].Id);
    }

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingGreaterThanOrEqualYear_ThenReturnsPatientsInOrAfterThatYear()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1980).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1985).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - ge1982 means >= start of 1982 (>= 1982-01-01T00:00:00)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=ge1982");

        // Assert
        results.Length.ShouldBe(2, "Should return patients born in or after 1982");
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[0].Id);
    }

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingLessThanYear_ThenReturnsPatientsBeforeThatYear()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1980).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1985).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - lt1982 means before start of 1982 (before 1982-01-01T00:00:00)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=lt1982");

        // Assert
        results.Length.ShouldBe(1, "Should return only patients born before 1982");
        results.ShouldContain(r => r.Id == created[0].Id, "1980 is before 1982");
        results.ShouldNotContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingGreaterThanMonth_ThenReturnsPatientsAfterThatMonth()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 4).WithTag(_testTag).Build(),  // April
            CreatePatient().WithBirthDate(1982, 5).WithTag(_testTag).Build(),  // May
            CreatePatient().WithBirthDate(1982, 6).WithTag(_testTag).Build()   // June
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - gt1982-05 means after end of May 1982 (after 1982-05-31T23:59:59.9999999)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=gt1982-05");

        // Assert
        results.Length.ShouldBe(1, "Should return only patients born after May 1982");
        results.ShouldContain(r => r.Id == created[2].Id, "June is after May");
        results.ShouldNotContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[1].Id);
    }

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingLessThanMonth_ThenReturnsPatientsBeforeThatMonth()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 4).WithTag(_testTag).Build(),  // April
            CreatePatient().WithBirthDate(1982, 5).WithTag(_testTag).Build(),  // May
            CreatePatient().WithBirthDate(1982, 6).WithTag(_testTag).Build()   // June
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - lt1982-05 means before start of May 1982 (before 1982-05-01T00:00:00)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=lt1982-05");

        // Assert
        results.Length.ShouldBe(1, "Should return only patients born before May 1982");
        results.ShouldContain(r => r.Id == created[0].Id, "April is before May");
        results.ShouldNotContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    #endregion

    #region OR Logic Tests

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingWithORLogicYears_ThenReturnsAllMatchingYears()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1980).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1983).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1985).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - birthdate=1982,1985 means born in 1982 OR 1985
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982,1985");

        // Assert
        results.Length.ShouldBe(2, "Should return patients born in 1982 or 1985");
        results.ShouldContain(r => r.Id == created[1].Id, "1982 matches");
        results.ShouldContain(r => r.Id == created[3].Id, "1985 matches");
        results.ShouldNotContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    [Fact]
    public async Task GivenPatientsWithVariousBirthdates_WhenSearchingWithORLogicMonths_ThenReturnsAllMatchingMonths()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1).WithTag(_testTag).Build(),   // January
            CreatePatient().WithBirthDate(1982, 3).WithTag(_testTag).Build(),   // March
            CreatePatient().WithBirthDate(1982, 5).WithTag(_testTag).Build(),   // May
            CreatePatient().WithBirthDate(1982, 12).WithTag(_testTag).Build()   // December
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - birthdate=1982-01,1982-12 means January 1982 OR December 1982
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-01,1982-12");

        // Assert
        results.Length.ShouldBe(2, "Should return patients born in January or December 1982");
        results.ShouldContain(r => r.Id == created[0].Id, "January matches");
        results.ShouldContain(r => r.Id == created[3].Id, "December matches");
        results.ShouldNotContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    #endregion

    #region Full Date Baseline Tests (for comparison)

    [Fact]
    public async Task GivenPatientsWithFullDateBirthdates_WhenSearchingByFullDate_ThenMatchesExactDate()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1, 14).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982, 1, 15).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982, 1, 16).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=1982-01-15");

        // Assert
        results.Length.ShouldBe(1, "Should match only the exact date");
        results.ShouldContain(r => r.Id == created[1].Id);
        results.ShouldNotContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[2].Id);
    }

    [Fact]
    public async Task GivenPatientsWithFullDateBirthdates_WhenSearchingWithComparisonOperators_ThenMatchesCorrectly()
    {
        // Capability check
        RequireSearchParameter("Patient", "birthdate");

        // Arrange
        var patients = new[]
        {
            CreatePatient().WithBirthDate(1982, 1, 14).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982, 1, 15).WithTag(_testTag).Build(),
            CreatePatient().WithBirthDate(1982, 1, 16).WithTag(_testTag).Build()
        };

        var created = await Harness.CreateResourcesAsync(patients);

        // Act - gt1982-01-15 means after end of 1982-01-15 (after 1982-01-15T23:59:59.9999999)
        var results = await Harness.SearchAsync("Patient", $"_tag={_testTag}&birthdate=gt1982-01-15");

        // Assert
        results.Length.ShouldBe(1, "Should return only dates after Jan 15");
        results.ShouldContain(r => r.Id == created[2].Id);
        results.ShouldNotContain(r => r.Id == created[0].Id);
        results.ShouldNotContain(r => r.Id == created[1].Id);
    }

    #endregion

    #region Helper Methods

    private ResourceJsonNode CreatePatientWithBirthDate(string birthDateString)
    {
        var patient = CreatePatient()
            .WithGender("male")
            .WithTag(_testTag)
            .Build();

        // Manually override birthDate for custom string values
        patient.MutableNode["birthDate"] = birthDateString;

        return patient;
    }

    #endregion
}
