using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.BackgroundOperations.Reindex.Models;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Infrastructure;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization;
using Medino;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using FhirInteraction = Ignixa.Application.Features.Authorization.Models.FhirInteraction;

namespace Ignixa.Api.Endpoints;

public static class ReindexEndpoints
{
    private const int RecentTerminalJobLimit = 10;

    public static IEndpointRouteBuilder MapReindexEndpoints(this IEndpointRouteBuilder endpoints)
    {
        MapRoutes(endpoints.MapGroup("/tenant/{tenantId:int}"), "ForTenant");
        MapRoutes(endpoints.MapGroup(string.Empty), string.Empty);
        return endpoints;
    }

    private static void MapRoutes(RouteGroupBuilder group, string nameSuffix)
    {
        group.AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();
        group.MapGet("/OperationDefinition/reindex", GetOperationDefinition)
            .WithName($"GetReindexOperationDefinition{nameSuffix}");

        var reindex = group.MapGroup(string.Empty)
            .WithMetadata(new FhirAuthorizationMetadata(FhirInteraction.Update, "*"))
            .AddEndpointFilter(RequireReindexAvailableAsync);
        reindex.MapPost("/$reindex", CreateAsync).WithName($"CreateReindex{nameSuffix}");
        reindex.MapGet("/$reindex", ListAsync).WithName($"ListReindex{nameSuffix}");
        reindex.MapGet("/$reindex/{jobId}", GetAsync).WithName($"GetReindex{nameSuffix}");
        reindex.MapDelete("/$reindex/{jobId}", CancelAsync).WithName($"CancelReindex{nameSuffix}");
    }

    // Tenant routing, including the reserved partition 0, is enforced by TenantResolutionMiddleware before
    // these handlers run; a job is server-wide, so no handler needs the tenant id.
    private static async Task<IResult> CreateAsync(
        HttpContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (context.Request.Headers.TryGetValue("Prefer", out var prefer) &&
            !prefer.All(value => string.Equals(value, "respond-async", StringComparison.OrdinalIgnoreCase)))
        {
            return Error(StatusCodes.Status400BadRequest, "Only Prefer: respond-async is supported.");
        }

        using var reader = new StreamReader(context.Request.Body, leaveOpen: true);
        var requestBody = await reader.ReadToEndAsync(cancellationToken);
        if (!ReindexRequestParser.TryParse(requestBody, out var command, out var error))
        {
            return Error(StatusCodes.Status400BadRequest, error);
        }

        var result = await mediator.SendAsync(command, cancellationToken);
        switch (result)
        {
            case ReindexJobCreatedResult created:
            {
                var statusUrl = GetStatusUrl(context, created.JobId);
                var status = await mediator.SendAsync(
                    new GetReindexStatusQuery(created.JobId),
                    cancellationToken)
                    ?? throw new InvalidOperationException($"Reindex job {created.JobId} was created but cannot be read.");
                context.Response.Headers["Content-Location"] = statusUrl;
                return new FhirResult(
                    StatusCodes.Status201Created,
                    Encoding.UTF8.GetBytes(BuildJobParameters(status, GetFhirVersion(context)).ToJsonString()));
            }
            case ActiveReindexJobResult active:
                context.Response.Headers["Content-Location"] = GetStatusUrl(context, active.ActiveJobId);
                return Error(StatusCodes.Status409Conflict, "A reindex job is already active.");
            case InvalidReindexRequestResult invalid:
                return Error(StatusCodes.Status400BadRequest, invalid.ErrorMessage);
            case NoReindexWorkResult noWork:
                return Error(StatusCodes.Status400BadRequest, noWork.ErrorMessage);
            default:
                throw new InvalidOperationException($"Unhandled reindex result {result.GetType().Name}.");
        }
    }

    private static async Task<IResult> ListAsync(
        HttpContext context,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var jobs = await mediator.SendAsync(
            new GetReindexJobsQuery(RecentTerminalJobLimit),
            cancellationToken);
        var parameters = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray(jobs.Select(job => BuildJobPart(job, GetFhirVersion(context))).ToArray())
        };
        return FhirResults.Ok(Encoding.UTF8.GetBytes(parameters.ToJsonString()));
    }

    private static async Task<IResult> GetAsync(
        HttpContext context,
        string jobId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var status = await mediator.SendAsync(new GetReindexStatusQuery(jobId), cancellationToken);
        return status is null
            ? Error(StatusCodes.Status404NotFound, $"Reindex job '{jobId}' was not found.")
            : FhirResults.Ok(Encoding.UTF8.GetBytes(
                BuildJobParameters(status, GetFhirVersion(context)).ToJsonString()));
    }

    private static async Task<IResult> CancelAsync(
        HttpContext context,
        string jobId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        var result = await mediator.SendAsync(new CancelReindexCommand(jobId), cancellationToken);
        return result switch
        {
            ReindexCancelledResult => new FhirResult(
                StatusCodes.Status202Accepted,
                Encoding.UTF8.GetBytes(BuildCancellationParameters(jobId).ToJsonString())),
            ReindexJobNotFoundResult => Error(
                StatusCodes.Status404NotFound,
                $"Reindex job '{jobId}' was not found."),
            ReindexJobAlreadyTerminalResult terminal => Error(
                StatusCodes.Status409Conflict,
                $"Reindex job '{terminal.JobId}' is already terminal with status '{terminal.Status}'."),
            _ => throw new InvalidOperationException($"Unhandled cancellation result {result.GetType().Name}.")
        };
    }

    private static IResult GetOperationDefinition() =>
        FhirResults.Ok(Encoding.UTF8.GetBytes(new JsonObject
        {
            ["resourceType"] = "OperationDefinition",
            ["id"] = "reindex",
            ["url"] = "http://hl7.org/fhir/OperationDefinition/reindex",
            ["version"] = "1.0",
            ["name"] = "Reindex",
            ["status"] = "active",
            ["kind"] = "operation",
            ["code"] = "reindex",
            ["system"] = true,
            ["type"] = false,
            ["instance"] = false,
            ["parameter"] = new JsonArray
            {
                OperationParameter("maximumNumberOfResourcesPerQuery", "integer",
                    "Maximum resources in each range query."),
                OperationParameter("maximumNumberOfResourcesPerWrite", "integer",
                    "Maximum resources in each index write batch."),
                OperationParameter("maximumConcurrency", "integer",
                    "Maximum concurrent range workers per tenant."),
                OperationParameter("queryDelayIntervalInMilliseconds", "integer",
                    "Delay between worker pages.")
            }
        }.ToJsonString()));

    private static JsonObject OperationParameter(string name, string type, string documentation) =>
        new()
        {
            ["name"] = name,
            ["use"] = "in",
            ["min"] = 0,
            ["max"] = "1",
            ["type"] = type,
            ["documentation"] = documentation
        };

    private static JsonObject BuildJobParameters(ReindexStatusResult status, FhirVersion fhirVersion)
    {
        var parameters = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray()
        };
        var values = parameters["parameter"]!.AsArray();
        AddValue(values, "id", "valueString", status.JobId);
        AddValue(values, "status", "valueString", status.Status.ToString());
        AddValue(values, "queuedTime", "valueDateTime", status.QueuedTime);
        AddValue(values, "startTime", "valueDateTime", status.StartTime);
        AddValue(values, "endTime", "valueDateTime", status.EndTime);
        AddValue(values, "lastModified", "valueDateTime", status.LastModified);
        AddValue(values, "stale", "valueBoolean", status.IsStale ? true : null);

        if (status.Progress is { } progress)
        {
            AddValue(values, "totalResourcesToReindex", CountValueName(fhirVersion), progress.TotalResourcesToReindex);
            AddValue(values, "resourcesSuccessfullyReindexed", CountValueName(fhirVersion), progress.ResourcesSuccessfullyReindexed);
            AddValue(values, "progress", "valueDecimal", progress.PercentComplete(status.Status));
            AddValue(values, "phase", "valueString", progress.Phase.ToString());
            AddValue(values, "cancellationReason", "valueString", progress.CancellationReason);
            AddValue(values, "conflicts", CountValueName(fhirVersion), progress.Conflicts);
        }

        AddValue(values, "failureDetails", "valueString", status.ErrorMessage);

        if (status.Definition is { } definition)
        {
            AddValue(values, "maximumNumberOfResourcesPerQuery", "valueInteger", definition.MaximumNumberOfResourcesPerQuery);
            AddValue(values, "maximumNumberOfResourcesPerWrite", "valueInteger", definition.MaximumNumberOfResourcesPerWrite);
            AddValue(values, "maximumConcurrency", "valueInteger", definition.MaximumConcurrency);
            AddValue(values, "queryDelayIntervalInMilliseconds", "valueInteger", definition.QueryDelayIntervalInMilliseconds);
            AddValue(values, "trigger", "valueString", definition.Trigger);
            AddIdentifierValue(values, "targetEventId", definition.TargetEventId, fhirVersion);
            foreach (var resourceType in definition.ResourceTypes)
            {
                AddValue(values, "resources", "valueString", resourceType);
            }
            foreach (var searchParameter in definition.SearchParameters)
            {
                AddValue(values, "searchParams", "valueString", searchParameter.Canonical);
            }
        }

        foreach (var ignored in status.Progress?.IgnoredLifecycleEvents ?? [])
        {
            AddValue(values, "ignoredLifecycleEvents", "valueString", ignored);
        }

        AddTenantParts(values, status.Progress?.Tenants ?? [], fhirVersion);
        AddFailedResources(values, status.Progress?.FailedResources ?? []);
        return parameters;
    }

    private static JsonObject BuildCancellationParameters(string jobId)
    {
        var parameters = new JsonObject
        {
            ["resourceType"] = "Parameters",
            ["parameter"] = new JsonArray()
        };
        AddValue(parameters["parameter"]!.AsArray(), "id", "valueString", jobId);
        AddValue(parameters["parameter"]!.AsArray(), "status", "valueString", ReindexJobStatus.Cancelled.ToString());
        return parameters;
    }

    private static JsonObject BuildJobPart(ReindexStatusResult status, FhirVersion fhirVersion)
    {
        var job = BuildJobParameters(status, fhirVersion);
        return new JsonObject
        {
            ["name"] = "job",
            ["part"] = job["parameter"]!.DeepClone()
        };
    }

    private static void AddTenantParts(
        JsonArray parameters,
        IReadOnlyList<ReindexTenantProgress> tenants,
        FhirVersion fhirVersion)
    {
        foreach (var tenant in tenants)
        {
            var parts = new JsonArray();
            AddValue(parts, "tenantId", "valueInteger", tenant.TenantId);
            AddIdentifierValue(parts, "cutoffTransactionId", tenant.CutoffTransactionId, fhirVersion);
            AddIdentifierValue(parts, "cutoffSurrogateId", tenant.CutoffSurrogateId, fhirVersion);
            AddValue(parts, "status", "valueString", tenant.Status.ToString());
            AddValue(parts, "resourcesToReindex", CountValueName(fhirVersion), tenant.ResourcesToReindex);
            AddValue(parts, "resourcesReindexed", CountValueName(fhirVersion), tenant.ResourcesReindexed);
            AddValue(parts, "conflicts", CountValueName(fhirVersion), tenant.Conflicts);
            AddValue(parts, "failedResources", CountValueName(fhirVersion), tenant.FailedResourceCount);
            parameters.Add(new JsonObject { ["name"] = "tenant", ["part"] = parts });
        }
    }

    private static void AddFailedResources(JsonArray parameters, IReadOnlyList<ReindexFailedResource> failures)
    {
        foreach (var failure in failures)
        {
            var parts = new JsonArray();
            AddValue(parts, "resourceType", "valueString", failure.ResourceType);
            AddValue(parts, "id", "valueString", failure.Id);
            AddValue(parts, "reason", "valueString", failure.Reason);
            parameters.Add(new JsonObject { ["name"] = "failedResource", ["part"] = parts });
        }
    }

    private static void AddValue(JsonArray parameters, string name, string valueName, object? value)
    {
        if (value is not null)
        {
            parameters.Add(new JsonObject { ["name"] = name, [valueName] = JsonValue.Create(value) });
        }
    }

    private static string CountValueName(FhirVersion fhirVersion) =>
        fhirVersion == FhirVersion.R5 ? "valueInteger64" : "valueDecimal";

    private static string IdentifierValueName(FhirVersion fhirVersion) =>
        fhirVersion == FhirVersion.R5 ? "valueInteger64" : "valueString";

    private static void AddIdentifierValue(
        JsonArray parameters,
        string name,
        long? value,
        FhirVersion fhirVersion)
    {
        if (value is { } identifier)
        {
            AddValue(
                parameters,
                name,
                IdentifierValueName(fhirVersion),
                fhirVersion == FhirVersion.R5
                    ? identifier
                    : identifier.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    private static IResult Error(int statusCode, string diagnostics) =>
        new FhirResult(statusCode, Encoding.UTF8.GetBytes(new JsonObject
        {
            ["resourceType"] = "OperationOutcome",
            ["issue"] = new JsonArray
            {
                new JsonObject
                {
                    ["severity"] = "error",
                    ["code"] = "invalid",
                    ["diagnostics"] = diagnostics
                }
            }
        }.ToJsonString()));

    /// <summary>
    /// Answers every operational $reindex route with 404 while Reindex:Enabled is false, and with 501 while
    /// any active tenant's storage provider cannot reindex. Evaluated per request, so a tenant added at
    /// runtime is reflected at once.
    /// </summary>
    private static async ValueTask<object?> RequireReindexAvailableAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var services = context.HttpContext.RequestServices;
        if (!services.GetRequiredService<IOptions<ReindexOptions>>().Value.Enabled)
        {
            return Disabled();
        }

        var unsupportedTenantId = await services.GetRequiredService<CompositeRepositoryFactory>()
            .FindTenantWithoutReindexSupportAsync(context.HttpContext.RequestAborted);
        return unsupportedTenantId is null ? await next(context) : Unsupported();
    }

    private static IResult Disabled() =>
        Error(StatusCodes.Status404NotFound, "The $reindex operation is disabled on this server.");

    private static IResult Unsupported() =>
        Error(
            StatusCodes.Status501NotImplemented,
            "The $reindex operation is not supported by all active tenant providers.");

    private static FhirVersion GetFhirVersion(HttpContext context) =>
        context.Items["TenantConfiguration"] is TenantConfiguration tenantConfiguration
            ? FhirSpecificationExtensions.FromVersionString(tenantConfiguration.FhirVersion)
            : FhirVersion.R4;

    private static string GetStatusUrl(HttpContext context, string jobId) =>
        $"{context.Request.PathBase}{context.Request.Path}/{jobId}";
}
