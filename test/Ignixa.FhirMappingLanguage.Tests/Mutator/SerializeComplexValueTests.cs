/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Regression tests for JsonNodeMutator's primitive-vs-complex classification.
 *
 * The FHIRPath engine represents Quantity as a wrapper that exposes a CLR object on Value
 * while reporting HasPrimitiveValue == false. Classifying on Value alone treated those as
 * primitives and serialized the CLR shape into the target resource.
 */

using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Mutator;

public class SerializeComplexValueTests
{
    private readonly JsonNodeMutator _mutator;
    private readonly IFhirSchemaProvider _schemaProvider;
    private readonly FhirPathEvaluator _evaluator = new();
    private readonly FhirPathParser _parser = new();

    public SerializeComplexValueTests()
    {
        _schemaProvider = FhirVersion.R4.GetSchemaProvider();
        _mutator = new JsonNodeMutator(_evaluator, _parser, () => _schemaProvider);
    }

    [Fact]
    public void GivenEngineQuantity_WhenSetProperty_ThenWritesFhirShapeNotClrShape()
    {
        // Arrange - a real engine-produced Quantity, not a stand-in
        var quantity = EvaluateSingle("4 'mg'");
        quantity.HasPrimitiveValue.ShouldBeFalse();
        quantity.Value.ShouldNotBeNull();

        var target = ResourceJsonNode.Parse("""
        {"resourceType": "Observation", "id": "target", "status": "final"}
        """);

        // Act
        _mutator.SetProperty(target, "Observation.valueQuantity", quantity, PropertyMutationMode.Replace);

        // Assert
        var written = target.MutableNode()["valueQuantity"]?.AsObject();
        written.ShouldNotBeNull();

        // Assert on raw text: JsonObject key lookup here is case-insensitive, so ContainsKey
        // cannot distinguish the FHIR "value" from the CLR "Value".
        var json = written.ToJsonString();
        json.ShouldNotContain("\"Value\"", Case.Sensitive, "CLR property names must not leak into the resource");
        json.ShouldNotContain("\"Precision\"", Case.Sensitive);

        written["value"]?.GetValue<decimal>().ShouldBe(4m);
        written["unit"]?.GetValue<string>().ShouldBe("mg");
        written["system"]?.GetValue<string>().ShouldBe("http://unitsofmeasure.org");
    }

    [Fact]
    public void GivenEngineQuantityNestedInComplexElement_WhenSetProperty_ThenWritesFhirShapeNotClrShape()
    {
        // Arrange - the same defect exists on the recursive child path, so cover it separately
        var quantity = EvaluateSingle("4 'mg'");
        var range = new SyntheticComplexElement("low", "Quantity", quantity);

        var target = ResourceJsonNode.Parse("""
        {"resourceType": "Observation", "id": "target", "status": "final"}
        """);

        // Act
        _mutator.SetProperty(target, "Observation.valueRange", range, PropertyMutationMode.Replace);

        // Assert
        var low = target.MutableNode()["valueRange"]?["low"]?.AsObject();
        low.ShouldNotBeNull();
        low.ToJsonString().ShouldNotContain(
            "\"Value\"",
            Case.Sensitive,
            "CLR property names must not leak into nested elements");
        low["value"]?.GetValue<decimal>().ShouldBe(4m);
        low["unit"]?.GetValue<string>().ShouldBe("mg");
    }

    [Fact]
    public void GivenSourceBackedPrimitive_WhenSetProperty_ThenStillWritesTheScalar()
    {
        // Arrange - guards the fix: gating on HasPrimitiveValue must not demote real primitives
        var source = ResourceJsonNode.Parse("""
        {"resourceType": "Patient", "id": "source", "birthDate": "1980-01-01"}
        """);
        var birthDate = source.ToElement(_schemaProvider).Children("birthDate")[0];
        birthDate.HasPrimitiveValue.ShouldBeTrue();

        var target = ResourceJsonNode.Parse("""{"resourceType": "Patient", "id": "target"}""");

        // Act
        _mutator.SetProperty(target, "Patient.birthDate", birthDate, PropertyMutationMode.Replace);

        // Assert - still a scalar, not degraded to an object by the new gate
        var written = target.MutableNode()["birthDate"];
        written.ShouldBeAssignableTo<JsonValue>();
        written!.GetValue<string>().ShouldBe("1980-01-01");
    }

    private IElement EvaluateSingle(string expression)
    {
        var root = ResourceJsonNode.Parse("""{"resourceType": "Observation", "id": "s", "status": "final"}""")
            .ToElement(_schemaProvider);

        return _evaluator.Evaluate(root, _parser.Parse(expression)).Single();
    }

    /// <summary>
    /// A complex element with no JsonNode backing, so serialization must recurse through
    /// <c>Children()</c> and classify each child on its own.
    /// </summary>
    private sealed class SyntheticComplexElement(string childName, string instanceType, IElement child)
        : IElement
    {
        public string Name => "value";

        public string InstanceType => instanceType;

        public object? Value => null;

        public string Location => string.Empty;

        public IType? Type => null;

        public bool HasPrimitiveValue => false;

        public IReadOnlyList<IElement> Children(string? name = null) =>
            name is null || name == childName ? [new NamedElement(childName, child)] : [];

        public T? Meta<T>() where T : class => null;
    }

    /// <summary>Renames an element so it can be placed under a chosen property.</summary>
    private sealed class NamedElement(string name, IElement inner) : IElement
    {
        public string Name => name;

        public string InstanceType => inner.InstanceType;

        public object? Value => inner.Value;

        public string Location => inner.Location;

        public IType? Type => inner.Type;

        public bool HasPrimitiveValue => inner.HasPrimitiveValue;

        public IReadOnlyList<IElement> Children(string? childName = null) => inner.Children(childName);

        public T? Meta<T>() where T : class => inner.Meta<T>();
    }
}
