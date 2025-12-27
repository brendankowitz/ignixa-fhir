// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Api.E2ETests._Infrastructure;
using Ignixa.FhirFakes.Builders;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Api.E2ETests._TestData.Fixtures.DataTypeSearch;

/// <summary>
/// Test fixture for token overflow tests.
/// Creates Patient resources with identifiers of various lengths to test token overflow handling.
/// Token values exceeding database column limits (typically 450 characters) are tested.
/// Ported from: Microsoft.Health.Fhir.Tests.E2E.Rest.Search.TokenOverflowTests
/// </summary>
public class TokenOverflowFixture : IAsyncLifetime
{
    private readonly IgnixaApiFixture _apiFixture;

    /// <summary>
    /// Maximum token length before overflow occurs (database column limit).
    /// </summary>
    private const int MaxTokenLength = 450;

    public TokenOverflowFixture(IgnixaApiFixture apiFixture)
    {
        _apiFixture = apiFixture ?? throw new ArgumentNullException(nameof(apiFixture));
    }

    /// <summary>
    /// Unique tag for isolating test data in this fixture.
    /// </summary>
    public string Tag { get; private set; } = null!;

    /// <summary>
    /// Patient resources with various identifier token lengths.
    /// Index mapping:
    /// [0] = PatientA - identifier with overflow (500+ chars), birthdate: 1990-01-15
    /// [1] = PatientB - identifier with overflow (500+ chars, different value), birthdate: 1985-06-20
    /// [2] = PatientC - identifier at max length (450 chars, no overflow), birthdate: 1992-03-10
    /// [3] = PatientD - identifier short (350 chars, no overflow), birthdate: 1988-11-05
    /// </summary>
    public IReadOnlyList<ResourceJsonNode> Patients { get; private set; } = null!;

    /// <summary>
    /// Identifier value A - with overflow (500+ chars).
    /// </summary>
    public string IdentifierA { get; private set; } = null!;

    /// <summary>
    /// Identifier value B - with overflow (500+ chars, different prefix).
    /// </summary>
    public string IdentifierB { get; private set; } = null!;

    /// <summary>
    /// Identifier value C - at max length (450 chars, no overflow).
    /// </summary>
    public string IdentifierC { get; private set; } = null!;

    /// <summary>
    /// Identifier value D - short (350 chars, no overflow).
    /// </summary>
    public string IdentifierD { get; private set; } = null!;

    /// <summary>
    /// Identifier system used for all test identifiers.
    /// </summary>
    public const string IdentifierSystem = "http://test.ignixa.io/overflow";

    public async Task InitializeAsync()
    {
        Tag = Guid.NewGuid().ToString();

        // Generate identifier values with various lengths
        IdentifierA = GetTokenValueWithOverflow("A");
        IdentifierB = GetTokenValueWithOverflow("B");
        IdentifierC = GetTokenValueMaxNoOverflow("C");
        IdentifierD = GetTokenValueShortNoOverflow("D");

        // Create patients with these identifiers
        var patients = new[]
        {
            // PatientA - overflow identifier
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("Overflow")
                .WithFamilyName("TestA")
                .WithBirthDate(1990, 1, 15)
                .WithTypedIdentifier(
                    value: IdentifierA,
                    typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                    typeCode: "MR",
                    typeDisplay: "Medical Record",
                    identifierSystem: IdentifierSystem)
                .Build(),

            // PatientB - overflow identifier (different value)
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("Overflow")
                .WithFamilyName("TestB")
                .WithBirthDate(1985, 6, 20)
                .WithTypedIdentifier(
                    value: IdentifierB,
                    typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                    typeCode: "MR",
                    typeDisplay: "Medical Record",
                    identifierSystem: IdentifierSystem)
                .Build(),

            // PatientC - max length no overflow
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("MaxLength")
                .WithFamilyName("TestC")
                .WithBirthDate(1992, 3, 10)
                .WithTypedIdentifier(
                    value: IdentifierC,
                    typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                    typeCode: "MR",
                    typeDisplay: "Medical Record",
                    identifierSystem: IdentifierSystem)
                .Build(),

            // PatientD - short no overflow
            PatientBuilderFactory.Create(_apiFixture.SchemaProvider)
                .WithTag(Tag)
                .WithGivenName("Short")
                .WithFamilyName("TestD")
                .WithBirthDate(1988, 11, 5)
                .WithTypedIdentifier(
                    value: IdentifierD,
                    typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                    typeCode: "MR",
                    typeDisplay: "Medical Record",
                    identifierSystem: IdentifierSystem)
                .Build()
        };

        Patients = await _apiFixture.Harness.CreateResourcesAsync(patients);
    }

    public Task DisposeAsync()
    {
        // Cleanup handled by tag isolation - no explicit cleanup needed
        return Task.CompletedTask;
    }

    /// <summary>
    /// Generates a token value with overflow (exceeds MaxTokenLength).
    /// Creates a value of 500+ characters to ensure overflow.
    /// </summary>
    private static string GetTokenValueWithOverflow(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix.Length > MaxTokenLength)
        {
            throw new ArgumentException($"Prefix length {prefix.Length} exceeds max token length {MaxTokenLength}", nameof(prefix));
        }

        // Pad to max length, then add overflow suffix
        var padded = prefix.PadRight(MaxTokenLength, 'x');
        var overflow = "overflow123456789012345678901234567890123456789012345"; // 50 chars overflow
        return padded + overflow;
    }

    /// <summary>
    /// Generates a token value at maximum length without overflow (exactly MaxTokenLength).
    /// </summary>
    private static string GetTokenValueMaxNoOverflow(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        if (prefix.Length > MaxTokenLength)
        {
            throw new ArgumentException($"Prefix length {prefix.Length} exceeds max token length {MaxTokenLength}", nameof(prefix));
        }

        return prefix.PadRight(MaxTokenLength, 'x');
    }

    /// <summary>
    /// Generates a token value that's 100 characters short of maximum (no overflow).
    /// </summary>
    private static string GetTokenValueShortNoOverflow(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var shortLength = MaxTokenLength - 100;

        if (prefix.Length > shortLength)
        {
            throw new ArgumentException($"Prefix length {prefix.Length} exceeds short length {shortLength}", nameof(prefix));
        }

        return prefix.PadRight(shortLength, 'x');
    }
}
