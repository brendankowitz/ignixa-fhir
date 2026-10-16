// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The extent of one patient's record.</summary>
/// <param name="Resources">The Patient and every resource in its compartment.</param>
/// <param name="From">The earliest main date among the domains' resources, as a date; absent when none has one.</param>
/// <param name="To">The latest main date among them, as a date; absent when none has one.</param>
public sealed record MapRecord(
    int Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To);
