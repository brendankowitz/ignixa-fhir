using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirPath.Tests.Evaluation;

/// <summary>
/// BUG #188: is() function lacks FHIR resource type inheritance hierarchy checking.
/// These tests verify is() supports type inheritance per FHIRPath spec:
/// - Patient.is(DomainResource) returns true
/// - Patient.is(Resource) returns true
/// - Bundle.is(Resource) returns true
/// - Age.is(Quantity) returns true
/// </summary>

public class IsTypeHierarchyRegressionTests
{
    private readonly FhirPathParser _parser = new();
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly IFhirSchemaProvider _schemaProvider = FhirVersion.R4.GetSchemaProvider();

    [Fact]
    public void GivenPatient_WhenIsPatient_ThenReturnsTrue()
    {
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "active": true,
          "name": [{"family": "Smith", "given": ["John"]}]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "is(Patient)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenPatient_WhenIsDomainResource_ThenShouldReturnTrue()
    {
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "active": true,
          "name": [{"family": "Smith", "given": ["John"]}]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "is(DomainResource)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenPatient_WhenIsResource_ThenShouldReturnTrue()
    {
        var patientJson = """
        {
          "resourceType": "Patient",
          "id": "example",
          "active": true,
          "name": [{"family": "Smith", "given": ["John"]}]
        }
        """;

        var resource = ResourceJsonNode.Parse(patientJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "is(Resource)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenBundle_WhenIsResource_ThenShouldReturnTrue()
    {
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "id": "example",
          "type": "searchset"
        }
        """;

        var resource = ResourceJsonNode.Parse(bundleJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "is(Resource)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenBundle_WhenIsDomainResource_ThenShouldReturnFalse()
    {
        var bundleJson = """
        {
          "resourceType": "Bundle",
          "id": "example",
          "type": "searchset"
        }
        """;

        var resource = ResourceJsonNode.Parse(bundleJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "is(DomainResource)");

        result.Single().Value.ShouldBe(false);
    }

    [Fact]
    public void GivenAge_WhenIsAge_ThenReturnsTrue()
    {
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "example",
          "status": "final",
          "code": {"text": "test"},
          "valueAge": {"value": 42, "unit": "years", "system": "http://unitsofmeasure.org", "code": "a"}
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "value.is(Age)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenAge_WhenIsQuantity_ThenShouldReturnTrue()
    {
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "example",
          "status": "final",
          "code": {"text": "test"},
          "valueAge": {"value": 42, "unit": "years", "system": "http://unitsofmeasure.org", "code": "a"}
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "value.is(Quantity)");

        result.Single().Value.ShouldBe(true);
    }

    [Fact]
    public void GivenCode_WhenIsString_ThenReturnsTrue()
    {
        var observationJson = """
        {
          "resourceType": "Observation",
          "id": "example",
          "status": "final",
          "code": {"text": "test"}
        }
        """;

        var resource = ResourceJsonNode.Parse(observationJson);
        var element = resource.ToElement(_schemaProvider);

        var result = EvaluatePath(element, "status.is(string)");

        result.Single().Value.ShouldBe(true);
    }

    private IEnumerable<IElement> EvaluatePath(IElement element, string pathExpression)
    {
        var expression = _parser.Parse(pathExpression);
        return _evaluator.Evaluate(element, expression, new EvaluationContext());
    }
}
