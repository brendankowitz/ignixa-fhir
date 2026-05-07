/*
 * Copyright (c) 2025, Ignixa Contributors
 *
 * Tag metadata for SQL on FHIR v2.1.0-pre column definitions.
 */

namespace Ignixa.SqlOnFhir.Models;

/// <summary>
/// Implementation metadata tag for a column definition.
/// Per SQL on FHIR 2.1.0-pre spec: select.column.tag[0..*].
/// </summary>
public class ColumnTag
{
    public required string Name { get; set; }
    public required string Value { get; set; }
}
