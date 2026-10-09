using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Application.Features.Conformance;
using Ignixa.Application.Features.Reindex;
using Ignixa.Domain.Models;
using Ignixa.Models;
using Ignixa.Serialization;
using Medino;
using Microsoft.AspNetCore.Mvc;
using FhirInteraction = Ignixa.Application.Features.Authorization.Models.FhirInteraction;

namespace Ignixa.Api.Endpoints;

public static class ReindexEndpoints
{
    private const int RecentTerminalJobLimit = 10;

    public static IEndpointRouteBuilder MapReindexEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var reindexAuthorization = new FhirAuthorizationMetadata(FhirInteraction.Update, "*");
        var tenantEndpoints = endpoints.MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();

        tenantEndpoints.MapGet("/OperationDefinition/reindex", GetOperationDefinition)
            .WithName("GetReindexOperationDefinitionForTenant");

        var systemEndpoints = endpoints.MapGroup(string.Empty)
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();

        systemEndpoints.MapGet("/OperationDefinition/reindex", GetOperationDefinition)
            .WithName("GetReindexOperationDefinition");

        var tenantReindexEndpoints = tenantEndpoints.MapGroup(string.Empty)
            .WithMetadata(reindexAuthorization);
        tenantReindexEndpoints.MapPost("/$reindex", CreateAsync).WithName("CreateReindexForTenant");
        tenantReindexEndpoints.MapGet("/$reindex", ListAsync).WithName("ListReindexForTenant");
        tenantReindexEndpoints.MapGet("/$reindex/{jobId}", GetAsync).WithName("GetReindexForTenant");
        tenantReindexEndpoints.MapDelete("/$reindex/{jobId}", CancelAsync).WithName("CancelReindexForTenant");

        var systemReindexEndpoints = systemEndpoints.MapGroup(string.Empty)
            .WithMetadata(reindexAuthorization);
        systemReindexEndpoints.MapPost("/$reindex", CreateAsync).WithName("CreateReindex");
        systemReindexEndpoints.MapGet("/$reindex", ListAsync).WithName("ListReindex");
        systemReindexEndpoints.MapGet("/$reindex/{jobId}", GetAsync).WithName("GetReindex");
        systemReindexEndpoints.MapDelete("/$reindex/{jobId}", CancelAsync).WithName("CancelReindex");

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        int? tenantId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        return await ResolveTenantAsync(
            context,
            tenantId,
            resolvedTenantId => CreateJobAsync(context, resolvedTenantId, mediator, cancellationToken));
    }

    private static async Task<IResult> CreateJobAsync(
        HttpContext context,
        int tenantId,
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
        if (!ReindexRequestParser.TryParse(requestBody, out var request, out var error))
        {
            return Error(StatusCodes.Status400BadRequest, error!);
        }

        var result = await mediator.SendAsync(new CreateReindexJobCommand
        {
            MaximumNumberOfResourcesPerQuery = request!.MaximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = request.MaximumNumberOfResourcesPerWrite,
            MaximumConcurrency = request.MaximumConcurrency,
            QueryDelayIntervalInMilliseconds = request.QueryDelayIntervalInMilliseconds
        }, cancellationToken);
        switch (result)
        {
            case ReindexJobCreatedResult created:
            {
                var statusUrl = GetStatusUrl(context, created.JobId);
                var status = await mediator.SendAsync(
                    new GetReindexStatusQuery(created.JobId),
                    cancellationToken);
                context.Response.Headers["Content-Location"] = statusUrl;
                return new FhirResult(
                    StatusCodes.Status201Created,
                    Encoding.UTF8.GetBytes(BuildJobParameters(status ?? new ReindexStatusResult(
                        created.JobId,
                        "Queued",
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        false,
                        null,
                        null,
                        null),
                        GetFhirVersion(context)).ToJsonString()));
            }
            case ActiveReindexJobResult active:
                context.Response.Headers["Content-Location"] = GetStatusUrl(context, active.ActiveJobId);
                return Error(StatusCodes.Status409Conflict, "A reindex job is already active.");
            case InvalidReindexRequestResult invalid:
                return Error(StatusCodes.Status400BadRequest, invalid.ErrorMessage);
            case NoReindexWorkResult noWork:
                return Error(StatusCodes.Status400BadRequest, noWork.ErrorMessage);
            case ReindexDisabledResult:
                return Disabled();
            case ReindexProviderUnavailableResult:
                return Unsupported();
            default:
                throw new InvalidOperationException($"Unhandled reindex result {result.GetType().Name}.");
        }
    }

    private static Task<IResult> ListAsync(
        HttpContext context,
        int? tenantId,
        IMediator mediator,
        IReindexAvailability availability,
        CancellationToken cancellationToken) =>
        ExecuteWhenAvailableAsync(
            availability,
            () => ResolveTenantAsync(
                context,
                tenantId,
                _ => ListJobsAsync(context, mediator, cancellationToken)),
            cancellationToken);

    private static async Task<IResult> ListJobsAsync(
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

    private static Task<IResult> GetAsync(
        HttpContext context,
        int? tenantId,
        string jobId,
        IMediator mediator,
        IReindexAvailability availability,
        CancellationToken cancellationToken) =>
        ExecuteWhenAvailableAsync(
            availability,
            () => ResolveTenantAsync(
                context,
                tenantId,
                _ => GetJobAsync(context, jobId, mediator, cancellationToken)),
            cancellationToken);

    private static async Task<IResult> GetJobAsync(
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

    private static Task<IResult> CancelAsync(
        HttpContext context,
        int? tenantId,
        string jobId,
        IMediator mediator,
        IReindexAvailability availability,
        CancellationToken cancellationToken) =>
        ExecuteWhenAvailableAsync(
            availability,
            () => ResolveTenantAsync(
                context,
                tenantId,
                _ => CancelJobAsync(context, jobId, mediator, cancellationToken)),
            cancellationToken);

    private static async Task<IResult> CancelJobAsync(
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
        AddValue(values, "status", "valueString", status.Status);
        AddValue(values, "queuedTime", "valueDateTime", status.QueuedTime);
        AddValue(values, "startTime", "valueDateTime", status.StartTime);
        AddValue(values, "endTime", "valueDateTime", status.EndTime);
        AddValue(values, "lastModified", "valueDateTime", status.LastModified);

        var progress = status.Progress as JsonObject;
        AddProgressValue(values, progress, "totalResourcesToReindex", CountValueName(fhirVersion));
        AddProgressValue(values, progress, "resourcesSuccessfullyReindexed", CountValueName(fhirVersion));
        AddProgressValue(values, progress, "progress", "valueDecimal", status.Status);
        AddProgressValue(values, progress, "phase", "valueString");
        AddProgressValue(values, progress, "cancellationReason", "valueString");
        AddProgressValue(values, progress, "conflicts", CountValueName(fhirVersion));
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

        AddStringArray(values, "ignoredLifecycleEvents", progress?["ignoredLifecycleEvents"] as JsonArray);
        AddTenantParts(values, progress?["tenants"] as JsonArray, fhirVersion);
        AddFailedResources(values, progress?["failedResources"] as JsonArray);
        AddFailedResources(values, status.Result?["failedResources"] as JsonArray);
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
        AddValue(parameters["parameter"]!.AsArray(), "status", "valueString", "Cancelled");
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

    private static void AddTenantParts(JsonArray parameters, JsonArray? tenants, FhirVersion fhirVersion)
    {
        foreach (var tenant in tenants?.OfType<JsonObject>() ?? [])
        {
            var parts = new JsonArray();
            AddJsonValue(parts, "tenantId", "valueInteger", tenant["tenantId"]);
            AddIdentifierJsonValue(parts, "cutoffTransactionId", tenant["cutoffTransactionId"], fhirVersion);
            AddIdentifierJsonValue(parts, "cutoffSurrogateId", tenant["cutoffSurrogateId"], fhirVersion);
            AddJsonValue(parts, "status", "valueString", tenant["status"]);
            AddJsonValue(parts, "resourcesToReindex", CountValueName(fhirVersion), tenant["resourcesToReindex"]);
            AddJsonValue(parts, "resourcesReindexed", CountValueName(fhirVersion), tenant["resourcesReindexed"]);
            AddJsonValue(parts, "conflicts", CountValueName(fhirVersion), tenant["conflicts"]);
            AddJsonValue(parts, "failedResources", CountValueName(fhirVersion), tenant["failedResources"]);
            parameters.Add(new JsonObject { ["name"] = "tenant", ["part"] = parts });
        }
    }

    private static void AddFailedResources(JsonArray parameters, JsonArray? failures)
    {
        foreach (var failure in failures?.OfType<JsonObject>().Take(100) ?? [])
        {
            var parts = new JsonArray();
            AddJsonValue(parts, "resourceType", "valueString", failure["resourceType"]);
            AddJsonValue(parts, "id", "valueString", failure["id"]);
            AddJsonValue(parts, "reason", "valueString", failure["reason"] ?? failure["errorMessage"]);
            parameters.Add(new JsonObject { ["name"] = "failedResource", ["part"] = parts });
        }
    }

    private static void AddStringArray(JsonArray parameters, string name, JsonArray? values)
    {
        foreach (var value in values?.OfType<JsonValue>() ?? [])
        {
            if (value.TryGetValue<string>(out var stringValue))
            {
                AddValue(parameters, name, "valueString", stringValue);
            }
        }
    }

    private static void AddProgressValue(
        JsonArray parameters,
        JsonObject? progress,
        string name,
        string valueName,
        string? status = null)
    {
        var value = progress?[name];
        if (name == "progress" && status is not "Completed" &&
            value is JsonValue progressValue &&
            progressValue.TryGetValue<double>(out var progressPercent))
        {
            value = JsonValue.Create(Math.Min(99.9, progressPercent));
        }
        AddJsonValue(parameters, name, valueName, value);
    }

    private static void AddJsonValue(JsonArray parameters, string name, string valueName, JsonNode? value)
    {
        if (value is not null)
        {
            parameters.Add(new JsonObject { ["name"] = name, [valueName] = value.DeepClone() });
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
        long value,
        FhirVersion fhirVersion) =>
        AddValue(
            parameters,
            name,
            IdentifierValueName(fhirVersion),
            fhirVersion == FhirVersion.R5
                ? value
                : value.ToString(System.Globalization.CultureInfo.InvariantCulture));

    private static void AddIdentifierJsonValue(
        JsonArray parameters,
        string name,
        JsonNode? value,
        FhirVersion fhirVersion)
    {
        if (value is not JsonValue jsonValue ||
            !jsonValue.TryGetValue<long>(out var identifier))
        {
            return;
        }

        AddIdentifierValue(parameters, name, identifier, fhirVersion);
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

    private static async Task<IResult> ExecuteWhenAvailableAsync(
        IReindexAvailability availability,
        Func<Task<IResult>> next,
        CancellationToken cancellationToken)
    {
        var result = await availability.GetAvailabilityAsync(cancellationToken);
        return result.Status switch
        {
            ReindexAvailabilityStatus.Available => await next(),
            ReindexAvailabilityStatus.Disabled => Disabled(),
            ReindexAvailabilityStatus.Unsupported => Unsupported(),
            _ => throw new InvalidOperationException($"Unknown reindex availability {result.Status}.")
        };
    }

    private static IResult Disabled() =>
        Error(StatusCodes.Status404NotFound, "The $reindex operation is disabled on this server.");

    private static IResult Unsupported() =>
        Error(
            StatusCodes.Status501NotImplemented,
            "The $reindex operation is not supported by all active tenant providers.");

    private static Task<IResult> ResolveTenantAsync(
        HttpContext context,
        int? routeTenantId,
        Func<int, Task<IResult>> next) =>
        routeTenantId is int tenantId
            ? ValidateTenantAsync(tenantId, () => next(tenantId))
            : context.Items.TryGetValue("TenantId", out var tenant) && tenant is int resolvedTenantId
            ? ValidateTenantAsync(resolvedTenantId, () => next(resolvedTenantId))
            : Task.FromResult<IResult>(Error(
                StatusCodes.Status400BadRequest,
                "Unable to determine tenant from request context."));

    private static Task<IResult> ValidateTenantAsync(int tenantId, Func<Task<IResult>> next) =>
        tenantId == 0
            ? Task.FromResult<IResult>(Error(StatusCodes.Status404NotFound, "Tenant 0 is reserved for system operations."))
            : next();

    private static FhirVersion GetFhirVersion(HttpContext context) =>
        context.Items["TenantConfiguration"] is TenantConfiguration tenantConfiguration
            ? FhirSpecificationExtensions.FromVersionString(tenantConfiguration.FhirVersion)
            : FhirVersion.R4;

    private static string GetStatusUrl(HttpContext context, string jobId) =>
        $"{context.Request.PathBase}{context.Request.Path}/{jobId}";
}
