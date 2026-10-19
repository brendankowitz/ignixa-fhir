// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.Application.BackgroundOperations.BulkDelete;

namespace Ignixa.Api.Endpoints;

/// <summary>
/// Builds the FHIR <c>Parameters</c> JSON for a <c>$bulk-delete</c> status response, matching
/// fhir-server's shape exactly: an "Issues" parameter carrying a nested <c>OperationOutcome</c> (when
/// there is one), followed by a "ResourceDeletedCount" parameter whose "part" entries list only
/// resource types with a positive count. The whole "parameter" array is omitted when there is nothing
/// to report, because FHIR JSON forbids an empty array.
/// </summary>
public static class BulkDeleteStatusResponseBuilder
{
    /// <summary>
    /// Builds the response for <paramref name="result"/>. <paramref name="fhirVersion"/> selects the
    /// deleted-count representation: R4/R5/R4B/R6 use <c>valueInteger64</c> (a JSON string, per the FHIR
    /// integer64 JSON representation); Stu3 has no integer64 type, so it uses <c>valueDecimal</c> (a
    /// JSON number) instead.
    /// </summary>
    public static BulkDeleteStatusResponse Build(GetBulkDeleteStatusResult result, FhirVersion fhirVersion)
    {
        ArgumentNullException.ThrowIfNull(result);

        return result.Status switch
        {
            "Queued" or "Running" => InProgress(result, fhirVersion),
            "Completed" => Terminal(StatusCodes.Status200OK, issues: null, result, fhirVersion),
            "Cancelled" => Terminal(StatusCodes.Status200OK, [("warning", "informational", "Job Canceled")], result, fhirVersion),
            "Failed" => Terminal(StatusCodes.Status500InternalServerError, FailureIssues(result), result, fhirVersion),
            _ => throw new InvalidOperationException($"Unexpected bulk delete job status '{result.Status}'."),
        };
    }

    private static BulkDeleteStatusResponse InProgress(GetBulkDeleteStatusResult result, FhirVersion fhirVersion) =>
        Terminal(StatusCodes.Status202Accepted, [("information", "informational", "Job In Progress")], result, fhirVersion);

    private static IReadOnlyList<(string Severity, string Code, string Diagnostics)> FailureIssues(GetBulkDeleteStatusResult result)
    {
        if (!string.IsNullOrEmpty(result.ErrorMessage))
        {
            return [("error", "exception", result.ErrorMessage)];
        }

        return result.Issues.Count > 0
            ? result.Issues.Select(issue => ("error", "exception", issue)).ToList()
            : [("error", "exception", "Bulk delete failed.")];
    }

    private static BulkDeleteStatusResponse Terminal(
        int statusCode,
        IReadOnlyList<(string Severity, string Code, string Diagnostics)>? issues,
        GetBulkDeleteStatusResult result,
        FhirVersion fhirVersion)
    {
        var parameters = new JsonArray();
        if (issues is { Count: > 0 })
        {
            parameters.Add(IssuesParameter(issues));
        }

        AddResourceDeletedCount(parameters, result.ResourceDeletedCount, fhirVersion);

        var body = new JsonObject { ["resourceType"] = "Parameters" };
        if (parameters.Count > 0)
        {
            body["parameter"] = parameters;
        }

        return new BulkDeleteStatusResponse(statusCode, body);
    }

    private static JsonObject IssuesParameter(IReadOnlyList<(string Severity, string Code, string Diagnostics)> issues)
    {
        var issueArray = new JsonArray();
        foreach (var (severity, code, diagnostics) in issues)
        {
            issueArray.Add(new JsonObject
            {
                ["severity"] = severity,
                ["code"] = code,
                ["diagnostics"] = diagnostics,
            });
        }

        return new JsonObject
        {
            ["name"] = "Issues",
            ["resource"] = new JsonObject
            {
                ["resourceType"] = "OperationOutcome",
                ["issue"] = issueArray,
            },
        };
    }

    private static void AddResourceDeletedCount(JsonArray parameters, IReadOnlyDictionary<string, long> counts, FhirVersion fhirVersion)
    {
        if (counts.Count == 0)
        {
            return;
        }

        var parts = new JsonArray();
        foreach (var (resourceType, count) in counts.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            parts.Add(fhirVersion == FhirVersion.Stu3
                ? new JsonObject { ["name"] = resourceType, ["valueDecimal"] = count }
                : new JsonObject { ["name"] = resourceType, ["valueInteger64"] = count.ToString(CultureInfo.InvariantCulture) });
        }

        parameters.Add(new JsonObject
        {
            ["name"] = "ResourceDeletedCount",
            ["part"] = parts,
        });
    }
}
