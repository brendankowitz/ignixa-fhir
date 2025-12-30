// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Shouldly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Definition;
using Ignixa.Specification.Generated;
using Ignixa.Serialization.SourceNodes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirPath.Evaluation;

namespace Ignixa.Application.Tests.Search.Indexing;

/// <summary>
/// Tests to verify that identifier type information is correctly
/// extracted during search indexing for the :of-type modifier support.
/// </summary>
public class IdentifierOfTypeIndexingTests
{
    private readonly R4CoreSchemaProvider _schemaProvider;
    private readonly ISearchIndexer _indexer;

    public IdentifierOfTypeIndexingTests()
    {
        _schemaProvider = new R4CoreSchemaProvider();
        var loggerFactory = NullLoggerFactory.Instance;

        var searchParamManager = new SearchParameterDefinitionManager(
            _schemaProvider,
            new NullLogger<SearchParameterDefinitionManager>());

        _indexer = SearchIndexerFactory.CreateInstance(
            _schemaProvider,
            loggerFactory,
            searchParamManager);
    }

    [Fact]
    public void GivenPatientWithTypedIdentifier_WhenIndexing_ThenTokenSearchValueHasIdentifierType()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithTypedIdentifier(
                value: "12345",
                typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                typeCode: "MR",
                typeDisplay: "Medical Record")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        // Assert
        var identifierIndices = indices
            .Where(i => i.SearchParameter.Code == "identifier")
            .ToList();

        identifierIndices.ShouldNotBeEmpty();

        var typedTokenValue = identifierIndices
            .Select(i => i.Value as TokenSearchValue)
            .FirstOrDefault(t => t?.Code == "12345");

        typedTokenValue.ShouldNotBeNull();
        typedTokenValue.IdentifierTypeSystem.ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        typedTokenValue.IdentifierTypeCode.ShouldBe("MR");
    }

    [Fact]
    public void GivenIdentifier_WhenConvertingDirectly_ThenTypeInfoExtracted()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithTypedIdentifier(
                value: "DIRECT-TEST",
                typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                typeCode: "MR",
                typeDisplay: "Medical Record")
            .Build();

        var patientElement = patient.ToElement(_schemaProvider);
        var identifiers = patientElement.Select("identifier").ToList();
        var identifier = identifiers[0];

        // Act
        var converter = new IdentifierToTokenSearchValueConverter();
        var searchValues = converter.ConvertTo(identifier).ToList();

        // Assert
        searchValues.ShouldNotBeEmpty();
        var tokenValue = searchValues[0] as TokenSearchValue;
        tokenValue.ShouldNotBeNull();
        tokenValue.Code.ShouldBe("DIRECT-TEST");
        tokenValue.IdentifierTypeSystem.ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        tokenValue.IdentifierTypeCode.ShouldBe("MR");
    }

    [Fact]
    public void GivenIdentifierWithType_WhenEvaluatingFhirPath_ThenTypeInfoExtracted()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(30)
            .WithGender("male")
            .WithTypedIdentifier(
                value: "SSN-123",
                typeSystem: "http://terminology.hl7.org/CodeSystem/v2-0203",
                typeCode: "SS",
                typeDisplay: "Social Security Number")
            .Build();

        var patientElement = patient.ToElement(_schemaProvider);
        var identifiers = patientElement.Select("identifier").ToList();
        var identifier = identifiers[0];

        // Act - Test FHIRPath expressions
        var value = identifier.Scalar("value") as string;
        var typeSystem = identifier.Scalar("type.coding.first().system") as string;
        var typeCode = identifier.Scalar("type.coding.first().code") as string;

        // Assert
        value.ShouldBe("SSN-123");
        typeSystem.ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        typeCode.ShouldBe("SS");
    }

    [Fact]
    public void GivenPatientWithMultipleTypedIdentifiers_WhenIndexing_ThenAllHaveTypeInfo()
    {
        // Arrange
        var patient = PatientBuilderFactory.Create(_schemaProvider)
            .WithAge(40)
            .WithGender("female")
            .WithTypedIdentifier("MR-12345", "http://terminology.hl7.org/CodeSystem/v2-0203", "MR", "Medical Record")
            .WithTypedIdentifier("123-45-6789", "http://terminology.hl7.org/CodeSystem/v2-0203", "SS", "Social Security")
            .WithTypedIdentifier("DL-ABC123", "http://terminology.hl7.org/CodeSystem/v2-0203", "DL", "Driver License")
            .Build();

        var element = patient.ToElement(_schemaProvider);

        // Act
        var indices = _indexer.Extract(element);

        var identifierTokens = indices
            .Where(i => i.SearchParameter.Code == "identifier")
            .Select(i => i.Value as TokenSearchValue)
            .Where(t => t is not null)
            .ToList();

        // Assert
        identifierTokens.Count.ShouldBeGreaterThanOrEqualTo(3);

        var mrToken = identifierTokens.FirstOrDefault(t => t!.Code == "MR-12345");
        mrToken.ShouldNotBeNull();
        mrToken!.IdentifierTypeCode.ShouldBe("MR");

        var ssToken = identifierTokens.FirstOrDefault(t => t!.Code == "123-45-6789");
        ssToken.ShouldNotBeNull();
        ssToken!.IdentifierTypeCode.ShouldBe("SS");

        var dlToken = identifierTokens.FirstOrDefault(t => t!.Code == "DL-ABC123");
        dlToken.ShouldNotBeNull();
        dlToken!.IdentifierTypeCode.ShouldBe("DL");
    }
}
