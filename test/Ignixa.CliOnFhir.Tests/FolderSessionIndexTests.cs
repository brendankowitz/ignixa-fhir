// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.CliOnFhir.Session;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class FolderSessionIndexTests
{
    private const string PatientJson = """{"resourceType":"Patient","id":"p1","name":[{"family":"Smith","given":["Ann"]}],"birthDate":"1980-01-02"}""";

    [Fact]
    public void GivenCataloguedPatient_WhenIndexRequested_ThenSearchParametersAreExtracted()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);

        ResourceTypeIndex index = Open(folder).GetIndex("Patient");

        IndexedResource patient = index.Resources.ShouldHaveSingleItem();
        patient.Key.Id.ShouldBe("p1");
        IReadOnlyCollection<SearchIndexEntry> entries = patient.Index;
        entries.Select(e => e.SearchParameter.Code).ShouldContain("family");
        entries.Select(e => e.SearchParameter.Code).ShouldContain("birthdate");
        index.ResourceType.ShouldBe("Patient");
        index.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GivenMixedTypes_WhenOneTypeIsRequested_ThenOnlyThatTypeIsIndexedAndOtherFilesAreNotRead()
    {
        using var folder = new TestFolder()
            .Write("p.json", PatientJson)
            .Write("obs.json", """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"x"}}""");
        FolderSession session = Open(folder);
        session.Catalog.Entries.Count.ShouldBe(2);

        // Corrupt the Observation file after cataloguing: an index request for Patient must never open it.
        File.WriteAllText(Path.Combine(folder.Path, "obs.json"), "{ not json");
        ResourceTypeIndex index = session.GetIndex("Patient");

        index.Resources.ShouldHaveSingleItem();
        index.Diagnostics.ShouldBeEmpty();
        session.BuiltIndexTypes.ShouldBe(["Patient"]);
    }

    [Fact]
    public void GivenIndexAlreadyBuilt_WhenRequestedAgain_ThenTheSameInstanceIsReturned()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);
        FolderSession session = Open(folder);

        ResourceTypeIndex first = session.GetIndex("Patient");
        ResourceTypeIndex second = session.GetIndex("Patient");

        second.ShouldBeSameAs(first);
    }

    [Fact]
    public void GivenConcurrentRequests_WhenIndexRequested_ThenItIsBuiltOnce()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);
        FolderSession session = Open(folder);

        ResourceTypeIndex[] indexes = [.. Enumerable.Range(0, 8).AsParallel().WithDegreeOfParallelism(8).Select(_ => session.GetIndex("Patient"))];

        indexes.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public void GivenNoResourcesOfType_WhenIndexRequested_ThenIndexIsEmpty()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);

        ResourceTypeIndex index = Open(folder).GetIndex("Observation");

        index.ResourceType.ShouldBe("Observation");
        index.Resources.ShouldBeEmpty();
        index.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GivenTypeUnknownToVersion_WhenIndexRequested_ThenArgumentExceptionIsThrown()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);

        Should.Throw<ArgumentException>(() => Open(folder).GetIndex("NotAResource"));
    }

    [Fact]
    public void GivenFreshSession_WhenCatalogIsRead_ThenNoIndexIsBuilt()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);
        FolderSession session = Open(folder);

        session.Catalog.Entries.ShouldHaveSingleItem();

        session.BuiltIndexTypes.ShouldBeEmpty();
    }

    [Fact]
    public void GivenManyResources_WhenIndexedRepeatedly_ThenOrderAndDiagnosticsAreIdentical()
    {
        using var folder = new TestFolder().Write(
            "obs.ndjson",
            string.Join('\n', Enumerable.Range(0, 200).Select(i => $$$"""{"resourceType":"Observation","id":"o{{{i}}}","status":"final","code":{"text":"x"},"effectiveDateTime":"{{{(i % 10 == 0 ? "garbage" : "2020-01-01")}}}"}""")));

        ResourceTypeIndex first = Open(folder).GetIndex("Observation");
        ResourceTypeIndex second = Open(folder).GetIndex("Observation");

        first.Resources.Select(r => r.Key.Id).ShouldBe([.. Enumerable.Range(0, 200).Select(i => $"o{i}")]);
        second.Resources.Select(r => r.Key.Id).ShouldBe(first.Resources.Select(r => r.Key.Id));
        second.Resources.Select(r => r.Index.Count).ShouldBe(first.Resources.Select(r => r.Index.Count));
        second.Diagnostics.ShouldBe(first.Diagnostics);

        // Parallel extraction must still attribute every failure to the right resource, in catalog order.
        first.Diagnostics.Count.ShouldBe(20);
        first.Diagnostics.Select(d => d.Code).ShouldAllBe(code => code == "index-extraction-failed");
        first.Diagnostics.Select(d => d.Line).ShouldBe([.. Enumerable.Range(0, 20).Select(i => (int?)(i * 10 + 1))]);
        for (int i = 0; i < 20; i++)
        {
            first.Diagnostics[i].Message.ShouldContain($" on Observation/o{i * 10}:");
        }
    }

    [Fact]
    public void GivenFileEditedAfterCataloguing_WhenRangePointsAtAnotherResource_ThenCatalogStaleDiagnosticIsReportedAndResourceIsNotIndexed()
    {
        const string original = """{"resourceType":"Patient","id":"p1"}""";
        const string replacement = """{"resourceType":"Patient","id":"p2"}""";
        using var folder = new TestFolder().Write("p.json", original);
        FolderSession session = Open(folder);
        session.Catalog.Entries.ShouldHaveSingleItem();

        File.WriteAllText(Path.Combine(folder.Path, "p.json"), replacement);
        ResourceTypeIndex index = session.GetIndex("Patient");

        index.Resources.ShouldBeEmpty();
        CatalogDiagnostic diagnostic = index.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("catalog-stale");
        diagnostic.FilePath.ShouldBe("p.json");
        diagnostic.Message.ShouldContain("expected Patient/p1 but found Patient/p2");
    }

    [Fact]
    public void GivenFileTruncatedAfterCataloguing_WhenIndexed_ThenCatalogStaleDiagnosticIsReported()
    {
        using var folder = new TestFolder().Write("obs.ndjson", """
            {"resourceType":"Observation","id":"o1","status":"final","code":{"text":"a"}}
            {"resourceType":"Observation","id":"o2","status":"final","code":{"text":"b"}}
            """);
        FolderSession session = Open(folder);
        session.Catalog.Entries.Count.ShouldBe(2);

        File.WriteAllText(Path.Combine(folder.Path, "obs.ndjson"), """{"resourceType":"Observation","id":"o1","status":"final","code":{"text":"a"}}""");
        ResourceTypeIndex index = session.GetIndex("Observation");

        index.Resources.Select(r => r.Key.Id).ShouldBe(["o1"]);
        CatalogDiagnostic diagnostic = index.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("catalog-stale");
        diagnostic.Line.ShouldBe(2);
    }

    [Fact]
    public void GivenIndexRequested_WhenIndexerLoadIsTimed_ThenItIsReportedSeparatelyAndOnlyOnceBuilt()
    {
        using var folder = new TestFolder().Write("p.json", PatientJson);
        FolderSession session = Open(folder);
        session.IndexerLoadMilliseconds.ShouldBeNull();

        session.GetIndex("Patient");

        session.IndexerLoadMilliseconds.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void GivenValueTheIndexerRejects_WhenIndexed_ThenExtractionFailureIsADiagnosticAtTheResourceLine()
    {
        using var folder = new TestFolder().Write(
            "obs.ndjson",
            """
            {"resourceType":"Observation","id":"good","status":"final","code":{"text":"x"},"effectiveDateTime":"2020-01-01"}
            {"resourceType":"Observation","id":"bad","status":"final","code":{"text":"x"},"effectiveDateTime":"garbage"}
            """);

        ResourceTypeIndex index = Open(folder).GetIndex("Observation");

        index.Resources.Count.ShouldBe(2);
        CatalogDiagnostic diagnostic = index.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("index-extraction-failed");
        diagnostic.FilePath.ShouldBe("obs.ndjson");
        diagnostic.Line.ShouldBe(2);
        diagnostic.Message.ShouldContain("http://hl7.org/fhir/SearchParameter/clinical-date");
        diagnostic.Message.ShouldContain("Observation/bad");

        // The value the indexer rejected is folder text, so the diagnostic shows its type of failure, not the value.
        diagnostic.Message.ShouldNotContain("garbage");
    }

    [Fact]
    public void GivenExtractionFailure_WhenIndexed_ThenTheLogIsStillForwardedToTheRealLogger()
    {
        using var folder = new TestFolder().Write(
            "o.json",
            """{"resourceType":"Observation","id":"bad","status":"final","code":{"text":"x"},"effectiveDateTime":"garbage"}""");
        using var provider = new RecordingLoggerProvider();
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(provider));

        new FolderSession(folder.Path, new R4CoreSchemaProvider(), loggerFactory).GetIndex("Observation").Diagnostics.ShouldHaveSingleItem();

        provider.Messages.ShouldContain(message => message.Contains("Observation/bad", StringComparison.Ordinal));
        // Guard (Task 6 round 3): what is forwarded never carries the rejected value.
        provider.Messages.ShouldNotContain(message => message.Contains("garbage", StringComparison.Ordinal));
    }

    private static FolderSession Open(TestFolder folder) => new(folder.Path, new R4CoreSchemaProvider(), NullLoggerFactory.Instance);
}
