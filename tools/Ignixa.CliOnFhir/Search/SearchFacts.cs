// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Search;

/// <summary>What a search found.</summary>
/// <param name="ResourceType">The searched resource type.</param>
/// <param name="Query">The query as received.</param>
/// <param name="Compartment">The compartment searched within (<c>Patient/123</c>); absent when the search was not limited to one.</param>
/// <param name="Total">Every resource matching the query, regardless of paging.</param>
/// <param name="Offset">Matches skipped before the first returned resource.</param>
/// <param name="Returned">Resources in <paramref name="Resources"/>.</param>
/// <param name="Resources">
/// The returned resources, in <c>_sort</c> order or else catalog order; absent when only the count was requested.
/// </param>
/// <param name="Included">
/// The resources <c>_include</c>/<c>_revinclude</c> add to the returned page, in discovery order; absent when the query
/// has neither. Not counted in <paramref name="Total"/> or <paramref name="Returned"/>; any left out are in <c>omitted</c>.
/// </param>
/// <param name="ResolvedReferences">
/// For each returned or included resource (<c>Type/id</c>), the Bundle references it shows as written (<c>urn:uuid:...</c>, a
/// fullUrl) and the <c>Type/id</c> they were matched as; the resources themselves are returned unchanged. At most
/// 100 are listed (the rest are counted in <c>omitted</c>); absent when the returned resources show none.
/// </param>
public sealed record SearchFacts(
    string ResourceType,
    string Query,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Compartment,
    int Total,
    int Offset,
    int Returned,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<JsonNode>? Included = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? ResolvedReferences = null)
{
    /// <summary>
    /// With <c>columns</c>: the table's header, <c>ref</c> (the row's resource, as evidence names it) and then each column; absent otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Columns { get; init; }

    /// <summary>With <c>columns</c>: one row per returned match, its cells in <see cref="Columns"/> order; absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<JsonArray>? Rows { get; init; }

    /// <summary>
    /// With <c>columns</c>: per column, how many of the returned rows have no value in it (a JSON null cell); absent when every
    /// cell has a value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, int>? EmptyCells { get; init; }

    /// <summary>With <c>fhirpath</c>: the expression as received; absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fhirpath")]
    public string? FhirPath { get; init; }

    /// <summary>With <c>fhirpath</c>: the expression's result, every item in order (empty when it yields nothing); absent otherwise.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonArray? Value { get; init; }

    /// <summary>
    /// With <c>fhirpath</c>: what evidence lists. <c>result-elements</c> when every result item was read from a match (evidence
    /// lists those matches); <c>counted</c> when the expression is <c>X.count()</c> and X's items are whole matches (evidence and
    /// <see cref="Counted"/> list the matches counted); <c>matches</c> when the result was otherwise computed (a maximum, a count of
    /// elements) or read from a resource <c>resolve()</c> reached (evidence lists every match, then each resource <c>resolve()</c>
    /// reached). Absent otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ValueFrom { get; init; }

    /// <summary>
    /// With a <c>fhirpath</c> of the form <c>X.count()</c> whose items are whole matches (<see cref="ValueFrom"/> <c>counted</c>):
    /// the references of the matches counted, in result order, as many as fit; absent otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Counted { get; init; }

    /// <summary>
    /// With <c>perPatient</c>: the matches and patients before the selection and the matches kept, one per patient. <see cref="Total"/>,
    /// paging, <c>fhirpath</c> and <c>summary=count</c> then see only the kept matches. Absent otherwise.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PerPatientFacts? PerPatient { get; init; }
}
