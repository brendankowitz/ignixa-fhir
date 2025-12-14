// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for number search tests.
/// Creates RiskAssessment resources with various probability values for testing
/// number search parameters with comparison operators (eq, gt, ge, lt, le).
/// </summary>
/// <remarks>
/// FHIR Number Search Semantics (http://hl7.org/fhir/search.html#number):
/// - Number search parameters support comparison prefixes (eq, gt, ge, lt, le)
/// - The 'probability' search parameter on RiskAssessment is a standard R4 number parameter
/// - Expression: RiskAssessment.prediction.probability
///
/// Test Data Setup (using exact decimal values to avoid precision issues):
/// Index mapping (probabilities as decimals 0.0 to 1.0):
/// [0] = probability = 0.125 (1/8 = exact in binary)
/// [1] = probability = 0.25 (1/4 = exact in binary)
/// [2] = probability = 0.375 (3/8 = exact in binary)
/// [3] = probability = 0.5 (1/2 = exact in binary)
/// [4] = probability = 0.625 (5/8 = exact in binary)
/// [5] = probability = 0.75 (3/4 = exact in binary)
/// [6] = no probability value (null/missing)
/// </remarks>
public class NumberSearchTestFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public NumberSearchTestFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// RiskAssessment test data with various probability values.
    /// See class-level remarks for index mapping.
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> RiskAssessments { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create RiskAssessments with exact decimal values (powers of 2 in denominator)
        var riskAssessments = new[]
        {
            // [0] - 0.125 (1/8)
            CreateRiskAssessmentWithProbability(0.125m),

            // [1] - 0.25 (1/4)
            CreateRiskAssessmentWithProbability(0.25m),

            // [2] - 0.375 (3/8)
            CreateRiskAssessmentWithProbability(0.375m),

            // [3] - 0.5 (1/2)
            CreateRiskAssessmentWithProbability(0.5m),

            // [4] - 0.625 (5/8)
            CreateRiskAssessmentWithProbability(0.625m),

            // [5] - 0.75 (3/4)
            CreateRiskAssessmentWithProbability(0.75m),

            // [6] - No probability value (tests :missing modifier)
            CreateRiskAssessmentWithoutProbability()
        };

        RiskAssessments = await _apiFixture.Harness.CreateResourcesAsync(riskAssessments);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    private ResourceJsonNode CreateRiskAssessmentWithProbability(decimal probability)
    {
        return new RiskAssessmentBuilder(_apiFixture.SchemaProvider)
            .WithProbability(probability)
            .WithTag(Tag)
            .Build();
    }

    private ResourceJsonNode CreateRiskAssessmentWithoutProbability()
    {
        return new RiskAssessmentBuilder(_apiFixture.SchemaProvider)
            .WithTag(Tag)
            .Build();
    }
}
