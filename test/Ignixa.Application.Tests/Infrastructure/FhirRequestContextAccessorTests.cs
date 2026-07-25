// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Application.Infrastructure;
using Shouldly;
using Xunit;

namespace Ignixa.Application.Tests.Infrastructure;

/// <summary>
/// <see cref="FhirRequestContextAccessor"/> stores the context in a <c>static</c> <see cref="AsyncLocal{T}"/>,
/// and singletons depend on that. <c>IFhirBaseUriProvider</c> is consumed by singletons
/// (FhirVersionContext, SearchOptionsBuilderFactory, the GraphQL type modules), so it is registered as a
/// singleton over one captured accessor instance; it only sees the calling request because every accessor
/// instance shares the one static slot. Making that field instance-scoped — a plausible "cleanup" — would
/// leave those singletons on a permanently empty context and silently stop reference reconciliation, with
/// no error anywhere. These tests exist so that change fails here instead.
/// </summary>
public class FhirRequestContextAccessorTests
{
    [Fact]
    public void GivenTwoAccessorInstances_WhenOneSetsTheContext_ThenTheOtherObservesIt()
    {
        var writer = new FhirRequestContextAccessor { RequestContext = null };
        var reader = new FhirRequestContextAccessor();

        var context = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 1);
        writer.RequestContext = context;

        reader.RequestContext.ShouldBeSameAs(context);
    }

    [Fact]
    public void GivenALongLivedAccessor_WhenAnotherAccessorSetsTheContextLater_ThenTheLongLivedOneSeesIt()
    {
        var capturedBySingleton = new FhirRequestContextAccessor { RequestContext = null };

        var perRequest = new FhirRequestContextAccessor();
        var context = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 7);
        perRequest.RequestContext = context;

        capturedBySingleton.RequestContext.ShouldNotBeNull();
        capturedBySingleton.RequestContext!.TenantId.ShouldBe(7);
    }

    [Fact]
    public async Task GivenAContextSetInsideAnAsyncFlow_WhenTheFlowCompletes_ThenTheAmbientContextIsUnchanged()
    {
        var accessor = new FhirRequestContextAccessor { RequestContext = null };

        await SetContextAsync(accessor);

        accessor.RequestContext.ShouldBeNull();
    }

    private static async Task SetContextAsync(FhirRequestContextAccessor accessor)
    {
        await Task.Yield();
        accessor.RequestContext = FhirRequestContextFactory.CreateBackgroundContext(tenantId: 2);
        accessor.RequestContext.ShouldNotBeNull();
    }
}
