// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

public class SearchsetBundleComposerTests
{
    [Fact]
    public void GivenEmptyGraph_WhenComposing_ThenReturnsExactlyOneEmptyPage()
    {
        var graph = new ResourceGraph();
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Appointment", MatchResourceType = "Appointment" });

        pages.Count.ShouldBe(1);
        pages[0].Type.ShouldBe(Ignixa.Serialization.Models.BundleJsonNode.BundleType.Searchset);
        pages[0].Entry.Count.ShouldBe(0);
    }

    [Fact]
    public void GivenMoreMatchesThanPageSize_WhenComposing_ThenSplitsIntoMultiplePagesWithLinks()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        for (var i = 0; i < 5; i++)
        {
            graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().Build());
        }
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Patient", MatchResourceType = "Patient", PageSize = 2 });

        pages.Count.ShouldBe(3);
        pages[0].Total.ShouldBe(5);
        pages[0].Link.Any(l => l.Relation == "next").ShouldBeTrue();
        pages[0].Link.Any(l => l.Relation == "previous").ShouldBeFalse();
        pages[2].Link.Any(l => l.Relation == "next").ShouldBeFalse();
        pages[2].Link.Any(l => l.Relation == "previous").ShouldBeTrue();
    }

    [Fact]
    public void GivenIncludeCompletenessMissing_WhenComposing_ThenNonMatchResourcesAreOmitted()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().AddState(EncounterState.Ambulatory()).Build());
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions
        {
            SearchUrl = "/Encounter",
            MatchResourceType = "Encounter",
            IncludeCompleteness = IncludeCompleteness.Missing,
        });

        pages[0].Entry.Count.ShouldBe(1);
        pages[0].Entry[0].Resource!.ResourceType.ShouldBe("Encounter");
    }

    [Fact]
    public void GivenMultiplePages_WhenComposing_ThenPageLinksUseValidQueryStringSeparator()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        for (var i = 0; i < 5; i++)
        {
            graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().Build());
        }
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Patient", MatchResourceType = "Patient", PageSize = 2 });

        var nextUrl = pages[0].Link.Single(l => l.Relation == "next").Url;
        nextUrl.ShouldContain("?_page=");
        nextUrl.ShouldNotContain("&_page=");
        nextUrl.ShouldStartWith("http://localhost/fhir/Patient");
    }

    [Fact]
    public void GivenSearchUrlWithExistingQueryString_WhenComposing_ThenPageLinksAppendWithAmpersand()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        for (var i = 0; i < 5; i++)
        {
            graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().Build());
        }
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Patient?_count=2", MatchResourceType = "Patient", PageSize = 2 });

        var nextUrl = pages[0].Link.Single(l => l.Relation == "next").Url;
        nextUrl.ShouldBe("http://localhost/fhir/Patient?_count=2&_page=1");
    }

    [Fact]
    public void GivenComposedPage_WhenReadingEntryFullUrl_ThenFullUrlIsAbsolute()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var graph = new ResourceGraph();
        graph.AddScenario(new ScenarioBuilder(schemaProvider).WithPatient().Build());
        var composer = new SearchsetBundleComposer();

        var pages = composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Patient", MatchResourceType = "Patient" });

        pages[0].Entry[0].FullUrl.ShouldStartWith("http://localhost/fhir/Patient/");
    }

    [Fact]
    public void GivenPageSizeZero_WhenComposing_ThenThrowsArgumentException()
    {
        var graph = new ResourceGraph();
        var composer = new SearchsetBundleComposer();

        var ex = Should.Throw<ArgumentException>(() =>
            composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Appointment", MatchResourceType = "Appointment", PageSize = 0 }));

        ex.Message.ShouldContain("PageSize");
        ex.Message.ShouldContain("0");
    }

    [Fact]
    public void GivenPageSizeNegative_WhenComposing_ThenThrowsArgumentException()
    {
        var graph = new ResourceGraph();
        var composer = new SearchsetBundleComposer();

        var ex = Should.Throw<ArgumentException>(() =>
            composer.Compose(graph, new SearchResponseOptions { SearchUrl = "/Appointment", MatchResourceType = "Appointment", PageSize = -5 }));

        ex.Message.ShouldContain("PageSize");
        ex.Message.ShouldContain("-5");
    }
}
