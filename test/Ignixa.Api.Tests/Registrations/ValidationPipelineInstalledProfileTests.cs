using System.Text.Json.Nodes;
using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.Middleware;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Serialization;
using Ignixa.Specification;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using WriteHarness = Ignixa.Api.Tests.Registrations.ValidationPipelineRegistrationTests.WriteHarness;

namespace Ignixa.Api.Tests.Registrations;

public class ValidationPipelineInstalledProfileTests
{
    private const string Profile = "https://example.org/StructureDefinition/review-patient";

    [Theory]
    [InlineData(1, "active")]
    [InlineData(2, "gender")]
    public async Task GivenTenantInstalledProfile_WhenBaseValidPatientViolatesProfile_ThenRejectsWithoutWriting(int tenantId, string requiredField)
    {
        using var harness = await CreateHarnessAsync(tenantId);
        harness.Container.Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>()(FhirVersion.R4, tenantId)
            .GetSchema(Profile)!.CanonicalUrl.ShouldBe(Profile);
        var exception = await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(Patient(Profile)));
        exception.ValidationResult.Issues.ShouldContain(issue =>
            issue.Path.Contains(requiredField, StringComparison.Ordinal) && issue.Severity == IssueSeverity.Error);
        harness.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(1, true, null)]
    [InlineData(2, null, "female")]
    public async Task GivenTenantSpecificValidPatient_WhenSending_ThenOtherTenantProfileDoesNotLeak(
        int tenantId, bool? active, string? gender)
    {
        using var harness = await CreateHarnessAsync(tenantId);
        await harness.SendAsync(Patient(Profile, active, gender));
        harness.Writes.ShouldBe(1);
    }

    [Theory]
    [InlineData(ValidationDepth.Minimal, true)]
    [InlineData(ValidationDepth.Spec, true)]
    [InlineData(ValidationDepth.Full, false)]
    public async Task GivenInstalledInvariant_WhenSelectingDepth_ThenPreservesFullOnlyPolicy(ValidationDepth depth, bool accepted)
    {
        using var harness = await CreateHarnessAsync(1, invariantOnly: true);
        if (accepted)
        {
            await harness.SendAsync(Patient(Profile, active: false), depth);
        }
        else
        {
            var exception = await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(Patient(Profile, active: false), depth));
            exception.ValidationResult.Issues.ShouldContain(issue => issue.Code == "review-active");
        }
        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData("|1", true, null)]
    [InlineData("|2", null, "female")]
    public async Task GivenExplicitInstalledVersion_WhenSending_ThenEnforcesThatVersion(
        string suffix, bool? active, string? gender)
    {
        using var harness = await CreateHarnessAsync(1, twoVersions: true);
        await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(Patient(Profile + suffix)));
        harness.Writes.ShouldBe(0);
        await harness.SendAsync(Patient(Profile + suffix, active, gender));
        harness.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task GivenUnavailableExplicitVersion_WhenResolving_ThenWarnsInsteadOfUsingAnotherVersion()
    {
        using var harness = await CreateHarnessAsync(1, twoVersions: true);
        var resolver = harness.Container.Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>()(FhirVersion.R4, 1);
        var element = JsonSourceNodeFactory.Parse(Patient(Profile + "|missing")).ToElement(
            harness.Container.Resolve<IFhirVersionContext>().GetSchemaProvider(FhirVersion.R4, 1));
        var schema = ((IElementSchemaResolver)resolver).ResolveForElement(element)!;
        var result = schema.Validate(element, new ValidationSettings { Depth = ValidationDepth.Spec });
        result.IsValid.ShouldBeTrue();
        result.Issues.ShouldContain(issue => issue.Code == "unresolvable-profile" && issue.Message.Contains("|missing", StringComparison.Ordinal));
        await harness.SendAsync(Patient(Profile + "|missing"));
        harness.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task GivenUnrelatedCanonicalEndingPatient_WhenResolving_ThenDoesNotSubstituteCorePatient()
    {
        using var harness = await CreateHarnessAsync(1);
        var resolver = harness.Container.Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>()(FhirVersion.R4, 1);
        resolver.GetSchema("https://unrelated.example/StructureDefinition/Patient").ShouldBeNull();
    }

    [Fact]
    public async Task GivenColdCompositeProvider_WhenSendingProfiledResource_ThenResolvesInstalledCanonicalWithoutPreloading()
    {
        using var harness = await CreateHarnessAsync(2, initialize: false);
        var exception = await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(Patient(Profile + "|1")));
        exception.ValidationResult.Issues.ShouldContain(issue => issue.Path.Contains("gender", StringComparison.Ordinal));
        harness.Writes.ShouldBe(0);
        harness.Container.Resolve<IFhirVersionContext>().GetSchemaProvider(FhirVersion.R4, 2)
            .IsKnownType(Profile + "|1").ShouldBeTrue();
        await harness.SendAsync(Patient(Profile + "|1", gender: "female"));
        harness.Writes.ShouldBe(1);
    }

    [Fact]
    public async Task GivenTwoCanonicalsWithSameTail_WhenResolving_ThenKeepsBothIdentities()
    {
        using var harness = await CreateHarnessAsync(1, differentCanonicals: true);
        await harness.SendAsync(Patient(Profile, active: true));
        var other = Profile.Replace("example.org", "other.example", StringComparison.Ordinal);
        await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(Patient(other, active: true)));
        await harness.SendAsync(Patient(other, gender: "female"));
        harness.Writes.ShouldBe(2);
    }

    [Fact]
    public async Task GivenAdmittedResourceWithoutBaseSchema_WhenSending_ThenReturnsFhirFailureWithoutWriting()
    {
        using var harness = new WriteHarness(resolver: Substitute.For<IValidationSchemaResolver>());
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            async _ => await harness.SendAsync("""{"resourceType":"Patient"}"""),
            NullLogger<FhirExceptionMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        context.Response.StatusCode.ShouldBe(500);
        context.Response.Body.Position = 0;
        var outcome = JsonNode.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        outcome["issue"]![0]!["code"]!.GetValue<string>().ShouldBe("exception");
        outcome["issue"]![0]!["diagnostics"]!.GetValue<string>().ShouldContain("Base validation schema");
        harness.Writes.ShouldBe(0);
    }

    private static string Patient(string profile, bool? active = null, string? gender = null)
    {
        var patient = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["meta"] = new JsonObject { ["profile"] = new JsonArray(profile) }
        };
        if (active.HasValue)
        {
            patient["active"] = active.Value;
        }
        if (gender != null)
        {
            patient["gender"] = gender;
        }
        return patient.ToJsonString();
    }

    private static async Task<WriteHarness> CreateHarnessAsync(
        int tenantId, bool invariantOnly = false, bool twoVersions = false, bool differentCanonicals = false, bool initialize = true)
    {
        var schemas = new Dictionary<int, IFhirSchemaProvider>();
        var harness = new WriteHarness(tenantId: tenantId, tenantSchemas: schemas);
        var baseSchema = harness.Container.Resolve<IFhirVersionContext>().GetBaseSchemaProvider(FhirVersion.R4);
        foreach (int id in new[] { 1, 2 })
        {
            var packages = new List<PackageResource>
            {
                ProfileResource(baseSchema, Profile, "1", id == 1 ? "active" : "gender", invariantOnly)
            };
            if (twoVersions)
            {
                packages.Insert(0, ProfileResource(baseSchema, Profile, "2", "gender", invariantOnly: false));
            }
            if (differentCanonicals)
            {
                packages.Add(ProfileResource(baseSchema, Profile.Replace("example.org", "other.example", StringComparison.Ordinal),
                    "1", "gender", invariantOnly: false));
            }
            var repository = Substitute.For<IPackageResourceRepository>();
            repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns((IReadOnlyList<PackageResource>)packages);
            repository.GetStructureDefinitionsByCanonicalAsync(Arg.Any<string>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(call => (IReadOnlyList<PackageResource>)packages.Where(p => p.Canonical == call.ArgAt<string>(0)).ToArray());
            repository.GetCustomResourceTypesAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                .Returns(new HashSet<string>());
            var composite = new CompositeStructureDefinitionSummaryProvider(
                baseSchema, repository, harness.Container.Resolve<IPackageResourceProvider>(),
                "4.0.1", NullLogger<CompositeStructureDefinitionSummaryProvider>.Instance);
            if (initialize)
            {
                await composite.InitializeAsync();
            }
            schemas[id] = composite;
        }
        return harness;
    }

    private static PackageResource ProfileResource(
        IFhirSchemaProvider schema, string canonical, string version, string requiredField, bool invariantOnly)
    {
        var root = new JsonObject { ["id"] = "Patient", ["path"] = "Patient", ["min"] = 0, ["max"] = "*" };
        if (invariantOnly)
        {
            root["constraint"] = JsonNode.Parse("""
                [{"key":"review-active","severity":"error","human":"Patient must be active","expression":"active = true"}]
                """);
        }
        var elements = new JsonArray(root);
        foreach (var child in schema.GetTypeDefinition("Patient")!.Children)
        {
            var extended = (ITypeExtended)child;
            var types = new JsonArray();
            foreach (var type in extended.Types)
            {
                types.Add(new JsonObject { ["code"] = type.Code });
            }
            elements.Add(new JsonObject
            {
                ["id"] = "Patient." + child.Info.Name,
                ["path"] = "Patient." + child.Info.Name,
                ["min"] = !invariantOnly && child.Info.Name == requiredField ? 1 : extended.Min,
                ["max"] = extended.Max,
                ["type"] = types
            });
        }
        var definition = new JsonObject
        {
            ["resourceType"] = "StructureDefinition", ["id"] = "review-patient", ["url"] = canonical,
            ["version"] = version, ["name"] = "ReviewPatient", ["status"] = "active", ["kind"] = "resource",
            ["abstract"] = false, ["type"] = "Patient", ["derivation"] = "constraint", ["fhirVersion"] = "4.0.1",
            ["baseDefinition"] = "http://hl7.org/fhir/StructureDefinition/Patient",
            ["snapshot"] = new JsonObject { ["element"] = elements }
        };
        return new PackageResource
        {
            PackageId = "review.patient", PackageVersion = version, ResourceType = "StructureDefinition",
            ResourceId = "review-patient", Canonical = canonical, Version = version, FhirVersion = "4.0.1",
            ResourceJson = definition.ToJsonString()
        };
    }
}
