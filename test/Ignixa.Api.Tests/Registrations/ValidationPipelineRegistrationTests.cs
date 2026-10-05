using System.Text;
using System.Text.Json.Nodes;
using Autofac;
using Ignixa.Abstractions;
using Ignixa.Api.Middleware;
using Ignixa.Api.Registrations;
using Ignixa.Application.Features.Metadata;
using Ignixa.Application.Features.Metadata.Models;
using Ignixa.Application.Features.Metadata.Segments;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Application.Infrastructure.Behaviors;
using Ignixa.Application.Infrastructure.Caching;
using Ignixa.Domain;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Definition;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;
using Ignixa.Specification;
using Ignixa.Validation;
using Ignixa.Validation.Abstractions;
using Ignixa.Validation.Services;
using Medino;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Shouldly;

namespace Ignixa.Api.Tests.Registrations;

public class ValidationPipelineRegistrationTests
{
    [Fact]
    public void GivenApplicationRegistration_WhenResolvingWritePipeline_ThenValidationUsesTheRequestResultContract()
    {
        using var harness = new WriteHarness();
        harness.Container.Resolve<IEnumerable<IPipelineBehavior<CreateOrUpdateResourceCommand, UpdateResult>>>()
            .ShouldContain(behavior => behavior is ValidationBehavior);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenValidResource_WhenSendingThroughRegisteredMediator_ThenPreservesCommittedResult(FhirVersion version)
    {
        using var harness = new WriteHarness(version);
        var result = await harness.SendAsync("""{"resourceType":"Patient","id":"patient-1","active":true}""");

        result.ShouldBeSameAs(harness.CommittedResult);
        result.Key.VersionId.ShouldBe("7");
        result.ResourceBytes.ToArray().ShouldBe(harness.CommittedResult.ResourceBytes.ToArray());
        harness.Writes.ShouldBe(1);
        harness.LastWrite!.SearchIndices.ShouldNotBeEmpty();
    }

    [Theory]
    [InlineData(FhirVersion.Stu3, "Spec")]
    [InlineData(FhirVersion.R4, "Spec")]
    [InlineData(FhirVersion.R4B, "Spec")]
    [InlineData(FhirVersion.R5, "Spec")]
    [InlineData(FhirVersion.R4, "Minimal")]
    public async Task GivenMissingObservationFields_WhenSendingThroughRegisteredMediator_ThenRejectsBeforeWriting(
        FhirVersion version, string depth)
    {
        using var harness = new WriteHarness(version, depth);
        var exception = await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync("""{"resourceType":"Observation"}"""));

        exception.OperationOutcome.Issue.ShouldNotBeEmpty();
        harness.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData("bad/id")]
    [InlineData("bad_id")]
    [InlineData("bad\n")]
    [InlineData("")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789abc")]
    public async Task GivenInvalidCommandId_WhenBodyIdIsAbsent_ThenRejectsBeforeWriting(string id)
    {
        using var harness = new WriteHarness(depth: "Minimal");
        await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync("""{"resourceType":"Patient"}""", id: id));
        harness.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData("patient-1")]
    [InlineData("A.b-09")]
    [InlineData("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ab")]
    public async Task GivenValidCommandId_WhenSending_ThenPreservesLogicalId(string id)
    {
        using var harness = new WriteHarness();
        await harness.SendAsync("""{"resourceType":"Patient"}""", id: id);
        harness.LastWrite!.ResourceId.ShouldBe(id);
    }

    [Fact]
    public async Task GivenMalformedBodyId_WhenSendingAtSpecDepth_ThenRejectsBeforeWriting()
    {
        using var harness = new WriteHarness();
        await Should.ThrowAsync<ValidationException>(() =>
            harness.SendAsync("""{"resourceType":"Patient","id":"bad/id"}"""));
        harness.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task GivenValidationFailure_WhenUsingExceptionMiddleware_ThenReturnsFhirOutcomeWithoutWrites()
    {
        using var harness = new WriteHarness();
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new FhirExceptionMiddleware(
            async _ => await harness.SendAsync("""{"resourceType":"Observation"}"""),
            NullLogger<FhirExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.StatusCode.ShouldBe(400);
        context.Response.ContentType.ShouldBe("application/fhir+json");
        context.Response.Body.Position = 0;
        var outcome = JsonNode.Parse(await new StreamReader(context.Response.Body).ReadToEndAsync())!;
        outcome["resourceType"]!.GetValue<string>().ShouldBe("OperationOutcome");
        outcome["issue"]!.AsArray().ShouldNotBeEmpty();
        foreach (var issue in outcome["issue"]!.AsArray())
        {
            issue!["severity"]!.GetValue<string>().ShouldBe("error");
            issue["code"]!.GetValue<string>().ShouldBe("required");
        }
        harness.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("Minimal", null, true)]
    [InlineData("Spec", null, false)]
    [InlineData("Full", null, false)]
    [InlineData("Spec", ValidationDepth.Minimal, true)]
    [InlineData("Minimal", ValidationDepth.Spec, false)]
    public async Task GivenExplicitDepth_WhenSending_ThenPreservesTenantAndOverridePrecedence(
        string? tenantDepth, ValidationDepth? depthOverride, bool accepted)
    {
        var check = Substitute.For<IValidationCheck>();
        check.Validate(Arg.Any<IElement>(), Arg.Any<ValidationSettings>(), Arg.Any<ValidationState>())
            .Returns(ValidationResult.Failure(ValidationIssue.InvariantFailure("test-spec", "Spec-only rule", "Patient")));
        var schema = new ValidationSchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient", [], [check], []);
        var resolver = Substitute.For<IValidationSchemaResolver>();
        resolver.GetSchema(Arg.Any<string>()).Returns(schema);
        using var harness = new WriteHarness(depth: tenantDepth, resolver: resolver);

        if (accepted)
        {
            (await harness.SendAsync("""{"resourceType":"Patient"}""", depthOverride)).ShouldBeSameAs(harness.CommittedResult);
        }
        else
        {
            await Should.ThrowAsync<ValidationException>(() =>
                harness.SendAsync("""{"resourceType":"Patient"}""", depthOverride));
        }
        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(ValidationDepth.Minimal, true)]
    [InlineData(ValidationDepth.Spec, true)]
    [InlineData(ValidationDepth.Full, false)]
    [InlineData(ValidationDepth.Compatibility, true)]
    public async Task GivenFullOnlyInvariant_WhenSelectingDepth_ThenDoesNotEnlargeOtherTiers(
        ValidationDepth depth, bool accepted)
    {
        var check = Substitute.For<IValidationCheck>();
        check.Validate(Arg.Any<IElement>(), Arg.Any<ValidationSettings>(), Arg.Any<ValidationState>())
            .Returns(ValidationResult.Failure(ValidationIssue.InvariantFailure("test-full", "Full-only rule", "Patient")));
        var schema = new ValidationSchema("http://hl7.org/fhir/StructureDefinition/Patient", "Patient", [], [], [check]);
        var resolver = Substitute.For<IValidationSchemaResolver>();
        resolver.GetSchema(Arg.Any<string>()).Returns(schema);
        using var harness = new WriteHarness(resolver: resolver);
        if (accepted)
        {
            await harness.SendAsync("""{"resourceType":"Patient"}""", depth);
        }
        else
        {
            await Should.ThrowAsync<ValidationException>(() => harness.SendAsync("""{"resourceType":"Patient"}""", depth));
        }
        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }

    [Fact]
    public async Task GivenCancelledRequest_WhenSending_ThenCancellationRemainsObservableAndNothingIsWritten()
    {
        using var harness = new WriteHarness();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() =>
            harness.SendAsync("""{"resourceType":"Patient"}""", cancellationToken: cancellation.Token));
        harness.Writes.ShouldBe(0);
    }

    [Fact]
    public async Task GivenUnexpectedResolverFailure_WhenSending_ThenOriginalErrorPropagatesWithoutWrites()
    {
        var resolver = Substitute.For<IValidationSchemaResolver>();
        var failure = new InvalidOperationException("schema unavailable");
        resolver.GetSchema(Arg.Any<string>()).Returns(_ => throw failure);
        using var harness = new WriteHarness(resolver: resolver);

        (await Should.ThrowAsync<InvalidOperationException>(() =>
            harness.SendAsync("""{"resourceType":"Patient"}"""))).ShouldBeSameAs(failure);
        harness.Writes.ShouldBe(0);
    }

    public static IEnumerable<object[]> Narratives()
    {
        string[] invalid =
        [
            "<div xmlns='http://www.w3.org/1999/xhtml'><script>alert(1)</script></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p onclick='alert(1)'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><a href='jav&#x09;ascript:alert(1)'>link</a></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><img src='data:text/html;base64,PHNjcmlwdD4=' /></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><svg xmlns='http://www.w3.org/2000/svg'/></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p>unclosed</div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><img%20src='example'/></div>",
            "<div>missing namespace</div>",
            "<p xmlns='http://www.w3.org/1999/xhtml'>wrong root</p>",
            "<?xml version='1.0'?><div xmlns='http://www.w3.org/1999/xhtml'>text</div>",
            "<!DOCTYPE div [<!ENTITY payload 'text'>]><div xmlns='http://www.w3.org/1999/xhtml'>&payload;</div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p xmlns='urn:other'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style='width:expression(alert(1))'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style='width:expre/**/ssion(alert(1))'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style='width:e\\78pression(alert(1))'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><a xmlns:xlink='http://www.w3.org/1999/xlink' xlink:href='javascript:alert(1)'>link</a></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'>   </div>",
            ""
        ];
        string[] valid =
        [
            "<div xmlns='http://www.w3.org/1999/xhtml'>Discuss script and javascript: as ordinary text.</div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'>&lt;script&gt; &amp; &#160; literal text</div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><![CDATA[<script>literal text</script>]]></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p title='javascript: is text' style='color: red; font-weight: bold'>text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><a href='Patient/p1'>relative</a><a href='https://example.org'>absolute</a><img src='#pic1' alt='image'/></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><img src='data:image/png;base64,aGVsbG8=' alt='image'/></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><pre xml:space='preserve'>A  B\n  C</pre></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style=\"background-image:url('https://example.org/images/javascript:logo.png')\">text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style=\"font-family:'@import'; color:red\">text</p></div>",
            "<div xmlns='http://www.w3.org/1999/xhtml'><p style=\"font-family:'javascript:logo'; color:red\">text</p></div>"
        ];
        foreach (var version in new[] { FhirVersion.Stu3, FhirVersion.R4, FhirVersion.R4B, FhirVersion.R5 })
        {
            foreach (var narrative in invalid)
            {
                yield return [version, narrative, false];
            }
            foreach (var narrative in valid)
            {
                yield return [version, narrative, true];
            }
        }
    }

    [Theory]
    [MemberData(nameof(Narratives))]
    public async Task GivenNarrative_WhenSendingThroughRegisteredMediator_ThenRejectsMarkupNotHarmlessText(
        FhirVersion version, string narrative, bool accepted)
    {
        using var harness = new WriteHarness(version);
        var resource = new JsonObject
        {
            ["resourceType"] = "Patient",
            ["text"] = new JsonObject { ["status"] = "generated", ["div"] = narrative }
        };

        if (accepted)
        {
            (await harness.SendAsync(resource.ToJsonString())).ShouldBeSameAs(harness.CommittedResult);
            harness.LastWrite!.Resource.ToElement(harness.Container.Resolve<IFhirVersionContext>().GetBaseSchemaProvider(version))
                .Children("text").Single().Children("div").Single().Value.ShouldBe(narrative);
        }
        else
        {
            var exception = await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(resource.ToJsonString()));
            exception.ValidationResult.Issues.ShouldContain(issue => issue.Path.Contains("text", StringComparison.Ordinal));
        }
        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }

    [Theory]
    [InlineData(FhirVersion.Stu3)]
    [InlineData(FhirVersion.R4)]
    [InlineData(FhirVersion.R4B)]
    [InlineData(FhirVersion.R5)]
    public async Task GivenInvalidChoiceDateTime_WhenSendingAtSpecDepth_ThenRejectsBeforeWriting(FhirVersion version)
    {
        using var harness = new WriteHarness(version);
        await Should.ThrowAsync<ValidationException>(() => harness.SendAsync("""
            {"resourceType":"Observation","status":"final","code":{"text":"test"},"effectiveDateTime":"2021-10-13+02:00"}
            """));
        harness.Writes.ShouldBe(0);
    }

    [Theory]
    [InlineData("2021")]
    [InlineData("2021-10")]
    [InlineData("2021-10-13")]
    [InlineData("2021-10-13T12:13:14+02:00")]
    public async Task GivenValidChoiceDateTime_WhenSending_ThenAcceptsSupportedPrecision(string dateTime)
    {
        using var harness = new WriteHarness();
        await harness.SendAsync($$"""
            {"resourceType":"Observation","status":"final","code":{"text":"test"},"effectiveDateTime":"{{dateTime}}"}
            """);
        harness.Writes.ShouldBe(1);
    }

    [Theory]
    [InlineData(ValidationDepth.Spec, true)]
    [InlineData(ValidationDepth.Full, false)]
    public async Task GivenNonChoiceDateTimeLeniency_WhenSelectingDepth_ThenPreservesExistingPolicy(
        ValidationDepth depth, bool accepted)
    {
        using var harness = new WriteHarness();
        const string resource = """
            {"resourceType":"Patient","identifier":[{"system":"https://example.org","value":"1","period":{"start":"2021-10-13+02:00"}}]}
            """;
        if (accepted)
        {
            await harness.SendAsync(resource, depth);
        }
        else
        {
            await Should.ThrowAsync<ValidationException>(() => harness.SendAsync(resource, depth));
        }
        harness.Writes.ShouldBe(accepted ? 1 : 0);
    }

    internal sealed class WriteHarness : IDisposable
    {
        private readonly FhirVersionContext _baseVersionContext;
        public IContainer Container { get; }
        public int Writes { get; private set; }
        public ResourceWrapper? LastWrite { get; private set; }
        public UpdateResult CommittedResult { get; } = new(
            new ResourceKey("Patient", "patient-1", "7"),
            Encoding.UTF8.GetBytes("""{"resourceType":"Patient","id":"patient-1","meta":{"versionId":"7"}}"""),
            DateTimeOffset.Parse("2026-09-17T00:00:00Z", System.Globalization.CultureInfo.InvariantCulture));

        public WriteHarness(
            FhirVersion version = FhirVersion.R4,
            string? depth = null,
            IValidationSchemaResolver? resolver = null,
            int tenantId = 1,
            IReadOnlyDictionary<int, IFhirSchemaProvider>? tenantSchemas = null)
        {
            _baseVersionContext = new FhirVersionContext(
                NullLoggerFactory.Instance, new SearchParameterResolutionOptions(), NullFhirBaseUriProvider.Instance);
            IFhirVersionContext versionContext = _baseVersionContext;
            if (tenantSchemas != null)
            {
                versionContext = Substitute.For<IFhirVersionContext>();
                versionContext.GetBaseSchemaProvider(Arg.Any<FhirVersion>())
                    .Returns(call => _baseVersionContext.GetBaseSchemaProvider(call.Arg<FhirVersion>()));
                versionContext.GetSchemaProvider(Arg.Any<FhirVersion>(), Arg.Any<int?>())
                    .Returns(call => tenantSchemas[call.Arg<int?>()!.Value]);
                versionContext.GetSearchIndexer(Arg.Any<FhirVersion>(), Arg.Any<int?>())
                    .Returns(call => _baseVersionContext.GetSearchIndexer(call.Arg<FhirVersion>()));
            }
            var tenant = new TenantConfiguration { TenantId = tenantId, DisplayName = "Validation", FhirVersion = version.ToVersionString() };
            if (depth != null)
            {
                tenant = tenant with { ValidationDepth = depth };
            }
            var context = Substitute.For<IFhirRequestContext>();
            context.TenantId.Returns(tenantId);
            context.TenantConfiguration.Returns(tenant);
            context.FhirVersion.Returns(version);
            var accessor = Substitute.For<IFhirRequestContextAccessor>();
            accessor.RequestContext.Returns(context);
            var store = Substitute.For<ITenantConfigurationStore>();
            store.GetTenantConfigurationAsync(tenantId, Arg.Any<CancellationToken>()).Returns(tenant);
            var repository = Substitute.For<IFhirRepository>();
            repository.CreateOrUpdateAsync(Arg.Any<ResourceWrapper>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    Writes++;
                    LastWrite = call.Arg<ResourceWrapper>();
                    return CommittedResult;
                });
            var repositories = Substitute.For<IFhirRepositoryFactory>();
            repositories.GetRepositoryAsync(tenantId, Arg.Any<CancellationToken>()).Returns(repository);
            var partitions = Substitute.For<IPartitionStrategy>();
            partitions.DetermineWritePartition(Arg.Any<PartitionResolutionContext>(), Arg.Any<ResourceJsonNode>())
                .Returns(new RequestPartition { Mode = PartitionMode.Isolated, PartitionIds = [tenantId] });

            // Keep capability enforcement real; only its advertised input is fixed so this test is
            // independent of storage capability discovery and unrelated operation registrations.
            var segment = Substitute.For<ICapabilitySegment>();
            segment.GetVersionHashAsync(Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>()).Returns("validation-test");
            segment.ApplyAsync(Arg.Any<CapabilityStatementJsonNode>(), Arg.Any<CapabilityContext>(), Arg.Any<CancellationToken>())
                .Returns(call =>
                {
                    call.Arg<CapabilityStatementJsonNode>().Rest.Add(new RestComponentJsonNode(JsonNode.Parse("""
                        {"mode":"server","resource":[
                          {"type":"Patient","interaction":[{"code":"update"}]},
                          {"type":"Observation","interaction":[{"code":"update"}]}]}
                        """)!.AsObject()));
                    return ValueTask.CompletedTask;
                });
            var capabilities = new CapabilityStatementService(
                [segment], Substitute.For<ICapabilityCache>(), store, versionContext,
                Substitute.For<IApplicationVersionInfo>(), NullLogger<CapabilityStatementService>.Instance);

            var builder = new ContainerBuilder();
            builder.RegisterApplicationServices(new ConfigurationBuilder().Build());
            builder.RegisterValidationServices();
            builder.RegisterGeneric(typeof(NullLogger<>)).As(typeof(ILogger<>));
            builder.RegisterInstance(NullLoggerFactory.Instance).As<ILoggerFactory>();
            builder.RegisterInstance(new Ignixa.FhirPath.Parser.FhirPathParser());
            builder.RegisterInstance(versionContext).As<IFhirVersionContext>().ExternallyOwned();
            builder.RegisterInstance(accessor).As<IFhirRequestContextAccessor>();
            builder.RegisterInstance(store).As<ITenantConfigurationStore>();
            builder.RegisterInstance(partitions).As<IPartitionStrategy>();
            builder.RegisterInstance(repositories).As<IFhirRepositoryFactory>();
            builder.RegisterInstance(capabilities);
            builder.RegisterInstance(new InMemoryTerminologyService(versionContext.GetBaseSchemaProvider(version).ValueSetProvider))
                .As<ITerminologyService>();
            if (resolver != null)
            {
                builder.RegisterInstance<Func<FhirVersion, IValidationSchemaResolver>>(_ => resolver);
                builder.RegisterInstance<Func<FhirVersion, int, IValidationSchemaResolver>>((_, _) => resolver);
            }
            Container = builder.Build();
        }

        public Task<UpdateResult> SendAsync(
            string json, ValidationDepth? depthOverride = null, string id = "patient-1", CancellationToken cancellationToken = default)
        {
            var resource = JsonSourceNodeFactory.Parse(json);
            return Container.Resolve<IMediator>().SendAsync(
                new CreateOrUpdateResourceCommand(resource.ResourceType, id, resource, HttpMethod.Put, ValidationDepthOverride: depthOverride),
                cancellationToken);
        }

        public void Dispose()
        {
            Container.Dispose();
            _baseVersionContext.Dispose();
        }
    }
}
