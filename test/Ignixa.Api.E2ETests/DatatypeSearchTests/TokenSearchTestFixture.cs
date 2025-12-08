// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.Api.E2ETests.Infrastructure;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests.DatatypeSearchTests;

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

        // Create observations with various token patterns
        var observations = new[]
        {
            // [0] - code1 with no system (system1 prefix)
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system1", "code1")
                };
            }),

            // [1] - system2|code2
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system2", "code2")
                };
            }),

            // [2] - text only
            CreateObservation(cc =>
            {
                cc["text"] = "text";
            }),

            // [3] - text2 display with system1|code2
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system1", "code2", "text2")
                };
            }),

            // [4] - system3|code3 with display "text"
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system3", "code3", "text")
                };
            }),

            // [5] - Multiple codings: system1|code1 + system3|code2, with text
            CreateObservation(cc =>
            {
                cc["text"] = "text";
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system1", "code1"),
                    CreateCoding("system3", "code2")
                };
            }),

            // [6] - Multiple codings: system2|code1 + system3|code3 with display text2
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding("system2", "code1"),
                    CreateCoding("system3", "code3", "text2")
                };
            }),

            // [7] - code3 with no system
            CreateObservation(cc =>
            {
                cc["coding"] = new JsonArray
                {
                    CreateCoding(null, "code3")
                };
            }),

            // [8] - Empty value-concept, but has category (for :not testing over missing values)
            CreateObservationWithCategory("system", "test"),

            // [9] - Case-sensitive identifier test
            CreateObservationWithIdentifiers()
        };

        Observations = await _apiFixture.Harness.CreateResourcesAsync(observations);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    private ResourceJsonNode CreateObservation(Action<JsonObject> valueConceptCustomizer)
    {
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = Guid.NewGuid().ToString()
        };

        // Set meta tag for isolation
        observation.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "testTag",
                    ["code"] = Tag
                }
            }
        };

        // Required fields
        observation.MutableNode["status"] = "registered";
        observation.MutableNode["code"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "system",
                    ["code"] = "code"
                }
            }
        };

        // Create value CodeableConcept
        var valueCodeableConcept = new JsonObject();
        valueConceptCustomizer(valueCodeableConcept);
        observation.MutableNode["valueCodeableConcept"] = valueCodeableConcept;

        return observation;
    }

    private ResourceJsonNode CreateObservationWithCategory(string system, string code)
    {
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = Guid.NewGuid().ToString()
        };

        // Set meta tag for isolation
        observation.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "testTag",
                    ["code"] = Tag
                }
            }
        };

        // Required fields
        observation.MutableNode["status"] = "registered";
        observation.MutableNode["code"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "system",
                    ["code"] = "code"
                }
            }
        };

        // Empty value (for testing :not over missing values)
        observation.MutableNode["valueCodeableConcept"] = new JsonObject();

        // Add category
        observation.MutableNode["category"] = new JsonArray
        {
            new JsonObject
            {
                ["coding"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["system"] = system,
                        ["code"] = code
                    }
                }
            }
        };

        return observation;
    }

    private ResourceJsonNode CreateObservationWithIdentifiers()
    {
        var observation = new ResourceJsonNode
        {
            ResourceType = "Observation",
            Id = Guid.NewGuid().ToString()
        };

        // Set meta tag for isolation
        observation.MutableNode["meta"] = new JsonObject
        {
            ["tag"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "testTag",
                    ["code"] = Tag
                }
            }
        };

        // Required fields
        observation.MutableNode["status"] = "registered";
        observation.MutableNode["code"] = new JsonObject
        {
            ["coding"] = new JsonArray
            {
                new JsonObject
                {
                    ["system"] = "system",
                    ["code"] = "code"
                }
            }
        };

        // Empty value
        observation.MutableNode["valueCodeableConcept"] = new JsonObject();

        // Add identifiers with case-sensitive values
        observation.MutableNode["identifier"] = new JsonArray
        {
            new JsonObject
            {
                ["system"] = "test",
                ["value"] = "VALUE"
            },
            new JsonObject
            {
                ["system"] = "test",
                ["value"] = "value"
            }
        };

        return observation;
    }

    private static JsonObject CreateCoding(string? system, string code, string? display = null)
    {
        var coding = new JsonObject();

        if (system is not null)
        {
            coding["system"] = system;
        }

        coding["code"] = code;

        if (display is not null)
        {
            coding["display"] = display;
        }

        return coding;
    }
}
