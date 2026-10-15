// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;
using Ignixa.CliOnFhir.Inspection;
using Ignixa.CliOnFhir.Session;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class FolderInspectorTests
{
    [Fact]
    public void GivenMixedFolder_WhenInspected_ThenTypesAreCountedMostFrequentFirst()
    {
        using var folder = new TestFolder()
            .Write("p.json", """{"resourceType":"Patient","id":"p1"}""")
            .Write("obs.ndjson", """
                {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"a"}}
                {"resourceType":"Observation","id":"o2","status":"final","code":{"text":"b"}}
                """);

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(Open(folder));

        envelope.Facts.ResourceCount.ShouldBe(3);
        envelope.Facts.PatientCount.ShouldBe(1);
        envelope.Facts.ResourceTypes.ShouldBe([new ResourceTypeCount("Observation", 2), new ResourceTypeCount("Patient", 1)]);
        envelope.Issues.ShouldBeEmpty();
        envelope.Omitted.ShouldBeEmpty();
        envelope.Derived.ShouldBeNull();
    }

    [Fact]
    public void GivenMoreProblemsThanTheIssueLimit_WhenInspected_ThenTheRemainderIsReportedAsOmitted()
    {
        using var folder = new TestFolder();
        for (int i = 0; i < 5; i++)
        {
            folder.Write($"bad{i}.json", "{ not json");
        }

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(Open(folder), issueLimit: 2);

        envelope.Issues.Count.ShouldBe(2);
        envelope.Omitted.ShouldHaveSingleItem().ShouldBe(new OmittedItem("3 more diagnostics", "limit"));
        envelope.Facts.DiagnosticCounts["invalid-json"].ShouldBe(5);
    }

    [Fact]
    public void GivenFolder_WhenInspected_ThenNoIndexIsBuilt()
    {
        using var folder = new TestFolder()
            .Write("p.json", """{"resourceType":"Patient","id":"p1"}""")
            .Write("o.json", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"a"},"effectiveDateTime":"garbage"}""");
        FolderSession session = Open(folder);

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session);

        session.BuiltIndexTypes.ShouldBeEmpty();
        envelope.Facts.Index.ShouldBeNull();
        envelope.Facts.DiagnosticCounts.ShouldBeEmpty();
        envelope.Facts.Build.CatalogMilliseconds.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GivenUnknownResourceType_WhenInspected_ThenItIsReportedAsDiagnosticAndNotCounted()
    {
        using var folder = new TestFolder()
            .Write("x.json", """{"resourceType":"NotAResource","id":"x"}""")
            .Write("p.json", """{"resourceType":"Patient","id":"p1"}""");

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(Open(folder));

        envelope.Facts.ResourceCount.ShouldBe(1);
        envelope.Facts.DiagnosticCounts["unknown-resource-type"].ShouldBe(1);
        envelope.Issues.ShouldHaveSingleItem().Location.ShouldBe("x.json");
    }

    [Fact]
    public void GivenBuildIndexesRequested_WhenInspected_ThenIndexFactsAndIndexDiagnosticsAreIncluded()
    {
        using var folder = new TestFolder()
            .Write("p.json", """{"resourceType":"Patient","id":"p1","birthDate":"1980-01-02"}""")
            .Write("obs.ndjson", """
                {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"a"},"effectiveDateTime":"garbage"}
                {"resourceType":"Observation","id":"o2","status":"final","code":{"text":"b"}}
                """);
        FolderSession session = Open(folder);

        ToolEnvelope<FolderInspection> envelope = FolderInspector.Inspect(session, buildIndexes: true);

        IndexFacts index = envelope.Facts.Index.ShouldNotBeNull();
        index.ResourceTypes.Select(t => (t.ResourceType, t.ResourceCount)).ShouldBe([("Observation", 2), ("Patient", 1)]);
        index.IndexEntries.ShouldBe(index.ResourceTypes.Sum(t => t.IndexEntries));
        index.IndexEntries.ShouldBeGreaterThan(0);
        index.DefinitionsLoadMilliseconds.ShouldNotBeNull();
        index.ElapsedMilliseconds.ShouldBe(index.ResourceTypes.Sum(t => t.ElapsedMilliseconds));
        session.BuiltIndexTypes.ShouldBe(["Observation", "Patient"]);
        envelope.Facts.DiagnosticCounts["index-extraction-failed"].ShouldBe(1);
        envelope.Issues.ShouldHaveSingleItem().Code.ShouldBe("index-extraction-failed");
    }

    private static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
}
