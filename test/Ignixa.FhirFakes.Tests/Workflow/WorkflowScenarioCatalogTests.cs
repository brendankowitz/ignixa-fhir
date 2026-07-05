// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Reflection;
using Ignixa.Abstractions;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Tests.Scenarios;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Specification.Generated;
using Shouldly;

namespace Ignixa.FhirFakes.Tests.Workflow;

[Collection(CatalogRegistrationGroup.Name)]
public class WorkflowScenarioCatalogTests
{
    [Fact]
    public void GivenCatalog_WhenGettingAll_ThenIncludesDailyAppointmentSchedule()
    {
        var ids = WorkflowScenarioCatalog.GetAll().Select(s => s.Id).ToList();

        ids.ShouldContain("DailyAppointmentSchedule");
    }

    [Fact]
    public void GivenUnknownId_WhenFinding_ThenReturnsNull()
    {
        WorkflowScenarioCatalog.Find("NotAWorkflow").ShouldBeNull();
    }

    [Fact]
    public void GivenValidPack_WhenInvoking_ThenPassesOptionsThrough()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(WorkflowScenarioCatalogTests).GetMethod(
            nameof(EchoSeedPack), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "EchoSeedPack",
            Title = "EchoSeedPack",
            Parameters = [],
            Method = method,
        };
        var options = new WorkflowScenarioOptions { Seed = 7 };

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, options);

        result.Manifest.Seed.ShouldBe(7);
    }

    [Fact]
    public void GivenPackThatThrows_WhenInvoking_ThenWrapsInScenarioInvocationException()
    {
        var schemaProvider = new R4CoreSchemaProvider();
        var method = typeof(WorkflowScenarioCatalogTests).GetMethod(
            nameof(ThrowingPack), BindingFlags.NonPublic | BindingFlags.Static)!;
        var scenario = new DiscoveredScenario
        {
            Id = "ThrowingPack",
            Title = "ThrowingPack",
            Parameters = [],
            Method = method,
        };

        var exception = Should.Throw<ScenarioInvocationException>(
            () => WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions()));

        exception.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    [Fact]
    public void GivenExternalAssemblyRegistered_WhenGettingAll_ThenItsPacksAreDiscovered()
    {
        WorkflowScenarioCatalog.RegisterAssembly(typeof(WorkflowScenarioCatalogTests).Assembly);

        var found = WorkflowScenarioCatalog.Find("RegisteredTestPack");

        found.ShouldNotBeNull();
        found.Method.Name.ShouldBe(nameof(Ignixa.FhirFakes.Tests.Workflow.Predefined.RegisteredTestPackScenario.GetRegisteredTestPack));
    }

    [Fact]
    public void GivenExternalAssemblyRegistered_WhenInvokedViaCatalog_ThenItRunsLikeAnyOtherPack()
    {
        WorkflowScenarioCatalog.RegisterAssembly(typeof(WorkflowScenarioCatalogTests).Assembly);
        var schemaProvider = new R4CoreSchemaProvider();
        var scenario = WorkflowScenarioCatalog.Find("RegisteredTestPack")!;

        var result = WorkflowScenarioCatalog.Invoke(scenario, schemaProvider, new WorkflowScenarioOptions { Seed = 3 });

        result.Manifest.Seed.ShouldBe(3);
    }

    [Fact]
    public void GivenAssemblyRegisteredTwice_WhenGettingAll_ThenItsPacksAppearOnlyOnce()
    {
        WorkflowScenarioCatalog.RegisterAssembly(typeof(WorkflowScenarioCatalogTests).Assembly);
        WorkflowScenarioCatalog.RegisterAssembly(typeof(WorkflowScenarioCatalogTests).Assembly);

        var matches = WorkflowScenarioCatalog.GetAll().Count(s => s.Id == "RegisteredTestPack");

        matches.ShouldBe(1);
    }

    [Fact]
    public void GivenNullAssembly_WhenRegistering_ThenThrowsArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => WorkflowScenarioCatalog.RegisterAssembly(null!));
    }

    private static WorkflowScenarioResult EchoSeedPack(IFhirSchemaProvider schemaProvider, WorkflowScenarioOptions options) =>
        new()
        {
            Graph = new ResourceGraph(),
            Manifest = new WorkflowManifest
            {
                ScenarioId = "EchoSeedPack",
                Seed = options.Seed,
                PrimaryResourceType = "Basic",
                ResourceCountsByType = new Dictionary<string, int>(),
            },
        };

    private static WorkflowScenarioResult ThrowingPack(IFhirSchemaProvider schemaProvider, WorkflowScenarioOptions options) =>
        throw new InvalidOperationException("boom");
}
