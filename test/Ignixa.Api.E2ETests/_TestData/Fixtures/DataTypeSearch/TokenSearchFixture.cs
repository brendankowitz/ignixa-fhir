// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
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
    /// Observation test data with various token patterns.
    /// Index mapping:
    /// [0] = code="code1" (no system)
    /// [1] = system="system2", code="code2"
    /// [2] = text="text" (display only)
    /// [3] = text="text2" (display only)
    /// [4] = system="system3", code="code3", display="text"
    /// [5] = system="system1", code="code1" + system="system3", code="code2", text="text"
    /// [6] = system="system2", code="code1" + system="system3", code="code3", display="text2"
    /// [7] = code="code3" (no system)
    /// [8] = category with system="system", code="test" (for :not testing over missing values)
    /// [9] = identifier with case-sensitive values: "VALUE" and "value"
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Observations { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create observations with various token patterns using ObservationBuilder
        var observations = new[]
        {
            // [0] - code1 with system1 (single coding, no display)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code1", "system1")
                .Build(),

            // [1] - system2|code2 (single coding)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code2", "system2")
                .Build(),

            // [2] - text only (no coding, just text field)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithTextOnlyCodedValue("text")
                .Build(),

            // [3] - text2 display with system1|code2
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code2", "system1", "text2")
                .Build(),

            // [4] - system3|code3 with display "text"
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code3", "system3", "text")
                .Build(),

            // [5] - Multiple codings: system1|code1 + system3|code2, with text
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code1", "system1")
                .WithCodedValue("code2", "system3", null, "text")
                .Build(),

            // [6] - Multiple codings: system2|code1 + system3|code3 with display text2
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code1", "system2")
                .WithCodedValue("code3", "system3", "text2")
                .Build(),

            // [7] - code3 with no system
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithCodedValue("code3", null)
                .Build(),

            // [8] - Empty value-concept, but has category (for :not testing over missing values)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithEmptyCodedValue()
                .WithCategory("test", "system")
                .Build(),

            // [9] - Case-sensitive identifier test (empty value, multiple identifiers)
            ObservationBuilder.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithCode("code", "system")
                .WithStatus("registered")
                .WithEmptyCodedValue()
                .WithIdentifier("VALUE", "test")
                .WithIdentifier("value", "test")
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
