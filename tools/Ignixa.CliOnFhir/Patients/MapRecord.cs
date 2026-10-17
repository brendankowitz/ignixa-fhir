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
/// <param name="AsOf">The asOf the call gave; absent when the map is read as of the patient's latest dated resource.</param>
/// <param name="AsOfSource">
/// Where the asOf came from (<see cref="Clock.AsOfSources"/>, the field <c>patient_cohort</c> also has): <c>argument</c> (the call's
/// <c>asOf</c>), <c>session-option</c> (<c>--as-of</c>), <c>patient-latest</c> (the default) or <c>clock</c> (the default for a
/// patient with no dated resource).
/// </param>
/// <param name="Window">
/// The window the map's lists, latest values and counts were read in: <c>to</c> is asOf, the given one or the default, and
/// <c>from</c> is null (no lower bound); a resource dated after <c>to</c> is in none of them (a <c>dated-after-as-of</c> note counts them).
/// </param>
/// <param name="AsOfPrecision">The given asOf's precision when it is not an instant (<c>year</c>, <c>month</c>, <c>date</c>); absent otherwise.</param>
/// <param name="AsOfZoneAssumed"><c>true</c> when the given asOf was a date and time without a zone, read as UTC; absent otherwise.</param>
public sealed record MapRecord(
    int Resources,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? From,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? To,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AsOf,
    string AsOfSource,
    ContextWindow Window,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? AsOfPrecision = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? AsOfZoneAssumed = null);
