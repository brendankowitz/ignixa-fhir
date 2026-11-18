// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;
using Ignixa.Application.Features.History;
using Ignixa.Application.Features.Patch;
using Ignixa.Application.Features.Resource;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Medino;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools;

/// <summary>
/// Issue #536: MCP tools take the resource type from an untrusted caller. A value the tenant does not serve
/// must be rejected before any query or command is built, and a type the tenant does serve (including a
/// package-defined one the base schema lacks) must still get through, as it does over HTTP. Rejection
/// tests are regressions: at the base commit the tools only checked for non-empty.
/// </summary>
public class McpResourceTypeValidationTests
{
    private const string InjectedType = "Patient' or true or type = '";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IFhirRequestContextAccessor _accessor = Substitute.For<IFhirRequestContextAccessor>();
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private readonly IFhirVersionContext _versionContext = Substitute.For<IFhirVersionContext>();
    private readonly ISearchOptionsBuilderFactory _builderFactory = Substitute.For<ISearchOptionsBuilderFactory>();

    public McpResourceTypeValidationTests()
    {
        _accessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        _tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "MCP test", FhirVersion = "4.0" });

        // The tenant provider is the source of truth (as in the HTTP filter): it knows "Foo", a
        // package-defined type, and matches names case-insensitively like the real schema providers.
        var tenantProvider = Substitute.For<IFhirSchemaProvider>();
        tenantProvider.ResourceTypeNames.Returns(new HashSet<string>(["Patient", "Foo"], StringComparer.OrdinalIgnoreCase));
        _versionContext.GetSchemaProvider(FhirVersion.R4, 1).Returns(tenantProvider);

        var builder = Substitute.For<ISearchOptionsBuilder>();
        builder.Build(Arg.Any<string?>(), Arg.Any<IReadOnlyList<QueryParameter>>(), Arg.Any<ISchema?>())
            .Returns(call => new SearchOptions { ResourceType = call.ArgAt<string?>(0) });
        _builderFactory.Create(Arg.Any<FhirVersion>(), Arg.Any<int?>()).Returns(builder);
        _mediator.SendAsync(Arg.Any<SearchResourcesQuery>(), Arg.Any<CancellationToken>())
            .Returns(new SearchResourcesResult(NoEntries<SearchEntryResult>()));
        _mediator.SendAsync(Arg.Any<GetResourceHistoryQuery>(), Arg.Any<CancellationToken>())
            .Returns(new HistoryResult { Entries = NoEntries<SearchEntryResult>(), Links = [] });
    }

    public static TheoryData<string, string> RejectedTypes() => Combine(InjectedType, "NotAResourceType");

    public static TheoryData<string, string> AcceptedTypes() => Combine("Foo", "foo");

    // Each row is its own case so a tool that skips the check fails by name.
    private static TheoryData<string, string> Combine(params string[] types)
    {
        var data = new TheoryData<string, string>();
        foreach (var tool in new[] { "get_fhir_resource", "get_fhir_resource_history", "search_fhir_resources", "patch_fhir_resource", "patch_resource_field" })
        {
            foreach (var type in types)
            {
                data.Add(tool, type);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RejectedTypes))]
    public async Task GivenAResourceTypeTheTenantDoesNotServe_WhenCallingATool_ThenItIsRejectedBeforeAnythingIsSent(string tool, string resourceType)
    {
        var outcome = await Invoke(tool, resourceType);

        outcome.ShouldNotBeNull().ShouldContain("not supported");
        _mediator.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [MemberData(nameof(AcceptedTypes))]
    public async Task GivenAPackageDefinedResourceType_WhenCallingATool_ThenItReachesTheMediatorInCanonicalCasing(string tool, string resourceType)
    {
        var outcome = await Invoke(tool, resourceType);

        outcome.ShouldBeNull();
        _mediator.ReceivedCalls()
            .Select(call => call.GetArguments()[0])
            .Select(request => request switch
            {
                GetResourceQuery query => query.ResourceType,
                GetResourceHistoryQuery query => query.ResourceType,
                SearchResourcesQuery query => query.ResourceType,
                PatchResourceCommand command => command.ResourceType,
                _ => null,
            })
            .ShouldBe(["Foo"]);
    }

    [Fact]
    public async Task GivenAVeryLongResourceType_WhenItIsRejected_ThenTheMessageEchoesOnlyItsStart()
    {
        var outcome = await Invoke("get_fhir_resource", new string('x', 500));

        outcome.ShouldNotBeNull().Length.ShouldBeLessThan(200);
    }

    /// <summary>
    /// Calls the tool and returns the rejection message, or null when the call got through to the mediator.
    /// </summary>
    private async Task<string?> Invoke(string tool, string resourceType)
    {
        const string operations = """[{"type":"replace","path":"Patient.active","value":true}]""";
        switch (tool)
        {
            case "get_fhir_resource":
                return await Rejection(() => new GetResourceTool(_accessor, _tenants, _mediator, _accessor, _versionContext)
                    .GetResourceAsync(resourceType, "1"));
            case "get_fhir_resource_history":
                return await Rejection(() => new GetResourceHistoryTool(_accessor, _tenants, _mediator, _versionContext)
                    .GetResourceHistoryAsync(resourceType, "1"));
            case "search_fhir_resources":
                return await Rejection(() => new SearchResourcesTool(_accessor, _tenants, _mediator, _builderFactory, _versionContext, _accessor)
                    .SearchResourcesAsync(resourceType, []));
            case "patch_fhir_resource":
                var patch = await new PatchResourceTool(_accessor, _tenants, _mediator, _versionContext)
                    .PatchResourceAsync(resourceType, "1", operations);
                return patch.Success || patch.ErrorMessage!.Contains("not found", StringComparison.Ordinal) ? null : patch.ErrorMessage;
            case "patch_resource_field":
                var field = await new PatchResourceFieldTool(_accessor, _tenants, _mediator, _versionContext)
                    .PatchResourceFieldAsync(resourceType, "1", "active", true);
                return field.Success || field.ErrorMessage!.Contains("not found", StringComparison.Ordinal) ? null : field.ErrorMessage;
            default:
                throw new ArgumentOutOfRangeException(nameof(tool));
        }
    }

    private static async Task<string?> Rejection(Func<Task> call)
    {
        try
        {
            await call();
            return null;
        }
        catch (Ignixa.Domain.Exceptions.BadRequestException ex)
        {
            return ex.Message;
        }
    }

    private static async IAsyncEnumerable<T> NoEntries<T>()
    {
        await Task.CompletedTask;
        yield break;
    }
}
