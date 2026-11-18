// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.Application.Features.Experimental.Mcp.Tools.FhirOperations;
using Ignixa.Application.Features.Search;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Abstractions;
using Ignixa.Domain.Models;
using Ignixa.Search.Parsing;
using Ignixa.Serialization;
using Ignixa.Specification.Extensions;
using Medino;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Experimental.Mcp.Tools;

/// <summary>
/// Issue #536: MCP tools take the resource type from an untrusted caller, so a value that is not a schema
/// resource type must be rejected before any query or command is built. Both tests are regressions:
/// at the base commit the tools only checked for non-empty and sent the value to the mediator.
/// </summary>
public class McpResourceTypeValidationTests
{
    private const string InjectedType = "Patient' or true or type = '";

    private readonly IMediator _mediator = Substitute.For<IMediator>();
    private readonly IFhirRequestContextAccessor _accessor = Substitute.For<IFhirRequestContextAccessor>();
    private readonly ITenantConfigurationStore _tenants = Substitute.For<ITenantConfigurationStore>();
    private readonly IFhirVersionContext _versionContext = Substitute.For<IFhirVersionContext>();

    public McpResourceTypeValidationTests()
    {
        _accessor.RequestContext.Returns(new FhirRequestContext { TenantId = 1 });
        _tenants.GetTenantConfigurationAsync(1, Arg.Any<CancellationToken>())
            .Returns(new TenantConfiguration { TenantId = 1, DisplayName = "MCP test", FhirVersion = "4.0" });
        _versionContext.GetBaseSchemaProvider(FhirVersion.R4).Returns(FhirVersion.R4.GetSchemaProvider());
    }

    [Theory]
    [InlineData(InjectedType)]
    [InlineData("NotAResourceType")]
    public async Task GivenAnUnknownResourceType_WhenCallingThrowingTools_ThenItIsRejectedBeforeAnythingIsSent(string resourceType)
    {
        var getTool = new GetResourceTool(_accessor, _tenants, _mediator, _accessor, _versionContext);
        var historyTool = new GetResourceHistoryTool(_accessor, _tenants, _mediator, _versionContext);
        var searchTool = new SearchResourcesTool(
            _accessor, _tenants, _mediator, Substitute.For<ISearchOptionsBuilderFactory>(), _versionContext, _accessor);

        await Should.ThrowAsync<ArgumentException>(() => getTool.GetResourceAsync(resourceType, "1"));
        await Should.ThrowAsync<ArgumentException>(() => historyTool.GetResourceHistoryAsync(resourceType, "1"));
        await Should.ThrowAsync<ArgumentException>(() => searchTool.SearchResourcesAsync(resourceType, []));

        _mediator.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(InjectedType)]
    [InlineData("NotAResourceType")]
    public async Task GivenAnUnknownResourceType_WhenCallingPatchTools_ThenTheErrorResultIsReturnedBeforeAnythingIsSent(string resourceType)
    {
        var patchTool = new PatchResourceTool(_accessor, _tenants, _mediator, _versionContext);
        var fieldTool = new PatchResourceFieldTool(_accessor, _tenants, _mediator, _versionContext);
        const string operations = """[{"type":"replace","path":"Patient.active","value":true}]""";

        var patch = await patchTool.PatchResourceAsync(resourceType, "1", operations);
        var field = await fieldTool.PatchResourceFieldAsync(resourceType, "1", "active", true);

        patch.Success.ShouldBeFalse();
        patch.ErrorMessage.ShouldNotBeNull().ShouldContain("not supported");
        field.Success.ShouldBeFalse();
        field.ErrorMessage.ShouldNotBeNull().ShouldContain("not supported");
        _mediator.ReceivedCalls().ShouldBeEmpty();
    }
}
