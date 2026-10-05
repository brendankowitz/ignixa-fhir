using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Schema;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using PackageResourceProvider = Ignixa.PackageManagement.Infrastructure.PackageResourceProvider;
using Ignixa.Application.Features.Specification;

namespace Ignixa.Application.Tests.Features.Specification;

public class ViewDefinitionCompositionTests
{
    private const string Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";
    private const string Valid = """
        {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
         "constant":[{"name":"example","valueString":"x"}],
         "select":[{"column":[{"name":"id","path":"id"}]}]}
        """;

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenInstalledModel_WhenColdCapabilitiesEnforceRead_ThenAdmitsModelWithItsRealCanonical(FhirVersion version)
    {
        var (composite, _) = CreateProvider(version, [Model(version)]);
        var baseSchema = version.GetSchemaProvider();
        var versions = Substitute.For<IFhirVersionContext>();
        versions.GetSchemaProvider(version, Arg.Any<int?>()).Returns(composite);
        versions.GetBaseSchemaProvider(version).Returns(baseSchema);
        var repositoryFactory = Substitute.For<IFhirRepositoryFactory>();
        var segment = new ResourceInteractionCapabilitySegment(versions,
            NullLogger<ResourceInteractionCapabilitySegment>.Instance, repositoryFactory);
        var statement = new CapabilityStatementJsonNode();

        await segment.ApplyAsync(statement, new CapabilityContext(version), CancellationToken.None);

        var entry = statement.Rest.Single().Resource.Single(r => r.Type == "ViewDefinition");
        entry.Profile.Reference.ShouldBe(Canonical);
        statement.ToElement(baseSchema).IsTrue(new GetResourceQuery("ViewDefinition", "example")
            .GetCapabilityRequirementExpression()).ShouldBeTrue();
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(new TenantConfiguration
        {
            TenantId = 1, DisplayName = "ViewDefinition tests", FhirVersion = baseSchema.FullVersion
        });
        var service = new CapabilityStatementService([segment], Substitute.For<ICapabilityCache>(),
            tenants, versions, Substitute.For<IApplicationVersionInfo>(),
            NullLogger<CapabilityStatementService>.Instance);
        var context = Substitute.For<IFhirRequestContext>();
        context.FhirVersion.Returns(version);
        context.TenantId.Returns(1);
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        var behavior = new CapabilityEnforcementBehavior<GetResourceQuery, SearchEntryResult?>(
            service, tenants, accessor, versions,
            NullLogger<CapabilityEnforcementBehavior<GetResourceQuery, SearchEntryResult?>>.Instance);
        bool reachedHandler = false;
        await behavior.HandleAsync(new GetResourceQuery("ViewDefinition", "example"), () =>
        {
            reachedHandler = true;
            return Task.FromResult<SearchEntryResult?>(null);
        }, CancellationToken.None);
        reachedHandler.ShouldBeTrue();
        await Should.ThrowAsync<ForbiddenException>(() => behavior.HandleAsync(
            new GetResourceQuery("UninstalledModel", "example"),
            () => Task.FromResult<SearchEntryResult?>(null), CancellationToken.None));
    }

    [Theory]
    [InlineData(FhirVersion.R4, ValidationDepth.Spec)]
    [InlineData(FhirVersion.R4B, ValidationDepth.Spec)]
    [InlineData(FhirVersion.R5, ValidationDepth.Spec)]
    [InlineData(FhirVersion.R4, ValidationDepth.Full)]
    [InlineData(FhirVersion.R4B, ValidationDepth.Full)]
    [InlineData(FhirVersion.R5, ValidationDepth.Full)]
    public void GivenInstalledModel_WhenValidatingInlineAndRecursiveTrees_ThenChecksEveryOccurrence(
        FhirVersion version, ValidationDepth depth)
    {
        var (provider, _) = CreateProvider(version, [Model(version)]);
        var schema = new StructureDefinitionSchemaResolver(provider).GetSchema(Canonical);
        schema.ShouldNotBeNull();
        var settings = new ValidationSettings { Depth = depth };
        foreach (string branch in new[] { "column", "select", "unionAll" })
        {
            var model = JsonNode.Parse(Valid)!.AsObject();
            if (branch != "column")
            {
                model["select"] = JsonNode.Parse($$"""[{"{{branch}}":[{"select":[{"column":[{"name":"id","path":"id"}]}]}]}]""");
            }
            var valid = schema.Validate(JsonSourceNodeFactory.Parse(model.ToJsonString()).ToElement(provider), settings);
            valid.IsValid.ShouldBeTrue(string.Join("; ", valid.Issues.Select(i => $"{i.Path}: {i.Message}")));
            var invalidJson = model.ToJsonString().Replace(",\"path\":\"id\"", string.Empty, StringComparison.Ordinal);
            var invalid = schema.Validate(JsonSourceNodeFactory.Parse(invalidJson).ToElement(provider), settings);
            invalid.IsValid.ShouldBeFalse(branch);
            invalid.Issues.ShouldContain(i => i.Path.Contains("path", StringComparison.Ordinal) && i.Severity == IssueSeverity.Error);
        }
        var missingValue = Valid.Replace(",\"valueString\":\"x\"", string.Empty, StringComparison.Ordinal);
        schema.Validate(JsonSourceNodeFactory.Parse(missingValue).ToElement(provider), settings).IsValid.ShouldBeFalse();
    }

    [Fact]
    public async Task GivenPackageReload_WhenResolvingAliasesAndCapabilities_ThenNoStaleNegativeOrPositiveEntrySurvives()
    {
        var (provider, repository) = CreateProvider(FhirVersion.R4, []);
        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[Model(FhirVersion.R4)]);
        provider.ClearCache();
        provider.GetTypeDefinition("ViewDefinition").ShouldNotBeNull();
        provider.ResourceTypeNames.ShouldContain("ViewDefinition");
        await provider.InitializeAsync();
        provider.GetTypeDefinition(Canonical).ShouldBeSameAs(provider.GetTypeDefinition("ViewDefinition"));
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[]);
        provider.ClearCache();
        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
    }

    [Theory]
    [InlineData("constraint", false)]
    [InlineData("specialization", true)]
    public void GivenNonAdmissibleLogicalDefinition_WhenLoading_ThenDoesNotGrantSimpleTypeAlias(string derivation, bool isAbstract)
    {
        var model = Model(FhirVersion.R4);
        var node = JsonNode.Parse(model.ResourceJson)!;
        node["derivation"] = derivation;
        node["abstract"] = isAbstract;
        model.ResourceJson = node.ToJsonString();
        var (provider, _) = CreateProvider(FhirVersion.R4, [model]);

        provider.GetTypeDefinition(Canonical).ShouldNotBeNull();
        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
    }

    [Fact]
    public void GivenModelWithCoreId_WhenLoading_ThenUsesSnapshotNameWithoutReplacingCoreType()
    {
        var node = JsonNode.Parse(ViewDefinitionAdapterConversionTests.LoadEmbeddedDefinition())!;
        node["id"] = "Patient";
        var model = Model(FhirVersion.R4);
        model.ResourceId = "Patient";
        model.ResourceJson = node.ToJsonString();
        var (provider, _) = CreateProvider(FhirVersion.R4, [model]);
        provider.GetTypeDefinition("Patient")!.Info.Name.ShouldBe("Patient");
        provider.GetTypeDefinition(Canonical)!.Info.Name.ShouldBe("ViewDefinition");
        provider.GetTypeDefinition("ViewDefinition").ShouldBeSameAs(provider.GetTypeDefinition(Canonical));
        provider.GetTypeDefinition("https://unrelated.example/StructureDefinition/ViewDefinition").ShouldBeNull();
        provider.GetTypeDefinition("http://hl7.org/fhir/StructureDefinition/ViewDefinition").ShouldBeNull();
    }

    [Fact]
    public void GivenRealPatientProfileNamedViewDefinition_WhenLoading_ThenDoesNotAdvertiseItAsAResource()
    {
        const string profileCanonical = "https://example.org/StructureDefinition/Patient";
        var profile = Model(FhirVersion.R4);
        profile.Canonical = profileCanonical;
        profile.ResourceJson = $$$"""
            {"resourceType":"StructureDefinition","url":"{{{profileCanonical}}}","id":"ViewDefinition",
             "type":"Patient","kind":"resource","abstract":false,"derivation":"constraint",
             "snapshot":{"element":[{"path":"Patient"},{"path":"Patient.active","min":1,"max":"1","type":[{"code":"boolean"}]}]}}
            """;
        var (provider, _) = CreateProvider(FhirVersion.R4, [profile]);

        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
        var core = provider.GetTypeDefinition("Patient");
        var installed = provider.GetTypeDefinition(profileCanonical);
        installed.ShouldNotBeNull();
        installed.ShouldNotBeSameAs(core);
        installed.Info.Name.ShouldBe("Patient");
        installed.Children.Single().IsRequired.ShouldBeTrue();
    }

    [Fact]
    public async Task GivenOldInitializationInFlight_WhenCleared_ThenItCannotPublishStaleAliases()
    {
        var (provider, repository) = CreateProvider(FhirVersion.R4, []);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var oldLoad = new TaskCompletionSource<IReadOnlyList<PackageResource>>(TaskCreationOptions.RunContinuationsAsynchronously);
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            started.TrySetResult();
            return oldLoad.Task;
        });
        var initial = provider.InitializeAsync();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[]);

        provider.ClearCache();
        await provider.InitializeAsync();
        oldLoad.SetResult([Model(FhirVersion.R4)]);
        await initial;

        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
    }

    [Theory]
    [InlineData("#ViewDefinition.select")]
    [InlineData(Canonical + "#ViewDefinition.select")]
    public void GivenVersionedSnapshotSelfReference_WhenResolving_ThenUsesRequestedSnapshotNotDefaultVersion(string reference)
    {
        var first = Model(FhirVersion.R4);
        first.Version = "1";
        var second = Model(FhirVersion.R4);
        second.Version = "2";
        var definition = JsonNode.Parse(second.ResourceJson)!;
        var elements = definition["snapshot"]!["element"]!.AsArray();
        elements.Single(e => e!["path"]!.GetValue<string>() == "ViewDefinition.select.column.description")!["min"] = 1;
        elements.Single(e => e!["path"]!.GetValue<string>() == "ViewDefinition.select.select")!["contentReference"] = reference;
        second.ResourceJson = definition.ToJsonString();
        var (provider, _) = CreateProvider(FhirVersion.R4, [first, second]);
        var resolver = new StructureDefinitionSchemaResolver(provider);
        const string json = """
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "select":[{"select":[{"column":[{"name":"id","path":"id"}]}]}]}
            """;
        var element = JsonSourceNodeFactory.Parse(json).ToElement(provider);
        var settings = new ValidationSettings { Depth = ValidationDepth.Spec };

        resolver.GetSchema(Canonical + "|1")!.Validate(element, settings).IsValid.ShouldBeTrue();
        var result = resolver.GetSchema(Canonical + "|2")!.Validate(element, settings);

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.Contains("description", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenForeignUnresolvableContentReference_WhenBuilding_ThenDoesNotSubstituteSameTailLocalModel()
    {
        var resource = Model(FhirVersion.R4);
        resource.ResourceJson = resource.ResourceJson.Replace(Canonical + "#ViewDefinition.select",
            "https://uninstalled.example/StructureDefinition/ViewDefinition#ViewDefinition.select", StringComparison.Ordinal);
        var (provider, _) = CreateProvider(FhirVersion.R4, [resource]);

        Should.Throw<InvalidOperationException>(() => new StructureDefinitionSchemaResolver(provider).GetSchema(Canonical))
            .Message.ShouldContain("https://uninstalled.example/");
    }

    [Fact]
    public void GivenForeignVersionedReference_WhenItRecursesLocally_ThenKeepsTheForeignSnapshotScope()
    {
        const string otherCanonical = "https://other.example/StructureDefinition/ViewDefinition";
        var first = Model(FhirVersion.R4);
        var firstDefinition = JsonNode.Parse(first.ResourceJson)!;
        firstDefinition["snapshot"]!["element"]!.AsArray()
            .Single(e => e!["path"]!.GetValue<string>() == "ViewDefinition.select.select")!["contentReference"] =
                otherCanonical + "|2#ViewDefinition.select";
        first.ResourceJson = firstDefinition.ToJsonString();
        var second = Model(FhirVersion.R4);
        second.Canonical = otherCanonical;
        second.Version = "1";
        second.ResourceJson = second.ResourceJson.Replace(Canonical, otherCanonical, StringComparison.Ordinal);
        var third = Model(FhirVersion.R4);
        third.Canonical = otherCanonical;
        third.Version = "2";
        var thirdDefinition = JsonNode.Parse(second.ResourceJson)!;
        var elements = thirdDefinition["snapshot"]!["element"]!.AsArray();
        elements.Single(e => e!["path"]!.GetValue<string>() == "ViewDefinition.select.select")!["contentReference"] = "#ViewDefinition.select";
        elements.Single(e => e!["path"]!.GetValue<string>() == "ViewDefinition.select.column.description")!["min"] = 1;
        third.ResourceJson = thirdDefinition.ToJsonString();
        var (provider, _) = CreateProvider(FhirVersion.R4, [first, second, third]);
        var schema = new StructureDefinitionSchemaResolver(provider).GetSchema(Canonical)!;
        const string json = """
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "select":[{"select":[{"select":[{"column":[{"name":"id","path":"id"}]}]}]}]}
            """;

        var result = schema.Validate(JsonSourceNodeFactory.Parse(json).ToElement(provider),
            new ValidationSettings { Depth = ValidationDepth.Spec });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Path.Contains("description", StringComparison.Ordinal));
    }

    [Fact]
    public void GivenRecursiveSelectWithConflictingExpressions_WhenValidatingFull_ThenRunsInvariantAtNestedScope()
    {
        var (provider, _) = CreateProvider(FhirVersion.R4, [Model(FhirVersion.R4)]);
        var schema = new StructureDefinitionSchemaResolver(provider).GetSchema(Canonical)!;
        const string json = """
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "select":[{"select":[{"forEach":"name","forEachOrNull":"name","column":[{"name":"id","path":"id"}]}]}]}
            """;
        var element = JsonSourceNodeFactory.Parse(json).ToElement(provider);

        schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec }).IsValid.ShouldBeTrue();
        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Full });

        result.IsValid.ShouldBeFalse();
        result.Issues.ShouldContain(i => i.Code == "sql-expressions" && i.Path.Contains("select", StringComparison.Ordinal));
        result.Issues.Count(i => i.Code == "sql-expressions").ShouldBe(1);
    }

    [Fact]
    public void GivenTwoLogicalCanonicalsWithSameRoot_WhenLoading_ThenRejectsAmbiguousAliasButRetainsCanonicals()
    {
        var first = Model(FhirVersion.R4);
        const string otherCanonical = "https://other.example/StructureDefinition/ViewDefinition";
        var second = Model(FhirVersion.R4);
        second.Canonical = otherCanonical;
        second.ResourceJson = first.ResourceJson.Replace(Canonical, otherCanonical, StringComparison.Ordinal);
        var (provider, _) = CreateProvider(FhirVersion.R4, [first, second]);

        provider.GetTypeDefinition(Canonical).ShouldNotBeNull();
        provider.GetTypeDefinition(otherCanonical).ShouldNotBeNull();
        provider.GetTypeDefinition("ViewDefinition").ShouldBeNull();
        provider.ResourceTypeNames.ShouldNotContain("ViewDefinition");
    }

    private static (CompositeStructureDefinitionSummaryProvider Provider, IPackageResourceRepository Repository) CreateProvider(
        FhirVersion version, IReadOnlyList<PackageResource> resources)
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(resources);
        repository.GetStructureDefinitionsByCanonicalAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(call => (IReadOnlyList<PackageResource>)resources.Where(r => r.Canonical == call.ArgAt<string>(0)).ToArray());
        repository.GetCustomResourceTypesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new HashSet<string>(resources.Count > 0 ? ["ViewDefinition"] : []));
        return (new CompositeStructureDefinitionSummaryProvider(version.GetSchemaProvider(), repository,
            new PackageResourceProvider(NullLogger<PackageResourceProvider>.Instance), version.GetSchemaProvider().FullVersion,
            NullLogger<CompositeStructureDefinitionSummaryProvider>.Instance), repository);
    }

    private static PackageResource Model(FhirVersion version) => new()
    {
        PackageId = "local.ignixa.sqlonfhir", PackageVersion = "2.1.0", ResourceType = "StructureDefinition",
        ResourceId = "ViewDefinition", Canonical = Canonical, Version = "2.1.0", FhirVersion = version.GetSchemaProvider().FullVersion,
        ResourceJson = ViewDefinitionAdapterConversionTests.LoadEmbeddedDefinition()
    };
}
