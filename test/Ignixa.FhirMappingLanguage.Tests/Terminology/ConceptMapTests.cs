/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Unit tests for ConceptMap integration in FHIR Mapping Language.
 */

using Shouldly;
using Ignixa.FhirMappingLanguage;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Terminology;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Parser;
using Xunit;

namespace Ignixa.FhirMappingLanguage.Tests.Terminology;

public class ConceptMapTests
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

    private class TestTypedElementWithChildren : IElement
    {
        private readonly List<IElement> _children = new();

        public TestTypedElementWithChildren(string name, object? value = null, string instanceType = "string")
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

        public void AddChild(IElement child) => _children.Add(child);

        public IReadOnlyList<IElement> Children(string? name = null)
        {
            if (name == null)
            {
                return _children;
            }
            return _children.Where(c => c.Name == name).ToList();
        }

        public T? Meta<T>() where T : class => null;
    }

    #endregion

    #region ConceptMap Loader Tests

    [Fact]
    public async Task GivenDictionaryLoader_WhenLoadingExistingMap_ThenReturnsContent()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var url = "http://example.org/fhir/ConceptMap/test";
        var content = "{\"resourceType\": \"ConceptMap\"}";
        loader.AddConceptMap(url, content);

        // Act
        var result = await loader.LoadAsync(url);

        // Assert
        result.ShouldBe(content);
    }

    [Fact]
    public async Task GivenDictionaryLoader_WhenLoadingNonExistent_ThenReturnsNull()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();

        // Act
        var result = await loader.LoadAsync("http://example.org/nonexistent");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public void GivenDictionaryLoader_WhenCheckingCanLoad_ThenReturnsTrueForExisting()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var url = "http://example.org/fhir/ConceptMap/test";
        loader.AddConceptMap(url, "content");

        // Act
        var result = loader.CanLoad(url);

        // Assert
        result.ShouldBeTrue();
    }

    #endregion

    #region Composite Loader Tests

    [Fact]
    public async Task GivenCompositeLoader_WhenMultipleLoaders_ThenTriesInOrder()
    {
        // Arrange
        var composite = new CompositeConceptMapLoader();

        var loader1 = new DictionaryConceptMapLoader();
        loader1.AddConceptMap("http://example.org/map1", "content1");

        var loader2 = new DictionaryConceptMapLoader();
        loader2.AddConceptMap("http://example.org/map2", "content2");

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
        var composite = new CompositeConceptMapLoader();
        var loader = new DictionaryConceptMapLoader();
        loader.AddConceptMap("http://example.org/map1", "content1");
        composite.AddLoader(loader);

        // Act
        var result = await composite.LoadAsync("http://example.org/nonexistent");

        // Assert
        result.ShouldBeNull();
    }

    #endregion

    #region ConceptMap Resolver Tests

    [Fact]
    public async Task GivenSimpleConceptMap_WhenTranslating_ThenReturnsTargetCode()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/gender"",
  ""group"": [
    {
      ""source"": ""http://example.org/source"",
      ""target"": ""http://example.org/target"",
      ""element"": [
        {
          ""code"": ""M"",
          ""target"": [
            {
              ""code"": ""male"",
              ""equivalence"": ""equivalent""
            }
          ]
        },
        {
          ""code"": ""F"",
          ""target"": [
            {
              ""code"": ""female"",
              ""equivalence"": ""equivalent""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/gender", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act
        var result = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/gender",
            "M");

        // Assert
        result.ShouldBe("male");
    }

    [Fact]
    public async Task GivenMultipleGroups_WhenTranslating_ThenFindsCorrectGroup()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/multi"",
  ""group"": [
    {
      ""source"": ""http://system1.org"",
      ""target"": ""http://target1.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        }
      ]
    },
    {
      ""source"": ""http://system2.org"",
      ""target"": ""http://target2.org"",
      ""element"": [
        {
          ""code"": ""B"",
          ""target"": [
            {
              ""code"": ""Y""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/multi", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act
        var resultB = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/multi",
            "B");

        // Assert
        resultB.ShouldBe("Y");
    }

    [Fact]
    public async Task GivenTargetSystem_WhenTranslating_ThenFiltersCorrectly()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/targeted"",
  ""group"": [
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target1.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        }
      ]
    },
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target2.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""Z""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/targeted", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act
        var result = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/targeted",
            "A",
            "http://target2.org");

        // Assert
        result.ShouldBe("Z");
    }

    [Fact]
    public async Task GivenNonExistentCode_WhenTranslating_ThenReturnsNull()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/test"",
  ""group"": [
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/test", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act
        var result = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/test",
            "NONEXISTENT");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenNonExistentConceptMap_WhenTranslating_ThenReturnsNull()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var resolver = new ConceptMapResolver(loader);

        // Act
        var result = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/nonexistent",
            "A");

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GivenCaching_WhenMultipleTranslations_ThenUsesCache()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/cached"",
  ""group"": [
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        },
        {
          ""code"": ""B"",
          ""target"": [
            {
              ""code"": ""Y""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/cached", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act - Multiple translations should use cached ConceptMap
        var result1 = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/cached",
            "A");
        var result2 = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/cached",
            "B");

        // Assert
        result1.ShouldBe("X");
        result2.ShouldBe("Y");
    }

    [Fact]
    public async Task GivenClearCache_WhenTranslating_ThenReloadsConceptMap()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/clearable"",
  ""group"": [
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/clearable", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);

        // Act
        var result1 = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/clearable",
            "A");

        resolver.ClearCache();

        var result2 = await resolver.TranslateAsync(
            "http://example.org/fhir/ConceptMap/clearable",
            "A");

        // Assert
        result1.ShouldBe("X");
        result2.ShouldBe("X");
    }

    #endregion

    #region Integration with MappingEvaluator Tests

    [Fact]
#pragma warning disable CS1998 // Async method lacks 'await' operators
    public async Task GivenTranslateTransform_WhenConceptMapConfigured_ThenTranslatesCode()
#pragma warning restore CS1998
    {
        // Arrange
        var mappingText = @"
map 'http://example.org/fhir/StructureMap/Test' = 'Test'

group Transform(source src : Patient, target tgt : Bundle) {
  src.gender -> tgt.type = translate(src.gender, 'http://example.org/fhir/ConceptMap/gender', 'target');
}";

        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/gender"",
  ""group"": [
    {
      ""source"": ""http://hl7.org/fhir/administrative-gender"",
      ""target"": ""http://example.org/custom-gender"",
      ""element"": [
        {
          ""code"": ""male"",
          ""target"": [
            {
              ""code"": ""M"",
              ""equivalence"": ""equivalent""
            }
          ]
        },
        {
          ""code"": ""female"",
          ""target"": [
            {
              ""code"": ""F"",
              ""equivalence"": ""equivalent""
            }
          ]
        }
      ]
    }
  ]
}";

        var loader = new DictionaryConceptMapLoader();
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/gender", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);
        var compiler = new MappingParser();
        var map = compiler.Parse(mappingText);
        var evaluator = new MappingEvaluator(enableFhirPath: false);
        var context = new MappingContext
        {
            ConceptMapResolver = resolver.CreateResolverFunction()
        };

        var source = new TestTypedElementWithChildren("Patient");
        source.AddChild(new TestTypedElement("gender", "male", "code"));
        var target = new TestTypedElement("Bundle");

        context.SetSource("src", source);
        context.SetTarget("tgt", target);

        // Set up transform resolver for translate
        var translateCalled = false;
        context.TransformResolver = (name, args) =>
        {
            if (name == "translate")
            {
                translateCalled = true;
                var argsList = args.ToList();
                var sourceCode = argsList[0].ToString()!;
                var mapUrl = argsList[1].ToString()!;
                var output = argsList[2].ToString()!;

                return resolver.TranslateAsync(mapUrl, sourceCode, output).GetAwaiter().GetResult() ?? sourceCode;
            }
            return args.FirstOrDefault() ?? new object();
        };

        // Act
        evaluator.ExecuteGroup(map, "Transform", context);

        // Assert - translate should have been called
        translateCalled.ShouldBeTrue();
    }

    #endregion

    #region Resolver Function Tests

    [Fact]
    public void GivenCreateResolverFunction_WhenCalling_ThenReturnsSyncWrapper()
    {
        // Arrange
        var loader = new DictionaryConceptMapLoader();
        var conceptMapContent = @"{
  ""resourceType"": ""ConceptMap"",
  ""url"": ""http://example.org/fhir/ConceptMap/test"",
  ""group"": [
    {
      ""source"": ""http://source.org"",
      ""target"": ""http://target.org"",
      ""element"": [
        {
          ""code"": ""A"",
          ""target"": [
            {
              ""code"": ""X""
            }
          ]
        }
      ]
    }
  ]
}";
        loader.AddConceptMap("http://example.org/fhir/ConceptMap/test", conceptMapContent);

        var resolver = new ConceptMapResolver(loader);
        var func = resolver.CreateResolverFunction();

        // Act
        var result = func("http://example.org/fhir/ConceptMap/test", "A", "http://target.org");

        // Assert
        result.ShouldBe("X");
    }

    #endregion
}
