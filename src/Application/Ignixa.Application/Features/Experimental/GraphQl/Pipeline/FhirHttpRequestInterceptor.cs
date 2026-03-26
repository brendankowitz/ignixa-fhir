// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License. See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using HotChocolate.AspNetCore;
using HotChocolate.Execution;
using Ignixa.Application.Infrastructure;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Features.Experimental.GraphQl.Pipeline;

public sealed class FhirHttpRequestInterceptor(
    IFhirRequestContextAccessor contextAccessor) : DefaultHttpRequestInterceptor
{
    public override ValueTask OnCreateAsync(
        HttpContext context,
        IRequestExecutor requestExecutor,
        OperationRequestBuilder requestBuilder,
        CancellationToken cancellationToken)
    {
        var fhirContext = contextAccessor.RequestContext;
        if (fhirContext is not null)
        {
            requestBuilder.AddGlobalState("TenantId", fhirContext.TenantId);
            requestBuilder.AddGlobalState("FhirVersion", fhirContext.FhirVersion);
        }

        return base.OnCreateAsync(context, requestExecutor, requestBuilder, cancellationToken);
    }
}
