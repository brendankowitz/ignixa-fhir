// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;

namespace Ignixa.Api.E2ETests.Operations.BulkDelete;

/// <summary>
/// Thin wrapper around an <see cref="HttpClient"/> for driving the <c>$bulk-delete</c> async job
/// protocol in E2E tests: kickoff (DELETE with <c>Prefer: respond-async</c>), status polling (honoring
/// <c>Retry-After</c>, capped for test speed), and reading the <c>ResourceDeletedCount</c> parameter.
/// </summary>
internal sealed class BulkDeleteClient(HttpClient client)
{
    private static readonly TimeSpan MaxPollDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// Sends the kickoff DELETE request with <c>Prefer: respond-async</c> (unless overridden) and an
    /// optional FHIR <c>Parameters</c> body.
    /// </summary>
    public async Task<HttpResponseMessage> KickoffAsync(
        string url,
        string? bodyJson = null,
        string? preferHeader = "respond-async",
        CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        if (preferHeader is not null)
        {
            request.Headers.TryAddWithoutValidation("Prefer", preferHeader);
        }

        if (bodyJson is not null)
        {
            request.Content = new StringContent(bodyJson, Encoding.UTF8, "application/fhir+json");
        }

        return await client.SendAsync(request, cancellationToken);
    }

    /// <summary>
    /// Polls the job status URL (from <c>Content-Location</c>) until it stops returning 202, honoring
    /// <c>Retry-After</c> but capping the delay at <see cref="MaxPollDelay"/> so tests stay fast.
    /// Throws <see cref="TimeoutException"/> if the job has not reached a terminal state within
    /// <paramref name="timeout"/> (default 60s).
    /// </summary>
    public async Task<(HttpStatusCode StatusCode, JsonObject Body, List<HttpStatusCode> IntermediatePolls)> PollToCompletionAsync(
        string statusUrl,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(60));
        var intermediatePolls = new List<HttpStatusCode>();
        while (true)
        {
            using var response = await client.GetAsync(statusUrl, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var body = string.IsNullOrWhiteSpace(text) ? new JsonObject() : JsonNode.Parse(text)!.AsObject();

            if (response.StatusCode != HttpStatusCode.Accepted)
            {
                return (response.StatusCode, body, intermediatePolls);
            }

            intermediatePolls.Add(response.StatusCode);
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException(
                    $"Bulk delete job at '{statusUrl}' did not complete within {timeout?.TotalSeconds ?? 60}s. Last body: {body}");
            }

            await Task.Delay(ResolveDelay(response.Headers.RetryAfter), cancellationToken);
        }
    }

    private static TimeSpan ResolveDelay(RetryConditionHeaderValue? retryAfter)
    {
        if (retryAfter?.Delta is { } delta && delta < MaxPollDelay)
        {
            return delta;
        }

        return MaxPollDelay;
    }

    /// <summary>
    /// Reads the <c>ResourceDeletedCount</c> parameter's per-type counts from a status response body
    /// (<c>valueInteger64</c> as a string, or <c>valueDecimal</c> as a number for Stu3). Returns an empty
    /// dictionary when the parameter is absent (no matches deleted).
    /// </summary>
    public static IReadOnlyDictionary<string, long> GetCounts(JsonObject body)
    {
        var counts = new Dictionary<string, long>(StringComparer.Ordinal);
        var parameters = body["parameter"]?.AsArray();
        if (parameters is null)
        {
            return counts;
        }

        foreach (var parameter in parameters)
        {
            if (parameter?["name"]?.GetValue<string>() != "ResourceDeletedCount")
            {
                continue;
            }

            foreach (var part in parameter["part"]!.AsArray())
            {
                var type = part!["name"]!.GetValue<string>();
                var value = part["valueInteger64"] is JsonValue int64Value
                    ? long.Parse(int64Value.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture)
                    : (long)part["valueDecimal"]!.GetValue<decimal>();
                counts[type] = value;
            }
        }

        return counts;
    }

    /// <summary>
    /// Reads the "Issues" parameter's nested OperationOutcome issue diagnostics, or an empty list when absent.
    /// </summary>
    public static IReadOnlyList<string> GetIssueDiagnostics(JsonObject body)
    {
        var parameters = body["parameter"]?.AsArray();
        if (parameters is null)
        {
            return [];
        }

        foreach (var parameter in parameters)
        {
            if (parameter?["name"]?.GetValue<string>() != "Issues")
            {
                continue;
            }

            return parameter["resource"]!["issue"]!.AsArray()
                .Select(issue => issue!["diagnostics"]!.GetValue<string>())
                .ToList();
        }

        return [];
    }
}
