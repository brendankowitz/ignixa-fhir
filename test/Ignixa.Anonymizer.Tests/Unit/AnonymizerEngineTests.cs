// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.
// Copyright (c) Ignixa Contributors.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
using System;
using System.IO;
using System.Linq;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification.Generated;
using Ignixa.Abstractions;
using Ignixa.Anonymizer.AnonymizerConfigurations;
using Ignixa.Anonymizer.Exceptions;
using Ignixa.Anonymizer.Extensions;
using Ignixa.Anonymizer.Processors;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.Anonymizer.Core.UnitTests
{
    public class AnonymizerEngineTests
    {
        private readonly R4CoreSchemaProvider _schema = new();

        [Fact]
        public void GivenIsPrettyOutputSetTrue_WhenAnonymizeJson_PrettyJsonOutputShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("TestConfigurations", "configuration-test-sample.json"), _schema);
            var settings = new AnonymizerSettings()
            {
                IsPrettyOutput = true
            };
            var result = engine.AnonymizeJson(TestPatientSample, settings);
            // Normalize line endings for cross-platform comparison
            Assert.Equal(PrettyOutputTarget.ReplaceLineEndings(), result.ReplaceLineEndings());
        }

        [Fact]
        public void GivenIsPrettyOutputSetFalse_WhenAnonymizeJson_OneLineJsonOutputShouldBeReturned()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("TestConfigurations", "configuration-test-sample.json"), _schema);

            var result = engine.AnonymizeJson(TestPatientSample);
            Assert.Equal(OneLineOutputTarget, result);
        }

        [Fact]
        public void GivenAnonymizerEngine_AddingCustomProcessor_WhenAnonymize_CorrectResultWillBeReturned()
        {
            var factory = new CustomProcessorFactory();
            factory.RegisterProcessors(typeof(MaskProcessor));
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("TestConfigurations", "configuration-custom-Processor.json"), _schema, factory);

            var result = engine.AnonymizeJson(TestPatientSample);
            Assert.Equal(CustomTarget, result);
        }

        [Fact]
        public void GivenAnonymizerEngine_IfConfigurationHasUnsupportedMethod_WhenAnonymize_ExceptionWillBeThrown()
        {
            AnonymizerEngine engine = new AnonymizerEngine(Path.Combine("TestConfigurations", "configuration-unsupported-method.json"), _schema);

            Assert.Throws<AnonymizerConfigurationException>(() => engine.AnonymizeJson(TestPatientSample));
        }

        private const string TestPatientSample =
@"{
  ""resourceType"": ""Patient"",
  ""id"": ""example"",
  ""name"": [
    {
      ""use"": ""official"",
      ""family"": ""Chalmers"",
      ""given"": [
        ""Peter"",
        ""James""
      ]
    }
  ]
}";

        private const string PrettyOutputTarget =
@"{
  ""resourceType"": ""Patient"",
  ""id"": ""example"",
  ""meta"": {
    ""security"": [
      {
        ""system"": ""http://terminology.hl7.org/CodeSystem/v3-ObservationValue"",
        ""code"": ""REDACTED"",
        ""display"": ""redacted""
      }
    ]
  }
}";

        private const string OneLineOutputTarget = "{\"resourceType\":\"Patient\",\"id\":\"example\",\"meta\":{\"security\":[{\"system\":\"http://terminology.hl7.org/CodeSystem/v3-ObservationValue\",\"code\":\"REDACTED\",\"display\":\"redacted\"}]}}";
        private const string CustomTarget = "{\"resourceType\":\"Patient\",\"id\":\"example\",\"name\":[{\"use\":\"***icial\",\"family\":\"***lmers\",\"given\":[\"***er\",\"***es\"]}]}";
    }

    public class NodeTreeDiagnosticTests
    {
        private readonly R4CoreSchemaProvider _schema = new();
        private readonly ITestOutputHelper _output;

        public NodeTreeDiagnosticTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void DumpGeneralizePatientTree()
        {
            var json = """{"resourceType":"Patient","id":"example","multipleBirthInteger":10,"birthDate":"2010-05-07","_birthDate":{"extension":[{"url":"http://hl7.org/fhir/StructureDefinition/patient-birthTime","valueDateTime":"2010-05-07T01:01:01-01:00"}]}}""";
            var resource = ResourceJsonNode.Parse(json);
            var element = resource.ToElement(_schema);
            DumpTree(element, 0);

            // Build type cache
            var typeCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<IElement>>();
            BuildTypeCache(element, typeCache);

            _output.WriteLine("\n=== Type Cache Contents ===");
            foreach (var kvp in typeCache.OrderBy(k => k.Key))
            {
                _output.WriteLine($"Type '{kvp.Key}': {kvp.Value.Count} nodes");
                foreach (var n in kvp.Value)
                {
                    _output.WriteLine($"  - {n.Location} (Name={n.Name}, Value={n.Value})");
                }
            }
        }

        [Fact]
        public void DumpNullDatePatientTree()
        {
            var json = """{"resourceType":"Patient","id":"example","_birthDate":{"extension":[{"url":"http://hl7.org/fhir/StructureDefinition/patient-birthTime","valueDateTime":"2000-01-01T01:01:01-01:00"}]}}""";
            var resource = ResourceJsonNode.Parse(json);
            var element = resource.ToElement(_schema);
            DumpTree(element, 0);
        }

        [Fact]
        public void TestNodesByTypeHumanNameFamily()
        {
            var json = """{"resourceType":"Patient","name":[{"family":"Smith","given":["John"]}]}""";
            var resource = ResourceJsonNode.Parse(json);
            var element = resource.ToElement(_schema);

            var typeCache = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<IElement>>();
            BuildTypeCache(element, typeCache);

            _output.WriteLine("=== nodesByType('HumanName') ===");
            if (typeCache.TryGetValue("HumanName", out var humanNames))
            {
                foreach (var n in humanNames)
                {
                    _output.WriteLine($"  HumanName: {n.Location}");
                    try
                    {
                        var familyNodes = n.Select(".family");
                        _output.WriteLine($"    .family found {familyNodes.Count} nodes");
                        foreach (var f in familyNodes)
                        {
                            _output.WriteLine($"      - {f.Location} = {f.Value}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"    .family threw: {ex.Message}");
                    }
                }
            }
            else
            {
                _output.WriteLine("  NOT FOUND in cache");
            }

            _output.WriteLine("\n=== Trying generalize on family 'Smith' ===");
            var nameNodes = element.Children("name");
            foreach (var nameNode in nameNodes)
            {
                foreach (var fam in nameNode.Children("family"))
                {
                    _output.WriteLine($"  family: {fam.Location} = '{fam.Value}' InstanceType={fam.InstanceType}");
                    try
                    {
                        var result = fam.Select("$this>=0 and $this<20");
                        _output.WriteLine($"    $this>=0 and $this<20 => {result.Count} results");
                        foreach (var r in result)
                            _output.WriteLine($"      {r.Value}");
                    }
                    catch (Exception ex)
                    {
                        _output.WriteLine($"    THREW: {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        private void BuildTypeCache(IElement node, System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<IElement>> cache)
        {
            foreach (var child in node.Children())
            {
                if (!cache.TryGetValue(child.InstanceType, out var list))
                {
                    list = new System.Collections.Generic.List<IElement>();
                    cache[child.InstanceType] = list;
                }
                list.Add(child);
                if (child.Type?.Info.IsResource != true)
                    BuildTypeCache(child, cache);
            }
        }

        private void DumpTree(IElement node, int depth)
        {
            var indent = new string(' ', depth * 2);
            _output.WriteLine($"{indent}{node.Name} [Type={node.InstanceType}, Location={node.Location}, Value={node.Value}]");
            foreach (var child in node.Children())
                DumpTree(child, depth + 1);
        }
    }
}
