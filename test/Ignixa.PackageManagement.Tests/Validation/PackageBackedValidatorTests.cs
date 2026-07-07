// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.PackageManagement.Models;
using Ignixa.PackageManagement.Validation;
using Ignixa.Validation;
using NSubstitute;
using Shouldly;
using Xunit;

namespace Ignixa.PackageManagement.Tests.Validation;

/// <summary>
/// Covers the product surface <see cref="PackageBackedValidator"/>: CodeSystem content resolution,
/// the base-type shadow filter, and the value-set layering flag. Uses a substitute base provider so
/// no FHIR-version specification assembly is required.
/// </summary>
public sealed class PackageBackedValidatorTests
{
    private const string ColorSystem = "http://example.org/colors";
    private const string ColorValueSet = "http://example.org/vs/colors";

    private const string ColorCodeSystem = """
    {
      "resourceType":"CodeSystem","id":"colors","url":"http://example.org/colors","content":"complete",
      "concept":[{"code":"red","display":"Red"}]
    }
    """;

    private const string ColorValueSetJson = """
    {
      "resourceType":"ValueSet","id":"colors","url":"http://example.org/vs/colors",
      "compose":{"include":[{"system":"http://example.org/colors","concept":[{"code":"red","display":"Red"}]}]}
    }
    """;

    private const string PatientShadowSd = """
    {
      "resourceType":"StructureDefinition","id":"Patient","type":"Patient","kind":"resource",
      "url":"http://example.org/StructureDefinition/Patient",
      "snapshot":{"element":[{"path":"Patient","min":0,"max":"*"}]}
    }
    """;

    [Fact]
    public async Task GivenCodeSystemResource_WhenCreated_ThenCodeSystemAndLookupResolveDisplay()
    {
        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = BaseProvider(),
            PackageResources = [CodeSystemResource()],
        });

        setup.CodeSystemProvider.GetDisplay(ColorSystem, "red").ShouldBe("Red");

        var lookup = await setup.TerminologyService.LookupCodeAsync(ColorSystem, "red", version: null, CancellationToken.None);
        lookup.Found.ShouldBeTrue();
        lookup.Display.ShouldBe("Red");
    }

    [Fact]
    public async Task GivenPackageValueSetLayered_WhenValidatingCode_ThenPackageValueSetIsConsulted()
    {
        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = BaseProvider(),
            PackageResources = [ValueSetResource(), CodeSystemResource()],
            LayerPackageValueSets = true,
        });

        var result = await setup.TerminologyService
            .ValidateCodeAsync(ColorSystem, "red", display: null, ColorValueSet, CancellationToken.None);

        result.IsValid.ShouldBeTrue();
        result.Severity.ShouldBe(IssueSeverity.Information);
    }

    [Fact]
    public async Task GivenPackageValueSetNotLayered_WhenValidatingCode_ThenBindingDegradesToWarning()
    {
        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = BaseProvider(),
            PackageResources = [ValueSetResource(), CodeSystemResource()],
            LayerPackageValueSets = false,
        });

        var result = await setup.TerminologyService
            .ValidateCodeAsync(ColorSystem, "red", display: null, ColorValueSet, CancellationToken.None);

        // Base provider does not know the value set and it is not layered, so membership is
        // unverifiable — a non-failing warning, never an over-strict rejection.
        result.Severity.ShouldBe(IssueSeverity.Warning);
    }

    [Fact]
    public void GivenBaseTypeShadowProfile_WhenExcludeBaseTypesTrue_ThenBaseTypeIsNotShadowed()
    {
        var sentinel = Substitute.For<IType>();
        var baseProvider = BaseProvider();
        baseProvider.IsKnownType("Patient").Returns(true);
        baseProvider.GetTypeDefinition("Patient").Returns(sentinel);

        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = baseProvider,
            PackageResources = [PatientShadowResource()],
            ExcludeBaseTypeStructureDefinitions = true,
        });

        // The package's Patient StructureDefinition was filtered out, so the base definition wins.
        setup.SchemaProvider.GetTypeDefinition("Patient").ShouldBeSameAs(sentinel);
    }

    [Fact]
    public void GivenBaseTypeShadowProfile_WhenExcludeBaseTypesFalse_ThenPackageProfileShadowsBase()
    {
        var sentinel = Substitute.For<IType>();
        var baseProvider = BaseProvider();
        baseProvider.IsKnownType("Patient").Returns(true);
        baseProvider.GetTypeDefinition("Patient").Returns(sentinel);

        var setup = PackageBackedValidator.Create(new PackageValidationOptions
        {
            BaseSchemaProvider = baseProvider,
            PackageResources = [PatientShadowResource()],
            ExcludeBaseTypeStructureDefinitions = false,
        });

        // Default behaviour: the package profile is layered and takes precedence over the base.
        setup.SchemaProvider.GetTypeDefinition("Patient").ShouldNotBeSameAs(sentinel);
    }

    private static IFhirSchemaProvider BaseProvider()
    {
        var baseProvider = Substitute.For<IFhirSchemaProvider>();
        baseProvider.FullVersion.Returns("4.0.1");
        baseProvider.IsKnownType(Arg.Any<string>()).Returns(false);

        var baseVs = Substitute.For<IValueSetProvider>();
        baseVs.GetCodes(Arg.Any<string>()).Returns((IReadOnlyList<FhirCode>?)null);
        baseVs.IsKnownValueSet(Arg.Any<string>()).Returns(false);
        baseVs.IsValidCode(Arg.Any<string>(), Arg.Any<string>()).Returns((bool?)null);
        baseProvider.ValueSetProvider.Returns(baseVs);

        return baseProvider;
    }

    private static ExtractedResource CodeSystemResource() => new()
    {
        ResourceType = "CodeSystem",
        Canonical = ColorSystem,
        ResourceId = "colors",
        ResourceJson = ColorCodeSystem,
        FhirVersion = "4.0.1",
    };

    private static ExtractedResource ValueSetResource() => new()
    {
        ResourceType = "ValueSet",
        Canonical = ColorValueSet,
        ResourceId = "colors",
        ResourceJson = ColorValueSetJson,
        FhirVersion = "4.0.1",
    };

    private static ExtractedResource PatientShadowResource() => new()
    {
        ResourceType = "StructureDefinition",
        Canonical = "http://example.org/StructureDefinition/Patient",
        ResourceId = "Patient",
        ResourceJson = PatientShadowSd,
        FhirVersion = "4.0.1",
    };
}
