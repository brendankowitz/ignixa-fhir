// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Catalog;
using Ignixa.CliOnFhir.Indexing;
using Ignixa.Search.Indexing;
using Ignixa.Specification.Generated;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace Ignixa.CliOnFhir.Tests;

public class FolderIndexBuilderTests
{
    [Fact]
    public void GivenCataloguedPatient_WhenIndexed_ThenSearchParametersAreExtracted()
    {
        using var folder = new TestFolder().Write(
            "p.json",
            """{"resourceType":"Patient","id":"p1","name":[{"family":"Smith","given":["Ann"]}],"birthDate":"1980-01-02"}""");

        FolderIndex index = BuildIndex(folder);

        IndexedResource patient = index.ByResourceType["Patient"].ShouldHaveSingleItem();
        patient.Key.Id.ShouldBe("p1");
        IReadOnlyCollection<SearchIndexEntry> entries = patient.Index;
        entries.Select(e => e.SearchParameter.Code).ShouldContain("family");
        entries.Select(e => e.SearchParameter.Code).ShouldContain("birthdate");
        index.Diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public void GivenResourceTypeUnknownToVersion_WhenIndexed_ThenDiagnosticIsReportedInsteadOfDropping()
    {
        using var folder = new TestFolder()
            .Write("x.json", """{"resourceType":"NotAResource","id":"x"}""")
            .Write("p.json", """{"resourceType":"Patient","id":"p"}""");

        FolderIndex index = BuildIndex(folder);

        index.ResourceCount.ShouldBe(1);
        CatalogDiagnostic diagnostic = index.Diagnostics.ShouldHaveSingleItem();
        diagnostic.Code.ShouldBe("unknown-resource-type");
        diagnostic.FilePath.ShouldBe("x.json");
    }

    [Fact]
    public void GivenManyResources_WhenIndexedRepeatedly_ThenOrderIsCatalogOrder()
    {
        using var folder = new TestFolder().Write(
            "obs.ndjson",
            string.Join('\n', Enumerable.Range(0, 200).Select(i => $$$"""{"resourceType":"Observation","id":"o{{{i}}}","status":"final","code":{"text":"x"}}""")));

        string[] first = [.. BuildIndex(folder).ByResourceType["Observation"].Select(r => r.Key.Id)];
        string[] second = [.. BuildIndex(folder).ByResourceType["Observation"].Select(r => r.Key.Id)];

        first.ShouldBe([.. Enumerable.Range(0, 200).Select(i => $"o{i}")]);
        second.ShouldBe(first);
    }

    private static FolderIndex BuildIndex(TestFolder folder)
    {
        FolderCatalog catalog = FolderCatalogBuilder.Build(folder.Path, CancellationToken.None);
        return new FolderIndexBuilder(new R4CoreSchemaProvider(), NullLoggerFactory.Instance).Build(catalog, CancellationToken.None);
    }
}
