using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Parses a <c>POST $reindex</c> body (an optional FHIR <c>Parameters</c>) into a manual
/// <see cref="CreateReindexJobCommand"/>. Ranges are not checked here; <see cref="ReindexJobParameters.Create"/>
/// owns them.
/// </summary>
public static class ReindexRequestParser
{
    private static readonly Dictionary<string, Func<CreateReindexJobCommand, int, CreateReindexJobCommand>> IntegerParameters =
        new(StringComparer.Ordinal)
        {
            ["maximumNumberOfResourcesPerQuery"] = (command, value) => command with { MaximumNumberOfResourcesPerQuery = value },
            ["maximumNumberOfResourcesPerWrite"] = (command, value) => command with { MaximumNumberOfResourcesPerWrite = value },
            ["maximumConcurrency"] = (command, value) => command with { MaximumConcurrency = value },
            ["queryDelayIntervalInMilliseconds"] = (command, value) => command with { QueryDelayIntervalInMilliseconds = value }
        };

    private static readonly HashSet<string> UnsupportedParameters =
        new(["targetResourceTypes", "targetSearchParameterTypes", "targetDataStoreUsagePercentage"], StringComparer.Ordinal);

    public static bool TryParse(
        string? requestBody,
        [NotNullWhen(true)] out CreateReindexJobCommand? command,
        [NotNullWhen(false)] out string? error)
    {
        command = null;
        error = null;
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            command = new CreateReindexJobCommand();
            return true;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(requestBody);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            error = "Invalid request body. Expected a FHIR Parameters resource.";
            return false;
        }

        if (node is not JsonObject body ||
            body["resourceType"] is not JsonValue resourceType ||
            !resourceType.TryGetValue<string>(out var resourceTypeName) ||
            resourceTypeName != "Parameters")
        {
            error = "Expected a FHIR Parameters resource.";
            return false;
        }

        if (body["parameter"] is not null and not JsonArray)
        {
            error = "Parameters.parameter must be an array.";
            return false;
        }

        var parsed = new CreateReindexJobCommand();
        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in body["parameter"]?.AsArray() ?? [])
        {
            error = TryApply(parameter, parameterNames, ref parsed);
            if (error is not null)
            {
                return false;
            }
        }

        command = parsed;
        return true;
    }

    private static string? TryApply(JsonNode? parameter, HashSet<string> parameterNames, ref CreateReindexJobCommand command)
    {
        if (parameter is not JsonObject value ||
            value["name"] is not JsonValue nameValue ||
            !nameValue.TryGetValue<string>(out var name))
        {
            return "Each Parameters.parameter must have a name.";
        }

        if (!parameterNames.Add(name))
        {
            return $"Parameter '{name}' must not be repeated.";
        }

        if (UnsupportedParameters.Contains(name))
        {
            return $"Parameter '{name}' is not supported.";
        }

        if (!IntegerParameters.TryGetValue(name, out var apply))
        {
            return $"Unknown reindex parameter '{name}'.";
        }

        if (value["valueInteger"] is not JsonValue integer || !integer.TryGetValue<int>(out var parsed))
        {
            return $"{name} must be an integer.";
        }

        command = apply(command, parsed);
        return null;
    }
}
