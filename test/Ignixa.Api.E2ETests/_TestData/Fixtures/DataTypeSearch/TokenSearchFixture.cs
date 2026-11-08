// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for token search tests.
/// Creates Observation test data with various token value combinations for testing
/// token search parameters with system|code|text patterns.
/// </summary>
public class TokenSearchTestFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public TokenSearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Observation test data with various token patterns; <see cref="TokenSearchObservations.Build"/> gives the index mapping.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();
        Observations = await _apiFixture.Harness.CreateResourcesAsync(TokenSearchObservations.Build(_apiFixture.SchemaProvider, Tag));
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }
}
