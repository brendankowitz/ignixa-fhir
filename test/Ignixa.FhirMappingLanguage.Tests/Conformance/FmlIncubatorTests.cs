/*
 * Copyright (c) 2026, Ignixa Contributors
 */

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
using Ignixa.FhirMappingLanguage.Registry;
using Ignixa.FhirMappingLanguage.Serialization;
using Ignixa.FhirPath.Evaluation;
using Ignixa.FhirPath.Parser;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Serialization.TestSupport;
using Ignixa.Specification.Extensions;
using Shouldly;
using Xunit;
using Xunit.Abstractions;

namespace Ignixa.FhirMappingLanguage.Tests.Conformance;

[Trait("Category", "OfficialTestSuite")]
[Trait("Category", "FmlIncubator")]
public class FmlIncubatorTests(ITestOutputHelper output)
{
    private static string CorpusRoot
    {
        get
        {
            return "C:\\git\\hl7-incubators\\fml-incubator";
            //var root = Environment.GetEnvironmentVariable(FmlIncubatorTheoryAttribute.CorpusPathVariable);
            //if (string.IsNullOrWhiteSpace(root) || !Path.IsPathFullyQualified(root))
            //{
            //    throw new InvalidOperationException(
            //        $"Set {FmlIncubatorTheoryAttribute.CorpusPathVariable} to the absolute path of a HL7/mapping-language-ig checkout.");
            //}

            //return root;
        }
    }

    public static TheoryData<string> FmlFiles()
    {
        var directory = Path.Combine(CorpusRoot, "input", "examples");
        var files = Directory.GetFiles(directory, "*.fml").OrderBy(Path.GetFileName, StringComparer.Ordinal).ToArray();
        if (files.Length == 0)
        {
            throw new InvalidDataException($"No FML examples were found in '{directory}'. Check the corpus checkout.");
        }

        var data = new TheoryData<string>();
        foreach (var file in files)
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [FmlIncubatorTheory]
    [MemberData(nameof(FmlFiles))]
    public void GivenAnIncubatorMap_WhenParsingAndRoundTripping_ThenTheStructureMapIsPreserved(string fileName)
    {
        var parser = new MappingParser();
        var serializer = new FmlSerializer();
        var builder = new StructureMapBuilder(FhirVersion.R5);
        var map = parser.Parse(ReadMap(fileName));

        var rendered = serializer.Serialize(map);
        output.WriteLine(rendered);
        var reparsed = parser.Parse(rendered);

        // Ignixa normalizes quotes, comments and metadata headers, unlike the Java renderer.
        var expected = CanonicalJson.Canonicalize(builder.Build(map).MutableNode().ToJsonString());
        var actual = CanonicalJson.Canonicalize(builder.Build(reparsed).MutableNode().ToJsonString());
        actual.ShouldBe(expected, $"StructureMap changed after round-tripping '{fileName}'.");
        reparsed.MetadataDeclarations.ShouldBe(map.MetadataDeclarations);
        reparsed.Constants.Select(c => c.Name).ShouldBe(map.Constants.Select(c => c.Name));
        serializer.Serialize(reparsed).ShouldBe(rendered);
    }

    [FmlIncubatorTheory]
    [MemberData(nameof(FmlFiles))]
    public async Task GivenAnIncubatorMap_WhenEvaluating_ThenTheResourceMatchesTheJavaOutput(string fileName)
    {
        var jsonName = Path.ChangeExtension(fileName, ".json");
        var inputPath = Path.Combine(CorpusRoot, "test-input-resources", jsonName);
        var expectedPath = Path.Combine(CorpusRoot, "test-output-resources", jsonName);
        output.WriteLine($"Input: {inputPath}");
        output.WriteLine($"Reference: {expectedPath}");
        var source = ResourceJsonNode.Parse(await File.ReadAllTextAsync(inputPath, Encoding.UTF8));
        var expected = ResourceJsonNode.Parse(File.Exists(expectedPath)
            ? await File.ReadAllTextAsync(expectedPath, Encoding.UTF8)
            : MissingReference(fileName));
        var parser = new MappingParser();
        var map = parser.Parse(ReadMap(fileName));
        var registry = new MapRegistry();

        // Match StructureMapToolsTestR5: imports.fml depends on simple-identity.fml.
        if (fileName == "imports.fml")
        {
            registry.Register(parser.Parse(ReadMap("simple-identity.fml")));
        }

        var importResolver = new ImportResolver(registry, parser);
        await importResolver.ResolveImportsAsync(map);

        // The Java transform entry point executes the first group, not every helper group.
        var group = map.Groups[0];
        var sourceParameter = group.Parameters.Single(p => p.Mode == ParameterMode.Source);
        var targetParameter = group.Parameters.Single(p => p.Mode == ParameterMode.Target);
        var targetType = map.Uses.Single(u => u.Mode == ModelMode.Target &&
            (u.Alias == targetParameter.Type || u.Url.Split('/').Last() == targetParameter.Type)).Url.Split('/').Last();
        var target = CreateResource(targetType);
        var schema = FhirVersion.R5.GetSchemaProvider();
        var mutator = new JsonNodeMutator(new FhirPathEvaluator(), new FhirPathParser(), () => schema);
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            ResourceCreator = type => mutator.CreateElement(type),
            Logger = message => output.WriteLine(message)
        };
        if (fileName == "transform-translate.fml")
        {
            var conceptMap = ResourceJsonNode.Parse(await File.ReadAllTextAsync(
                Path.Combine(FmlTestCasesLocator.Root, "r5", "examples", "conceptmap-example.json"), Encoding.UTF8)).ToElement(schema);
            context.TranslationResolver = (code, url, resultType) =>
            {
                url.ShouldBe(conceptMap.Scalar("url"));
                resultType.ShouldBe("CodeableConcept");
                code.ShouldBe("home");
                var group = conceptMap.Select("group.where(source = 'http://hl7.org/fhir/address-use')").Single();
                return new JsonObject
                {
                    ["coding"] = new JsonArray(new JsonObject
                    {
                        ["system"] = (string)group.Scalar("target")!,
                        ["code"] = (string)group.Scalar("element.where(code = 'home').target.code")!
                    })
                };
            };
        }
        context.SetSource(sourceParameter.Name, source.ToElement(schema));
        context.SetTarget(targetParameter.Name, target.ToElement(schema));
        context.SetTargetResource(targetParameter.Name, target);

        var evaluator = new MappingEvaluator(MappingEvaluatorOptions.Default, mutator, importResolver);
        evaluator.ExecuteGroup(map, group.Name, context);

        context.Errors.ShouldBeEmpty();
        output.WriteLine($"Actual: {target.MutableNode().ToJsonString()}");
        if (fileName == "transform-uuid.fml")
        {
            // Only this example's generated id is nondeterministic; compare every other field.
            Guid.TryParseExact(target.Id, "D", out var id).ShouldBeTrue("uuid() must produce a UUID.");
            id.ShouldNotBe(Guid.Empty);
            Guid.TryParseExact(expected.Id, "D", out _).ShouldBeTrue("The UUID reference output must be valid.");
            target.Id = expected.Id;
        }

        CanonicalJson.Canonicalize(target.MutableNode().ToJsonString())
            .ShouldBe(CanonicalJson.Canonicalize(expected.MutableNode().ToJsonString()), fileName);
    }

    private string ReadMap(string fileName)
    {
        var path = Path.Combine(CorpusRoot, "input", "examples", fileName);
        output.WriteLine($"Map: {path}");
        return File.ReadAllText(path, Encoding.UTF8);
    }

    private static ResourceJsonNode CreateResource(string resourceType) =>
        ResourceJsonNode.Parse(new JsonObject { ["resourceType"] = resourceType }.ToJsonString());

    private string MissingReference(string fileName)
    {
        output.WriteLine($"No Java reference for {fileName}; comparing with the result derived from its map and supplied input.");
        return fileName switch
        {
            // These outcomes have been verified and now included (java doesn't support them)
            //// Two names crossed with two identifiers; each rule invocation creates one name.
            //"source-multiple.fml" => """
            //    {"resourceType":"Patient","name":[
            //      {"family":"Alpha","text":"Alpha-MRN-001","given":["MRN-001"]},
            //      {"family":"Alpha","text":"Alpha-MEM-002","given":["MEM-002"]},
            //      {"family":"Beta","text":"Beta-MRN-001","given":["MRN-001"]},
            //      {"family":"Beta","text":"Beta-MEM-002","given":["MEM-002"]}]}
            //    """,
            //// The map swaps the root value with the first component's value.
            //"target-subelement.fml" => """
            //    {"resourceType":"Observation","component":[{"valueCodeableConcept":{"text":"A"}}],
            //     "valueCodeableConcept":{"text":"B"}}
            //    """,
            _ => throw new FileNotFoundException($"No reference output or reviewed expectation exists for '{fileName}'.")
        };
    }
}
