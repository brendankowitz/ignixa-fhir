// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Api.E2ETests.Fixtures;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;

namespace Ignixa.Api.E2ETests.Infrastructure;

/// <summary>
/// Base class for capability-driven E2E tests.
/// Provides access to test harness and helpers for capability checking.
/// Tests skip automatically if required capabilities are not supported.
/// </summary>
/// <remarks>
/// All derived classes must use the [Collection(E2ETestCollection.Name)] attribute
/// to ensure they share a single fixture instance and avoid database race conditions.
/// </remarks>
[Collection(E2ETestCollection.Name)]
public abstract class CapabilityDrivenTestBase
{
    protected readonly SearchTestHarness Harness;
    protected readonly HttpClient Client;
    protected readonly IFhirSchemaProvider SchemaProvider;
    protected readonly FhirVersion FhirVersion;

    protected CapabilityDrivenTestBase(IgnixaApiFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        Harness = fixture.Harness;
        Client = fixture.Client;
        SchemaProvider = fixture.SchemaProvider;
        FhirVersion = fixture.FhirVersion;
    }

    /// <summary>
    /// Requires that a search parameter is supported for the given resource type.
    /// Throws SkipException if not supported (xUnit will mark test as skipped).
    /// </summary>
    protected void RequireSearchParameter(string resourceType, string parameterName)
    {
        if (!Harness.SupportsSearch(resourceType, parameterName))
        {
            throw new SkipException($"Search parameter '{parameterName}' not supported for {resourceType} (FHIR {FhirVersion})");
        }
    }

    /// <summary>
    /// Requires that multiple search parameters are supported for the given resource type.
    /// Throws SkipException if any are not supported.
    /// </summary>
    protected void RequireSearchParameters(string resourceType, params string[] parameterNames)
    {
        foreach (var parameterName in parameterNames)
        {
            RequireSearchParameter(resourceType, parameterName);
        }
    }

    /// <summary>
    /// Creates a new ScenarioBuilder for building test scenarios.
    /// </summary>
    /// <returns>A new ScenarioBuilder instance configured with the test's SchemaProvider.</returns>
    protected ScenarioBuilder CreateScenario()
    {
        return new ScenarioBuilder(SchemaProvider);
    }

    /// <summary>
    /// Creates a fluent PatientBuilder for building standalone patients (not part of a scenario).
    /// Use this when you need multiple patients in a single test.
    /// Call .Build() at the end to generate the patient resource.
    /// </summary>
    /// <returns>A PatientBuilder instance for fluent configuration.</returns>
    /// <example>
    /// var patient = CreatePatient().FromSeattle().WithAge(45).Build();
    /// </example>
    protected PatientBuilder CreatePatient()
    {
        return PatientBuilderFactory.Create(SchemaProvider);
    }
}
