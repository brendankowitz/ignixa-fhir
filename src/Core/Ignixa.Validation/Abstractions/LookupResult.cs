// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Validation.Abstractions;

/// <summary>
/// Result from $lookup operation.
/// Contains concept details including display, definition, properties, and designations.
/// </summary>
public record LookupResult(
    bool Found,
    string? Name,
    string? Version,
    string? Display,
    string? Definition,
    IReadOnlyList<PropertyValue>? Properties,
    IReadOnlyList<Designation>? Designations);

/// <summary>
/// Property value from a concept (e.g., inactive=true, parent=123).
/// </summary>
public record PropertyValue(string Code, string? Value);

/// <summary>
/// Designation (translation/synonym) for a concept.
/// </summary>
public record Designation(string? Language, string? Use, string Value);
