/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for standard transform functions.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Abstractions;
using System.Text.Json.Nodes;
using Xunit;

#pragma warning disable CS8602 // Dereference of a possibly null reference - Test assertions handle nulls

namespace Ignixa.FhirMappingLanguage.Tests.Transforms;

public class StandardTransformsTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
        public TestTypedElement(string name, object? value = null, string instanceType = "string")
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public IType? Definition => null;

        public IReadOnlyList<IElement> Children(string? name) => new List<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    private class TestContext : ITransformContext
    {
        private readonly Dictionary<string, IElement> _sources = new();
        private readonly Dictionary<string, IElement> _targets = new();
        private readonly Dictionary<string, object> _variables = new();

        public IElement? GetSource(string name) => _sources.TryGetValue(name, out var value) ? value : null;
        public IElement? GetTarget(string name) => _targets.TryGetValue(name, out var value) ? value : null;
        public object? GetVariable(string name) => _variables.TryGetValue(name, out var value) ? value : null;
        public void SetVariable(string name, object value) => _variables[name] = value;

        public Func<string, IElement>? ResourceCreator { get; set; }
        public Func<string, IElement, IEnumerable<IElement>>? FhirPathEvaluator { get; set; }
        public Func<string, string, string, string?>? ConceptMapResolver { get; set; }

        public void SetSource(string name, IElement element) => _sources[name] = element;
        public void SetTarget(string name, IElement element) => _targets[name] = element;
    }

    #endregion

    #region Registry Tests

    [Theory]
    [InlineData("create")]
    [InlineData("copy")]
    [InlineData("uuid")]
    [InlineData("truncate")]
    [InlineData("escape")]
    [InlineData("cast")]
    [InlineData("append")]
    [InlineData("evaluate")]
    [InlineData("cc")]
    [InlineData("c")]
    [InlineData("qty")]
    [InlineData("id")]
    [InlineData("cp")]
    [InlineData("reference")]
    [InlineData("translate")]
    [InlineData("pointer")]
    [InlineData("dateOp")]
    public void GivenStandardTransformName_WhenGetting_ThenReturnsTransform(string name)
    {
        // Act
        var transform = StandardTransforms.Get(name);

        // Assert
        transform.ShouldNotBeNull();
        transform.Name.ShouldBe(name);
    }

    [Fact]
    public void GivenUnknownTransformName_WhenGetting_ThenReturnsNull()
    {
        // Act
        var result = StandardTransforms.Get("unknownTransform");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenAllTransforms_WhenListing_ThenReturns17Transforms()
    {
        // Act
        var transforms = StandardTransforms.All().ToList();

        // Assert
        transforms.Count.ShouldBe(17);
    }

    #endregion

    #region Core Transform Tests

    [Fact]
    public void GivenCreateTransform_WhenExecuting_ThenCallsResourceCreator()
    {
        // Arrange
        var context = new TestContext
        {
            ResourceCreator = typeName =>
            {
                typeName.ShouldBe("Patient");
                return new TestTypedElement("Patient", null, "Patient");
            }
        };
        var transform = StandardTransforms.Get("create");

        // Act
        var result = transform.Execute(new List<object> { "Patient" }, context);

        // Assert
        result.ShouldNotBeNull();
        result.ShouldBeOfType<TestTypedElement>();
        ((TestTypedElement)result).InstanceType.ShouldBe("Patient");
    }

    [Fact]
    public void GivenCopyTransform_WhenExecuting_ThenReturnsOriginalValue()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("copy");
        var original = "test value";

        // Act
        var result = transform.Execute(new List<object> { original }, context);

        // Assert
        result.ShouldBeSameAs(original);
    }

    [Fact]
    public void GivenUuidTransform_WhenExecuting_ThenReturnsValidGuid()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("uuid");

        // Act
        var result = transform.Execute(new List<object>(), context);

        // Assert
        result.ShouldBeOfType<string>();
        Guid.TryParse((string)result, out _).ShouldBeTrue();
    }

    #endregion

    #region String Transform Tests

    [Fact]
    public void GivenTruncateTransform_WhenStringLongerThanLimit_ThenTruncates()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("truncate");

        // Act
        var result = transform.Execute(new List<object> { "Hello World", 5 }, context);

        // Assert
        result.ShouldBe("Hello");
    }

    [Fact]
    public void GivenTruncateTransform_WhenStringShorterThanLimit_ThenReturnsOriginal()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("truncate");

        // Act
        var result = transform.Execute(new List<object> { "Hi", 10 }, context);

        // Assert
        result.ShouldBe("Hi");
    }

    [Theory]
    [InlineData("json", "test\"quote", "test\\\"quote")]
    [InlineData("xml", "<tag>", "&lt;tag&gt;")]
    [InlineData("html", "<b>text</b>", "&lt;b&gt;text&lt;/b&gt;")]
    public void GivenEscapeTransform_WhenExecuting_ThenEscapesCorrectly(string format, string input, string expected)
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("escape");

        // Act
        var result = transform.Execute(new List<object> { input, format }, context);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenAppendTransform_WhenExecuting_ThenAppendsValue()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("append");

        // Act
        var result = transform.Execute(new List<object> { "Hello", " World" }, context);

        // Assert
        result.ShouldBe("Hello World");
    }

    #endregion

    #region Type Conversion Tests

    [Theory]
    [InlineData("123", "integer", 123)]
    [InlineData("45.67", "decimal", 45.67)]
    [InlineData("true", "boolean", true)]
    [InlineData("test", "string", "test")]
    public void GivenCastTransform_WhenExecuting_ThenConvertsType(object input, string targetType, object expected)
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("cast");

        // Act
        var result = transform.Execute(new List<object> { input, targetType }, context);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenEvaluateTransform_WhenExecuting_ThenCallsFhirPathEvaluator()
    {
        // Arrange
        var sourceElement = new TestTypedElement("Patient", null, "Patient");
        var resultElement = new TestTypedElement("name", "John", "HumanName");

        var context = new TestContext
        {
            FhirPathEvaluator = (expression, element) =>
            {
                expression.ShouldBe("name.first()");
                element.ShouldBeSameAs(sourceElement);
                return new[] { resultElement };
            }
        };

        var transform = StandardTransforms.Get("evaluate");

        // Act
        var result = transform.Execute(new List<object> { sourceElement, "name.first()" }, context);

        // Assert
        result.ShouldBe("John");
    }

    #endregion

    #region FHIR-Specific Transform Tests

    [Fact]
    public void GivenCodeableConceptTransform_WhenExecutingWithSystemAndCode_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("cc");

        // Act
        var result = transform.Execute(new List<object> { "http://loinc.org", "1234-5" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["coding"].ShouldNotBeNull();
        var coding = obj["coding"]!.AsArray();
        coding.Count.ShouldBe(1);
        coding[0]!["system"]!.GetValue<string>().ShouldBe("http://loinc.org");
        coding[0]!["code"]!.GetValue<string>().ShouldBe("1234-5");
    }

    [Fact]
    public void GivenCodeableConceptTransform_WhenExecutingWithDisplay_ThenIncludesDisplay()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("cc");

        // Act
        var result = transform.Execute(new List<object> { "http://loinc.org", "1234-5", "Test Display" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        var coding = obj["coding"]!.AsArray();
        coding[0]!["display"]!.GetValue<string>().ShouldBe("Test Display");
    }

    [Fact]
    public void GivenCodingTransform_WhenExecuting_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("c");

        // Act
        var result = transform.Execute(new List<object> { "http://snomed.info/sct", "12345" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["system"]!.GetValue<string>().ShouldBe("http://snomed.info/sct");
        obj["code"]!.GetValue<string>().ShouldBe("12345");
    }

    [Fact]
    public void GivenQuantityTransform_WhenExecuting_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("qty");

        // Act
        var result = transform.Execute(new List<object> { 12.5, "mg" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["value"]!.GetValue<decimal>().ShouldBe(12.5m);
        obj["unit"]!.GetValue<string>().ShouldBe("mg");
    }

    [Fact]
    public void GivenIdentifierTransform_WhenExecuting_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("id");

        // Act
        var result = transform.Execute(new List<object> { "12345", "http://example.org/ids" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["system"]!.GetValue<string>().ShouldBe("http://example.org/ids");
        obj["value"]!.GetValue<string>().ShouldBe("12345");
    }

    [Fact]
    public void GivenContactPointTransform_WhenExecuting_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("cp");

        // Act
        var result = transform.Execute(new List<object> { "phone", "555-1234" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["system"]!.GetValue<string>().ShouldBe("phone");
        obj["value"]!.GetValue<string>().ShouldBe("555-1234");
    }

    [Fact]
    public void GivenReferenceTransform_WhenExecutingWithTypedElement_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var element = new TestTypedElement("Patient", "patient-123", "Patient");
        var transform = StandardTransforms.Get("reference");

        // Act
        var result = transform.Execute(new List<object> { element }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["reference"]!.GetValue<string>().ShouldBe("Patient/patient-123");
    }

    [Fact]
    public void GivenReferenceTransform_WhenExecutingWithString_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("reference");

        // Act
        var result = transform.Execute(new List<object> { "Observation/obs-456" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["reference"]!.GetValue<string>().ShouldBe("Observation/obs-456");
    }

    #endregion

    #region Terminology Transform Tests

    [Fact]
    public void GivenTranslateTransform_WhenExecuting_ThenCallsConceptMapResolver()
    {
        // Arrange
        var context = new TestContext
        {
            ConceptMapResolver = (conceptMapUrl, sourceSystem, sourceCode) =>
            {
                conceptMapUrl.ShouldBe("http://example.org/ConceptMap/test");
                sourceSystem.ShouldBe("http://loinc.org");
                sourceCode.ShouldBe("1234-5");
                return "http://snomed.info/sct|98765";
            }
        };
        var transform = StandardTransforms.Get("translate");

        // Act
        var result = transform.Execute(
            new List<object> { "http://example.org/ConceptMap/test", "http://loinc.org", "1234-5" },
            context);

        // Assert
        result.ShouldBe("http://snomed.info/sct|98765");
    }

    [Fact]
    public void GivenTranslateTransform_WhenNoMapping_ThenReturnsNull()
    {
        // Arrange
        var context = new TestContext
        {
            ConceptMapResolver = (_, _, _) => null
        };
        var transform = StandardTransforms.Get("translate");

        // Act
        var result = transform.Execute(
            new List<object> { "http://example.org/ConceptMap/test", "http://loinc.org", "1234-5" },
            context);

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region Utility Transform Tests

    [Fact]
    public void GivenPointerTransform_WhenExecuting_ThenReturnsJsonPointer()
    {
        // Arrange
        var context = new TestContext();
        var element = new TestTypedElement("name", null, "HumanName");
        var transform = StandardTransforms.Get("pointer");

        // Act
        var result = transform.Execute(new List<object> { element }, context);

        // Assert
        result.ShouldBeOfType<string>();
        var pointer = (string)result;
        pointer.ShouldStartWith("/");
    }

    [Theory]
    [InlineData("add", "2025-01-15", 7, "days", "2025-01-22")]
    [InlineData("subtract", "2025-01-15", 3, "days", "2025-01-12")]
    public void GivenDateOpTransform_WhenExecuting_ThenPerformsDateOperation(
        string operation, string dateStr, int amount, string unit, string expectedStr)
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("dateOp");
        var date = DateTime.Parse(dateStr);
        var expected = DateTime.Parse(expectedStr);

        // Act
        var result = transform.Execute(new List<object> { date, operation, amount, unit }, context);

        // Assert
        result.ShouldBeOfType<DateTime>();
        ((DateTime)result).Date.ShouldBe(expected.Date);
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public void GivenCreateTransform_WhenResourceCreatorNotSet_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var context = new TestContext(); // No ResourceCreator set
        var transform = StandardTransforms.Get("create");

        // Act
        var act = () => transform.Execute(new List<object> { "Patient" }, context);

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("ResourceCreator");
    }

    [Fact]
    public void GivenEvaluateTransform_WhenFhirPathEvaluatorNotSet_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var context = new TestContext(); // No FhirPathEvaluator set
        var element = new TestTypedElement("Patient");
        var transform = StandardTransforms.Get("evaluate");

        // Act
        var act = () => transform.Execute(new List<object> { element, "name.first()" }, context);

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("FhirPathEvaluator");
    }

    [Fact]
    public void GivenTranslateTransform_WhenConceptMapResolverNotSet_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var context = new TestContext(); // No ConceptMapResolver set
        var transform = StandardTransforms.Get("translate");

        // Act
        var act = () => transform.Execute(
            new List<object> { "http://example.org/ConceptMap/test", "http://loinc.org", "1234-5" },
            context);

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("ConceptMapResolver");
    }

    [Fact]
    public void GivenTruncateTransform_WhenInsufficientArguments_ThenThrowsArgumentException()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("truncate");

        // Act
        var act = () => transform.Execute(new List<object> { "test" }, context); // Missing length argument

        // Assert
        Should.Throw<ArgumentException>(act);
    }

    #endregion
}
