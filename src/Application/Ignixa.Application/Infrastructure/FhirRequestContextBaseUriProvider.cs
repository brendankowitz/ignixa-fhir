// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Resolves this server's service base URIs from the ambient FHIR request context, falling back to the
/// configured deployment root when there is no request — reindex, $import, and subscription delivery all
/// index resources outside the HTTP pipeline.
/// </summary>
/// <remarks>
/// Request and background paths run the same <see cref="FhirServiceBaseUriResolver"/> over the same tenant,
/// so a reindex classifies a given absolute self-reference exactly as the request that first stored it did.
/// Without a configured root the fallback yields nothing, and background-indexed rows will store
/// self-references as external while request-indexed rows collapsed them — see
/// <see cref="FhirServiceBaseUriResolver"/> for why <c>Fhir:BaseUri</c> is not optional in practice.
/// </remarks>
public sealed class FhirRequestContextBaseUriProvider(
    IFhirRequestContextAccessor requestContextAccessor,
    FhirServiceBaseUriResolver resolver) : IFhirBaseUriProvider
{
    /// <inheritdoc />
    public Uri? GetBaseUri() => GetServiceBaseUris() is [var canonical, ..] ? canonical : null;

    /// <inheritdoc />
    public IReadOnlyList<Uri> GetServiceBaseUris()
    {
        var context = requestContextAccessor.RequestContext;

        if (context?.ServiceBaseUris is { Count: > 0 } fromRequest)
        {
            return fromRequest;
        }

        return resolver.Resolve(
            requestOrigin: null,
            context?.TenantId,
            FhirServiceBaseUriForm.TenantScoped);
    }
}
