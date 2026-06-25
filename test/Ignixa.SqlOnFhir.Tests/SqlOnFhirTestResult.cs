/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Serialization record for a single test result in the sql-on-fhir.js report format.
 */

using System.Text.Json.Serialization;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// The "result" object of a test entry in the sql-on-fhir.js report format.
/// <c>reason</c> is omitted when null (present only on non-passing entries).
/// </summary>
public record SqlOnFhirTestResult(
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("reason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? Reason);
