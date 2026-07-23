// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Supplies the FHIR service base URI for the current tenant, so that a reference written as an absolute
/// URL pointing back at this server can be recognized as internal.
/// </summary>
/// <remarks>
/// Reference reconciliation depends on indexing and searching agreeing on one representation: an absolute
/// self-reference must collapse to the same stored form as the equivalent relative reference. That
/// collapse happens in <c>ReferenceSearchValueParser</c>, which runs on both paths, so the base URI has to
/// be reachable from Core rather than only from the API layer.
///
/// Implementations must also answer outside an HTTP request — reindex, $import, and subscription delivery
/// index resources with no ambient context, and if they disagreed with the request path about what
/// "internal" means, the two would write different rows for the same reference.
/// </remarks>
public interface IFhirBaseUriProvider
{
    /// <summary>
    /// Gets the service base URI for the current tenant, or null when it cannot be determined and
    /// absolute references must therefore be left unnormalized.
    /// </summary>
    Uri? GetBaseUri();
}
