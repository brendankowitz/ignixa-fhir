// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using Ignixa.Specification.Generated;
using Ignixa.Anonymizer.Tools;

namespace Ignixa.Anonymizer.Tests.Tools;

public class ReferenceToolTests
{
    private static readonly R4CoreSchemaProvider Schema = new();
    private static readonly Func<string, string> Transformation = _ => "@ID";

    public static IEnumerable<object[]> GetKnownReferenceData()
    {
        yield return ["#p1", "#@ID"];
        yield return ["Patient/034AB16", "Patient/@ID"];
        yield return ["http://fhir.hl7.org/svc/StructureDefinition/c8973a22-2b5b-4e76-9c66-00639c99e61b", "http://fhir.hl7.org/svc/StructureDefinition/@ID"];
        yield return ["http://example.org/fhir/Observation/apo89654/_history/2", "http://example.org/fhir/Observation/@ID/_history/2"];
        yield return ["urn:uuid:C757873D-EC9A-4326-A141-556F43239520", "urn:uuid:@ID"];
        yield return ["urn:oid:1.2.3.4.5", "urn:oid:@ID"];
    }

    public static IEnumerable<object[]> GetUnknownReferenceData()
    {
        yield return ["034AB16", "@ID"];
        yield return ["Patient/AbcW??", "@ID"];
        yield return ["http://fhir.hl7.org/svc/StructureDefinitionTest/c8973a22-2b5b-4e76-9c66-00639c99e61b", "@ID"];
        yield return ["ftp://fhir.hl7.org/svc/StructureDefinition/c8973a22-2b5b-4e76-9c66-00639c99e61b", "@ID"];
        yield return ["wwurn:uuid:c757873d-ec9a-4326-a141-556f43239520", "@ID"];
        yield return ["urn:oid:1.2.3=4.5", "@ID"];
    }

    [Theory]
    [MemberData(nameof(GetKnownReferenceData))]
    public void GivenAKnownReference_WhenTransform_ThenCorrectPartShouldBeTransformed(string reference, string expected)
    {
        // Act
        var newReference = ReferenceTool.TransformReferenceId(reference, Schema, Transformation);

        // Assert
        newReference.ShouldBe(expected);
    }

    [Theory]
    [MemberData(nameof(GetUnknownReferenceData))]
    public void GivenAnUnknownReference_WhenTransform_ThenWholeReferenceShouldBeTransformed(string reference, string expected)
    {
        // Act
        var newReference = ReferenceTool.TransformReferenceId(reference, Schema, Transformation);

        // Assert
        newReference.ShouldBe(expected);
    }
}
