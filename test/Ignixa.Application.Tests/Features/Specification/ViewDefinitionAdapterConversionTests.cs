// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.PackageManagement.Infrastructure;
using Ignixa.PackageManagement.Models;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.SqlOnFhir.packages;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;

namespace Ignixa.Application.Tests.Features.Specification;

public class ViewDefinitionAdapterConversionTests(ITestOutputHelper output)
{
    private const string Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenEmbeddedViewDefinition_WhenConverting_ThenPreservesLogicalRootAndInheritedSnapshot(FhirVersion version)
    {
        var json = LoadEmbeddedDefinition();
        var provider = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);

        var type = provider.ToTypeDefinition(json, version.GetSchemaProvider().FullVersion);

        type.ShouldNotBeNull();
        type.Info.Name.ShouldBe("ViewDefinition");
        type.Info.IsResource.ShouldBeFalse();
        type.Info.IsAbstract.ShouldBeFalse();
        type.IsCollection.ShouldBeFalse();
        var root = type.ShouldBeAssignableTo<ITypeExtended>();
        root.Min.ShouldBe(1);
        root.Max.ShouldBe("1");
        root.Types.ShouldBeEmpty();
        root.DefaultTypeName.ShouldBeNullOrEmpty();
        root.Constraints.ShouldContain(c => c.Key == "cnl-0");
        root.Constraints.ShouldContain(c => c.Key == "dom-2");
        var id = type.Children.Single(c => c.Info.Name == "id").ShouldBeAssignableTo<ITypeExtended>();
        id.DefaultTypeName.ShouldBe("id");
        var status = type.Children.Single(c => c.Info.Name == "status").ShouldBeAssignableTo<ITypeExtended>();
        status.Min.ShouldBe(1);
        status.DefaultTypeName.ShouldBe("code");
        var select = type.Children.Single(c => c.Info.Name == "select");
        select.IsRequired.ShouldBeTrue();
        select.IsCollection.ShouldBeTrue();
        select.Info.IsResource.ShouldBeFalse();
        var column = select.Children.Single(c => c.Info.Name == "column");
        column.Children.Single(c => c.Info.Name == "path").IsRequired.ShouldBeTrue();
        var recursiveSelect = select.Children.Single(c => c.Info.Name == "select").ShouldBeAssignableTo<ITypeExtended>();
        recursiveSelect.ContentReference.ShouldBe(Canonical + "#ViewDefinition.select");
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenEmbeddedViewDefinition_WhenCompositeLoadsCanonical_ThenResolvesOnlyItsFullIdentity(FhirVersion version)
    {
        var baseSchema = version.GetSchemaProvider();
        var json = LoadEmbeddedDefinition();
        using var document = JsonDocument.Parse(json);
        var businessVersion = document.RootElement.GetProperty("version").GetString();
        var resource = new PackageResource
        {
            PackageId = "local.ignixa.sqlonfhir", PackageVersion = "2.1.0",
            ResourceType = "StructureDefinition", ResourceId = "ViewDefinition",
            Canonical = Canonical, Version = businessVersion, FhirVersion = baseSchema.FullVersion,
            ResourceJson = json
        };
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync(baseSchema.FullVersion, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[resource]);
        var converter = new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance);
        var composite = new CompositeStructureDefinitionSummaryProvider(
            baseSchema, repository, converter, baseSchema.FullVersion,
            NullLogger<CompositeStructureDefinitionSummaryProvider>.Instance);

        await composite.InitializeAsync();

        var type = composite.GetTypeDefinition(Canonical);
        type.ShouldNotBeNull();
        type.Info.Name.ShouldBe("ViewDefinition");
        composite.GetTypeDefinition(Canonical + "|" + businessVersion).ShouldBeSameAs(type);
        composite.GetTypeDefinition(Canonical + "|missing").ShouldBeNull();
        composite.GetTypeDefinition("https://unrelated.example/StructureDefinition/ViewDefinition").ShouldBeNull();
        composite.GetTypeDefinition("Patient").ShouldBeSameAs(baseSchema.GetTypeDefinition("Patient"));
        output.WriteLine(
            "FHIR {0}: ViewDefinition simple-name resolved={1}; core CanonicalResource known={2}; core integer64 known={3}",
            baseSchema.FullVersion,
            composite.GetTypeDefinition("ViewDefinition") != null,
            baseSchema.IsKnownType("CanonicalResource"),
            baseSchema.IsKnownType("integer64"));
    }

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public void GivenEmbeddedViewDefinition_WhenResolvingSchema_ThenEnforcesInheritedStatusAndRequiredSelect(FhirVersion version)
    {
        var baseSchema = version.GetSchemaProvider();
        var layered = new ProfileLayeredSchemaProvider(baseSchema, [new ExtractedResource
        {
            ResourceType = "StructureDefinition", ResourceId = "ViewDefinition",
            Canonical = Canonical, FhirVersion = baseSchema.FullVersion, ResourceJson = LoadEmbeddedDefinition()
        }]);
        var type = layered.GetTypeDefinition(Canonical);
        type.ShouldNotBeNull();
        var schema = new StructureDefinitionSchemaResolver(layered).GetSchema(Canonical);
        schema.ShouldNotBeNull();
        schema.CanonicalUrl.ShouldBe(Canonical);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };
        const string valid = """
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "select":[{"column":[{"name":"id","path":"id"}]}]}
            """;

        var result = schema.Validate(JsonSourceNodeFactory.Parse(valid).ToElement(layered), settings);

        result.IsValid.ShouldBeTrue(string.Join("; ", result.Issues.Select(i => i.Message)));
        var missingStatus = valid.Replace("\"status\":\"active\",", string.Empty, StringComparison.Ordinal);
        var statusResult = schema.Validate(JsonSourceNodeFactory.Parse(missingStatus).ToElement(layered), settings);
        statusResult.IsValid.ShouldBeFalse();
        statusResult.Issues.ShouldContain(i => i.Path.Contains("status", StringComparison.Ordinal));
        const string missingSelect = """{"resourceType":"ViewDefinition","status":"active","resource":"Patient"}""";
        var selectResult = schema.Validate(JsonSourceNodeFactory.Parse(missingSelect).ToElement(layered), settings);
        selectResult.IsValid.ShouldBeFalse();
        selectResult.Issues.ShouldContain(i => i.Path.Contains("select", StringComparison.Ordinal));
        var missingPath = valid.Replace(",\"path\":\"id\"", string.Empty, StringComparison.Ordinal);
        var pathResult = schema.Validate(JsonSourceNodeFactory.Parse(missingPath).ToElement(layered), settings);
        pathResult.IsValid.ShouldBeFalse();
        pathResult.Issues.ShouldContain(i => i.Path.Contains("path", StringComparison.Ordinal));
    }

    internal static string LoadEmbeddedDefinition()
    {
        var package = new SqlOnFhirEmbeddedPackage();
        var name = package.Assembly.GetManifestResourceNames().Single(n =>
            n.StartsWith(package.ResourcePrefix, StringComparison.Ordinal)
            && n.EndsWith(".StructureDefinition-ViewDefinition.json", StringComparison.Ordinal));
        using var stream = package.Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
