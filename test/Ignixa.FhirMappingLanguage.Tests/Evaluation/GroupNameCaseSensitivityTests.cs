using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Validation;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Evaluation;

public class GroupNameCaseSensitivityTests
{
    private sealed class TestTypedElement(string name) : IElement
    {
        public string Name { get; } = name;
        public string InstanceType => "Patient";
        public object? Value => null;
        public string Location => string.Empty;
        public IType? Type => null;
        public bool HasPrimitiveValue => false;
        public IReadOnlyList<IElement> Children(string? name = null) => [];
        public T? Meta<T>() where T : class => null;
    }

    [Fact]
    public void GivenGroupsDifferingOnlyByCase_WhenInvokingExactName_ThenSelectsMatchingGroup()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element(source upper: Patient) {}
            group element(source lower: Patient) {}
            """;
        var map = new MappingParser().Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var upperContext = new MappingContext();
        upperContext.SetSource("upper", new TestTypedElement("Patient"));
        var lowerContext = new MappingContext();
        lowerContext.SetSource("lower", new TestTypedElement("Patient"));

        Should.NotThrow(() => evaluator.ExecuteGroup(map, "Element", upperContext));
        Should.NotThrow(() => evaluator.ExecuteGroup(map, "element", lowerContext));
    }

    [Fact]
    public void GivenGroupsDifferingOnlyByCase_WhenInvokingWrongCase_ThenRejectsName()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element() {}
            group element() {}
            """;
        var map = new MappingParser().Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);

        var exception = Should.Throw<InvalidOperationException>(
            () => evaluator.ExecuteGroup(map, "ELEMENT", new MappingContext()));

        exception.Message.ShouldContain("Group 'ELEMENT' not found");
    }

    [Fact]
    public void GivenLocalGroupInvocationsDifferingOnlyByCase_WhenInvokingExactNames_ThenMatchesEachGroup()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element(source upper: Patient) {}
            group element(source lower: Patient, target output: Bundle) {}
            group Main(source src: Patient, target tgt: Bundle) {
              src -> tgt then Element(src);
              src -> tgt then element(src, tgt);
            }
            """;
        var map = new MappingParser().Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();
        context.SetSource("src", new TestTypedElement("Patient"));
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        Should.NotThrow(() => evaluator.ExecuteGroup(map, "Main", context));
    }

    [Fact]
    public void GivenLocalGroupInvocation_WhenUsingWrongCase_ThenRejectsName()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element(source upper: Patient) {}
            group element(source lower: Patient, target output: Bundle) {}
            group Main(source src: Patient, target tgt: Bundle) {
              src -> tgt then ELEMENT(src);
            }
            """;
        var map = new MappingParser().Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext();
        context.SetSource("src", new TestTypedElement("Patient"));
        context.SetTarget("tgt", new TestTypedElement("Bundle"));

        var exception = Should.Throw<InvalidOperationException>(
            () => evaluator.ExecuteGroup(map, "Main", context));

        exception.Message.ShouldContain("Group 'ELEMENT' not found");
    }
    [Fact]
    public void GivenGroupsDifferingOnlyByCase_WhenValidatingExactInheritance_ThenAcceptsBothGroups()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element() extends element {}
            group element() {}
            """;
        var map = new MappingParser().Parse(mappingText);

        var result = new MappingValidator().Validate(map);

        result.IsValid.ShouldBeTrue();
        result.Errors.ShouldBeEmpty();
    }

    [Fact]
    public void GivenGroupsDifferingOnlyByCase_WhenExtendingWrongCase_ThenReportsMissingBase()
    {
        var mappingText = """
            map 'http://example.org/fhir/StructureMap/Test' = 'Test'
            group Element() extends ELEMENT {}
            group element() {}
            """;
        var map = new MappingParser().Parse(mappingText);

        var result = new MappingValidator().Validate(map);

        result.IsValid.ShouldBeFalse();
        result.Errors.ShouldContain(error => error.Code == "MISSING_BASE_GROUP");
    }
}