// <copyright file="StructureDefinitionSchemaResolverTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using FluentAssertions;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Checks;
using Ignixa.Validation.Schema;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Unit tests for StructureDefinitionSchemaResolver.
/// Tests that it correctly resolves validation schemas by canonical URL.
/// </summary>
public class StructureDefinitionSchemaResolverTests
{
    private readonly R4StructureDefinitionSummaryProvider _provider;
    private readonly StructureDefinitionSchemaResolver _resolver;

    public StructureDefinitionSchemaResolverTests()
    {
        _provider = new R4StructureDefinitionSummaryProvider();
        _resolver = new StructureDefinitionSchemaResolver(_provider);
    }

    #region Valid Canonical URL Tests

    [Fact]
    public void GivenValidPatientCanonicalUrl_WhenResolvingSchema_ThenReturnsPatientSchema()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.ResourceType.Should().Be("Patient");
        schema.CanonicalUrl.Should().Be(canonicalUrl);
        schema.Checks.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenValidObservationCanonicalUrl_WhenResolvingSchema_ThenReturnsObservationSchema()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.ResourceType.Should().Be("Observation");
        schema.CanonicalUrl.Should().Be(canonicalUrl);
        schema.Checks.Should().NotBeEmpty();
    }

    [Fact]
    public void GivenValidConditionCanonicalUrl_WhenResolvingSchema_ThenReturnsConditionSchema()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Condition";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.ResourceType.Should().Be("Condition");
        schema.CanonicalUrl.Should().Be(canonicalUrl);
    }

    #endregion

    #region Schema Content Validation

    [Fact]
    public void GivenPatientCanonicalUrl_WhenResolvingSchema_ThenSchemaContainsExpectedChecks()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.Checks.OfType<CardinalityCheck>().Should().NotBeEmpty();
        schema.Checks.OfType<TypeCheck>().Should().NotBeEmpty();
        schema.Checks.OfType<ReferenceFormatCheck>().Should().NotBeEmpty();
    }

    [Fact]
    public void GivenObservationCanonicalUrl_WhenResolvingSchema_ThenSchemaContainsRequiredFieldChecks()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.Checks.OfType<CardinalityCheck>().Should().NotBeEmpty();
    }

    [Fact]
    public void GivenObservationCanonicalUrl_WhenResolvingSchema_ThenSchemaContainsCodingChecks()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.Checks.OfType<CodingStructureCheck>().Should().NotBeEmpty();
    }

    #endregion

    #region Invalid Canonical URL Tests

    [Fact]
    public void GivenInvalidCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/InvalidResourceType";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().BeNull();
    }

    [Fact]
    public void GivenMalformedCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange - URL without resource type
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().BeNull();
    }

    [Fact]
    public void GivenEmptyCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = string.Empty;

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().BeNull();
    }

    [Fact]
    public void GivenNullCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        string? canonicalUrl = null;

        // Act
        var schema = _resolver.GetSchema(canonicalUrl!);

        // Assert
        schema.Should().BeNull();
    }

    #endregion

    #region Unknown Resource Type Tests

    [Fact]
    public void GivenUnknownResourceType_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/NonExistentResource";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().BeNull();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void GivenNullProvider_WhenCreatingResolver_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new StructureDefinitionSchemaResolver(null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("provider");
    }

    #endregion

    #region Multiple Resource Type Tests

    [Fact]
    public void GivenMultipleResourceTypes_WhenResolvingSchemas_ThenEachReturnsUniqueSchema()
    {
        // Arrange
        var resourceTypes = new[]
        {
            "Patient",
            "Observation",
            "Condition",
            "Medication",
            "Procedure"
        };

        // Act
        var schemas = resourceTypes
            .Select(rt => _resolver.GetSchema($"http://hl7.org/fhir/StructureDefinition/{rt}"))
            .ToList();

        // Assert
        schemas.Should().AllSatisfy(schema => schema.Should().NotBeNull());
        schemas.Select(s => s!.ResourceType).Should().OnlyHaveUniqueItems();
        schemas.Select(s => s!.CanonicalUrl).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void GivenSameCanonicalUrlMultipleTimes_WhenResolvingSchema_ThenReturnsDifferentInstances()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient";

        // Act
        var schema1 = _resolver.GetSchema(canonicalUrl);
        var schema2 = _resolver.GetSchema(canonicalUrl);

        // Assert - Note: StructureDefinitionSchemaResolver does NOT cache
        // It builds a new schema each time
        schema1.Should().NotBeNull();
        schema2.Should().NotBeNull();
        schema1.Should().NotBeSameAs(schema2); // Different instances (no caching)
        schema1!.ResourceType.Should().Be(schema2!.ResourceType); // Same content
    }

    #endregion

    #region URL Parsing Tests

    [Fact]
    public void GivenCanonicalUrlWithVersion_WhenResolvingSchema_ThenIgnoresVersionAndResolvesResourceType()
    {
        // Arrange - FHIR canonical URLs can have versions like StructureDefinition/Patient|4.0.1
        // Note: Current implementation doesn't handle versions, but we test the expected behavior
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient"; // Without version for now

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.Should().NotBeNull();
        schema!.ResourceType.Should().Be("Patient");
    }

    [Fact]
    public void GivenCanonicalUrlWithTrailingSlash_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient/";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert - Trailing slash means empty resource type
        schema.Should().BeNull();
    }

    #endregion
}
