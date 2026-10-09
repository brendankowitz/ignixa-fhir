/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for standard transform functions.
 */

using System.Globalization;
using System.Text.Json.Nodes;
using Shouldly;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Transforms;
using Ignixa.Abstractions;
using Xunit;

#pragma warning disable CS8602 // Dereference of a possibly null reference - Test assertions handle nulls

namespace Ignixa.FhirMappingLanguage.Tests.Transforms;

public class StandardTransformsTests
{
    #region Helper Classes

    private class TestTypedElement : IElement
    {
        private readonly IReadOnlyList<IElement> _children;

        public TestTypedElement(
            string name,
            object? value = null,
            string instanceType = "string",
            IReadOnlyList<IElement>? children = null)
        {
            Name = name;
            Value = value;
            InstanceType = instanceType;
            _children = children ?? [];
        }

        public string Name { get; }
        public string InstanceType { get; }
        public object? Value { get; }
        public string Location => string.Empty;
        public IType? Type => null;
        public IType? Definition => null;
        public bool HasPrimitiveValue => Value != null;

        public IReadOnlyList<IElement> Children(string? name) =>
            name is null ? _children : _children.Where(child => child.Name == name).ToList();

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
        public Func<object, string, string, object?>? TranslationResolver { get; set; }

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

    [Fact]
    public void GivenAppendTransform_WhenConcatenatingSeveralValues_ThenPreservesWhitespaceAndEveryArgument()
    {
        var transform = StandardTransforms.Get("append")!;

        var result = transform.Execute(
            [new TestTypedElement("family", "Append"), " ", 5, "blog"],
            new TestContext());

        result.ShouldBe("Append 5blog");
    }

    [Fact]
    public void GivenAppendTransform_WhenExecutingWithOneValue_ThenReturnsItsPrimitiveText()
    {
        var result = StandardTransforms.Get("append")!.Execute(
            [new TestTypedElement("active", true, "boolean")],
            new TestContext());

        result.ShouldBe("true");
    }

    #endregion

    #region Type Conversion Tests

    [Theory]
    [InlineData("123", "integer", 123)]
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
    public void GivenNonEnglishCulture_WhenConvertingTransformValues_ThenUsesInvariantFhirLexicalForms()
    {
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var context = new TestContext();

            var cast = StandardTransforms.Get("cast")!.Execute(
                [new TestTypedElement("value", "45.67"), "FHIR.decimal"], context);
            var append = StandardTransforms.Get("append")!.Execute([45.67m, " ", false], context);
            var quantity = StandardTransforms.Get("qty")!.Execute(["45.67", "mg"], context);

            cast.ShouldBeOfType<decimal>().ShouldBe(45.67m);
            append.ShouldBe("45.67 false");
            quantity.ShouldBeOfType<JsonObject>()["value"]!.GetValue<decimal>().ShouldBe(45.67m);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
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
    public void GivenCodeableConceptTransform_WhenExecutingWithTextOnly_ThenCreatesOnlyText()
    {
        var result = StandardTransforms.Get("cc")!.Execute(
            [new TestTypedElement("text", "Non coded text")], new TestContext());

        JsonNode.DeepEquals(
            result.ShouldBeOfType<JsonObject>(),
            JsonNode.Parse("""{"text":"Non coded text"}""")).ShouldBeTrue();
    }

    [Fact]
    public void GivenCodeableConceptTransform_WhenNoDisplayIsProvided_ThenOmitsDisplayProperty()
    {
        var result = StandardTransforms.Get("cc")!.Execute(
            ["http://example.org/cs", "example"], new TestContext());

        JsonNode.DeepEquals(
            result.ShouldBeOfType<JsonObject>(),
            JsonNode.Parse("""{"coding":[{"system":"http://example.org/cs","code":"example"}]}""")).ShouldBeTrue();
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
    public void GivenQuantityTransform_WhenCodeDiffersFromDisplayUnit_ThenPreservesExplicitCode()
    {
        var result = StandardTransforms.Get("qty")!.Execute(
            [12.5m, "milligrams", "http://unitsofmeasure.org", "mg"], new TestContext());

        JsonNode.DeepEquals(
            result.ShouldBeOfType<JsonObject>(),
            JsonNode.Parse("""{"value":12.5,"unit":"milligrams","system":"http://unitsofmeasure.org","code":"mg"}"""))
            .ShouldBeTrue();
    }

    [Fact]
    public void GivenQuantityTransform_WhenOnlyDisplayUnitIsProvided_ThenDoesNotInventCoding()
    {
        var result = StandardTransforms.Get("qty")!.Execute([12.5m, "tablets"], new TestContext());

        JsonNode.DeepEquals(
            result.ShouldBeOfType<JsonObject>(),
            JsonNode.Parse("""{"value":12.5,"unit":"tablets"}""")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("< 5.5 mg", """{"comparator":"<","value":5.5,"unit":"mg"}""")]
    [InlineData(">=-2.5 kg", """{"comparator":">=","value":-2.5,"unit":"kg"}""")]
    [InlineData("1.2e2 mm Hg", """{"value":120,"unit":"mm Hg"}""")]
    [InlineData("42", """{"value":42}""")]
    public void GivenQuantityTransform_WhenParsingNaturalText_ThenPreservesQuantityComponents(string text, string expected)
    {
        var result = StandardTransforms.Get("qty")!.Execute([text], new TestContext());

        JsonNode.DeepEquals(result.ShouldBeOfType<JsonObject>(), JsonNode.Parse(expected)).ShouldBeTrue();
    }

    [Fact]
    public void GivenQuantityTransform_WhenTextHasNoNumericValue_ThenThrowsFormatException()
    {
        Should.Throw<FormatException>(() => StandardTransforms.Get("qty")!.Execute(["unknown mg"], new TestContext()));
    }

    [Fact]
    public void GivenIdentifierTransform_WhenExecuting_ThenCreatesCorrectStructure()
    {
        // Arrange
        var context = new TestContext();
        var transform = StandardTransforms.Get("id");

        // Act
        var result = transform.Execute(new List<object> { "http://example.org/ids", "12345" }, context);

        // Assert
        result.ShouldBeOfType<JsonObject>();
        var obj = (JsonObject)result;
        obj["system"]!.GetValue<string>().ShouldBe("http://example.org/ids");
        obj["value"]!.GetValue<string>().ShouldBe("12345");
    }

    [Fact]
    public void GivenIdentifierTransform_WhenTypeIsProvided_ThenAddsStandardIdentifierTypeCoding()
    {
        var result = StandardTransforms.Get("id")!.Execute(
            ["http://example.org/mrn", "12345", "MR"], new TestContext());

        JsonNode.DeepEquals(
            result.ShouldBeOfType<JsonObject>(),
            JsonNode.Parse("""
                {"system":"http://example.org/mrn","value":"12345",
                 "type":{"coding":[{"system":"http://terminology.hl7.org/CodeSystem/v2-0203","code":"MR"}]}}
                """)).ShouldBeTrue();
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

    [Theory]
    [InlineData("person@example.org", "email")]
    [InlineData("+1 (555) 0100", "phone")]
    [InlineData("https://example.org/contact", "url")]
    public void GivenContactPointTransform_WhenOnlyValueIsProvided_ThenInfersSystem(string value, string system)
    {
        var result = StandardTransforms.Get("cp")!.Execute([value], new TestContext());
        var contactPoint = result.ShouldBeOfType<JsonObject>();

        contactPoint.Count.ShouldBe(2);
        contactPoint["system"]!.GetValue<string>().ShouldBe(system);
        contactPoint["value"]!.GetValue<string>().ShouldBe(value);
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
        result.ShouldBe("Patient/patient-123");
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
        result.ShouldBe("Observation/obs-456");
    }

    [Fact]
    public void GivenReferenceTransform_WhenResourceHasIdChild_ThenReturnsRelativeReferenceString()
    {
        var resource = new TestTypedElement(
            "Patient",
            instanceType: "Patient",
            children: [new TestTypedElement("id", "referenced-patient", "id")]);

        var result = StandardTransforms.Get("reference")!.Execute([resource], new TestContext());

        result.ShouldBe("Patient/referenced-patient");
    }

    [Fact]
    public void GivenReferenceTransform_WhenResourceHasNoId_ThenThrowsInsteadOfReturningTypeName()
    {
        Should.Throw<InvalidOperationException>(() => StandardTransforms.Get("reference")!.Execute(
            [new TestTypedElement("Patient", instanceType: "Patient")], new TestContext()))
            .Message.ShouldContain("id");
    }

    #endregion

    #region Terminology Transform Tests

    [Fact]
    public void GivenTranslateTransform_WhenExecuting_ThenCallsConceptMapResolver()
    {
        // Arrange
        var context = new TestContext
        {
            ConceptMapResolver = (conceptMapUrl, sourceCode, targetSystem) =>
            {
                conceptMapUrl.ShouldBe("http://example.org/ConceptMap/test");
                sourceCode.ShouldBe("1234-5");
                targetSystem.ShouldBe(string.Empty);
                return "98765";
            }
        };
        var transform = StandardTransforms.Get("translate");

        // Act
        var result = transform.Execute(
            new List<object> { new TestTypedElement("code", "1234-5", "code"), "http://example.org/ConceptMap/test", "code" },
            context);

        // Assert
        result.ShouldBe("98765");
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
            new List<object> { "1234-5", "http://example.org/ConceptMap/test", "code" },
            context);

        // Assert
        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("Coding", """{"system":"http://example.org/target","code":"F"}""")]
    [InlineData("CodeableConcept", """{"coding":[{"system":"http://example.org/target","code":"F"}]}""")]
    public void GivenTranslateTransform_WhenStructuredOutputIsRequested_ThenPreservesResolverResult(string output, string expectedJson)
    {
        var source = new TestTypedElement("gender", "female", "code");
        var expected = JsonNode.Parse(expectedJson)!.AsObject();
        var context = new TestContext
        {
            TranslationResolver = (value, mapUri, requestedOutput) =>
            {
                value.ShouldBeSameAs(source);
                mapUri.ShouldBe("#GenderMap");
                requestedOutput.ShouldBe(output);
                return expected;
            }
        };

        var result = StandardTransforms.Get("translate")!.Execute([source, "#GenderMap", output], context);

        result.ShouldBeSameAs(expected);
    }

    [Theory]
    [InlineData("code", "F")]
    [InlineData("system", "http://example.org/target")]
    [InlineData("display", "Female")]
    public void GivenTranslateTransform_WhenScalarOutputIsRequested_ThenUsesModernResolver(string output, string expected)
    {
        var source = new TestTypedElement("gender", "female", "code");
        var context = new TestContext
        {
            TranslationResolver = (value, mapUri, requestedOutput) =>
            {
                value.ShouldBeSameAs(source);
                mapUri.ShouldBe("#GenderMap");
                requestedOutput.ShouldBe(output);
                return expected;
            },
            ConceptMapResolver = (_, _, _) => throw new InvalidOperationException("The legacy resolver must not be called.")
        };

        var result = StandardTransforms.Get("translate")!.Execute([source, "#GenderMap", output], context);

        result.ShouldBe(expected);
    }

    [Fact]
    public void GivenTranslateTransform_WhenModernResolverReturnsNoMapping_ThenDoesNotFallBack()
    {
        var context = new TestContext
        {
            TranslationResolver = (_, _, _) => null,
            ConceptMapResolver = (_, _, _) => throw new InvalidOperationException("The legacy resolver must not be called.")
        };

        var result = StandardTransforms.Get("translate")!.Execute(["missing", "#GenderMap", "code"], context);

        result.ShouldBeNull();
    }

    [Theory]
    [InlineData("Coding")]
    [InlineData("CodeableConcept")]
    [InlineData("system")]
    [InlineData("display")]
    public void GivenTranslateTransform_WhenLegacyResolverCannotProduceRequestedOutput_ThenFailsExplicitly(string output)
    {
        var context = new TestContext { ConceptMapResolver = (_, _, _) => "F" };

        Should.Throw<NotSupportedException>(() => StandardTransforms.Get("translate")!.Execute(
            ["female", "#GenderMap", output], context)).Message.ShouldContain("TranslationResolver");
    }

    [Fact]
    public void GivenTranslateTransform_WhenOutputIsUnknown_ThenRejectsItBeforeCallingResolver()
    {
        var context = new TestContext
        {
            TranslationResolver = (_, _, _) => throw new InvalidOperationException("An unsupported output must not reach the resolver.")
        };

        Should.Throw<ArgumentException>(() => StandardTransforms.Get("translate")!.Execute(
            ["female", "#GenderMap", "unknown"], context)).Message.ShouldContain("unknown");
    }

    [Fact]
    public void GivenTranslateTransform_WhenLegacyResolverReceivesStructuredSource_ThenFailsExplicitly()
    {
        var context = new TestContext { ConceptMapResolver = (_, _, _) => "F" };

        Should.Throw<NotSupportedException>(() => StandardTransforms.Get("translate")!.Execute(
            [new TestTypedElement("code", instanceType: "Coding"), "#GenderMap", "code"], context))
            .Message.ShouldContain("structured sources");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public void GivenTranslateTransform_WhenArgumentCountIsNotThree_ThenThrowsArgumentException(int argumentCount)
    {
        object[] arguments = Enumerable.Repeat<object>("argument", argumentCount).ToArray();

        Should.Throw<ArgumentException>(() => StandardTransforms.Get("translate")!.Execute(arguments, new TestContext()))
            .Message.ShouldContain("source, map_uri, and output");
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
            new List<object> { "1234-5", "http://example.org/ConceptMap/test", "code" },
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
