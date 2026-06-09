// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.PackageManagement.Infrastructure;
using Ignixa.Specification.Generated;
using Ignixa.Validation.Schema;
using Ignixa.Validation.Services;
using Shouldly;
using Xunit;

namespace Ignixa.Validation.Tests.Schema;

/// <summary>
/// Pins the latent bug where <c>StructureDefinitionSchemaBuilder.ExtractInvariantChecks</c>
/// casts every <see cref="Ignixa.Abstractions.IConstraint"/> to
/// <c>Specification.ConstraintDefinition</c> - a type that does not implement
/// <c>IConstraint</c> - causing the cast to always fail and no
/// <see cref="Ignixa.Validation.Checks.FhirPathInvariantCheck"/> instances to be created.
/// </summary>
public class InvariantExtractionFromBuilderTests
{
    [Fact]
    public void GivenAdaptedTypeWithEle1Constraint_WhenBuildingSchema_ThenContainsFhirPathInvariantCheck()
    {
        var sdJson = File.ReadAllText(Path.Combine(
            "TestData", "StructureDefinitions", "PatientWithEle1Constraint.json"));
        var adapter = new StructureDefinitionTypeAdapter();
        var adaptedRoot = adapter.Adapt(sdJson, "4.0.1");
        adaptedRoot.ShouldNotBeNull();

        var schema = new R4CoreSchemaProvider();
        var terminology = new InMemoryTerminologyService(schema.ValueSetProvider);
        var builder = new StructureDefinitionSchemaBuilder();
        var validationSchema = builder.BuildSchema(adaptedRoot!, schema, terminologyService: terminology);

        validationSchema.Checks
            .OfType<Ignixa.Validation.Checks.FhirPathInvariantCheck>()
            .ShouldContain(
                c => c.ConstraintKey == "ele-1",
                customMessage: "Builder must extract ele-1 from adapted constraints as a FhirPathInvariantCheck");
    }
}
