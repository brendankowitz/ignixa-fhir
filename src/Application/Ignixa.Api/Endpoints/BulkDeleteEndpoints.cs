// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Abstractions;
using Ignixa.Api.Extensions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.BackgroundOperations.BulkDelete;
using Ignixa.Application.Infrastructure;
using Medino;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Ignixa.Serialization;
using FhirOperationOutcomeIssue = Ignixa.Models.OperationOutcomeIssue;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// API endpoints for the FHIR <c>$bulk-delete</c> operation (fhir-server/AHDS-compatible), used by
/// AHDS-compatible clients to deprovision a tenant's data. Kickoff validates and starts a DurableTask
/// orchestration; status and cancel address the resulting job. Request parsing and response shaping
/// are pure static helpers (<see cref="BulkDeleteRequestParser"/>, <see cref="BulkDeleteStatusResponseBuilder"/>)
/// so the endpoints themselves stay thin glue between HTTP and the Medino commands Task 4 implements.
/// </summary>
public static class BulkDeleteEndpoints
{
    /// <summary>
    /// Registers the bulk-delete endpoints. Must be mapped before <c>MapFhirEndpoints</c>: although
    /// ASP.NET Core's routing prefers the literal <c>$bulk-delete</c>/<c>_operations</c> segments over
    /// the generic <c>{resourceType}</c>/<c>{id}</c> routes regardless of registration order, registering
    /// bulk operations first keeps this file consistent with <c>MapExportEndpoints</c>/<c>MapImportEndpoints</c>.
    /// </summary>
    public static IEndpointRouteBuilder MapBulkDeleteEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var tenantGroup = endpoints
            .MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        tenantGroup.MapDelete("/$bulk-delete", (
                HttpContext context,
                [FromRoute] int tenantId,
                [FromServices] IMediator mediator,
                [FromServices] BulkDeleteKickoffAuthorizer authorizer,
                CancellationToken cancellationToken) =>
            StartBulkDeleteAsync(context, tenantId, isTenantScopedRoute: true, resourceType: null, mediator, authorizer, cancellationToken))
            .WithName("StartBulkDeleteSystemLevel");

        tenantGroup.MapDelete("/{resourceType}/$bulk-delete", (
                HttpContext context,
                [FromRoute] int tenantId,
                [FromRoute] string resourceType,
                [FromServices] IMediator mediator,
                [FromServices] BulkDeleteKickoffAuthorizer authorizer,
                CancellationToken cancellationToken) =>
            StartBulkDeleteAsync(context, tenantId, isTenantScopedRoute: true, resourceType, mediator, authorizer, cancellationToken))
            .WithName("StartBulkDeleteTypeLevel");

        tenantGroup.MapGet("/_operations/bulk-delete/{jobId}", (
                HttpContext context,
                [FromRoute] int tenantId,
                [FromRoute] string jobId,
                [FromServices] IMediator mediator,
                [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
                [FromServices] ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            GetBulkDeleteStatusAsync(context, tenantId, jobId, mediator, fhirContextAccessor, loggerFactory, cancellationToken))
            .WithName("GetBulkDeleteStatus");

        tenantGroup.MapDelete("/_operations/bulk-delete/{jobId}", (
                [FromRoute] int tenantId,
                [FromRoute] string jobId,
                [FromServices] IMediator mediator,
                CancellationToken cancellationToken) =>
            CancelBulkDeleteAsync(tenantId, jobId, mediator, cancellationToken))
            .WithName("CancelBulkDelete");

        var agnosticGroup = endpoints
            .MapGroup(string.Empty)
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>()
            .AddEndpointFilter<ResourceTypeValidationFilter>();

        agnosticGroup.MapDelete("/$bulk-delete", (
                HttpContext context,
                [FromServices] IMediator mediator,
                [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
                [FromServices] BulkDeleteKickoffAuthorizer authorizer,
                CancellationToken cancellationToken) =>
            StartBulkDeleteAsync(context, fhirContextAccessor.RequestContext!.TenantId, isTenantScopedRoute: false, resourceType: null, mediator, authorizer, cancellationToken))
            .WithName("StartBulkDeleteSystemLevelAgnostic");

        agnosticGroup.MapDelete("/{resourceType}/$bulk-delete", (
                HttpContext context,
                [FromRoute] string resourceType,
                [FromServices] IMediator mediator,
                [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
                [FromServices] BulkDeleteKickoffAuthorizer authorizer,
                CancellationToken cancellationToken) =>
            StartBulkDeleteAsync(context, fhirContextAccessor.RequestContext!.TenantId, isTenantScopedRoute: false, resourceType, mediator, authorizer, cancellationToken))
            .WithName("StartBulkDeleteTypeLevelAgnostic");

        agnosticGroup.MapGet("/_operations/bulk-delete/{jobId}", (
                HttpContext context,
                [FromRoute] string jobId,
                [FromServices] IMediator mediator,
                [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
                [FromServices] ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            GetBulkDeleteStatusAsync(context, fhirContextAccessor.RequestContext!.TenantId, jobId, mediator, fhirContextAccessor, loggerFactory, cancellationToken))
            .WithName("GetBulkDeleteStatusAgnostic");

        agnosticGroup.MapDelete("/_operations/bulk-delete/{jobId}", (
                [FromRoute] string jobId,
                [FromServices] IMediator mediator,
                [FromServices] IFhirRequestContextAccessor fhirContextAccessor,
                CancellationToken cancellationToken) =>
            CancelBulkDeleteAsync(fhirContextAccessor.RequestContext!.TenantId, jobId, mediator, cancellationToken))
            .WithName("CancelBulkDeleteAgnostic");

        return endpoints;
    }

    /// <summary>
    /// Kicks off a bulk-delete job: parses the query/body/<c>Prefer</c> header, authorizes the deletes the
    /// job will perform (<see cref="BulkDeleteKickoffAuthorizer"/>; 403 when denied), sends
    /// <see cref="CreateBulkDeleteJobCommand"/>, and returns 202 with a <c>Content-Location</c> pointing
    /// at the status endpoint. Every validation failure (including a missing <c>Prefer: respond-async</c>)
    /// is a <see cref="Ignixa.Serialization.Abstractions.FhirException"/>, mapped to a 400 OperationOutcome
    /// by <c>FhirExceptionMiddleware</c>; this method deliberately does not catch it. Parsing precedes
    /// authorization because the access required depends on the parsed request; it is pure and has no
    /// side effects.
    /// </summary>
    private static async Task<IResult> StartBulkDeleteAsync(
        HttpContext httpContext,
        int tenantId,
        bool isTenantScopedRoute,
        string? resourceType,
        IMediator mediator,
        BulkDeleteKickoffAuthorizer authorizer,
        CancellationToken cancellationToken)
    {
        // Route values are logged downstream by the authorization handlers.
        resourceType = resourceType.SanitizeForLog();

        var queryParameters = Flatten(httpContext.Request.Query);
        var preferHeader = httpContext.Request.Headers.TryGetValue("Prefer", out var preferValues)
            ? preferValues.ToString()
            : null;
        var body = await ReadBodyAsync(httpContext.Request, cancellationToken);

        var parsed = BulkDeleteRequestParser.Parse(queryParameters, preferHeader, body);

        var authorization = await authorizer.AuthorizeAsync(httpContext, resourceType, parsed, cancellationToken);
        if (!authorization.Allowed)
        {
            return FhirAuthorizationFilter.CreateForbiddenResponse(authorization.DenialReason ?? "Access denied");
        }

        var command = new CreateBulkDeleteJobCommand(
            tenantId,
            resourceType,
            parsed.SearchParameters,
            parsed.Mode,
            parsed.ExcludedResourceTypes,
            parsed.RemoveReferences,
            httpContext.Request.GetEncodedUrl());

        var result = await mediator.SendAsync(command, cancellationToken);

        httpContext.Response.Headers["Content-Location"] = BuildContentLocation(
            httpContext.Request.Scheme,
            httpContext.Request.Host.Value ?? string.Empty,
            httpContext.Request.PathBase.Value ?? string.Empty,
            isTenantScopedRoute,
            tenantId,
            result.JobId);

        // fhir-server returns 202 with no body; Results.StatusCode emits none.
        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    /// <summary>
    /// Reads a bulk-delete job's status and shapes it into the FHIR <c>Parameters</c> fhir-server emits.
    /// </summary>
    private static async Task<IResult> GetBulkDeleteStatusAsync(
        HttpContext httpContext,
        int tenantId,
        string jobId,
        IMediator mediator,
        IFhirRequestContextAccessor fhirContextAccessor,
        ILoggerFactory loggerFactory,
        CancellationToken cancellationToken)
    {
        GetBulkDeleteStatusResult result;
        try
        {
            result = await mediator.SendAsync(new GetBulkDeleteStatusQuery(tenantId, jobId), cancellationToken);
        }
        catch (System.Collections.Generic.KeyNotFoundException)
        {
            return NotFoundOutcome("Bulk delete job not found.");
        }
        catch (JsonException ex)
        {
            loggerFactory.CreateLogger(typeof(BulkDeleteEndpoints)).LogError(
                ex, "Bulk delete job {JobId} for tenant {TenantId} has an invalid persisted result or progress record", jobId.SanitizeForLog(), tenantId);
            return OperationOutcomeResult(
                StatusCodes.Status500InternalServerError,
                FhirOperationOutcomeIssue.IssueSeverityCode.Error,
                FhirOperationOutcomeIssue.IssueTypeCommon.Exception,
                "Bulk delete job has an invalid persisted result or progress record.");
        }

        var fhirVersion = fhirContextAccessor.RequestContext?.FhirVersion ?? FhirVersion.R4;
        var response = BulkDeleteStatusResponseBuilder.Build(result, fhirVersion);

        if (response.StatusCode == StatusCodes.Status202Accepted)
        {
            httpContext.Response.Headers["Progress"] = "In Progress";
            httpContext.Response.Headers["Retry-After"] = "1";
        }

        return Results.Content(response.Body.ToJsonString(), KnownContentTypes.ApplicationFhirJson, statusCode: response.StatusCode);
    }

    /// <summary>
    /// Cancels a bulk-delete job. <see cref="CancelBulkDeleteOutcome.AlreadyTerminal"/> does not carry the
    /// job's terminal status, so it is re-read via <see cref="GetBulkDeleteStatusQuery"/> to report the
    /// exact status in the conflict message; a race that removes the job before this read falls back to a
    /// generic message rather than failing the cancellation response.
    /// </summary>
    private static async Task<IResult> CancelBulkDeleteAsync(
        int tenantId,
        string jobId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var outcome = await mediator.SendAsync(new CancelBulkDeleteCommand(tenantId, jobId), cancellationToken);
        switch (outcome)
        {
            case CancelBulkDeleteOutcome.Accepted:
                return Results.StatusCode(StatusCodes.Status202Accepted);

            case CancelBulkDeleteOutcome.NotFound:
                return NotFoundOutcome("Bulk delete job not found.");

            case CancelBulkDeleteOutcome.AlreadyTerminal:
                var status = await TryGetTerminalStatusAsync(tenantId, jobId, mediator, cancellationToken);
                return OperationOutcomeResult(
                    StatusCodes.Status409Conflict,
                    FhirOperationOutcomeIssue.IssueSeverityCode.Error,
                    FhirOperationOutcomeIssue.IssueTypeCommon.Conflict,
                    $"Bulk delete job is already {status}.");

            default:
                throw new InvalidOperationException($"Unexpected bulk delete cancellation outcome '{outcome}'.");
        }
    }

    private static async Task<string> TryGetTerminalStatusAsync(
        int tenantId, string jobId, IMediator mediator, CancellationToken cancellationToken)
    {
        try
        {
            var status = await mediator.SendAsync(new GetBulkDeleteStatusQuery(tenantId, jobId), cancellationToken);
            return status.Status;
        }
        catch (Exception ex) when (ex is System.Collections.Generic.KeyNotFoundException or JsonException)
        {
            // The job reached a terminal state between the cancel attempt and this follow-up read (or
            // disappeared entirely); report the conflict generically rather than failing the response.
            return "terminal";
        }
    }

    private static List<KeyValuePair<string, string>> Flatten(IQueryCollection query)
    {
        var flattened = new List<KeyValuePair<string, string>>();
        foreach (var (key, values) in query)
        {
            foreach (var value in values)
            {
                if (value is not null)
                {
                    flattened.Add(new KeyValuePair<string, string>(key, value));
                }
            }
        }

        return flattened;
    }

    private static async Task<byte[]> ReadBodyAsync(HttpRequest request, CancellationToken cancellationToken)
    {
        if (request.ContentLength == 0)
        {
            return [];
        }

        using var buffer = new MemoryStream();
        await request.Body.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>
    /// Builds the <c>Content-Location</c> header value from the request's own scheme/host/path-base, the
    /// way <c>ExportEndpoints</c> does, rather than <see cref="IFhirRequestContext.BaseUri"/>: that
    /// resolver's canonical base is the deployment root in single-tenant mode regardless of which route
    /// form the request arrived on, which would drop the <c>/tenant/{t}</c> segment for a tenant-explicit
    /// kickoff. <paramref name="isTenantScopedRoute"/> instead reflects the literal route the handler is
    /// wired to, so the status URL always matches the form the caller used.
    /// </summary>
    internal static string BuildContentLocation(
        string scheme, string host, string pathBase, bool isTenantScopedRoute, int tenantId, string jobId)
    {
        var tenantSegment = isTenantScopedRoute ? $"/tenant/{tenantId}" : string.Empty;
        return $"{scheme}://{host}{pathBase}{tenantSegment}/_operations/bulk-delete/{jobId}";
    }

    private static IResult NotFoundOutcome(string diagnostics) =>
        OperationOutcomeResult(
            StatusCodes.Status404NotFound,
            FhirOperationOutcomeIssue.IssueSeverityCode.Error,
            FhirOperationOutcomeIssue.IssueTypeCommon.NotFound,
            diagnostics);

    private static IResult OperationOutcomeResult(
        int statusCode,
        FhirOperationOutcomeIssue.IssueSeverityCode severity,
        FhirOperationOutcomeIssue.IssueTypeCommon code,
        string diagnostics)
    {
        var outcome = new Ignixa.Models.OperationOutcome();
        outcome.Issue.Add(new FhirOperationOutcomeIssue
        {
            SeverityCode = severity,
            IssueTypeCode = code,
            Diagnostics = diagnostics,
        });

        return Results.Content(outcome.SerializeToString(), KnownContentTypes.ApplicationFhirJson, statusCode: statusCode);
    }
}
