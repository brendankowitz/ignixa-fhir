// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Views;

/// <summary>One column of a view's output, as the ViewDefinition declares it.</summary>
/// <param name="Name">The column name.</param>
/// <param name="Type">The declared <c>type</c>; absent when the ViewDefinition declares none.</param>
/// <param name="Collection">Whether the column holds all values of its path, as a JSON array in text; absent when false.</param>
public sealed record ViewColumn(
    string Name,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Type,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Collection);
