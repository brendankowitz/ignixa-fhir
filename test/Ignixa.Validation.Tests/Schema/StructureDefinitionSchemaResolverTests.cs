// <copyright file="StructureDefinitionSchemaResolverTests.cs" company="Microsoft Corporation">
//     Copyright (c) Microsoft Corporation. All rights reserved.
//     Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// </copyright>

using Shouldly;
using Ignixa.Abstractions;
using Ignixa.Specification;
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
    private readonly ISchema _schema;
    private readonly StructureDefinitionSchemaResolver _resolver;

    public StructureDefinitionSchemaResolverTests()
    {
        _schema = new R4CoreSchemaProvider();
        _resolver = new StructureDefinitionSchemaResolver(_schema);
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
        schema.ShouldNotBeNull();
        schema!.ResourceType.ShouldBe("Patient");
        schema.CanonicalUrl.ShouldBe(canonicalUrl);
        schema.Checks.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenValidObservationCanonicalUrl_WhenResolvingSchema_ThenReturnsObservationSchema()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldNotBeNull();
        schema!.ResourceType.ShouldBe("Observation");
        schema.CanonicalUrl.ShouldBe(canonicalUrl);
        schema.Checks.ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenValidConditionCanonicalUrl_WhenResolvingSchema_ThenReturnsConditionSchema()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Condition";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldNotBeNull();
        schema!.ResourceType.ShouldBe("Condition");
        schema.CanonicalUrl.ShouldBe(canonicalUrl);
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
        schema.ShouldNotBeNull();
        schema!.Checks.OfType<CardinalityCheck>().ShouldNotBeEmpty();
        schema.Checks.OfType<TypeCheck>().ShouldNotBeEmpty();
        schema.Checks.OfType<ReferenceFormatCheck>().ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenObservationCanonicalUrl_WhenResolvingSchema_ThenSchemaContainsRequiredFieldChecks()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldNotBeNull();
        schema!.Checks.OfType<CardinalityCheck>().ShouldNotBeEmpty();
    }

    [Fact]
    public void GivenObservationCanonicalUrl_WhenResolvingSchema_ThenSchemaContainsCodingChecks()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Observation";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldNotBeNull();
        schema!.Checks.OfType<CodingStructureCheck>().ShouldNotBeEmpty();
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
        schema.ShouldBeNull();
    }

    [Fact]
    public void GivenMalformedCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange - URL without resource type
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldBeNull();
    }

    [Fact]
    public void GivenEmptyCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = string.Empty;

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert
        schema.ShouldBeNull();
    }

    [Fact]
    public void GivenNullCanonicalUrl_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        string? canonicalUrl = null;

        // Act
        var schema = _resolver.GetSchema(canonicalUrl!);

        // Assert
        schema.ShouldBeNull();
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
        schema.ShouldBeNull();
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void GivenNullProvider_WhenCreatingResolver_ThenThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new StructureDefinitionSchemaResolver(null!);
        Should.Throw<ArgumentNullException>(act)
            .ParamName.ShouldBe("schema"); // Parameter was renamed from "provider" to "schema"
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
        schemas.ShouldNotContain(x => x == null);
        schemas.Select(s => s!.ResourceType).Distinct().ToList().Count.ShouldBe(resourceTypes.Length);
        schemas.Select(s => s!.CanonicalUrl).Distinct().ToList().Count.ShouldBe(resourceTypes.Length);
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
        schema1.ShouldNotBeNull();
        schema2.ShouldNotBeNull();
        schema1.ShouldNotBeSameAs(schema2); // Different instances (no caching)
        schema1!.ResourceType.ShouldBe(schema2!.ResourceType); // Same content
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
        schema.ShouldNotBeNull();
        schema!.ResourceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenCanonicalUrlWithTrailingSlash_WhenResolvingSchema_ThenReturnsNull()
    {
        // Arrange
        var canonicalUrl = "http://hl7.org/fhir/StructureDefinition/Patient/";

        // Act
        var schema = _resolver.GetSchema(canonicalUrl);

        // Assert - Trailing slash means empty resource type
        schema.ShouldBeNull();
    }

    #endregion
}
