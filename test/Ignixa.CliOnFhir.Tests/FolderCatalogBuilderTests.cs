// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Ignixa.CliOnFhir.Catalog;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class FolderCatalogBuilderTests
{
    private static readonly IReadOnlySet<string> ValidTypes = new R4CoreSchemaProvider().ResourceTypeNames;

    [Fact]
    public void GivenResourceTypeUnknownToVersion_WhenBuilt_ThenDiagnosticIsReportedAndEntryIsNotCatalogued()
    {
        using var folder = new TestFolder()
            .Write("x.json", """{"resourceType":"NotAResource","id":"x"}""")
            .Write("obs.ndjson", "{\"resourceType\":\"Observation\",\"id\":\"o1\"}\n{\"resourceType\":\"Bogus\",\"id\":\"b\"}\n")
            .Write("p.json", """{"resourceType":"Patient","id":"p"}""");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.Select(e => e.ResourceType).ShouldBe(["Observation", "Patient"]);
        catalog.Diagnostics.Select(d => (d.Code, d.FilePath, d.Line)).ShouldBe(
            [("unknown-resource-type", "obs.ndjson", (int?)2), ("unknown-resource-type", "x.json", null)]);
    }

    [Fact]
    public void GivenSingleResourceFile_WhenBuilt_ThenEntryCoversWholeFile()
    {
        using var folder = new TestFolder().Write("Patient/p1.json", """{"resourceType":"Patient","id":"p1"}""");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        CatalogEntry entry = catalog.Entries.ShouldHaveSingleItem();
        entry.ShouldBe(new CatalogEntry("Patient", "p1", "Patient/p1.json", 0, folder.ReadBytes("Patient/p1.json").Length, null, false));
        catalog.FilesScanned.ShouldBe(1);
        catalog.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GivenNdjsonWithCrlfAndBlankLines_WhenBuilt_ThenEachRecordIsAddressableByOffsetAndLine()
    {
        using var folder = new TestFolder().Write(
            "obs.ndjson",
            "{\"resourceType\":\"Observation\",\"id\":\"o1\"}\r\n\r\n{\"resourceType\":\"Observation\",\"id\":\"o2\"}\n");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.Select(e => (e.Id, e.Line)).ShouldBe([("o1", (int?)1), ("o2", 3)]);
        byte[] bytes = folder.ReadBytes("obs.ndjson");
        foreach (CatalogEntry entry in catalog.Entries)
        {
            IdAt(bytes, entry).ShouldBe(entry.Id);
        }
    }

    [Fact]
    public void GivenBundle_WhenBuilt_ThenEntryResourcesAreCataloguedIndividually()
    {
        using var folder = new TestFolder().Write("bundle.json", """
            {
              "resourceType": "Bundle",
              "type": "transaction",
              "entry": [
                { "fullUrl": "urn:uuid:a", "resource": { "resourceType": "Patient", "id": "a", "name": [ { "family": "X" } ] }, "request": { "method": "POST", "url": "Patient" } },
                { "request": { "method": "DELETE", "url": "Patient/gone" } },
                { "resource": { "resourceType": "Encounter", "id": "e1", "subject": { "reference": "urn:uuid:a" } } }
              ]
            }
            """);

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.ContainerBundles.ShouldBe(1);
        catalog.Entries.Select(e => $"{e.ResourceType}/{e.Id}").ShouldBe(["Patient/a", "Encounter/e1"]);
        byte[] bytes = folder.ReadBytes("bundle.json");
        foreach (CatalogEntry entry in catalog.Entries)
        {
            IdAt(bytes, entry).ShouldBe(entry.Id);
        }
    }

    [Fact]
    public void GivenInvalidJson_WhenBuilt_ThenDiagnosticIsReportedAndOtherFilesStillCatalogued()
    {
        using var folder = new TestFolder()
            .Write("bad.json", """{"resourceType":"Patient", "id": """)
            .Write("good.json", """{"resourceType":"Patient","id":"ok"}""");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.ShouldHaveSingleItem().Id.ShouldBe("ok");
        CatalogDiagnostic diagnostic = catalog.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("invalid-json");
        diagnostic.FilePath.ShouldBe("bad.json");
    }

    [Fact]
    public void GivenJsonWithoutResourceType_WhenBuilt_ThenNotAResourceDiagnostic()
    {
        using var folder = new TestFolder().Write("config.json", """{"name":"not fhir"}""");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.ShouldBeEmpty();
        catalog.Diagnostics.ShouldHaveSingleItem().Code.ShouldBe("not-a-resource");
    }

    [Fact]
    public void GivenDuplicateIds_WhenBuilt_ThenFirstOccurrenceWinsAndDuplicateIsReported()
    {
        using var folder = new TestFolder().Write(
            "patients.ndjson",
            "{\"resourceType\":\"Patient\",\"id\":\"p1\",\"gender\":\"male\"}\n{\"resourceType\":\"Patient\",\"id\":\"p1\",\"gender\":\"female\"}\n");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.ShouldHaveSingleItem().Line.ShouldBe(1);
        CatalogDiagnostic diagnostic = catalog.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("duplicate-id");
        diagnostic.Line.ShouldBe(2);
        diagnostic.Message.ShouldContain("patients.ndjson:1");
    }

    [Fact]
    public void GivenResourceWithoutId_WhenBuiltTwice_ThenSameSyntheticIdIsAssigned()
    {
        using var folder = new TestFolder().Write("x.json", """{"resourceType":"Basic"}""");

        CatalogEntry first = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None).Entries.ShouldHaveSingleItem();
        CatalogEntry second = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None).Entries.ShouldHaveSingleItem();

        first.HasSyntheticId.ShouldBeTrue();
        first.Id.ShouldStartWith("synthetic-");
        second.Id.ShouldBe(first.Id);
    }

    [Fact]
    public void GivenCacheDirectoryAndUnsupportedExtensions_WhenBuilt_ThenTheyAreNotCatalogued()
    {
        using var folder = new TestFolder()
            .Write(".ignixa/index.json", """{"resourceType":"Patient","id":"cached"}""")
            .Write("notes.txt", "not json")
            .Write("p.json", """{"resourceType":"Patient","id":"p"}""");

        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, ValidTypes, CancellationToken.None);

        catalog.Entries.ShouldHaveSingleItem().Id.ShouldBe("p");
        catalog.FilesScanned.ShouldBe(1);
        catalog.FilesSkipped.ShouldBe(1);
    }

    private static string? IdAt(byte[] fileBytes, CatalogEntry entry)
    {
        string json = Encoding.UTF8.GetString(fileBytes, (int)entry.Offset, entry.Length);
        using JsonDocument document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("id").GetString();
    }
}
