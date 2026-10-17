// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.CliOnFhir.Contracts;

namespace Ignixa.CliOnFhir.Clock;

/// <summary>A resolved <see cref="AsOf"/> and what the resolution noticed on the way (timestamps that differ, a manifest that could not be read).</summary>
/// <param name="Value">The instant and its source.</param>
/// <param name="Issues">Information issues; none name text from the folder.</param>
public sealed record AsOfResolution(AsOf Value, IReadOnlyList<IssueItem> Issues);
