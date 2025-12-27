// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for canonical search tests.
/// Creates Observation test data with various profile URIs including versions and fragments
/// for testing canonical URI search patterns.
/// </summary>
public class CanonicalSearchFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public CanonicalSearchFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Base canonical URI used for profile testing.
    /// </summary>
    public string ObservationProfileUri { get; } = "http://example.org/fhir/StructureDefinition/observation-profile";

    /// <summary>
    /// Alternative canonical URI used for testing resources with multiple profiles.
    /// </summary>
    public string ObservationProfileUriAlternate { get; } = "http://example.org/fhir/StructureDefinition/observation-profile-alternate";

    /// <summary>
    /// Profile URI with version 1.
    /// </summary>
    public string ObservationProfileV1 => $"{ObservationProfileUri}|1";

    /// <summary>
    /// Profile URI with version 2.
    /// </summary>
    public string ObservationProfileV2 => $"{ObservationProfileUri}|2";

    /// <summary>
    /// Profile URI with version 2 and fragment.
    /// Format: http://example.org/fhir/StructureDefinition/observation-profile|2#section
    /// </summary>
    public string ObservationProfileV2WithFragment => $"{ObservationProfileUri}|2#section";

    /// <summary>
    /// Observation test data with various canonical profile URIs.
    /// Index mapping:
    /// [0] = Profile with version 1
    /// [1] = Profile with version 2
    /// [2] = Profile with version 2 and fragment
    /// [3] = Multiple profiles (base + alternate)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create observations with various canonical profile URIs
        var observations = new[]
        {
            // [0] - Profile with version 1: http://example.org/.../observation-profile|1
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("final")
                .WithProfile(ObservationProfileV1)
                .Build(),

            // [1] - Profile with version 2: http://example.org/.../observation-profile|2
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("final")
                .WithProfile(ObservationProfileV2)
                .Build(),

            // [2] - Profile with version 2 and fragment: http://example.org/.../observation-profile|2#section
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("final")
                .WithProfile(ObservationProfileV2WithFragment)
                .Build(),

            // [3] - Multiple profiles: base profile + alternate profile (no version)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("final")
                .WithProfile(ObservationProfileUri)
                .WithProfile(ObservationProfileUriAlternate)
                .Build()
        };

        Observations = await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }
}
