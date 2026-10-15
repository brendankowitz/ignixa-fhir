// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Patients;

/// <summary>The time window a time-dependent domain was read in; both ends inclusive.</summary>
/// <param name="From">The earliest date counted; null for no lower bound.</param>
/// <param name="To">The latest date counted: the call's <c>asOf</c> instant.</param>
public sealed record ContextWindow(DateTimeOffset? From, DateTimeOffset To);
