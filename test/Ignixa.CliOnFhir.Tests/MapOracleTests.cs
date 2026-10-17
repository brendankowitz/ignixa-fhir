// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.CliOnFhir.Session;
using Ignixa.FhirMappingLanguage.Tests.Conformance;
using Ignixa.Specification.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>
/// The official FML oracle cases that <c>FmlTransformOracleTests</c> runs against the library, run through the <c>map</c> verb: the
/// same case list, exclusions and known gaps (compiled in from that project), the source as the folder's only resource and the map
/// as a file outside it. A case the library passes passes here; a known gap stays broken here too, so the two cannot drift.
/// </summary>
public class MapOracleTests
{
    private static readonly string[] Versions = ["r5", "r4b"];

    public static TheoryData<FmlOracleCase> InScopeCases() => [.. Cases()];

    // As the library's oracle counts them: six per version, two of each passing; a case dropped from the list must not pass by absence.
    [Fact]
    public void GivenTheOfficialManifests_WhenListingTheCases_ThenTwelveAreInScopeAndFourAreExpectedToMatch()
    {
        FmlOracleCase[] cases = [.. Cases()];

        cases.Length.ShouldBe(12);
        cases.Count(oracleCase => !FmlKnownEvaluatorGaps.IsKnownGap(oracleCase.Name)).ShouldBe(4);
    }

    [Theory]
    [MemberData(nameof(InScopeCases))]
    public async Task GivenAnOfficialFmlTestCase_WhenRunThroughTheMapVerb_ThenTheResultIsTheLibrarysOutcome(FmlOracleCase oracleCase)
    {
        string directory = FmlTestCasesLocator.StructureMappingDirectory(oracleCase.Version);
        string source = JsonNode.Parse(await ReadAsync(directory, oracleCase.SourceFile))!.ToJsonString();
        using TestFolder root = new TestFolder()
            .Write("data/source.ndjson", source + "\n")
            .Write("maps/" + oracleCase.MapFile, await ReadAsync(directory, oracleCase.MapFile));
        IFhirSchemaProvider schema = (oracleCase.Version == "r4b" ? FhirVersion.R4B : FhirVersion.R5).GetSchemaProvider();

        ToolEnvelope<MapFacts?> envelope = await MapRunner.RunAsync(
            new FolderSession(Path.Combine(root.Path, "data"), schema, NullLoggerFactory.Instance),
            new MapRequest(Path.Combine(root.Path, "maps", oracleCase.MapFile)),
            NullLogger.Instance,
            CancellationToken.None);

        MapFacts facts = MapFixture.Facts(envelope);
        string expected = CanonicalJson.Canonicalize(await ReadAsync(directory, oracleCase.OutputFile));
        if (FmlKnownEvaluatorGaps.IsKnownGap(oracleCase.Name))
        {
            facts.Resources.ShouldNotBeNull().Select(resource => CanonicalJson.Canonicalize(resource.ToJsonString())).ShouldNotContain(expected);
            return;
        }

        (facts.Transformed, facts.Failed).ShouldBe((1, 0), MapFixture.Json(envelope));
        CanonicalJson.Canonicalize(facts.Resources.ShouldNotBeNull().Single().ToJsonString()).ShouldBe(expected);
    }

    private static IEnumerable<FmlOracleCase> Cases() =>
        Versions
            .SelectMany(FmlManifestLoader.Load)
            .Where(oracleCase => !FmlOracleExclusions.IsExcluded(oracleCase.Name) && oracleCase.OutputFile.EndsWith(".json", StringComparison.OrdinalIgnoreCase));

    private static Task<string> ReadAsync(string directory, string file) => File.ReadAllTextAsync(Path.Combine(directory, file), Encoding.UTF8);
}
