// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Resolves the service base URI from the ambient FHIR request context, falling back to a configured
/// value when there is no request — reindex, $import, and subscription delivery all index resources
/// outside the HTTP pipeline.
/// </summary>
/// <remarks>
/// The fallback is what keeps background-indexed rows consistent with request-indexed ones. Without it a
/// reindex would classify every absolute self-reference as external and store a BaseUri that the request
/// path would have stripped, so the same reference would be searchable before a reindex and not after.
/// </remarks>
public sealed class FhirRequestContextBaseUriProvider(
    IFhirRequestContextAccessor requestContextAccessor,
    Uri? configuredBaseUri = null) : IFhirBaseUriProvider
{
    /// <inheritdoc />
    public Uri? GetBaseUri() => requestContextAccessor.RequestContext?.BaseUri ?? configuredBaseUri;
}
