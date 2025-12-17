/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for import resolution in FHIR Mapping Language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

#pragma warning disable xUnit1031 // Test methods should not use blocking task operations - Intentional for testing

namespace Ignixa.FhirMappingLanguage.Tests.Registry;

public class ImportResolutionTests
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

        public IReadOnlyList<IElement> Children(string? name = null) => new List<IElement>();

        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region Map Registry Tests

    [Fact]
    public void GivenMapRegistry_WhenRegisteringMap_ThenStoresSuccessfully()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var map = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group TestGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}");

        // Act
        registry.Register(map);

        // Assert
        registry.Contains(map.Url).ShouldBeTrue();
        registry.GetByUrl(map.Url).ShouldNotBeNull();
    }

    [Fact]
    public void GivenMapRegistry_WhenRegisteringDuplicateUrl_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var map1 = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test' = 'Test1'
group TestGroup(source src : Patient, target tgt : Bundle) {}");

        var map2 = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test' = 'Test2'
group TestGroup(source src : Patient, target tgt : Bundle) {}");

        registry.Register(map1);

        // Act
        var act = () => registry.Register(map2);

        // Assert
        Should.Throw<InvalidOperationException>(act).Message.ShouldContain("already registered");
    }

    [Fact]
    public void GivenMapRegistry_WhenUnregistering_ThenRemovesMap()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var map = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'
group TestGroup(source src : Patient, target tgt : Bundle) {}");

        registry.Register(map);

        // Act
        var result = registry.Unregister(map.Url);

        // Assert
        result.ShouldBeTrue();
        registry.Contains(map.Url).ShouldBeFalse();
    }

    [Fact]
    public void GivenMapRegistry_WhenGettingAllUrls_ThenReturnsAllRegisteredUrls()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();

        var map1 = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test1' = 'Test1'
group TestGroup(source src : Patient, target tgt : Bundle) {}");

        var map2 = compiler.Parse(@"
map 'http://example.org/fhir/StructureMap/Test2' = 'Test2'
group TestGroup(source src : Patient, target tgt : Bundle) {}");

        registry.Register(map1);
        registry.Register(map2);

        // Act
        var urls = registry.GetAllUrls().ToList();

        // Assert
        urls.Count.ShouldBe(2);
        urls.ShouldContain(map1.Url);
        urls.ShouldContain(map2.Url);
    }

    #endregion

    #region Dictionary Map Loader Tests

    [Fact]
    public void GivenDictionaryMapLoader_WhenLoadingExistingMap_ThenReturnsContent()
    {
        // Arrange
        var loader = new DictionaryMapLoader();
        var url = "http://example.org/fhir/StructureMap/Test";
        var content = "map content";
        loader.AddMap(url, content);

        // Act
        var result = loader.LoadAsync(url).Result;

        // Assert
        result.ShouldBe(content);
    }

    [Fact]
    public void GivenDictionaryMapLoader_WhenLoadingNonExistentMap_ThenReturnsNull()
    {
        // Arrange
        var loader = new DictionaryMapLoader();

        // Act
        var result = loader.LoadAsync("http://example.org/nonexistent").Result;

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenDictionaryMapLoader_WhenCheckingCanLoad_ThenReturnsTrueForExisting()
    {
        // Arrange
        var loader = new DictionaryMapLoader();
        var url = "http://example.org/fhir/StructureMap/Test";
        loader.AddMap(url, "content");

        // Act
        var result = loader.CanLoad(url);

        // Assert
        result.ShouldBeTrue();
    }

    #endregion

    #region Import Resolver Tests

    [Fact]
    public async Task GivenImportResolver_WhenResolvingSimpleImport_ThenLoadsSuccessfully()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var importedMapContent = @"
map 'http://example.org/fhir/StructureMap/Imported' = 'Imported'
group ImportedGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var mainMapContent = @"
map 'http://example.org/fhir/StructureMap/Main' = 'Main'
imports 'http://example.org/fhir/StructureMap/Imported'

group MainGroup(source src : Patient, target tgt : Bundle) extends ImportedGroup {
  src.name -> tgt.type;
}";

        loader.AddMap("http://example.org/fhir/StructureMap/Imported", importedMapContent);

        var resolver = new ImportResolver(registry, compiler, loader);
        var mainMap = compiler.Parse(mainMapContent);

        // Act
        await resolver.ResolveImportsAsync(mainMap);

        // Assert
        registry.Contains("http://example.org/fhir/StructureMap/Main").ShouldBeTrue();
        registry.Contains("http://example.org/fhir/StructureMap/Imported").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenImportResolver_WhenResolvingTransitiveImports_ThenLoadsAll()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var baseMapContent = @"
map 'http://example.org/fhir/StructureMap/Base' = 'Base'
group BaseGroup(source src : Patient, target tgt : Bundle) {}";

        var middleMapContent = @"
map 'http://example.org/fhir/StructureMap/Middle' = 'Middle'
imports 'http://example.org/fhir/StructureMap/Base'
group MiddleGroup(source src : Patient, target tgt : Bundle) {}";

        var topMapContent = @"
map 'http://example.org/fhir/StructureMap/Top' = 'Top'
imports 'http://example.org/fhir/StructureMap/Middle'
group TopGroup(source src : Patient, target tgt : Bundle) {}";

        loader.AddMap("http://example.org/fhir/StructureMap/Base", baseMapContent);
        loader.AddMap("http://example.org/fhir/StructureMap/Middle", middleMapContent);

        var resolver = new ImportResolver(registry, compiler, loader);
        var topMap = compiler.Parse(topMapContent);

        // Act
        await resolver.ResolveImportsAsync(topMap);

        // Assert
        registry.Contains("http://example.org/fhir/StructureMap/Top").ShouldBeTrue();
        registry.Contains("http://example.org/fhir/StructureMap/Middle").ShouldBeTrue();
        registry.Contains("http://example.org/fhir/StructureMap/Base").ShouldBeTrue();
    }

    [Fact]
    public async Task GivenImportResolver_WhenCircularImport_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var map1Content = @"
map 'http://example.org/fhir/StructureMap/Map1' = 'Map1'
imports 'http://example.org/fhir/StructureMap/Map2'
group Group1(source src : Patient, target tgt : Bundle) {}";

        var map2Content = @"
map 'http://example.org/fhir/StructureMap/Map2' = 'Map2'
imports 'http://example.org/fhir/StructureMap/Map1'
group Group2(source src : Patient, target tgt : Bundle) {}";

        loader.AddMap("http://example.org/fhir/StructureMap/Map1", map1Content);
        loader.AddMap("http://example.org/fhir/StructureMap/Map2", map2Content);

        var resolver = new ImportResolver(registry, compiler, loader);
        var map1 = compiler.Parse(map1Content);

        // Act
        var act = async () => await resolver.ResolveImportsAsync(map1);

        // Assert
        var exception = await Should.ThrowAsync<InvalidOperationException>(act);
        exception.Message.ShouldContain("circular");
        exception.Message.ShouldContain("import");
    }

    [Fact]
    public async Task GivenImportResolver_WhenFindingGroup_ThenSearchesImports()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var importedMapContent = @"
map 'http://example.org/fhir/StructureMap/Imported' = 'Imported'
group ImportedGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var mainMapContent = @"
map 'http://example.org/fhir/StructureMap/Main' = 'Main'
imports 'http://example.org/fhir/StructureMap/Imported'

group MainGroup(source src : Patient, target tgt : Bundle) {
  src.name -> tgt.type;
}";

        loader.AddMap("http://example.org/fhir/StructureMap/Imported", importedMapContent);

        var resolver = new ImportResolver(registry, compiler, loader);
        var mainMap = compiler.Parse(mainMapContent);

        await resolver.ResolveImportsAsync(mainMap);

        // Act
        var group = resolver.FindGroup(mainMap, "ImportedGroup");

        // Assert
        group.ShouldNotBeNull();
        group!.Name.ShouldBe("ImportedGroup");
    }

    [Fact]
    public async Task GivenImportResolver_WhenMissingImport_ThenThrowsInvalidOperationException()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var mainMapContent = @"
map 'http://example.org/fhir/StructureMap/Main' = 'Main'
imports 'http://example.org/fhir/StructureMap/NonExistent'

group MainGroup(source src : Patient, target tgt : Bundle) {}";

        var resolver = new ImportResolver(registry, compiler, loader);
        var mainMap = compiler.Parse(mainMapContent);

        // Act
        var act = async () => await resolver.ResolveImportsAsync(mainMap);

        // Assert
        (await Should.ThrowAsync<InvalidOperationException>(act)).Message.ShouldContain("Failed to load");
    }

    #endregion

    #region Integration Tests with MappingEvaluator

    [Fact]
    public async Task GivenEvaluator_WhenGroupExtendsImportedGroup_ThenExecutesBoth()
    {
        // Arrange
        var registry = new MapRegistry();
        var compiler = new MappingParser();
        var loader = new DictionaryMapLoader();

        var importedMapContent = @"
map 'http://example.org/fhir/StructureMap/Imported' = 'Imported'
group ImportedGroup(source src : Patient, target tgt : Bundle) {
  src.id -> tgt.id;
}";

        var mainMapContent = @"
map 'http://example.org/fhir/StructureMap/Main' = 'Main'
imports 'http://example.org/fhir/StructureMap/Imported'

group MainGroup(source src : Patient, target tgt : Bundle) extends ImportedGroup {
  src.name -> tgt.type;
}";

        loader.AddMap("http://example.org/fhir/StructureMap/Imported", importedMapContent);

        var resolver = new ImportResolver(registry, compiler, loader);
        var mainMap = compiler.Parse(mainMapContent);

        await resolver.ResolveImportsAsync(mainMap);

        var evaluator = new MappingEvaluator(enableFhirPath: false, importResolver: resolver);
        var context = new MappingContext();

        var source = new TestTypedElement("Patient");
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Act
        var act = () => evaluator.ExecuteGroup(mainMap, "MainGroup", context);

        // Assert - Should execute both imported and main group without errors
        Should.NotThrow(act);
    }

    #endregion

    #region Composite Map Loader Tests

    [Fact]
    public async Task GivenCompositeLoader_WhenMultipleLoaders_ThenTriesInOrder()
    {
        // Arrange
        var compiler = new MappingParser();
        var composite = new CompositeMapLoader(compiler);

        var loader1 = new DictionaryMapLoader();
        loader1.AddMap("http://example.org/map1", "content1");

        var loader2 = new DictionaryMapLoader();
        loader2.AddMap("http://example.org/map2", "content2");

        composite.AddLoader(loader1);
        composite.AddLoader(loader2);

        // Act
        var result1 = await composite.LoadAsync("http://example.org/map1");
        var result2 = await composite.LoadAsync("http://example.org/map2");

        // Assert
        result1.ShouldBe("content1");
        result2.ShouldBe("content2");
    }

    [Fact]
    public async Task GivenCompositeLoader_WhenNoLoaderCanHandle_ThenReturnsNull()
    {
        // Arrange
        var compiler = new MappingParser();
        var composite = new CompositeMapLoader(compiler);

        var loader = new DictionaryMapLoader();
        loader.AddMap("http://example.org/map1", "content1");

        composite.AddLoader(loader);

        // Act
        var result = await composite.LoadAsync("http://example.org/nonexistent");

        // Assert
        result.ShouldBeNull();
    }

    #endregion
}
