// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for string search tests.
/// Creates Patient test data with various name and address combinations for testing
/// string search parameters with :exact and :contains modifiers.
/// </summary>
public class StringSearchTestFixture : IAsyncLifetime
{
    internal const string LongString = "Lorem ipsum dolor sit amet consectetur adipiscing elit. Ut eget ultricies justo. Maecenas bibendum convallis sodales. Vestibulum quis molestie dui. Nulla porta elementum tristique. Aenean neque libero convallis sit amet dui ullamcorper congue lacinia erat. Sed finibus ex ac massa tincidunt tristique. In sed auctor massa. Proin cursus porttitor arcu. Maecenas a leo nunc. Sed pretium porta volutpat. In aliquet tempor sapien vitae laoreet nisl tempor ac. Vestibulum lacus leo luctus vitae pharetra at tempus ac diam. Integer at dui eu dolor gravida vehicula. Phasellus malesuada elit orci quis maximus purus consectetur ac. In semper consequat augue sit amet ultricies.";

    private readonly IgnixaApiFixture _apiFixture;

    public StringSearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Patient test data with various string search patterns.
    /// Index mapping:
    /// [0] = City "Seattle", Family "Smith", Given "Bea"
    /// [1] = City "Portland", Family "Williams"
    /// [2] = City "Vancouver", Family "Anderson"
    /// [3] = City LongString (500+ chars), Family "Murphy"
    /// [4] = City "Montreal", Family "Richard", Given "Bea"
    /// [5] = City "New York", Family "Muller"
    /// [6] = City "Portland", Family "Müller" (with accent)
    /// [7] = City "Moscow", Family "Richard,Muller" (escaped comma)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Patients { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create 8 patients with various string patterns
        var patients = new[]
        {
            // [0] - Seattle, Smith, Bea
            CreatePatient("Seattle", "Smith", "Bea"),

            // [1] - Portland, Williams
            CreatePatient("Portland", "Williams", null),

            // [2] - Vancouver, Anderson
            CreatePatient("Vancouver", "Anderson", null),

            // [3] - LongString city, Murphy
            CreatePatient(LongString, "Murphy", null),

            // [4] - Montreal, Richard, Bea
            CreatePatient("Montreal", "Richard", "Bea"),

            // [5] - New York, Muller
            CreatePatient("New York", "Muller", null),

            // [6] - Portland, Müller (with accent)
            CreatePatient("Portland", "Müller", null),

            // [7] - Moscow, Richard,Muller (comma in name)
            CreatePatient("Moscow", "Richard,Muller", null)
        };

        Patients = await _apiFixture.Harness.CreateResourcesAsync(patients);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    private ResourceJsonNode CreatePatient(string city, string family, string? given)
    {
        var builder = PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
            .WithCity(city)
            .WithFamilyName(family)
            .WithTag(Tag);

        if (given is not null)
        {
            builder = builder.WithGivenName(given);
        }

        return builder.Build();
    }
}
