// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Search.Indexing;

public class ReferenceToTokenSearchValueConverterTests
{
    private readonly R4CoreSchemaProvider _schemaProvider = new();
    private readonly ReferenceToTokenSearchValueConverter _converter = new();

    [Fact]
    public void GivenReferenceWithIdentifier_WhenConverting_ThenIdentifierBecomesToken()
    {
        IElement reference = CreateSubject("""
            {
              "identifier": {
                "system": "http://example.org/mrn",
                "value": "1234",
                "type": {
                  "text": "Medical record number",
                  "coding": [
                    {
                      "system": "http://terminology.hl7.org/CodeSystem/v2-0203",
                      "code": "MR"
                    }
                  ]
                }
              }
            }
            """);

        TokenSearchValue token = _converter.ConvertTo(reference).ShouldHaveSingleItem().ShouldBeOfType<TokenSearchValue>();

        token.System.ShouldBe("http://example.org/mrn");
        token.Code.ShouldBe("1234");
        token.Text.ShouldBe("Medical record number");
        token.IdentifierTypeSystem.ShouldBe("http://terminology.hl7.org/CodeSystem/v2-0203");
        token.IdentifierTypeCode.ShouldBe("MR");
    }

    [Fact]
    public void GivenReferenceWithoutIdentifier_WhenConverting_ThenNoTokenIsProduced()
    {
        IElement reference = CreateSubject("""
            {
              "reference": "Patient/123"
            }
            """);

        IReadOnlyList<ISearchValue> tokens = _converter.ConvertTo(reference).ToList();

        tokens.ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferenceWithEmptyIdentifier_WhenConverting_ThenNoTokenIsProduced()
    {
        IElement reference = CreateSubject("""
            {
              "identifier": {}
            }
            """);

        IReadOnlyList<ISearchValue> tokens = _converter.ConvertTo(reference).ToList();

        tokens.ShouldBeEmpty();
    }

    [Fact]
    public void GivenReferenceIdentifierWithOnlySystem_WhenConverting_ThenNoTokenIsProduced()
    {
        IElement reference = CreateSubject("""
            {
              "identifier": {
                "system": "http://example.org/mrn"
              }
            }
            """);

        IReadOnlyList<ISearchValue> tokens = _converter.ConvertTo(reference).ToList();

        tokens.ShouldBeEmpty();
    }

    private IElement CreateSubject(string subject)
    {
        string json = $$"""
            {
              "resourceType": "Encounter",
              "status": "planned",
              "class": {
                "system": "http://terminology.hl7.org/CodeSystem/v3-ActCode",
                "code": "AMB"
              },
              "subject": {{subject}}
            }
            """;

        return ResourceJsonNode.Parse(json).ToElement(_schemaProvider).Select("subject").Single();
    }
}
