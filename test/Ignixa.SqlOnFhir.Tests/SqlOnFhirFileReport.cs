/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Serialization record for the per-file report in the sql-on-fhir.js report format.
 */

using System.Text.Json.Serialization;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// The per-file report in the sql-on-fhir.js report format: an ordered list of test entries.
/// </summary>
public class SqlOnFhirFileReport
{
#pragma warning disable CA1002, CA2227 // Serialization shape mirrors the sql-on-fhir.js report format.
    [JsonPropertyName("tests")]
    public List<SqlOnFhirTestEntry> Tests { get; } = [];
#pragma warning restore CA1002, CA2227
}
