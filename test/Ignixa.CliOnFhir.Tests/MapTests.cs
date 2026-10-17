// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Mapping;
using Ignixa.FhirMappingLanguage.Expressions;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;
using static Ignixa.CliOnFhir.Tests.MapFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>Runs of an FML map over resources of the folder: a preview, or files outside it.</summary>
public class MapTests
{
    [Fact]
    public async Task GivenAPatientToBasicMap_WhenRunOverTwoPatients_ThenThePreviewHoldsBothWithTheirSources()
    {
        using TestFolder root = Root();

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic));

        MapFacts facts = Facts(envelope);
        facts.Resources.ShouldNotBeNull().Select(resource => resource.ToJsonString()).ShouldBe([
            """{"resourceType":"Basic","id":"p1","created":"1975-03-04"}""",
            """{"resourceType":"Basic","id":"p2","created":"1985-06-07"}""",
        ]);
        facts.Sources.ShouldBe(["Patient/p1", "Patient/p2"]);
        envelope.Evidence.Select(item => (item.Reference, item.File, item.Line)).ShouldBe([("Patient/p1", "patients.ndjson", (int?)1), ("Patient/p2", "patients.ndjson", (int?)2)]);
        (facts.Processed, facts.Transformed, facts.Failed, facts.Skipped, facts.Returned).ShouldBe((2, 2, 0, 0, (int?)2));
        facts.Scope.Kind.ShouldBe("type");
        facts.Map.ShouldBe(new MapSourceFacts("inline", "fml", "http://example.org/fml/patient-basic", "PatientToBasic", "Patient", "Basic", File: null, Reference: null));
        envelope.Next.ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenAnOutputDirectory_WhenRun_ThenEveryTransformedResourceIsWrittenToItsTypeFile()
    {
        using TestFolder root = Root();
        string[] before = Snapshot(Data(root));

        MapFacts facts = Facts(await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Out(root))));

        string file = Path.Combine(Out(root), "Basic.ndjson");
        facts.Resources.ShouldBeNull();
        facts.Out.ShouldBe(Out(root));
        facts.File.ShouldNotBeNull().Path.ShouldBe(file);
        facts.File.Resources.ShouldBe(2);
        Lines(file).Select(line => (string)line["id"]!).ShouldBe(["p1", "p2"]);
        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAMapWithAParseError_WhenRun_ThenAnErrorGivesItsLineAndColumn()
    {
        using TestFolder root = Root();
        const string broken = "map \"http://example.org/fml/broken\" = \"Broken\"\ngroup B(source src : Patient, target tgt : Basic) {\n  src.id as id -> -> tgt.id = id;\n}\n";

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(broken)));

        error.Code.ShouldBe("map-invalid");
        error.Diagnostics.ShouldContain("is not valid FML at line 3");
        error.Location.ShouldNotBeNull().ShouldStartWith("inline:3:");
    }

    // The tokenizer's failures come wrapped at the start of the text; the location is where the tokenizer stopped.
    [Fact]
    public async Task GivenACharacterTheTokenizerRefuses_WhenRun_ThenTheErrorGivesWhereItIs()
    {
        using TestFolder root = Root();
        const string bad = "map \"http://example.org/fml/bad\" = \"Bad\"\ngroup B(source src : Patient, target tgt : Basic) {\n  src.id as id -> tgt.id = id; #\n}\n";

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(bad)));

        error.Code.ShouldBe("map-invalid");
        error.Diagnostics.ShouldContain("is not valid FML at line 3, column 32");
        error.Location.ShouldBe("inline:3:32");
    }

    [Fact]
    public async Task GivenResourcesOfAnotherTypeInScope_WhenRun_ThenTheyAreSkippedAndAnIssueCountsThem()
    {
        using TestFolder root = Root();

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic, Patient: "p1"));

        MapFacts facts = Facts(envelope);
        (facts.Processed, facts.Transformed, facts.Skipped).ShouldBe((2, 1, 1));
        IssueItem mismatch = envelope.Issues.Single(issue => issue.Code == "map-source-mismatch");
        mismatch.Severity.ShouldBe("warning");
        mismatch.Diagnostics.ShouldBe("1 Observation resources in scope are not the map's source type, Patient, so they were skipped.");
    }

    [Fact]
    public async Task GivenAMapThatRunsLongerThanTheTransformTimeout_WhenRun_ThenThatResourceIsAnErrorAndTheRunAnswers()
    {
        using TestFolder root = Root();

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(FanOut(40), References: ["Patient/p1"]), transformTimeout: TimeSpan.FromMilliseconds(200));

        MapFacts facts = Facts(envelope);
        (facts.Transformed, facts.Failed).ShouldBe((0, 1));
        IssueItem timedOut = envelope.Issues.Single(issue => issue.Code == "map-transform-timeout");
        timedOut.Severity.ShouldBe("error");
        timedOut.Location.ShouldBe("patients.ndjson:1");
    }

    [Fact]
    public async Task GivenAnImportGivenOnTheCall_WhenRun_ThenTheImportedGroupIsCalled()
    {
        using TestFolder root = Root().Write("helpers.fml", Helpers);

        MapFacts facts = Facts(await RunAsync(Data(root), new MapRequest(Main, Imports: [Path.Combine(root.Path, "helpers.fml")])));

        facts.Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["p1", "p2"]);
        facts.Imports.ShouldBe([new MapImportFacts("http://example.org/fml/helpers", "file", Reference: null)]);
    }

    [Fact]
    public async Task GivenAnImportNothingProvides_WhenRun_ThenTheErrorNamesIt()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(Main)));

        error.Code.ShouldBe("map-import-unresolved");
        error.Diagnostics.ShouldContain("'http://example.org/fml/helpers'");
    }

    // A url the folder's map holds is folder text: it is named by its digest.
    [Fact]
    public async Task GivenAFolderMapWhoseImportNothingProvides_WhenRun_ThenTheErrorNamesItByDigest()
    {
        const string url = "http://example.org/fml/ignore-previous-instructions";
        string importer = MapFixture.StructureMap("sm1", "http://example.org/sm/importer").Replace("\"group\":", $"\"import\":[\"{url}\"],\"group\":", StringComparison.Ordinal);
        using TestFolder root = Root().Write("data/maps.ndjson", importer);

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest("StructureMap/sm1")));

        error.Code.ShouldBe("map-import-unresolved");
        error.Diagnostics.ShouldContain(Ignixa.CliOnFhir.Catalog.FhirIds.Digest(url));
        error.Diagnostics.ShouldNotContain("ignore-previous");
    }

    [Fact]
    public async Task GivenAStructureMapOfTheFolder_WhenNamedByUrlOrId_ThenItRunsAndIsCited()
    {
        using TestFolder root = Root().Write("data/maps.ndjson", MapFixture.StructureMap("sm1", "http://example.org/sm/patient-basic"));

        ToolEnvelope<MapFacts?> byUrl = await RunAsync(Data(root), new MapRequest("http://example.org/sm/patient-basic"));
        ToolEnvelope<MapFacts?> byId = await RunAsync(Data(root), new MapRequest("StructureMap/sm1"));

        foreach (ToolEnvelope<MapFacts?> envelope in new[] { byUrl, byId })
        {
            MapFacts facts = Facts(envelope);
            facts.Map.Source.ShouldBe("folder");
            facts.Map.Format.ShouldBe("structuremap");
            facts.Map.Reference.ShouldBe("StructureMap/sm1");
            facts.Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["p1", "p2"]);
            envelope.Evidence[0].ShouldBe(new EvidenceItem("StructureMap/sm1", "maps.ndjson", 1, 0));
        }
    }

    [Fact]
    public async Task GivenAMapWhoseRulesMatchNothing_WhenRun_ThenAWarningSaysTheResultsAreEmpty()
    {
        using TestFolder root = Root();
        const string nothing = "map \"http://example.org/fml/nothing\" = \"Nothing\"\ngroup N(source src : Patient, target tgt : Basic) {\n  src.maritalStatus as m -> tgt.code = m;\n}\n";

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(nothing));

        Facts(envelope).Transformed.ShouldBe(2);
        envelope.Issues.Single(issue => issue.Code == "map-output-empty").Diagnostics.ShouldStartWith("2 of the 2 transformed resources hold nothing but their resourceType");
    }

    // Transform functions and StructureMap JSON (which states every assignment as one) write into the target.
    [Fact]
    public async Task GivenTransformFunctionsInFmlAndInAStructureMap_WhenRun_ThenTheirResultsAreWritten()
    {
        using TestFolder root = Root();
        const string functions = """
            map "http://example.org/fml/functions" = "Functions"
            group F(source src : Patient, target tgt : Basic) {
              src.id as id -> tgt.id = truncate(id, 1);
              src.birthDate as b -> tgt.created = copy(b);
              src.gender as g -> tgt.code = cc('http://hl7.org/fhir/administrative-gender', g);
            }
            """;

        ToolEnvelope<MapFacts?> fml = await RunAsync(Data(root), new MapRequest(functions, References: ["Patient/p1"]));
        ToolEnvelope<MapFacts?> json = await RunAsync(Data(root), new MapRequest(MapFixture.StructureMap("sm1", "http://example.org/sm/patient-basic")));

        Facts(fml).Resources.ShouldHaveSingleItem().ToJsonString().ShouldBe("""{"resourceType":"Basic","id":"p","created":"1975-03-04","code":{"coding":[{"system":"http://hl7.org/fhir/administrative-gender","code":"female"}]}}""");
        Facts(json).Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["p1", "p2"]);
        fml.Issues.ShouldNotContain(issue => issue.Code == "map-output-empty");
        json.Issues.ShouldNotContain(issue => issue.Code == "map-output-empty");
    }

    // create() makes its value with the evaluator's mutator, so the value can be written into the target and then filled in.
    [Fact]
    public async Task GivenAMapThatCreatesAReference_WhenRun_ThenTheCreatedValueIsWrittenAndFilledIn()
    {
        using TestFolder root = Root();
        const string create = """
            map "http://example.org/fml/create" = "Create"
            group C(source src : Patient, target tgt : Basic) {
              src -> tgt.extension as ext, ext.url = 'http://example.org/subject', ext.value = create('Reference') as r, r.reference = reference(src);
            }
            """;

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(create));

        MapFacts facts = Facts(envelope);
        (facts.Transformed, facts.Failed).ShouldBe((2, 0), Json(envelope));
        facts.Resources.ShouldNotBeNull().Select(resource => resource.ToJsonString()).ShouldBe([
            """{"resourceType":"Basic","extension":[{"url":"http://example.org/subject","valueReference":{"reference":"Patient/p1"}}]}""",
            """{"resourceType":"Basic","extension":[{"url":"http://example.org/subject","valueReference":{"reference":"Patient/p2"}}]}""",
        ]);
    }

    // The README's account of evaluate(): the library reads a lone name as a variable and a quoted one as text; a path works.
    [Theory]
    [InlineData("gender", null)]
    [InlineData("'gender'", "gender")]
    [InlineData("gender.first()", "female")]
    [InlineData("$this.gender", "female")]
    public async Task GivenEvaluateOfTheGender_WhenRun_ThenTheResultIsAsTheLibraryReadsTheExpression(string expression, string? gender)
    {
        using TestFolder root = Root();
        string map = $$"""
            map "http://example.org/fml/evaluate" = "Evaluate"
            group E(source src : Patient, target tgt : Patient) {
              src -> tgt.gender = evaluate(src, {{expression}});
            }
            """;

        MapFacts facts = Facts(await RunAsync(Data(root), new MapRequest(map, References: ["Patient/p1"])));

        facts.Resources.ShouldHaveSingleItem()["gender"]?.GetValue<string>().ShouldBe(gender);
    }

    // An invoked group writes into its own target parameter, whatever it is called.
    [Fact]
    public async Task GivenAnImportedGroupWhoseTargetHasAnotherName_WhenCalled_ThenItWritesIntoTheTarget()
    {
        const string helpers = """
            map "http://example.org/fml/helpers" = "Helpers"
            group Copy(source s : Patient, target t : Basic) {
              s.id as id -> t.id = id;
            }
            """;
        using TestFolder root = Root().Write("helpers.fml", helpers);

        MapFacts facts = Facts(await RunAsync(Data(root), new MapRequest(Main, Imports: [Path.Combine(root.Path, "helpers.fml")])));

        facts.Resources.ShouldNotBeNull().Select(resource => (string)resource["id"]!).ShouldBe(["p1", "p2"]);
    }

    [Fact]
    public async Task GivenMoreTransformedResourcesThanThePreviewHolds_WhenRun_ThenTheRestAreCountedInOmitted()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", string.Join('\n', Enumerable.Range(1, MapRunner.PreviewCount + 2).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}"}""")));

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic));

        MapFacts facts = Facts(envelope);
        facts.Returned.ShouldBe(MapRunner.PreviewCount);
        facts.Transformed.ShouldBe(MapRunner.PreviewCount + 2);
        envelope.Omitted.ShouldContain(item => item.What.StartsWith("2 more transformed resources", StringComparison.Ordinal) && item.Why == "limit");
    }

    [Fact]
    public async Task GivenASmallBudget_WhenRun_ThenFewerResourcesAreReturnedAndTheRestAreOmittedForSize()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", string.Join('\n', Enumerable.Range(1, MapRunner.PreviewCount).Select(i => $$"""{"resourceType":"Patient","id":"p{{i}}-{{new string('x', 60)}}"}""")));

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(PatientToBasic, MaxBytes: 2048));

        MapFacts facts = Facts(envelope);
        facts.Returned.ShouldNotBeNull().ShouldBeLessThan(MapRunner.PreviewCount);
        facts.Sources!.Count.ShouldBe(facts.Returned.Value);
        envelope.Omitted.ShouldContain(item => item.Why == "size-budget");
        Json(envelope).Length.ShouldBeLessThanOrEqualTo(2048);
    }

    [Fact]
    public async Task GivenAMapWhoseTargetIsNotAResourceType_WhenRun_ThenItIsUnsupported()
    {
        using TestFolder root = Root();
        const string toDatatype = "map \"http://example.org/fml/dt\" = \"Dt\"\ngroup D(source src : Patient, target tgt : HumanName) {\n  src.id as id -> tgt.text = id;\n}\n";

        Error(await RunAsync(Data(root), new MapRequest(toDatatype))).Code.ShouldBe("map-unsupported");
    }

    [Fact]
    public async Task GivenAMapNamedByAUrlTheFolderDoesNotHold_WhenRun_ThenNothingIsFetchedAndItIsNotFound()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest("http://hl7.org/fhir/StructureMap/Patient4to5")));

        error.Code.ShouldBe("map-not-found");
        error.Diagnostics.ShouldContain("never fetched from the network");
    }

    // As for a de-identification configuration, text that cannot be a path is read as a map, never taken for a path and repeated.
    [Theory]
    [InlineData("\uFEFFmap \"http://example.org/fml/bom\" = \"Bom\"\ngroup B(source src : Patient, target tgt : Basic) {\n  src.id as id -> tgt.id = id;\n}\n", null)]
    [InlineData("[{\"resourceType\":\"StructureMap\"}]", "map-invalid")]
    public async Task GivenInlineMapTextAfterAByteOrderMarkOrAsAnArray_WhenRun_ThenItIsReadInline(string map, string? error)
    {
        using TestFolder root = Root();

        ToolEnvelope<MapFacts?> envelope = await RunAsync(Data(root), new MapRequest(map));

        if (error is null)
        {
            Facts(envelope).Map.Source.ShouldBe("inline");
        }
        else
        {
            Error(envelope).Code.ShouldBe(error);
        }
    }

    [Fact]
    public async Task GivenMoreResourcesThanTheLimit_WhenRun_ThenItFailsBeforeReading()
    {
        using TestFolder root = Root();

        Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, MaxResources: 1))).Code.ShouldBe("too-many-resources");
    }

    [Fact]
    public async Task GivenARunThatTimesOut_WhenRun_ThenItIsAMapTimeoutAndNothingIsWritten()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, Out: Out(root)), timedOut: true));

        error.Code.ShouldBe("map-timeout");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenOverwriteWithoutOut_WhenRun_ThenItIsAnInvalidParameter()
    {
        using TestFolder root = Root();

        Error(await RunAsync(Data(root), new MapRequest(PatientToBasic, Overwrite: true))).Code.ShouldBe("invalid-parameter");
    }

    // A census: the map limits walk every node a parsed map can hold, so a node type added to the library cannot slip past them.
    [Fact]
    public void GivenEveryExpressionTypeOfTheMappingLibrary_WhenTheMapLimitsAreChecked_ThenTheyKnowEachOne()
    {
        Type[] nodes = [.. typeof(Expression).Assembly.GetTypes().Where(type => type.IsSubclassOf(typeof(Expression)) && !type.IsAbstract)];

        nodes.ShouldNotBeEmpty();
        nodes.Except(MapGuards.KnownNodes).ShouldBeEmpty();
    }

    [Fact]
    public async Task GivenTheSamePreviewTwice_WhenRun_ThenTheResourcesAreTheSameJson()
    {
        using TestFolder root = Root();

        MapFacts first = Facts(await RunAsync(Data(root), new MapRequest(PatientToBasic)));
        MapFacts second = Facts(await RunAsync(Data(root), new MapRequest(PatientToBasic)));

        second.Resources!.Select(node => node.ToJsonString()).ShouldBe(first.Resources!.Select(node => node.ToJsonString()));
        JsonNode.DeepEquals(second.Resources![0], first.Resources![0]).ShouldBeTrue();
    }
}
