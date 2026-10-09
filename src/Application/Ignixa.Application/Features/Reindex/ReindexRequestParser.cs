using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Application.Features.Reindex;

public sealed record ReindexRequestParameters(
    int? MaximumNumberOfResourcesPerQuery,
    int? MaximumNumberOfResourcesPerWrite,
    int? MaximumConcurrency,
    int? QueryDelayIntervalInMilliseconds,
    IReadOnlyList<string>? TargetResourceTypes);

public static class ReindexRequestParser
{
    public static bool TryParse(
        string? requestBody,
        out ReindexRequestParameters? request,
        out string? error)
    {
        request = null;
        error = null;
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            request = EmptyRequest();
            return true;
        }

        JsonObject? body;
        try
        {
            body = JsonNode.Parse(requestBody) as JsonObject;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = "Invalid request body. Expected a FHIR Parameters resource.";
            return false;
        }

        if (body is null)
        {
            request = EmptyRequest();
            return true;
        }

        if (
            body["resourceType"] is not JsonValue resourceType ||
            !resourceType.TryGetValue<string>(out var resourceTypeName) ||
            resourceTypeName != "Parameters")
        {
            error = "Expected a FHIR Parameters resource.";
            return false;
        }

        if (body["parameter"] is not null && body["parameter"] is not JsonArray)
        {
            error = "Parameters.parameter must be an array.";
            return false;
        }

        int? maximumNumberOfResourcesPerQuery = null;
        int? maximumNumberOfResourcesPerWrite = null;
        int? maximumConcurrency = null;
        int? queryDelayIntervalInMilliseconds = null;
        var targetResourceTypes = new List<string>();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in body["parameter"]?.AsArray() ?? [])
        {
            if (parameter is not JsonObject value ||
                value["name"] is not JsonValue nameValue ||
                !nameValue.TryGetValue<string>(out var name))
            {
                error = "Each Parameters.parameter must have a name.";
                return false;
            }

            if (!parameterNames.Add(name))
            {
                error = $"Parameter '{name}' must not be repeated.";
                return false;
            }

            switch (name)
            {
                case "maximumNumberOfResourcesPerQuery":
                    if (!TryGetInteger(value, out maximumNumberOfResourcesPerQuery))
                    {
                        error = "maximumNumberOfResourcesPerQuery must be an integer.";
                        return false;
                    }
                    break;
                case "maximumNumberOfResourcesPerWrite":
                    if (!TryGetInteger(value, out maximumNumberOfResourcesPerWrite))
                    {
                        error = "maximumNumberOfResourcesPerWrite must be an integer.";
                        return false;
                    }
                    break;
                case "maximumConcurrency":
                    if (!TryGetInteger(value, out maximumConcurrency))
                    {
                        error = "maximumConcurrency must be an integer.";
                        return false;
                    }
                    break;
                case "queryDelayIntervalInMilliseconds":
                    if (!TryGetInteger(value, out queryDelayIntervalInMilliseconds))
                    {
                        error = "queryDelayIntervalInMilliseconds must be an integer.";
                        return false;
                    }
                    break;
                case "targetResourceTypes":
                    if (value["valueString"] is not JsonValue resourceTypeValue ||
                        !resourceTypeValue.TryGetValue<string>(out var resourceTypes))
                    {
                        error = "targetResourceTypes must be a string.";
                        return false;
                    }
                    targetResourceTypes.AddRange(resourceTypes.Split(
                        ',',
                        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                    break;
                case "targetSearchParameterTypes":
                case "targetDataStoreUsagePercentage":
                    error = $"Parameter '{name}' is not supported.";
                    return false;
                default:
                    error = $"Unknown reindex parameter '{name}'.";
                    return false;
            }
        }

        request = new ReindexRequestParameters(
            maximumNumberOfResourcesPerQuery,
            maximumNumberOfResourcesPerWrite,
            maximumConcurrency,
            queryDelayIntervalInMilliseconds,
            targetResourceTypes.Count == 0 ? null : targetResourceTypes);
        return true;
    }

    private static ReindexRequestParameters EmptyRequest() =>
        new(null, null, null, null, null);

    private static bool TryGetInteger(JsonObject parameter, out int? value)
    {
        value = null;
        return parameter["valueInteger"] is JsonValue integer &&
            integer.TryGetValue<int>(out var parsed) &&
            (value = parsed) is not null;
    }
}
