// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Security.Claims;
using Ignixa.Application.Features.Authorization;
using Ignixa.Domain.Models;
using Microsoft.AspNetCore.Http;

namespace Ignixa.Application.Infrastructure.Audit;

/// <summary>
/// Resolves who and what a request is attributed to in audit records.
/// </summary>
public static class AuditAttribution
{
    /// <summary>
    /// User identifier recorded when the request carries no identifying claim.
    /// </summary>
    public const string AnonymousUser = "anonymous";

    /// <summary>
    /// Returns the caller's <c>sub</c>, <c>oid</c>, or name-identifier claim, or <see cref="AnonymousUser"/> when none is present.
    /// </summary>
    public static string GetUserId(ClaimsPrincipal user) =>
        user.FindFirst(FhirClaimTypes.Subject)?.Value ??
        user.FindFirst(FhirClaimTypes.ObjectId)?.Value ??
        user.FindFirst(FhirClaimTypes.NameIdentifier)?.Value ??
        AnonymousUser;

    /// <summary>
    /// Captures the kick-off request's attribution and validated custom audit headers for a background job.
    /// </summary>
    /// <exception cref="Ignixa.Domain.Exceptions.AuditHeaderException">The request's custom audit headers exceed the limits.</exception>
    public static BackgroundJobAuditContext CreateJobAuditContext(HttpContext httpContext) => new()
    {
        UserId = GetUserId(httpContext.User),
        CorrelationId = httpContext.TraceIdentifier,
        CustomHeaders = CustomAuditHeaders.Read(httpContext)
    };
}
