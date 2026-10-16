// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.DeId;
using Shouldly;
using static Ignixa.CliOnFhir.Tests.DeIdFixture;

namespace Ignixa.CliOnFhir.Tests;

/// <summary>De-identification of a scope of the folder into NDJSON files outside it.</summary>
public class DeIdTests
{
    [Fact]
    public async Task GivenAPatientWithANameBirthDateAndIdentifier_WhenDeIdentified_ThenTheFileHoldsItDeIdentifiedAndTheFactsCountIt()
    {
        using TestFolder root = Root();
        string[] before = Snapshot(Data(root));

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(Config, Out(root), References: ["Patient/p1"])));

        string file = Path.Combine(Out(root), "Patient.ndjson");
        JsonObject patient = Lines(file).ShouldHaveSingleItem();
        patient.ContainsKey("name").ShouldBeFalse();
        ((string)patient["birthDate"]!).ShouldNotBe("1975-03-04");
        ((string)patient["birthDate"]!).ShouldMatch("^1975-[0-9]{2}-[0-9]{2}$");
        ((string)patient["identifier"]![0]!["value"]!).ShouldMatch("^[0-9a-f]{64}$");
        ((string)patient["identifier"]![0]!["system"]!).ShouldBe("urn:mrn");
        ((string)patient["id"]!).ShouldMatch("^[0-9a-f]{64}$");
        ((string)patient["gender"]!).ShouldBe("female");
        facts.Scope.Kind.ShouldBe("references");
        facts.Out.ShouldBe(Out(root));
        facts.Types.ShouldHaveSingleItem().ShouldBe(new DeIdTypeFacts("Patient", 1, 1, 0, file, new FileInfo(file).Length));
        (facts.Processed, facts.Written, facts.Failed).ShouldBe((1, 1, 0));
        facts.Config.Source.ShouldBe("inline");
        facts.Config.Rules.ShouldBe([new DeIdRuleFacts("cryptoHash", 3), new DeIdRuleFacts("redact", 1), new DeIdRuleFacts("dateShift", 1)]);
        facts.Operations["cryptoHash"].ShouldBe(2);
        facts.Operations["redact"].ShouldBeGreaterThan(0);
        Snapshot(Data(root)).ShouldBe(before);
    }

    // The ids and references are hashed with the same key, so a de-identified Observation still points at its de-identified Patient.
    [Fact]
    public async Task GivenAPatientCompartment_WhenDeIdentified_ThenItsResourcesAreWrittenPerTypeAndStillLinked()
    {
        using TestFolder root = Root();

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(Config, Out(root), Patient: "p1")));

        facts.Scope.Kind.ShouldBe("patient");
        facts.Types.Select(type => (type.ResourceType, type.Written)).ShouldBe([("Patient", 1), ("Observation", 1)], ignoreOrder: true);
        JsonObject patient = Lines(Path.Combine(Out(root), "Patient.ndjson")).ShouldHaveSingleItem();
        JsonObject observation = Lines(Path.Combine(Out(root), "Observation.ndjson")).ShouldHaveSingleItem();
        ((string)observation["subject"]!["reference"]!).ShouldBe($"Patient/{(string)patient["id"]!}");
    }

    // A guard of determinism: the keys are the caller's, so the same configuration writes the same bytes on every run.
    [Fact]
    public async Task GivenTheSameConfiguration_WhenRunTwice_ThenTheFilesAreByteForByteTheSame()
    {
        using TestFolder root = Root();
        string second = Path.Combine(root.Path, "out2");

        Facts(await RunAsync(Data(root), new DeIdRequest(Config, Out(root))));
        Facts(await RunAsync(Data(root), new DeIdRequest(Config, second)));

        (await File.ReadAllBytesAsync(Path.Combine(second, "Patient.ndjson"))).ShouldBe(await File.ReadAllBytesAsync(Path.Combine(Out(root), "Patient.ndjson")));
        (await File.ReadAllBytesAsync(Path.Combine(second, "Observation.ndjson"))).ShouldBe(await File.ReadAllBytesAsync(Path.Combine(Out(root), "Observation.ndjson")));
    }

    [Fact]
    public async Task GivenAResourceARuleFailsOn_WhenRun_ThenItIsNotWrittenAndAnIssueNamesItWithoutItsContent()
    {
        using TestFolder root = Root();

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(OneNameConfig, Out(root), ResourceType: "Patient"));

        DeIdFacts facts = Facts(envelope);
        Type(facts, "Patient").ShouldBe(new DeIdTypeFacts("Patient", 2, 1, 1, Path.Combine(Out(root), "Patient.ndjson"), new FileInfo(Path.Combine(Out(root), "Patient.ndjson")).Length));
        Lines(Path.Combine(Out(root), "Patient.ndjson")).Select(line => (string)line["id"]!).ShouldBe(["p1"]);
        IssueItem issue = envelope.Issues.Single(item => item.Code == "deid-resource-incomplete");
        issue.Severity.ShouldBe("error");
        issue.Location.ShouldBe("patients.ndjson:2");
        issue.Diagnostics.ShouldContain("Patient/p2");
        issue.Diagnostics.ShouldNotContain("single");
        issue.Diagnostics.ShouldNotContain("Jones");
        envelope.Evidence.ShouldHaveSingleItem().Reference.ShouldBe("Patient/p2");
    }

    [Fact]
    public async Task GivenMoreFailuresThanTheCap_WhenRun_ThenTheFirstAreIssuesAndTheRestAreCountedInOmitted()
    {
        using TestFolder root = new TestFolder().Write("data/patients.ndjson", string.Join('\n', Enumerable.Range(1, DeIdTally.MaxFailureIssues + 5).Select(i => $$$"""{"resourceType":"Patient","id":"p{{{i}}}","name":[{"family":"A"},{"family":"B"}]}""")));

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(OneNameConfig, Out(root)));

        DeIdFacts facts = Facts(envelope);
        facts.Failed.ShouldBe(DeIdTally.MaxFailureIssues + 5);
        envelope.Issues.Count(issue => issue.Code == "deid-resource-incomplete").ShouldBe(DeIdTally.MaxFailureIssues);
        envelope.Omitted.ShouldContain(item => item.What.StartsWith("5 more resources that were not written", StringComparison.Ordinal));
        envelope.Issues.ShouldContain(issue => issue.Code == "out-not-created");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Theory]
    [InlineData("data")]
    [InlineData("data/sub")]
    [InlineData(".")]
    public async Task GivenAnOutputDirectoryInOrAroundTheFolder_WhenRun_ThenItIsRefusedAndTheFolderIsUnchanged(string where)
    {
        using TestFolder root = Root();
        string[] before = Snapshot(Data(root));

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Path.Combine(root.Path, where))));

        error.Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnOutputDirectoryThatLinksIntoTheFolder_WhenRun_ThenItIsRefused()
    {
        if (OperatingSystem.IsWindows())
        {
            return; // creating symbolic links needs a privilege Windows CI does not grant
        }

        using TestFolder root = Root();
        Directory.CreateDirectory(Path.Combine(Data(root), "inner"));
        Directory.CreateSymbolicLink(Out(root), Path.Combine(Data(root), "inner"));
        string[] before = Snapshot(Data(root));

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root))));

        error.Code.ShouldBe("out-refused");
        Snapshot(Data(root)).ShouldBe(before);
    }

    [Fact]
    public async Task GivenAnExistingTypeFileWithoutOverwrite_WhenRun_ThenItIsRefusedBeforeAnythingIsReadAndLeftAsItWas()
    {
        using TestFolder root = Root().Write("out/Patient.ndjson", "old\n");

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root))));

        error.Code.ShouldBe("out-refused");
        error.Diagnostics.ShouldContain("already exists");
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Patient.ndjson"))).ShouldBe("old\n");
        Directory.GetFileSystemEntries(Out(root)).ShouldBe([Path.Combine(Out(root), "Patient.ndjson")]);
    }

    [Fact]
    public async Task GivenAnExistingTypeFileWithOverwrite_WhenRun_ThenItIsReplacedAndAnotherTypesFileIsKeptWithANote()
    {
        using TestFolder root = Root().Write("out/Patient.ndjson", "old\n").Write("out/Condition.ndjson", "kept\n");

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(Config, Out(root), ResourceType: "Patient", Overwrite: true));

        Facts(envelope).Written.ShouldBe(2);
        Lines(Path.Combine(Out(root), "Patient.ndjson")).Length.ShouldBe(2);
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Condition.ndjson"))).ShouldBe("kept\n");
    }

    [Fact]
    public async Task GivenTypeFileWithOverwriteThatNothingIsWrittenTo_WhenRun_ThenTheOldFileIsKeptAndANoteSaysSo()
    {
        using TestFolder root = Root().Write("out/Patient.ndjson", "old\n");
        const string onlyTwoNames = """{"fhirPathRules":[{"path":"Patient.name.single()","method":"redact"}]}""";

        ToolEnvelope<DeIdFacts?> envelope = await RunAsync(Data(root), new DeIdRequest(onlyTwoNames, Out(root), References: ["Patient/p2"], Overwrite: true));

        Facts(envelope).Written.ShouldBe(0);
        (await File.ReadAllTextAsync(Path.Combine(Out(root), "Patient.ndjson"))).ShouldBe("old\n");
        envelope.Issues.ShouldContain(issue => issue.Code == "out-kept");
    }

    [Fact]
    public async Task GivenMoreResourcesThanTheLimit_WhenRun_ThenItFailsBeforeReadingAndCreatesNothing()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root), MaxResources: 3)));

        error.Code.ShouldBe("too-many-resources");
        error.Diagnostics.ShouldContain("4 resources");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenARunThatTimesOut_WhenRun_ThenItIsATimeoutAndNothingIsWritten()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config, Out(root)), timedOut: true));

        error.Code.ShouldBe("deid-timeout");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenNoOutputDirectory_WhenRun_ThenItIsAnInvalidParameter()
    {
        using TestFolder root = Root();

        Error(await RunAsync(Data(root), new DeIdRequest(Config, Out: null))).Code.ShouldBe("invalid-parameter");
    }

    [Fact]
    public async Task GivenNoConfiguration_WhenRun_ThenItSaysThereIsNoSafeDefault()
    {
        using TestFolder root = Root();

        IssueItem error = Error(await RunAsync(Data(root), new DeIdRequest(Config: null, Out(root))));

        error.Code.ShouldBe("deid-config-required");
        error.Diagnostics.ShouldContain("no default profile");
        Directory.Exists(Out(root)).ShouldBeFalse();
    }

    [Fact]
    public async Task GivenAConfigurationFile_WhenRun_ThenTheFactsNameTheFile()
    {
        using TestFolder root = Root().Write("config.json", Config);

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(Path.Combine(root.Path, "config.json"), Out(root))));

        facts.Config.Source.ShouldBe("file");
        facts.Config.File.ShouldBe(Path.Combine(root.Path, "config.json"));
    }

    // Bundle entries reference each other by urn:uuid; written one type per file, those would point at nothing, so they are written as Type/id.
    [Fact]
    public async Task GivenABundleWithUrnReferences_WhenDeIdentified_ThenTheReferencesAreWrittenAsTypeAndId()
    {
        const string bundle = """
            {"resourceType":"Bundle","type":"transaction","entry":[
              {"fullUrl":"urn:uuid:7f9a1c52-0000-4000-8000-000000000001","resource":{"resourceType":"Patient","id":"p1"}},
              {"fullUrl":"urn:uuid:7f9a1c52-0000-4000-8000-000000000002","resource":{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"},"subject":{"reference":"urn:uuid:7f9a1c52-0000-4000-8000-000000000001"}}}]}
            """;
        using TestFolder root = new TestFolder().Write("data/bundle.json", bundle);
        const string keepAll = """{"fhirPathRules":[{"path":"Observation.code.text","method":"redact"}]}""";

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(keepAll, Out(root), ResourceType: "Observation")));

        facts.ReferencesResolved.ShouldBe(1);
        ((string)Lines(Path.Combine(Out(root), "Observation.ndjson")).Single()["subject"]!["reference"]!).ShouldBe("Patient/p1");
    }

    // The library counts a date shift as a perturbation; the facts say what the library counted, named as it names methods.
    [Fact]
    public async Task GivenOnlyRedactRules_WhenRun_ThenOperationsHoldOnlyRedact()
    {
        using TestFolder root = Root();
        const string redact = """{"fhirPathRules":[{"path":"Patient.name","method":"REDACT"}]}""";

        DeIdFacts facts = Facts(await RunAsync(Data(root), new DeIdRequest(redact, Out(root), ResourceType: "Patient")));

        facts.Config.Rules.ShouldBe([new DeIdRuleFacts("redact", 1)]);
        facts.Operations.Keys.ShouldBe(["redact"]);
    }
}
