using System.Text;
using System.Text.Json.Nodes;
using Ignixa.Api.Filters;
using Ignixa.Api.Http;
using Ignixa.Application.BackgroundOperations.Reindex;
using Ignixa.Models;
using Medino;
using Microsoft.AspNetCore.Mvc;

namespace Ignixa.Api.Endpoints;

public static class ReindexEndpoints
{
    private const int RecentTerminalJobLimit = 10;

    public static IEndpointRouteBuilder MapReindexEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var tenantEndpoints = endpoints.MapGroup("/tenant/{tenantId:int}")
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();

        tenantEndpoints.MapPost("/$reindex", CreateForTenantAsync).WithName("CreateReindexForTenant");
        tenantEndpoints.MapGet("/$reindex", ListForTenantAsync).WithName("ListReindexForTenant");
        tenantEndpoints.MapGet("/$reindex/{jobId}", GetForTenantAsync).WithName("GetReindexForTenant");
        tenantEndpoints.MapDelete("/$reindex/{jobId}", CancelForTenantAsync).WithName("CancelReindexForTenant");
        tenantEndpoints.MapGet("/OperationDefinition/reindex", GetOperationDefinition)
            .WithName("GetReindexOperationDefinitionForTenant");

        var systemEndpoints = endpoints.MapGroup(string.Empty)
            .AddEndpointFilter<FhirAuthorizationFilter>()
            .AddEndpointFilter<FhirAuditFilter>()
            .AddEndpointFilter<FhirMetricsFilter>();

        systemEndpoints.MapPost("/$reindex", CreateSystemAsync).WithName("CreateReindex");
        systemEndpoints.MapGet("/$reindex", ListSystemAsync).WithName("ListReindex");
        systemEndpoints.MapGet("/$reindex/{jobId}", GetSystemAsync).WithName("GetReindex");
        systemEndpoints.MapDelete("/$reindex/{jobId}", CancelSystemAsync).WithName("CancelReindex");
        systemEndpoints.MapGet("/OperationDefinition/reindex", GetOperationDefinition)
            .WithName("GetReindexOperationDefinition");

        return endpoints;
    }

    private static Task<IResult> CreateSystemAsync(
        HttpContext context,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ResolveTenantAsync(context, tenantId => CreateAsync(context, tenantId, mediator, cancellationToken));

    private static Task<IResult> ListSystemAsync(
        HttpContext context,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ResolveTenantAsync(context, _ => ListAsync(context, mediator, cancellationToken));

    private static Task<IResult> GetSystemAsync(
        HttpContext context,
        string jobId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ResolveTenantAsync(context, _ => GetAsync(context, jobId, mediator, cancellationToken));

    private static Task<IResult> CancelSystemAsync(
        HttpContext context,
        string jobId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ResolveTenantAsync(context, _ => CancelAsync(context, jobId, mediator, cancellationToken));

    private static Task<IResult> CreateForTenantAsync(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        CreateAsync(context, tenantId, mediator, cancellationToken);

    private static Task<IResult> ListForTenantAsync(
        HttpContext context,
        int tenantId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ValidateTenantAsync(tenantId, () => ListAsync(context, mediator, cancellationToken));

    private static Task<IResult> GetForTenantAsync(
        HttpContext context,
        int tenantId,
        string jobId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ValidateTenantAsync(tenantId, () => GetAsync(context, jobId, mediator, cancellationToken));

    private static Task<IResult> CancelForTenantAsync(
        HttpContext context,
        int tenantId,
        string jobId,
        [FromServices] IMediator mediator,
        CancellationToken cancellationToken) =>
        ValidateTenantAsync(tenantId, () => CancelAsync(context, jobId, mediator, cancellationToken));

    private static async Task<IResult> CreateAsync(
        HttpContext context,
        int tenantId,
        IMediator mediator,
        CancellationToken cancellationToken)
    {
        if (tenantId == 0)
        {
            return Error(StatusCodes.Status404NotFound, "Tenant 0 is reserved for system operations.");
        }

        if (context.Request.Headers.TryGetValue("Prefer", out var prefer) &&
            !prefer.All(value => string.Equals(value, "respond-async", StringComparison.OrdinalIgnoreCase)))
        {
            return Error(StatusCodes.Status400BadRequest, "Only Prefer: respond-async is supported.");
        }

        var commandResult = await ParseCommandAsync(context, cancellationToken);
        if (commandResult.Error is not null)
        {
            return Error(StatusCodes.Status400BadRequest, commandResult.Error);
        }

        var result = await mediator.SendAsync(commandResult.Command!, cancellationToken);
        switch (result)
        {
            case ReindexJobCreatedResult created:
            {
                var statusUrl = GetStatusUrl(context, created.JobId);
                var status = await mediator.SendAsync(
                    new GetReindexStatusQuery(created.JobId),
                    cancellationToken);
                context.Response.Headers["Content-Location"] = statusUrl;
                return FhirResponse(
                    StatusCodes.Status201Created,
                    BuildJobParameters(status ?? new ReindexStatusResult(
                        created.JobId,
                        "Queued",
                        DateTimeOffset.UtcNow,
                        null,
                        null,
                        DateTimeOffset.UtcNow,
                        false,
                        null,
                        null,
                        null)));
            }
            case ActiveReindexJobResult active:
                context.Response.Headers["Content-Location"] = GetStatusUrl(context, active.ActiveJobId);
                return Error(StatusCodes.Status409Conflict, "A reindex job is already active.");
            case InvalidReindexRequestResult invalid:
                return Error(StatusCodes.Status400BadRequest, invalid.ErrorMessage);
            case NoReindexWorkResult noWork:
                return Error(StatusCodes.Status400BadRequest, noWork.ErrorMessage);
            case ReindexProviderUnavailableResult unavailable:
                return Error(
                    StatusCodes.Status501NotImplemented,
                    $"Reindex is not supported by tenant provider {unavailable.TenantId}.");
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
            ["parameter"] = new JsonArray(jobs.Select(BuildJobPart).ToArray())
        };
        return FhirResponse(StatusCodes.Status200OK, parameters);
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
            : FhirResponse(StatusCodes.Status200OK, BuildJobParameters(status));
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
            ReindexCancelledResult => FhirResponse(
                StatusCodes.Status202Accepted,
                BuildCancellationParameters(jobId)),
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
        FhirResponse(StatusCodes.Status200OK, new JsonObject
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
                    "Delay between worker pages."),
                OperationParameter("targetResourceTypes", "string",
                    "Comma-separated concrete resource types to reindex.")
            }
        });

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

    private static async Task<(CreateReindexJobCommand? Command, string? Error)> ParseCommandAsync(
        HttpContext context,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentLength is 0)
        {
            return (new CreateReindexJobCommand(), null);
        }

        JsonObject? body;
        try
        {
            body = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: cancellationToken) as JsonObject;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException)
        {
            return (null, "Invalid request body. Expected a FHIR Parameters resource.");
        }

        if (body is null ||
            body["resourceType"] is not JsonValue resourceType ||
            !resourceType.TryGetValue<string>(out var resourceTypeName) ||
            resourceTypeName != "Parameters")
        {
            return (null, "Expected a FHIR Parameters resource.");
        }

        if (body["parameter"] is not null && body["parameter"] is not JsonArray)
        {
            return (null, "Parameters.parameter must be an array.");
        }

        int? maximumNumberOfResourcesPerQuery = null;
        int? maximumNumberOfResourcesPerWrite = null;
        int? maximumConcurrency = null;
        int? queryDelayIntervalInMilliseconds = null;
        var targetResourceTypes = new List<string>();
        foreach (var parameter in body["parameter"]?.AsArray() ?? [])
        {
            if (parameter is not JsonObject value ||
                value["name"] is not JsonValue nameValue ||
                !nameValue.TryGetValue<string>(out var name))
            {
                return (null, "Each Parameters.parameter must have a name.");
            }

            switch (name)
            {
                case "maximumNumberOfResourcesPerQuery":
                    if (!TryGetInteger(value, out maximumNumberOfResourcesPerQuery))
                    {
                        return (null, "maximumNumberOfResourcesPerQuery must be an integer.");
                    }
                    break;
                case "maximumNumberOfResourcesPerWrite":
                    if (!TryGetInteger(value, out maximumNumberOfResourcesPerWrite))
                    {
                        return (null, "maximumNumberOfResourcesPerWrite must be an integer.");
                    }
                    break;
                case "maximumConcurrency":
                    if (!TryGetInteger(value, out maximumConcurrency))
                    {
                        return (null, "maximumConcurrency must be an integer.");
                    }
                    break;
                case "queryDelayIntervalInMilliseconds":
                    if (!TryGetInteger(value, out queryDelayIntervalInMilliseconds))
                    {
                        return (null, "queryDelayIntervalInMilliseconds must be an integer.");
                    }
                    break;
                case "targetResourceTypes":
                    if (value["valueString"] is not JsonValue resourceTypeValue ||
                        !resourceTypeValue.TryGetValue<string>(out var resourceTypes))
                    {
                        return (null, "targetResourceTypes must be a string.");
                    }
                    targetResourceTypes.AddRange(resourceTypes.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "targetSearchParameterTypes":
                case "targetDataStoreUsagePercentage":
                    return (null, $"Parameter '{name}' is not supported.");
                default:
                    return (null, $"Unknown reindex parameter '{name}'.");
            }
        }

        return (new CreateReindexJobCommand
        {
            MaximumNumberOfResourcesPerQuery = maximumNumberOfResourcesPerQuery,
            MaximumNumberOfResourcesPerWrite = maximumNumberOfResourcesPerWrite,
            MaximumConcurrency = maximumConcurrency,
            QueryDelayIntervalInMilliseconds = queryDelayIntervalInMilliseconds,
            TargetResourceTypes = targetResourceTypes.Count == 0 ? null : targetResourceTypes
        }, null);
    }

    private static bool TryGetInteger(JsonObject parameter, out int? value)
    {
        value = null;
        return parameter["valueInteger"] is JsonValue integer &&
            integer.TryGetValue<int>(out var parsed) &&
            (value = parsed) is not null;
    }

    private static JsonObject BuildJobParameters(ReindexStatusResult status)
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
        AddProgressValue(values, progress, "totalResourcesToReindex", "valueInteger");
        AddProgressValue(values, progress, "resourcesSuccessfullyReindexed", "valueInteger");
        AddProgressValue(values, progress, "progress", "valueDecimal", status.Status);
        AddProgressValue(values, progress, "phase", "valueString");
        AddProgressValue(values, progress, "cancellationReason", "valueString");
        AddProgressValue(values, progress, "conflicts", "valueInteger");
        AddValue(values, "failureDetails", "valueString", status.ErrorMessage);

        if (status.Definition is { } definition)
        {
            AddValue(values, "maximumNumberOfResourcesPerQuery", "valueInteger", definition.MaximumNumberOfResourcesPerQuery);
            AddValue(values, "maximumNumberOfResourcesPerWrite", "valueInteger", definition.MaximumNumberOfResourcesPerWrite);
            AddValue(values, "maximumConcurrency", "valueInteger", definition.MaximumConcurrency);
            AddValue(values, "queryDelayIntervalInMilliseconds", "valueInteger", definition.QueryDelayIntervalInMilliseconds);
            AddValue(values, "trigger", "valueString", definition.Trigger);
            AddValue(values, "targetEventId", "valueInteger", definition.TargetEventId);
            foreach (var resourceType in definition.ResourceTypes)
            {
                AddValue(values, "resources", "valueString", resourceType);
            }
            foreach (var searchParameter in definition.SearchParameters)
            {
                AddValue(values, "searchParams", "valueString", searchParameter.Canonical);
            }
        }

        AddStringArray(values, "notCovered", progress?["notCovered"] as JsonArray);
        AddStringArray(values, "ignoredLifecycleEvents", progress?["ignoredLifecycleEvents"] as JsonArray);
        AddTenantParts(values, progress?["tenants"] as JsonArray);
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

    private static JsonObject BuildJobPart(ReindexStatusResult status)
    {
        var job = BuildJobParameters(status);
        return new JsonObject
        {
            ["name"] = "job",
            ["part"] = job["parameter"]!.DeepClone()
        };
    }

    private static void AddTenantParts(JsonArray parameters, JsonArray? tenants)
    {
        foreach (var tenant in tenants?.OfType<JsonObject>() ?? [])
        {
            var parts = new JsonArray();
            AddJsonValue(parts, "tenantId", "valueInteger", tenant["tenantId"]);
            AddJsonValue(parts, "cutoffTransactionId", "valueInteger", tenant["cutoffTransactionId"]);
            AddJsonValue(parts, "cutoffSurrogateId", "valueInteger", tenant["cutoffSurrogateId"]);
            AddJsonValue(parts, "status", "valueString", tenant["status"]);
            AddJsonValue(parts, "resourcesToReindex", "valueInteger", tenant["resourcesToReindex"]);
            AddJsonValue(parts, "resourcesReindexed", "valueInteger", tenant["resourcesReindexed"]);
            AddJsonValue(parts, "conflicts", "valueInteger", tenant["conflicts"]);
            AddJsonValue(parts, "failedResources", "valueInteger", tenant["failedResources"]);
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

    private static IResult FhirResponse(int statusCode, JsonObject body) =>
        new FhirResult(statusCode, Encoding.UTF8.GetBytes(body.ToJsonString()));

    private static IResult Error(int statusCode, string diagnostics) =>
        FhirResponse(statusCode, new JsonObject
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
        });

    private static Task<IResult> ResolveTenantAsync(HttpContext context, Func<int, Task<IResult>> next) =>
        context.Items.TryGetValue("TenantId", out var tenant) && tenant is int tenantId
            ? ValidateTenantAsync(tenantId, () => next(tenantId))
            : Task.FromResult<IResult>(Error(
                StatusCodes.Status400BadRequest,
                "Unable to determine tenant from request context."));

    private static Task<IResult> ValidateTenantAsync(int tenantId, Func<Task<IResult>> next) =>
        tenantId == 0
            ? Task.FromResult<IResult>(Error(StatusCodes.Status404NotFound, "Tenant 0 is reserved for system operations."))
            : next();

    private static string GetStatusUrl(HttpContext context, string jobId) =>
        $"{context.Request.PathBase}{context.Request.Path}/{jobId}";
}
