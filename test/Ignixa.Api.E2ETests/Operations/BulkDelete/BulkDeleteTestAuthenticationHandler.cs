// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using System.Text.Encodings.Web;
using Ignixa.Application.Features.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Test-only authentication scheme that builds a <see cref="ClaimsPrincipal"/> straight from request
/// headers, so <c>BulkDeleteAuthorizationTests</c> can drive the real authorization pipeline -- route-level
/// <c>FhirAuthorizationFilter</c>, <c>BulkDeleteKickoffAuthorizer</c>, and the RBAC/SMART handlers they
/// call -- with real claims, without standing up a JWT issuer.
/// </summary>
/// <remarks>
/// <para>
/// Registered only by <c>BulkDeleteAuthorizationTests</c>'s derived <c>WithWebHostBuilder</c> host, as the
/// default authentication scheme for that host alone (overriding production's JwtBearer default, which
/// <c>CoreServicesRegistration.ConfigureJwtAuthentication</c> registers whenever <c>Authorization:Enabled
/// =true</c>). It never runs against the shared <see cref="BulkDeleteApiFixture"/> host or any other E2E
/// fixture, where <c>Authorization:Enabled=false</c> skips authentication middleware entirely.
/// </para>
/// <para>
/// A request with neither <see cref="ScopeHeaderName"/> nor <see cref="RoleHeaderName"/> authenticates as
/// anonymous (<see cref="AuthenticateResult.NoResult"/>), the same as a request with no bearer token in
/// production: <c>HttpContext.User</c> stays unauthenticated, and the real
/// <c>Ignixa.Application.Features.Authorization.Handlers.AuthenticationHandler</c> denies it.
/// </para>
/// </remarks>
internal sealed class BulkDeleteTestAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>The scheme name registered as this host's default authentication scheme.</summary>
    public const string SchemeName = "BulkDeleteE2ETest";

    /// <summary>Request header whose value becomes the caller's <c>scope</c> (SMART) claim.</summary>
    public const string ScopeHeaderName = "X-Test-Scope";

    /// <summary>Request header whose value becomes the caller's SMART <c>patient</c> launch-context claim.</summary>
    public const string PatientHeaderName = "X-Test-Patient";

    /// <summary>Request header whose value becomes the caller's <c>role</c> claim (RBAC).</summary>
    public const string RoleHeaderName = "X-Test-Role";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var hasScope = Request.Headers.TryGetValue(ScopeHeaderName, out var scope);
        var hasRole = Request.Headers.TryGetValue(RoleHeaderName, out var role);
        if (!hasScope && !hasRole)
        {
            // No test credentials supplied: stay anonymous, exactly like a request with no bearer token.
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        List<Claim> claims = [new(FhirClaimTypes.Subject, "e2e-test-client")];
        if (hasScope)
        {
            claims.Add(new Claim(FhirClaimTypes.Scope, scope.ToString()));
        }

        if (hasRole)
        {
            claims.Add(new Claim(FhirClaimTypes.Role, role.ToString()));
        }

        if (Request.Headers.TryGetValue(PatientHeaderName, out var patient))
        {
            claims.Add(new Claim(FhirClaimTypes.Patient, patient.ToString()));
        }

        var ticket = new AuthenticationTicket(
            new ClaimsPrincipal(new ClaimsIdentity(claims, SchemeName)), SchemeName);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
