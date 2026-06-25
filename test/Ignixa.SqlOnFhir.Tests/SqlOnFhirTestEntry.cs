/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Serialization record for a named test entry in the sql-on-fhir.js report format.
 */

using System.Text.Json.Serialization;

namespace Ignixa.SqlOnFhir.Tests;

/// <summary>
/// A single test entry in the sql-on-fhir.js report format: a test name and its result.
/// </summary>
public record SqlOnFhirTestEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("result")] SqlOnFhirTestResult Result);
