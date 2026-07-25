// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.RegularExpressions;
using Ignixa.Domain.Models;

namespace Ignixa.Application.Infrastructure;

/// <summary>
/// Validates tenant hostname configuration: each hostname is a bare DNS host, and no hostname is claimed by
/// more than one tenant. Returns typed problems; an empty list means valid.
/// </summary>
public static partial class TenantHostnameValidator
{
    [GeneratedRegex(@"^(?=.{1,253}$)([a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$")]
    private static partial Regex HostnameShape();

    /// <summary>
    /// True when <paramref name="host"/> is a bare lowercase DNS hostname -- no scheme, port, path, or
    /// invalid label. This is the single source of truth for hostname shape: both <see cref="Validate"/>
    /// (startup diagnostics) and <c>AppSettingsTenantConfigurationStore.BuildHostIndex</c> (inbound routing)
    /// call it, so a hostname the index would refuse to serve is exactly the one this reports as a problem.
    /// </summary>
    public static bool IsValidHostname(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        return HostnameShape().IsMatch(host);
    }

    public static IReadOnlyList<HostnameProblem> Validate(IReadOnlyList<TenantConfiguration> tenants)
    {
        ArgumentNullException.ThrowIfNull(tenants);

        var problems = new List<HostnameProblem>();
        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var tenant in tenants)
        {
            foreach (var host in tenant.Hostnames)
            {
                var value = host.Trim();

                if (!IsValidHostname(value))
                {
                    problems.Add(new HostnameProblem(
                        HostnameProblemKind.Format,
                        $"Tenant {tenant.TenantId}: '{host}' is not a bare lowercase DNS hostname (no scheme, port, or path)."));
                    continue;
                }

                if (seen.TryGetValue(value, out var otherTenant))
                {
                    problems.Add(new HostnameProblem(
                        HostnameProblemKind.Duplicate,
                        $"Hostname '{value}' is claimed by tenant {otherTenant} and tenant {tenant.TenantId}; a hostname must resolve exactly one tenant."));
                    continue;
                }

                seen[value] = tenant.TenantId;
            }
        }

        return problems;
    }
}
