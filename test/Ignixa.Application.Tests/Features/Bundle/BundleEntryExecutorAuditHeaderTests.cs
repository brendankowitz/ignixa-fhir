// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Features.Bundle;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IO;
using NSubstitute;
using Shouldly;

namespace Ignixa.Application.Tests.Features.Bundle;

public class BundleEntryExecutorAuditHeaderTests
{
    private readonly IPipelineExecutor _pipeline = Substitute.For<IPipelineExecutor>();
    private readonly DefaultHttpContext _parent = new();
    private HttpContext? _entryHttpContext;

    public BundleEntryExecutorAuditHeaderTests()
    {
        _pipeline.ExecuteAsync(Arg.Do<HttpContext>(context => _entryHttpContext = context)).Returns(Task.CompletedTask);
        _parent.Request.Scheme = "https";
        _parent.Request.Host = new HostString("fhir.example.org");
        _parent.Request.Headers["X-IGNIXA-AUDIT-OPERATIONID"] = "op-1";
        _parent.Request.Headers["X-IGNIXA-AUDIT-BUNDLEID"] = "bundle-1";
        _parent.Request.Headers["Authorization"] = "Bearer secret";
    }

    [Theory]
    [InlineData("GET", "Patient/1")]
    [InlineData("DELETE", "Patient/1")]
    [InlineData("GET", "Patient?name=smith")]
    public async Task GivenOuterRequestWithCustomAuditHeaders_WhenExecutingEntry_ThenEntryRequestCarriesThem(
        string verb, string url)
    {
        await CreateExecutor().ExecuteAsync(Entry(verb, url), new ReferenceResolutionContext(), CancellationToken.None);

        _entryHttpContext.ShouldNotBeNull();
        _entryHttpContext.Request.Headers["X-IGNIXA-AUDIT-OPERATIONID"].ToString().ShouldBe("op-1");
        _entryHttpContext.Request.Headers["X-IGNIXA-AUDIT-BUNDLEID"].ToString().ShouldBe("bundle-1");
    }

    [Fact]
    public async Task GivenOuterRequestWithOtherHeaders_WhenExecutingEntry_ThenOnlyAuditHeadersArePropagated()
    {
        await CreateExecutor().ExecuteAsync(Entry("GET", "Patient/1"), new ReferenceResolutionContext(), CancellationToken.None);

        _entryHttpContext.ShouldNotBeNull();
        _entryHttpContext.Request.Headers.ContainsKey("Authorization").ShouldBeFalse();
    }

    private BundleEntryExecutor CreateExecutor()
    {
        var httpContextAccessor = Substitute.For<IHttpContextAccessor>();
        httpContextAccessor.HttpContext.Returns(_parent);
        var fhirContextAccessor = Substitute.For<IFhirRequestContextAccessor>();
        fhirContextAccessor.RequestContext = new FhirRequestContext { TenantId = 1 };

        return new BundleEntryExecutor(
            _pipeline,
            httpContextAccessor,
            fhirContextAccessor,
            new RecyclableMemoryStreamManager(),
            NullLogger<BundleEntryExecutor>.Instance);
    }

    private static BundleEntryContext Entry(string verb, string url) => new()
    {
        Index = 0,
        HttpVerb = verb,
        ResourceType = "Patient",
        ResourceId = null,
        RequestUrl = url,
        Resource = null,
        FullUrl = null
    };
}
