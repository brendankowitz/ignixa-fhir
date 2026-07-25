// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// The single authority for "which base URIs identify this server for tenant N".
/// </summary>
/// <remarks>
/// One tenant answers to two bases: the deployment root (<c>https://host/</c>, used by tenant-agnostic
/// routes) and the tenant-scoped base (<c>https://host/tenant/1/</c>). Both are forms this server itself
/// hands out in Location headers and pagination links, so both must be recognized as self-references.
/// Deriving the set from the tenant instead of from the incoming route is what makes the answer identical
/// whichever route a request arrived on, and reachable from background indexing that has no route at all.
/// The rule deliberately does not vary by single- vs multi-tenant mode: a rule that varies is how the two
/// paths drifted apart in the first place, and <c>https://host/Patient/1</c> does not resolve to any other
/// tenant's resource on a multi-tenant deployment either.
///
/// <paramref name="configuredServiceRoot"/> (<c>Fhir:BaseUri</c>) is authoritative when set, and the
/// request's <c>Host</c> header is then ignored entirely. That is the fix for a forged <c>Host</c> deciding
/// how references are stored, and it is also what lets a reindex reach the same answer with no request.
/// Leave it unset only when the deployment never reindexes and never imports.
/// </remarks>
public sealed class FhirServiceBaseUriResolver(Uri? configuredServiceRoot = null)
{
    private readonly Uri? _configuredServiceRoot = FhirServiceBaseUri.Normalize(configuredServiceRoot);

    /// <summary>
    /// Gets the configured deployment root, or null when the deployment relies on the request origin.
    /// </summary>
    public Uri? ConfiguredServiceRoot => _configuredServiceRoot;

    /// <summary>
    /// Resolves every base URI that identifies this server for <paramref name="tenantId"/>, canonical first.
    /// Returns empty when neither a configured root nor a request origin is available.
    /// </summary>
    /// <param name="requestOrigin">
    /// Scheme, host and path base of the current request. Ignored when a service root is configured.
    /// </param>
    /// <param name="tenantId">Resolved tenant, or null when no tenant applies to the request.</param>
    /// <param name="canonicalForm">Which base to place first, for callers that need one base to emit.</param>
    public IReadOnlyList<Uri> Resolve(
        Uri? requestOrigin,
        int? tenantId,
        FhirServiceBaseUriForm canonicalForm = FhirServiceBaseUriForm.Root)
    {
        var root = _configuredServiceRoot ?? FhirServiceBaseUri.Normalize(requestOrigin);

        if (root is null || !root.IsAbsoluteUri)
        {
            return [];
        }

        // Tenant 0 is the reserved system partition and is never reachable over a tenant route.
        if (tenantId is not > 0)
        {
            return [root];
        }

        var tenantScoped = new Uri(root, $"tenant/{tenantId.Value}/");

        return canonicalForm == FhirServiceBaseUriForm.TenantScoped
            ? [tenantScoped, root]
            : [root, tenantScoped];
    }

    /// <summary>
    /// Resolves every base URI that identifies this server for a tenant, canonical first. The canonical base
    /// is the tenant's first configured hostname, or the <c>tenant/{id}/</c> path form when it has none. The
    /// remaining hostnames and the path form are additional recognized inbound bases; the deployment root is
    /// recognized only when <see cref="TenantAddressing.IncludeDeploymentRoot"/> is set. Both the request
    /// path and the background path call this method, so a self-reference classifies identically either way.
    /// </summary>
    public IReadOnlyList<Uri> Resolve(Uri? requestOrigin, TenantAddressing tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        var root = _configuredServiceRoot ?? FhirServiceBaseUri.Normalize(requestOrigin);

        if (root is null || !root.IsAbsoluteUri)
        {
            return [];
        }

        var bases = new List<Uri>();

        foreach (var host in tenant.Hostnames)
        {
            var trimmed = host.Trim();
            if (!Uri.TryCreate($"{root.Scheme}://{trimmed}/", UriKind.Absolute, out var candidate)
                || candidate.AbsolutePath != "/"
                || !candidate.IsDefaultPort
                || candidate.Host.Length == 0)
            {
                // Malformed config (embedded path, explicit port, or an unparseable authority) is never
                // admitted into the recognition set. There is no logger here by design; startup validation
                // (a later task) is the place operator-facing diagnostics for a bad hostname belong.
                continue;
            }

            var hostBase = FhirServiceBaseUri.Normalize(candidate);
            if (hostBase is not null)
            {
                bases.Add(hostBase);
            }
        }

        // Canonical precedence: a configured hostname (added above), else the deployment root for a sole tenant,
        // else the tenant/{id}/ path form. Keeping root canonical for a hostname-less sole tenant preserves the
        // service base existing single-tenant deployments already emit, so this feature does not silently rewrite
        // their Location headers and fullUrls.
        if (tenant.IncludeDeploymentRoot)
        {
            bases.Add(root);
        }

        // Numeric path form is always recognized (and canonical when no hostname is configured and no root
        // applies), so a reference stored via /tenant/{id}/ still classifies as internal after the switch to
        // hostnames.
        bases.Add(new Uri(root, $"tenant/{tenant.TenantId}/"));

        return bases.Distinct().ToArray();
    }
}
