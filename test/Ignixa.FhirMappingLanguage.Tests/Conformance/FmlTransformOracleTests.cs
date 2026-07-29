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
    [InlineData("r5")]
    [InlineData("r4b")]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenTheOfficialManifests_WhenCheckingTheCorpus_ThenEveryInScopeCaseHasItsThreeFilesOnDisk(string version)
    {
        var directory = FmlTestCasesLocator.StructureMappingDirectory(version);

        var inScope = FmlManifestLoader.Load(version)
            .Where(c => !FmlOracleExclusions.IsExcluded(c.Name))
            .Where(c => c.OutputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .ToList();

        inScope.Count.ShouldBe(6);

        foreach (var oracleCase in inScope)
        {
            AssertCorpusFileExists(directory, oracleCase, oracleCase.MapFile);
            AssertCorpusFileExists(directory, oracleCase, oracleCase.SourceFile);
            AssertCorpusFileExists(directory, oracleCase, oracleCase.OutputFile);
        }
    }

    [Fact]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenTheInScopeCases_WhenPartitioningByKnownGaps_ThenTwoPassAndTenAreRatchetedGapExecutions()
    {
        var cases = SupportedCases().Select(row => (FmlOracleCase)row[0]).ToList();

        cases.Count.ShouldBe(12);
        cases.Count(c => FmlKnownEvaluatorGaps.IsKnownGap(c.Name)).ShouldBe(10);
        cases.Count(c => !FmlKnownEvaluatorGaps.IsKnownGap(c.Name)).ShouldBe(2);
    }

    [Fact]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenTheKnownEvaluatorGaps_WhenListingEntries_ThenContainsExactlyTheFiveDocumentedGaps()
    {
        var expected = new List<string>
        {
            "qr2patgender",
            "qr2pathumannametwice",
            "qr2pathumannameshared",
            "reference",
            "qr2pat-gender-conformstoqr"
        };

        FmlKnownEvaluatorGaps.All.Keys
            .OrderBy(k => k, StringComparer.Ordinal)
            .ShouldBe(expected.OrderBy(k => k, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("r5")]
    [InlineData("r4b")]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenTheKnownEvaluatorGaps_WhenCheckingAgainstTheManifest_ThenEveryGapKeyMatchesAnInScopeCase(string version)
    {
        var inScopeSegments = FmlManifestLoader.Load(version)
            .Where(c => !FmlOracleExclusions.IsExcluded(c.Name))
            .Select(c => c.Name.Split('/', StringSplitOptions.RemoveEmptyEntries).Last())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        FmlKnownEvaluatorGaps.All.ShouldNotBeEmpty();

        foreach (var key in FmlKnownEvaluatorGaps.All.Keys)
        {
            inScopeSegments.ShouldContain(key);
        }
    }

    [Theory]
    [MemberData(nameof(SupportedCases))]
    [Trait("Category", "OfficialTestSuite")]
    public void GivenAnOfficialFmlTestCase_WhenExecutingTheMap_ThenTheResultMatchesTheReferenceOutput(FmlOracleCase oracleCase)
    {
        if (FmlKnownEvaluatorGaps.IsKnownGap(oracleCase.Name))
        {
            AssertKnownGapStillBroken(oracleCase);
            return;
        }

        var (expected, actual) = ExecuteCase(oracleCase);

        output.WriteLine("EXPECTED:");
        output.WriteLine(expected);
        output.WriteLine("ACTUAL:");
        output.WriteLine(actual);

        actual.ShouldBe(expected);
    }

    private void AssertKnownGapStillBroken(FmlOracleCase oracleCase)
    {
        string expected;
        string actual;

        try
        {
            (expected, actual) = ExecuteCase(oracleCase);
        }
        catch (MappingExecutionException)
        {
            return;
        }
        catch (NotSupportedException)
        {
            return;
        }

        actual.ShouldNotBe(
            expected,
            $"Known evaluator gap '{oracleCase}' now produces output matching the reference. " +
            $"The underlying defect appears fixed — remove '{oracleCase.Name}' from {nameof(FmlKnownEvaluatorGaps)}.");
    }

    private (string Expected, string Actual) ExecuteCase(FmlOracleCase oracleCase)
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

        var expected = CanonicalJson.Canonicalize(File.ReadAllText(Path.Combine(directory, oracleCase.OutputFile), Encoding.UTF8));
        var actual = CanonicalJson.Canonicalize(target.MutableNode().ToJsonString());

        return (expected, actual);
    }

    private static void AssertCorpusFileExists(string directory, FmlOracleCase oracleCase, string fileName)
    {
        var path = Path.Combine(directory, fileName);

        File.Exists(path).ShouldBeTrue(
            $"Corpus file for case '{oracleCase.Name}' is missing: {path}. " +
            "Verify the fhir-test-cases download completed.");
    }

    private static string DetermineTargetType(MapExpression map)
    {
        var targetUses = map.Uses.FirstOrDefault(u => u.Mode == ModelMode.Target)
            ?? throw new InvalidDataException($"Map '{map.Url}' declares no target 'uses' statement.");

        return targetUses.Url.Split('/').Last();
    }
}
