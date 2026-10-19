// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The time window a time-dependent domain was read in; both ends inclusive, written as FHIR instants in UTC.</summary>
/// <param name="From">The earliest date counted; null for no lower bound.</param>
/// <param name="To">The latest date counted: the call's <c>asOf</c> instant.</param>
public sealed record ContextWindow(
    [property: JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset? From,
    [property: JsonConverter(typeof(UtcInstantConverter))] DateTimeOffset To);
