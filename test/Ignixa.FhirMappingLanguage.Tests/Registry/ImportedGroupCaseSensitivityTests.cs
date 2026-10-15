using Shouldly;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Validation;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Registry;

public class ImportedGroupCaseSensitivityTests
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
    public async Task GivenImportedGroupsDifferingOnlyByCase_WhenInheritingExactName_ThenResolvesExactGroup()
    {
        var importedMapUrl = "http://example.org/fhir/StructureMap/Imported";
        var importedMapContent = """
            map 'http://example.org/fhir/StructureMap/Imported' = 'Imported'
            group Element(source upper: Patient) {}
            group element(source lower: Patient, target output: Bundle) {}
            """;
        var mainMapContent = """
            map 'http://example.org/fhir/StructureMap/Main' = 'Main'
            imports 'http://example.org/fhir/StructureMap/Imported'
            group Main() extends element {}
            group WrongCase() extends ELEMENT {}
            group Invoke(source src: Patient, target tgt: Bundle) {
              src -> tgt then Element(src);
              src -> tgt then element(src, tgt);
            }
            group WrongInvocation(source src: Patient, target tgt: Bundle) {
              src -> tgt then ELEMENT(src);
            }
            """;
        var registry = new MapRegistry();
        var parser = new MappingParser();
        var loader = new DictionaryMapLoader();
        loader.AddMap(importedMapUrl, importedMapContent);
        var resolver = new ImportResolver(registry, parser, loader);
        var mainMap = parser.Parse(mainMapContent);
        await resolver.ResolveImportsAsync(mainMap);

        var importedUpper = resolver.FindGroup(mainMap, "Element");
        var importedLower = resolver.FindGroup(mainMap, "element");
        var wrongCase = resolver.FindGroup(mainMap, "ELEMENT");

        importedUpper!.Name.ShouldBe("Element");
        importedLower!.Name.ShouldBe("element");
        wrongCase.ShouldBeNull();

        var validation = new MappingValidator(resolver).Validate(mainMap);
        validation.Errors.Count(error => error.Code == "MISSING_BASE_GROUP").ShouldBe(1);
        validation.Errors.ShouldContain(error => error.Code == "MISSING_BASE_GROUP" && error.Message.Contains("'ELEMENT'", StringComparison.Ordinal));
        validation.Errors.Count(error => error.Code == "UNKNOWN_GROUP").ShouldBe(1);
        validation.Errors.ShouldContain(error => error.Code == "UNKNOWN_GROUP" && error.Message.Contains("'ELEMENT'", StringComparison.Ordinal));

        var evaluator = new MappingEvaluator(enableFhirPath: false, importResolver: resolver);
        var context = new MappingContext();
        context.SetSource("lower", new TestTypedElement("Patient"));
        context.SetTarget("output", new TestTypedElement("Bundle"));
        Should.NotThrow(() => evaluator.ExecuteGroup(mainMap, "Main", context));

        var exception = Should.Throw<InvalidOperationException>(
            () => evaluator.ExecuteGroup(mainMap, "WrongCase", context));
        exception.Message.ShouldContain("base group not found");

        var invocationContext = new MappingContext();
        invocationContext.SetSource("src", new TestTypedElement("Patient"));
        invocationContext.SetTarget("tgt", new TestTypedElement("Bundle"));
        Should.NotThrow(() => evaluator.ExecuteGroup(mainMap, "Invoke", invocationContext));

        var invocationException = Should.Throw<InvalidOperationException>(
            () => evaluator.ExecuteGroup(mainMap, "WrongInvocation", invocationContext));
        invocationException.Message.ShouldContain("Group 'ELEMENT' not found");
    }

    [Fact]
    public async Task GivenImportedBaseExtendsItsOwnImport_WhenExecutingDerivedGroup_ThenResolvesFromOwningMap()
    {
        var leafMapUrl = "http://example.org/fhir/StructureMap/Leaf";
        var importedMapUrl = "http://example.org/fhir/StructureMap/Imported";
        var leafMapContent = """
            map 'http://example.org/fhir/StructureMap/Leaf' = 'Leaf'
            group LeafGroup() {}
            """;
        var importedMapContent = """
            map 'http://example.org/fhir/StructureMap/Imported' = 'Imported'
            imports 'http://example.org/fhir/StructureMap/Leaf'
            group ImportedBase() extends LeafGroup {}
            """;
        var mainMapContent = """
            map 'http://example.org/fhir/StructureMap/Main' = 'Main'
            imports 'http://example.org/fhir/StructureMap/Imported'
            group Main() extends ImportedBase {}
            """;
        var registry = new MapRegistry();
        var parser = new MappingParser();
        var loader = new DictionaryMapLoader();
        loader.AddMap(leafMapUrl, leafMapContent);
        loader.AddMap(importedMapUrl, importedMapContent);
        var resolver = new ImportResolver(registry, parser, loader);
        var mainMap = parser.Parse(mainMapContent);
        await resolver.ResolveImportsAsync(mainMap);

        var evaluator = new MappingEvaluator(enableFhirPath: false, importResolver: resolver);

        Should.NotThrow(() => evaluator.ExecuteGroup(mainMap, "Main", new MappingContext()));
    }
}
