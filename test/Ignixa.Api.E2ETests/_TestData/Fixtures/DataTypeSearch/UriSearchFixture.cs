// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for URI search tests.
/// Creates ValueSet test data with various URI patterns for testing
/// URI search parameters with :above and :below modifiers.
/// </summary>
/// <remarks>
/// URI search modifiers:
/// - No modifier: exact match
/// - :below: hierarchical match (value starts with parameter, like prefix matching)
/// - :above: hierarchical match (parameter starts with value, like contains matching)
/// </remarks>
public class UriSearchFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    public UriSearchFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// ValueSet test data with various URI patterns.
    /// Index mapping (based on Microsoft's original test):
    /// [0] = http://hl7.org/fhir/ValueSet/v3-AcknowledgementDetailCode
    /// [1] = http://hl7.org/fhir/ValueSet/v3-AcknowledgementDetailType
    /// [2] = http://hl7.org/fhir/ValueSet/v3-ActClass
    /// [3] = HTTP://hl7.org/fhir/ValueSet/v3-ActClass (uppercase HTTP)
    /// [4] = urn:oid:2.16.840.1.113883.1.11.16929
    /// [5] = urn:oid:2.16.840.1.113883.1.11.16930
    /// [6] = urn:oid:2.16.840.1.113883.1.11.16931
    /// [7] = http://sample#data (fragment identifier)
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> ValueSets { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Create ValueSets with various URI patterns
        var valueSets = new[]
        {
            // [0] - Standard HL7 FHIR ValueSet URL
            CreateValueSet("http://hl7.org/fhir/ValueSet/v3-AcknowledgementDetailCode"),

            // [1] - Standard HL7 FHIR ValueSet URL (different resource)
            CreateValueSet("http://hl7.org/fhir/ValueSet/v3-AcknowledgementDetailType"),

            // [2] - Standard HL7 FHIR ValueSet URL (ActClass)
            CreateValueSet("http://hl7.org/fhir/ValueSet/v3-ActClass"),

            // [3] - Uppercase HTTP protocol (case sensitivity test)
            CreateValueSet("HTTP://hl7.org/fhir/ValueSet/v3-ActClass"),

            // [4] - URN OID format
            CreateValueSet("urn:oid:2.16.840.1.113883.1.11.16929"),

            // [5] - URN OID format (different OID)
            CreateValueSet("urn:oid:2.16.840.1.113883.1.11.16930"),

            // [6] - URN OID format (different OID)
            CreateValueSet("urn:oid:2.16.840.1.113883.1.11.16931"),

            // [7] - URL with fragment identifier
            CreateValueSet("http://sample#data")
        };

        ValueSets = await _apiFixture.Harness.CreateResourcesAsync(valueSets);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    private ResourceJsonNode CreateValueSet(string url)
    {
        return ValueSetBuilder.Create(_apiFixture.SchemaProvider)
            .WithUrl(url)
            .WithName($"TestValueSet_{Guid.NewGuid().ToString("N")[..8]}")
            .WithStatus("active")
            .WithTag(Tag)
            .Build();
    }
}
