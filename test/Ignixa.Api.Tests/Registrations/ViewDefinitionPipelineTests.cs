using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Features.Specification;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Conformance.Events;
using Ignixa.Conformance.Events.Abstractions;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Search.Definition;
using Ignixa.SqlOnFhir.packages;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Medino;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;
using Xunit.Abstractions;

namespace Ignixa.Api.Tests.Registrations;

public class ViewDefinitionPipelineTests(ITestOutputHelper output)
{
    private const string Canonical = "https://sql-on-fhir.org/ig/StructureDefinition/ViewDefinition";

    [Theory]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenShippedModel_WhenRegisteredWritePipelineExecutes_ThenCapabilitiesAndRecursiveValidationRemainEnforced(
        FhirVersion version)
    {
        var repository = Substitute.For<IPackageResourceRepository>();
        using var state = new ConformanceState();
        var events = Substitute.For<ISourceEventStore>();
        events.ReadAllAsync(Arg.Any<CancellationToken>()).Returns(NoEvents());
        await state.InitializeFromEventsAsync(events, CancellationToken.None);
        using var versions = new FhirVersionContext(NullLoggerFactory.Instance,
            new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance,
            repository, new Ignixa.PackageManagement.Infrastructure.PackageResourceProvider(
                NullLogger<Ignixa.PackageManagement.Infrastructure.PackageResourceProvider>.Instance),
            conformanceState: state);
        var baseSchema = versions.GetBaseSchemaProvider(version);
        var embedded = new SqlOnFhirEmbeddedPackage();
        var name = embedded.Assembly.GetManifestResourceNames().Single(n =>
            n.StartsWith(embedded.ResourcePrefix, StringComparison.Ordinal)
            && n.EndsWith(".StructureDefinition-ViewDefinition.json", StringComparison.Ordinal));
        using var stream = embedded.Assembly.GetManifestResourceStream(name)!;
        using var reader = new StreamReader(stream);
        var resource = new PackageResource
        {
            PackageId = embedded.PackageId, PackageVersion = "2.1.0", Canonical = Canonical,
            ResourceType = "StructureDefinition", ResourceId = "ViewDefinition",
            FhirVersion = baseSchema.FullVersion, ResourceJson = reader.ReadToEnd()
        };
        repository.GetAllStructureDefinitionsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<PackageResource>)[resource]);
        var schema = versions.GetSchemaProvider(version, 1);
        int writes = 0;
        var storage = Substitute.For<IFhirRepository>();
        storage.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            call.Arg<ResourceWrapper>().ResourceType.ShouldBe("ViewDefinition");
            call.Arg<ResourceWrapper>().SearchIndices.ShouldNotBeEmpty();
            writes++;
            return new UpdateResult(new ResourceKey("ViewDefinition", "view-1", "1"),
                Encoding.UTF8.GetBytes("""{"resourceType":"ViewDefinition","id":"view-1","meta":{"versionId":"1"}}"""),
                DateTimeOffset.UtcNow);
        });
        var stores = Substitute.For<IFhirRepositoryFactory>();
        stores.GetRepositoryAsync(1, Arg.Any<CancellationToken>()).Returns(storage);
        var tenant = new TenantConfiguration { TenantId = 1, DisplayName = "Models", FhirVersion = baseSchema.FullVersion };
        var tenants = Substitute.For<ITenantConfigurationStore>();
        tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>()).Returns(tenant);
        var context = Substitute.For<IFhirRequestContext>();
        context.TenantId.Returns(1);
        context.FhirVersion.Returns(version);
        context.TenantConfiguration.Returns(tenant);
        var accessor = Substitute.For<IFhirRequestContextAccessor>();
        accessor.RequestContext.Returns(context);
        var partitions = Substitute.For<IPartitionStrategy>();
        partitions.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
            .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [1] });
        var segment = new ResourceInteractionCapabilitySegment(versions,
            NullLogger<ResourceInteractionCapabilitySegment>.Instance, stores);
        var capabilities = new CapabilityStatementService([segment], Substitute.For<ICapabilityCache>(),
            tenants, versions,
            Substitute.For<IApplicationVersionInfo>(), NullLogger<CapabilityStatementService>.Instance);
        var builder = new ContainerBuilder();
        builder.RegisterApplicationServices(new ConfigurationBuilder().Build());
        builder.RegisterValidationServices();
        builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
        builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
        builder.RegisterInstance(new Ignixa.FhirPath.Parser.FhirPathParser());
        builder.RegisterInstance(versions).As<IFhirVersionContext>().ExternallyOwned();
        builder.RegisterInstance(stores).As<IFhirRepositoryFactory>();
        builder.RegisterInstance(tenants).As<ITenantConfigurationStore>();
        builder.RegisterInstance(accessor).As<IFhirRequestContextAccessor>();
        builder.RegisterInstance(partitions).As<IPartitionStrategy>();
        builder.RegisterInstance(capabilities);
        builder.RegisterInstance(new Ignixa.Validation.Services.InMemoryTerminologyService(baseSchema.ValueSetProvider))
            .As<ITerminologyService>();
        using var container = builder.Build();
        var mediator = container.Resolve<IMediator>();
        const string valid = """
            {"resourceType":"ViewDefinition","status":"active","resource":"Patient",
             "meta":{"tag":[{"system":"https://example.org","code":"model"}]},
             "select":[{"unionAll":[{"select":[{"column":[{"name":"id","path":"id"}]}]}]}]}
            """;
        string invalid = valid.Replace(",\"path\":\"id\"", string.Empty, StringComparison.Ordinal);
        var resolved = ((IElementSchemaResolver)container
            .Resolve<Func<FhirVersion, int, IValidationSchemaResolver>>()(version, 1))
            .ResolveForElement(JsonSourceNodeFactory.Parse(valid).ToElement(schema));
        resolved.ShouldNotBeNull();
        resolved.CanonicalUrl.ShouldBe(Canonical);

        var failure = await Record.ExceptionAsync(() => SendAsync(mediator, invalid));
        failure.ShouldNotBeNull();
        var exception = failure.ShouldBeOfType<ValidationException>(failure.ToString());

        exception.ValidationResult.Issues.ShouldContain(i => i.Path.Contains("path", StringComparison.Ordinal));
        writes.ShouldBe(0);
        var unsafeNarrative = JsonNode.Parse(valid)!;
        unsafeNarrative["text"] = new JsonObject
        {
            ["status"] = "generated",
            ["div"] = "<div xmlns='http://www.w3.org/1999/xhtml'><script>bad()</script></div>"
        };
        await Should.ThrowAsync<ValidationException>(() => SendAsync(mediator, unsafeNarrative.ToJsonString()));
        writes.ShouldBe(0);
        var searchParameters = versions.GetSearchParameterDefinitionManager(version, 1);
        string tagExpression = searchParameters.GetSearchParameter("ViewDefinition", "_tag").Expression;
        var typed = JsonSourceNodeFactory.Parse(valid).ToElement(schema);
        var tagNodes = typed.Select(tagExpression).ToList();
        tagNodes.Count.ShouldBe(1, $"Inherited _tag expression: {tagExpression}");
        string originalTagExpression = versions.GetSearchParameterDefinitionManager(version).GetSearchParameter("Resource", "_tag").Expression;
        output.WriteLine("FHIR {0}: original _tag '{1}' matches {2}; effective '{3}' matches {4}",
            baseSchema.FullVersion, originalTagExpression, typed.Select(originalTagExpression).Count(), tagExpression, tagNodes.Count);
        await SendAsync(mediator, valid);
        writes.ShouldBe(1);
        searchParameters.GetSearchParameter("ViewDefinition", "_id").ShouldNotBeNull();
        searchParameters.TryGetSearchParameters("UnregisteredModel", out _).ShouldBeFalse();
    }

    private static Task<UpdateResult> SendAsync(IMediator mediator, string json)
    {
        var resource = JsonSourceNodeFactory.Parse(json);
        return mediator.SendAsync(new CreateOrUpdateResourceCommand(
            resource.ResourceType, "view-1", resource, HttpMethod.Put, ValidationDepthOverride: ValidationDepth.Spec));
    }

    private static async IAsyncEnumerable<SourceEvent> NoEvents()
    {
        await Task.CompletedTask;
        yield break;
    }
}
