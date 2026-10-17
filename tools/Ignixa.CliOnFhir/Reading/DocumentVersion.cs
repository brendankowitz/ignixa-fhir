// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Catalog;

namespace Ignixa.CliOnFhir.Reading;

/// <summary>One document of a chain: what tells its place in it.</summary>
/// <param name="Id">The DocumentReference's id.</param>
/// <param name="Reference">The DocumentReference, <c>Type/id</c>.</param>
/// <param name="Date">Its <c>date</c> as written.</param>
/// <param name="Status">Its <c>status</c> (current, superseded, entered-in-error).</param>
/// <param name="DocStatus">Its <c>docStatus</c> (preliminary, final, amended).</param>
/// <param name="RelatesTo">Its own relatesTo links, <c>code Type/id</c>.</param>
public sealed record DocumentVersion(
    [property: JsonIgnore] string Id,
    string Reference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Date,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Status,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? DocStatus,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<string>? RelatesTo)
{
    /// <summary>The <c>date</c> as an instant, for ordering; null when absent or not a date.</summary>
    [JsonIgnore]
    public DateTimeOffset? Instant =>
        DateTimeOffset.TryParse(Date, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset instant) ? instant : null;

    /// <summary>The ids of the local DocumentReferences this one's relatesTo targets.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> Targets { get; init; } = [];

    public static DocumentVersion From(string id, JsonObject resource)
    {
        List<(string Code, string Target)> links = [.. (resource["relatesTo"] as JsonArray ?? [])
            .OfType<JsonObject>()
            .Select(link => (Code: JsonText.Of(link["code"]) ?? "relates", Target: JsonText.Of(link["target"]?["reference"])))
            .Where(link => link.Target is not null)
            .Select(link => (link.Code, link.Target!))];
        return new DocumentVersion(
            id,
            FhirIds.Reference("DocumentReference", id),
            JsonText.Of(resource["date"]),
            JsonText.Of(resource["status"]),
            JsonText.Of(resource["docStatus"]),
            links.Count == 0 ? null : [.. links.Select(link => $"{link.Code} {link.Target}")])
        {
            Targets = [.. links.Select(link => link.Target).Where(target => target.StartsWith("DocumentReference/", StringComparison.Ordinal)).Select(target => target["DocumentReference/".Length..])],
        };
    }
}
