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

    private static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
}
