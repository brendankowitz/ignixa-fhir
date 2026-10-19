// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json;
using Ignixa.Domain.Exceptions;
using Ignixa.Domain.Models;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Parses a <c>$bulk-delete</c> kickoff request into its effective flags and search parameters,
/// combining the query string with an optional <c>Parameters</c> body. Pure and HTTP-framework-free
/// (plain key/value pairs and bytes) so it is directly unit-testable.
/// </summary>
/// <remarks>
/// Control parameters are removed from the query string before it is treated as a search; a FHIR
/// body, when present, supplies only <c>hardDelete</c>/<c>purgeHistory</c> (the names fhir-server does
/// not accept from the query). A flag set by both the query and the body must agree, or the request
/// is rejected: silently preferring one source over the other would make the deletion scope depend on
/// which source the caller least expected to win.
/// </remarks>
public static class BulkDeleteRequestParser
{
    private const string HardDeleteQueryName = "_hardDelete";
    private const string HardDeleteQueryAlias = "hardDelete";
    private const string PurgeHistoryQueryName = "_purgeHistory";
    private const string RemoveReferencesQueryName = "_remove-references";
    private const string RemoveReferencesQueryAlias = "removeReferences";
    private const string ExcludedResourceTypesQueryName = "excludedResourceTypes";

    private static readonly HashSet<string> ControlParameterNames =
    [
        HardDeleteQueryName,
        HardDeleteQueryAlias,
        PurgeHistoryQueryName,
        RemoveReferencesQueryName,
        RemoveReferencesQueryAlias,
        ExcludedResourceTypesQueryName,
    ];

    /// <summary>
    /// Parses the kickoff request. Throws <see cref="BadRequestException"/> (400, mapped automatically
    /// by <c>FhirExceptionMiddleware</c>) for every rejected input.
    /// </summary>
    /// <param name="queryParameters">
    /// Every query parameter, one entry per value of a multi-valued key, in the order the caller supplied them.
    /// </param>
    /// <param name="preferHeader">The raw <c>Prefer</c> header value, or null/empty if absent.</param>
    /// <param name="requestBody">The raw request body bytes, or empty when no body was sent.</param>
    public static BulkDeleteRequestParseResult Parse(
        IReadOnlyList<KeyValuePair<string, string>> queryParameters,
        string? preferHeader,
        ReadOnlyMemory<byte> requestBody)
    {
        ArgumentNullException.ThrowIfNull(queryParameters);

        RequireRespondAsync(preferHeader);

        var hardDeleteQuery = ParseBooleanControl(
            Values(queryParameters, HardDeleteQueryName, HardDeleteQueryAlias), HardDeleteQueryName);
        var purgeHistoryQuery = ParseBooleanControl(
            Values(queryParameters, PurgeHistoryQueryName), PurgeHistoryQueryName);
        var removeReferencesQuery = ParseBooleanControl(
            Values(queryParameters, RemoveReferencesQueryName, RemoveReferencesQueryAlias), RemoveReferencesQueryName);
        var excludedResourceTypes = ExpandCsv(Values(queryParameters, ExcludedResourceTypesQueryName));

        var (hardDeleteBody, purgeHistoryBody) = ParseBody(requestBody);

        var hardDelete = CombineFlag(hardDeleteQuery, hardDeleteBody, "hardDelete");
        var purgeHistory = CombineFlag(purgeHistoryQuery, purgeHistoryBody, "purgeHistory");
        var removeReferences = removeReferencesQuery ?? false;

        var mode = hardDelete
            ? BulkDeleteMode.HardDelete
            : purgeHistory ? BulkDeleteMode.PurgeHistory : BulkDeleteMode.SoftDelete;

        var searchParameters = queryParameters
            .Where(parameter => !ControlParameterNames.Contains(parameter.Key))
            .ToList();

        return new BulkDeleteRequestParseResult(mode, removeReferences, excludedResourceTypes, searchParameters);
    }

    private static void RequireRespondAsync(string? preferHeader)
    {
        var hasRespondAsync = preferHeader?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(token => string.Equals(token, "respond-async", StringComparison.OrdinalIgnoreCase)) ?? false;
        if (!hasRespondAsync)
        {
            throw new BadRequestException("Bulk delete requires the 'Prefer: respond-async' header.");
        }
    }

    private static List<string> Values(IReadOnlyList<KeyValuePair<string, string>> parameters, params string[] names) =>
        parameters.Where(parameter => names.Contains(parameter.Key, StringComparer.Ordinal))
            .Select(parameter => parameter.Value)
            .ToList();

    private static bool? ParseBooleanControl(IReadOnlyList<string> values, string name)
    {
        bool? result = null;
        foreach (var raw in values)
        {
            if (!TryParseBoolean(raw, out var value))
            {
                throw new BadRequestException($"'{name}' must be 'true' or 'false'.");
            }

            if (result.HasValue && result.Value != value)
            {
                throw new BadRequestException($"'{name}' was specified more than once with conflicting values.");
            }

            result = value;
        }

        return result;
    }

    private static bool TryParseBoolean(string raw, out bool value)
    {
        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        return false;
    }

    private static IReadOnlyList<string> ExpandCsv(IReadOnlyList<string> values)
    {
        var seen = new List<string>();
        foreach (var raw in values)
        {
            foreach (var entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!seen.Contains(entry, StringComparer.Ordinal))
                {
                    seen.Add(entry);
                }
            }
        }

        return seen;
    }

    private static bool CombineFlag(bool? queryValue, bool? bodyValue, string name)
    {
        if (queryValue.HasValue && bodyValue.HasValue && queryValue.Value != bodyValue.Value)
        {
            throw new BadRequestException(
                $"'{name}' was specified with conflicting values in the query string and the request body.");
        }

        return queryValue ?? bodyValue ?? false;
    }

    /// <summary>
    /// Reads the optional <c>Parameters</c> body. Only <c>hardDelete</c>/<c>_hardDelete</c> and
    /// <c>purgeHistory</c>/<c>_purgeHistory</c> (as <c>valueBoolean</c>) are accepted; everything else
    /// about the body is a 400, because a tolerated-but-ignored field would silently not do what the
    /// caller asked for.
    /// </summary>
    private static (bool? HardDelete, bool? PurgeHistory) ParseBody(ReadOnlyMemory<byte> requestBody)
    {
        if (requestBody.Length == 0)
        {
            return (null, null);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(requestBody);
        }
        catch (JsonException ex)
        {
            throw new BadRequestException("Bulk delete request body is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("resourceType", out var resourceTypeElement) ||
                resourceTypeElement.ValueKind != JsonValueKind.String ||
                resourceTypeElement.GetString() != "Parameters")
            {
                throw new BadRequestException("Bulk delete request body must be a FHIR 'Parameters' resource.");
            }

            bool? hardDelete = null;
            bool? purgeHistory = null;
            if (root.TryGetProperty("parameter", out var parameterArray))
            {
                if (parameterArray.ValueKind != JsonValueKind.Array)
                {
                    throw new BadRequestException("Bulk delete request body 'parameter' must be an array.");
                }

                foreach (var parameter in parameterArray.EnumerateArray())
                {
                    var (name, value) = ReadBodyParameter(parameter);
                    switch (name)
                    {
                        case "hardDelete" or "_hardDelete":
                            hardDelete = CombineBodyValue(hardDelete, value, "hardDelete");
                            break;
                        case "purgeHistory" or "_purgeHistory":
                            purgeHistory = CombineBodyValue(purgeHistory, value, "purgeHistory");
                            break;
                        default:
                            throw new BadRequestException(
                                $"Bulk delete request body does not support parameter '{name}'.");
                    }
                }
            }

            return (hardDelete, purgeHistory);
        }
    }

    private static bool CombineBodyValue(bool? existing, bool value, string name) =>
        existing.HasValue && existing.Value != value
            ? throw new BadRequestException($"'{name}' was specified more than once in the request body with conflicting values.")
            : value;

    private static (string Name, bool Value) ReadBodyParameter(JsonElement parameter)
    {
        if (parameter.ValueKind != JsonValueKind.Object ||
            !parameter.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            throw new BadRequestException("Bulk delete request body has a parameter without a 'name'.");
        }

        var name = nameElement.GetString()!;
        if (!parameter.TryGetProperty("valueBoolean", out var valueElement) ||
            valueElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            throw new BadRequestException($"Bulk delete request body parameter '{name}' must have a boolean 'valueBoolean'.");
        }

        return (name, valueElement.GetBoolean());
    }
}
