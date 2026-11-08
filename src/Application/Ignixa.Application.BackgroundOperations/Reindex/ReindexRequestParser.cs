using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Domain.Exceptions;

namespace Ignixa.Application.BackgroundOperations.Reindex;

/// <summary>
/// Parses a <c>POST $reindex</c> body (an optional FHIR <c>Parameters</c>) into a manual
/// <see cref="CreateReindexJobCommand"/>. Ranges are not checked here; <see cref="ReindexJobParameters.Create"/>
/// owns them.
/// </summary>
public static class ReindexRequestParser
{
    private static readonly HashSet<string> UnsupportedParameters =
        new(["targetResourceTypes", "targetSearchParameterTypes", "targetDataStoreUsagePercentage"], StringComparer.Ordinal);

    /// <exception cref="BadRequestException">The body is not a Parameters resource of supported parameters.</exception>
    public static CreateReindexJobCommand Parse(string? requestBody)
    {
        var command = new CreateReindexJobCommand();
        if (string.IsNullOrWhiteSpace(requestBody))
        {
            return command;
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(requestBody);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            throw new BadRequestException("Invalid request body. Expected a FHIR Parameters resource.", ex);
        }

        if (node is not JsonObject body ||
            body["resourceType"] is not JsonValue resourceType ||
            !resourceType.TryGetValue<string>(out var resourceTypeName) ||
            resourceTypeName != "Parameters")
        {
            throw new BadRequestException("Expected a FHIR Parameters resource.");
        }

        if (body["parameter"] is not null and not JsonArray)
        {
            throw new BadRequestException("Parameters.parameter must be an array.");
        }

        var parameterNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in body["parameter"]?.AsArray() ?? [])
        {
            command = Apply(command, parameter, parameterNames);
        }

        return command;
    }

    private static CreateReindexJobCommand Apply(CreateReindexJobCommand command, JsonNode? parameter, HashSet<string> parameterNames)
    {
        if (parameter is not JsonObject value ||
            value["name"] is not JsonValue nameValue ||
            !nameValue.TryGetValue<string>(out var name))
        {
            throw new BadRequestException("Each Parameters.parameter must have a name.");
        }

        if (!parameterNames.Add(name))
        {
            throw new BadRequestException($"Parameter '{name}' must not be repeated.");
        }

        if (UnsupportedParameters.Contains(name))
        {
            throw new BadRequestException($"Parameter '{name}' is not supported.");
        }

        return name switch
        {
            "maximumNumberOfResourcesPerQuery" => command with { MaximumNumberOfResourcesPerQuery = GetInteger(value, name) },
            "maximumNumberOfResourcesPerWrite" => command with { MaximumNumberOfResourcesPerWrite = GetInteger(value, name) },
            "maximumConcurrency" => command with { MaximumConcurrency = GetInteger(value, name) },
            "queryDelayIntervalInMilliseconds" => command with { QueryDelayIntervalInMilliseconds = GetInteger(value, name) },
            _ => throw new BadRequestException($"Unknown reindex parameter '{name}'.")
        };
    }

    private static int GetInteger(JsonObject parameter, string name) =>
        parameter["valueInteger"] is JsonValue integer && integer.TryGetValue<int>(out var value)
            ? value
            : throw new BadRequestException($"{name} must be an integer.");
}
