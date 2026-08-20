using Ignixa.Abstractions;
using Ignixa.Benchmarks.Firely5;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Extensions;

namespace Ignixa.FhirPath.Tests.Analysis;

public class ShippedSearchParameterEvaluationEvidenceTests
{
    [Fact]
    public void GivenConceptMapProductProperty_WhenEvaluated_ThenReturnsProperty()
    {
        const string json = """
            {
              "resourceType": "ConceptMap",
              "status": "active",
              "group": [{
                "element": [{
                  "code": "source",
                  "target": [{
                    "code": "target",
                    "product": [{
                      "property": "relationship",
                      "value": "equivalent"
                    }]
                  }]
                }]
              }]
            }
            """;

        var result = Evaluate(
            FhirVersion.R4,
            json,
            Expression(FhirVersion.R4, "http://hl7.org/fhir/SearchParameter/ConceptMap-product"));

        result.ShouldHaveSingleItem().Value.ShouldBe("relationship");
    }

    [Fact]
    public void GivenExcludedBodyStructure_WhenEvaluated_ThenReturnsStructure()
    {
        const string json = """
            {
              "resourceType": "BodyStructure",
              "patient": { "reference": "Patient/p1" },
              "includedStructure": {
                "structure": { "coding": [{ "code": "included" }] }
              },
              "excludedStructure": [{
                "structure": { "coding": [{ "code": "excluded" }] }
              }]
            }
            """;

        var result = Evaluate(
            FhirVersion.R5,
            json,
            Expression(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/BodyStructure-excludedstructure"));

        result.ShouldHaveSingleItem().Children("coding")
            .ShouldHaveSingleItem().Children("code")
            .ShouldHaveSingleItem().Value.ShouldBe("excluded");
    }

    [Fact]
    public void GivenNestedCompositionSections_WhenEvaluated_ThenReturnsBothNarratives()
    {
        const string json = """
            {
              "resourceType": "Composition",
              "status": "final",
              "type": { "coding": [{ "code": "document" }] },
              "date": "2026-01-01T00:00:00Z",
              "author": [{ "reference": "Practitioner/p1" }],
              "title": "Example",
              "section": [{
                "text": {
                  "status": "generated",
                  "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">outer</div>"
                },
                "section": [{
                  "text": {
                    "status": "generated",
                    "div": "<div xmlns=\"http://www.w3.org/1999/xhtml\">inner</div>"
                  }
                }]
              }]
            }
            """;

        var result = Evaluate(
            FhirVersion.R5,
            json,
            Expression(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/Composition-section-text"));

        result.Count.ShouldBe(2);
    }

    [Fact]
    public void GivenCarePlanPeriod_WhenClinicalDateEvaluated_ThenReturnsPeriod()
    {
        const string json = """
            {
              "resourceType": "CarePlan",
              "status": "active",
              "intent": "plan",
              "subject": { "reference": "Patient/p1" },
              "period": {
                "start": "2026-01-01",
                "end": "2026-01-31"
              }
            }
            """;

        var result = Evaluate(
            FhirVersion.R5,
            json,
            Expression(FhirVersion.R5, "http://hl7.org/fhir/SearchParameter/clinical-date"));

        result.ShouldHaveSingleItem().InstanceType.ShouldBe("Period");
    }

    [Theory]
    [InlineData("http://hl7.org/fhir/SearchParameter/Specimen-container-location", "Location/l1")]
    [InlineData("http://hl7.org/fhir/SearchParameter/Specimen-organization", "Organization/o1")]
    public void GivenSpecimenContainerDevice_WhenResolveExpressionEvaluated_ThenReturnsDeviceReference(
        string parameterUrl,
        string expectedReference)
    {
        const string specimenJson = """
            {
              "resourceType": "Specimen",
              "status": "available",
              "type": { "coding": [{ "code": "blood" }] },
              "subject": { "reference": "Patient/p1" },
              "container": [{
                "device": { "reference": "Device/d1" }
              }]
            }
            """;
        const string deviceJson = """
            {
              "resourceType": "Device",
              "id": "d1",
              "location": { "reference": "Location/l1" },
              "owner": { "reference": "Organization/o1" }
            }
            """;
        var schema = FhirVersion.R6.GetSchemaProvider();
        var specimen = ResourceJsonNode.Parse(specimenJson).ToElement(schema);
        var device = ResourceJsonNode.Parse(deviceJson).ToElement(schema);
        var expression = new FhirPathParser().Parse(Expression(FhirVersion.R6, parameterUrl));
        var context = new FhirEvaluationContext
        {
            Resource = specimen,
            RootResource = specimen,
            Schema = schema,
            ElementResolver = reference => reference == "Device/d1" ? device : null
        };

        var result = new FhirPathEvaluator().Evaluate(specimen, expression, context).ToList();

        result.ShouldHaveSingleItem().Children("reference")
            .ShouldHaveSingleItem().Value.ShouldBe(expectedReference);
    }

    private static List<IElement> Evaluate(FhirVersion version, string json, string expression)
    {
        var schema = version.GetSchemaProvider();
        var element = ResourceJsonNode.Parse(json).ToElement(schema);
        var parsed = new FhirPathParser().Parse(expression);
        var context = new FhirEvaluationContext
        {
            Resource = element,
            RootResource = element,
            Schema = schema
        };

        return new FhirPathEvaluator().Evaluate(element, parsed, context).ToList();
    }

    private static string Expression(FhirVersion version, string parameterUrl) =>
        SearchParameterExpressionCorpus.Load(version).Parameters
            .Single(parameter => parameter.Url.AbsoluteUri == parameterUrl)
            .Expression!;
}
