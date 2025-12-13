// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for date search tests.
/// Creates Observation test data with various date precisions for testing
/// date search parameters with different prefixes (eq, ne, lt, gt, le, ge, sa, eb).
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.DateSearchTestFixture
/// </summary>
public class DateSearchTestFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public DateSearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Observation test data with various date precisions.
    /// Index mapping:
    /// [0] = effectiveDateTime="1979-12-31" (before 1980 boundary)
    /// [1] = effectiveDateTime="1980" (year precision - entire 1980)
    /// [2] = effectiveDateTime="1980-05" (month precision - May 1980)
    /// [3] = effectiveDateTime="1980-05-11" (day precision)
    /// [4] = effectiveDateTime="1980-05-11T16:32:15" (second precision)
    /// [5] = effectiveDateTime="1980-05-11T16:32:15.500" (millisecond precision)
    /// [6] = effectiveDateTime="1981-01-01" (after 1980 boundary)
    /// [7] = effectivePeriod={start: "1980-05-16", end: "1980-05-17"} (period datatype)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create unique code for each test run to ensure isolation
        var testCode = Guid.NewGuid().ToString();

        // Create observations with various date precisions
        var observations = new[]
        {
            // [0] - 1979-12-31 (day precision)
            CreateObservationWithDateTime(testCode, "1979-12-31"),

            // [1] - 1980 (year precision - entire year 1980)
            CreateObservationWithDateTime(testCode, "1980"),

            // [2] - 1980-05 (month precision - entire May 1980)
            CreateObservationWithDateTime(testCode, "1980-05"),

            // [3] - 1980-05-11 (day precision)
            CreateObservationWithDateTime(testCode, "1980-05-11"),

            // [4] - 1980-05-11T16:32:15 (second precision)
            CreateObservationWithDateTime(testCode, "1980-05-11T16:32:15"),

            // [5] - 1980-05-11T16:32:15.500 (millisecond precision)
            CreateObservationWithDateTime(testCode, "1980-05-11T16:32:15.500"),

            // [6] - 1981-01-01 (day precision - after 1980)
            CreateObservationWithDateTime(testCode, "1981-01-01"),

            // [7] - Period from 1980-05-16 to 1980-05-17
            CreateObservationWithPeriod(testCode, "1980-05-16", "1980-05-17")
        };

        Observations = await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    /// <summary>
    /// Creates an Observation with effectiveDateTime.
    /// </summary>
    private ResourceJsonNode CreateObservationWithDateTime(string codeValue, string dateTime)
    {
        var faker = _apiFixture.Harness.CreateFaker();

        return ObservationBuilder.Create(faker.SchemaProvider)
            .WithTag(Tag)
            .WithCode(codeValue, "http://fhir-server-test/guid")
            .WithEffectiveDateTime(dateTime)
            .Build();
    }

    /// <summary>
    /// Creates an Observation with effectivePeriod (start and end dates).
    /// </summary>
    private ResourceJsonNode CreateObservationWithPeriod(string codeValue, string startDate, string endDate)
    {
        var faker = _apiFixture.Harness.CreateFaker();

        return ObservationBuilder.Create(faker.SchemaProvider)
            .WithTag(Tag)
            .WithCode(codeValue, "http://fhir-server-test/guid")
            .WithEffectivePeriod(startDate, endDate)
            .Build();
    }
}
