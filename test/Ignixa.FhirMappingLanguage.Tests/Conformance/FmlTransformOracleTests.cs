/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Executes every in-scope official FML oracle case end-to-end and compares
 * the produced resource against the reference implementation's expected output.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.FhirMappingLanguage.Expressions;
using Ignixa.FhirMappingLanguage.Mutator;
using Ignixa.FhirMappingLanguage.Parser;
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

/// <summary>
/// Executes every in-scope case from the official <c>&lt;fml-tests&gt;</c> manifest and
/// compares the produced resource against the reference implementation's expected output.
/// </summary>
public class FmlTransformOracleTests(ITestOutputHelper output)
{
    private static readonly string[] Versions = ["r5", "r4b"];

    public static IEnumerable<object[]> SupportedCases()
    {
        foreach (var version in Versions)
        {
            var directory = FmlTestCasesLocator.StructureMappingDirectory(version);

            foreach (var oracleCase in FmlManifestLoader.Load(version))
            {
                if (FmlOracleExclusions.IsExcluded(oracleCase.Name))
                {
                    continue;
                }

                if (!oracleCase.OutputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!File.Exists(Path.Combine(directory, oracleCase.MapFile)) ||
                    !File.Exists(Path.Combine(directory, oracleCase.SourceFile)) ||
                    !File.Exists(Path.Combine(directory, oracleCase.OutputFile)))
                {
                    continue;
                }

                yield return [oracleCase];
            }
        }
    }

    [Fact]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenTheOfficialManifests_WhenDiscoveringSupportedCases_ThenSixInScopeCasesExistPerVersion()
    {
        var cases = SupportedCases().Select(row => (FmlOracleCase)row[0]).ToList();

        cases.Count(c => c.Version == "r5").ShouldBe(6);
        cases.Count(c => c.Version == "r4b").ShouldBe(6);
        cases.Count.ShouldBe(12);
    }

    [Theory]
    [MemberData(nameof(SupportedCases))]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenAnOfficialFmlTestCase_WhenExecutingTheMap_ThenTheResultMatchesTheReferenceOutput(FmlOracleCase oracleCase)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(oracleCase.Version);
        var fhirVersion = oracleCase.Version == "r4b" ? FhirVersion.R4B : FhirVersion.R5;
        var schema = fhirVersion.GetSchemaProvider();

        var map = new MappingParser().Parse(File.ReadAllText(Path.Combine(directory, oracleCase.MapFile), Encoding.UTF8));

        var source = ResourceJsonNode.Parse(File.ReadAllText(Path.Combine(directory, oracleCase.SourceFile), Encoding.UTF8));
        var targetType = DetermineTargetType(map);
        var target = JsonSourceNodeFactory.Parse<ResourceJsonNode>($"{{\"resourceType\":\"{targetType}\"}}");

        var fhirPathParser = new FhirPathParser();
        var fhirPathEvaluator = new FhirPathEvaluator();
        var context = new MappingContext
        {
            ErrorMode = ErrorMode.Strict,
            ResourceCreator = type => JsonSourceNodeFactory.Parse<ResourceJsonNode>($"{{\"resourceType\":\"{type}\"}}").ToElement(schema),
            Logger = message => output.WriteLine(message),
            FhirPathEvaluator = (expression, element) =>
                string.IsNullOrWhiteSpace(expression)
                    ? Enumerable.Empty<IElement>()
                    : fhirPathEvaluator.Evaluate(element, fhirPathParser.Parse(expression))
        };

        context.SetSource("src", source.ToElement(schema));
        context.SetTarget("tgt", target.ToElement(schema));
        context.SetTargetResource("tgt", target);

        var mutator = new JsonNodeMutator(fhirPathEvaluator, fhirPathParser, () => schema);
        new MappingEvaluator(MappingEvaluatorOptions.Default, mutator).Execute(map, context);

        context.Errors.ShouldBeEmpty(
            context.Errors.Count == 0
                ? string.Empty
                : "Execution accumulated errors:" + Environment.NewLine +
                  string.Join(Environment.NewLine, context.Errors.Select(e => e.ToString())));

        var expected = CanonicalJson.Canonicalize(File.ReadAllText(Path.Combine(directory, oracleCase.OutputFile), Encoding.UTF8));
        var actual = CanonicalJson.Canonicalize(target.MutableNode().ToJsonString());

        output.WriteLine("EXPECTED:");
        output.WriteLine(expected);
        output.WriteLine("ACTUAL:");
        output.WriteLine(actual);

        actual.ShouldBe(expected);
    }

    private static string DetermineTargetType(MapExpression map)
    {
        var targetUses = map.Uses.FirstOrDefault(u => u.Mode == ModelMode.Target)
            ?? throw new InvalidDataException($"Map '{map.Url}' declares no target 'uses' statement.");

        return targetUses.Url.Split('/').Last();
    }
}
