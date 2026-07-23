// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Abstractions;

/// <summary>
/// Supplies the FHIR service base URIs for the current tenant, so that a reference written as an absolute
/// URL pointing back at this server can be recognized as internal.
/// </summary>
/// <remarks>
/// Reference reconciliation depends on indexing and searching agreeing on one representation: an absolute
/// self-reference must collapse to the same stored form as the equivalent relative reference. That
/// collapse happens in <c>ReferenceSearchValueParser</c>, which runs on both paths, so the base URI has to
/// be reachable from Core rather than only from the API layer.
///
/// One server answers to more than one base. A single-tenant deployment serves both <c>/Patient/1</c> and
/// <c>/tenant/1/Patient/1</c>, and it hands out absolute links in both forms depending on how the request
/// arrived. Recognition therefore works over a <em>set</em> of equivalent bases rather than one scalar;
/// picking a single winner is what made the answer depend on the route form, so the same reference
/// reconciled on one route and not the other. Only <see cref="IsServiceBaseUri"/> should drive that
/// decision — <see cref="GetBaseUri"/> exists for callers that need one base to emit.
///
/// Implementations must also answer outside an HTTP request — reindex, $import, and subscription delivery
/// index resources with no ambient context, and if they disagreed with the request path about what
/// "internal" means, the two would write different rows for the same reference.
/// </remarks>
public interface IFhirBaseUriProvider
{
    /// <summary>
    /// Gets the canonical service base URI for the current tenant, or null when it cannot be determined.
    /// This is the base to emit; use <see cref="IsServiceBaseUri"/> to recognize one.
    /// </summary>
    Uri? GetBaseUri();

    /// <summary>
    /// Gets every base URI that identifies this server for the current tenant, canonical first.
    /// Empty when no base can be determined.
    /// </summary>
    IReadOnlyList<Uri> GetServiceBaseUris()
        => GetBaseUri() is { } baseUri ? [baseUri] : [];

    /// <summary>
    /// Determines whether <paramref name="candidate"/> is one of this server's service base URIs, ignoring
    /// a trailing-slash difference, scheme/host casing, and a default port.
    /// </summary>
    bool IsServiceBaseUri(Uri? candidate)
    {
        if (candidate is null || !candidate.IsAbsoluteUri)
        {
            return false;
        }

        foreach (var serviceBase in GetServiceBaseUris())
        {
            if (FhirServiceBaseUri.AreEquivalent(serviceBase, candidate))
            {
                return true;
            }
        }

        return false;
    }
}
