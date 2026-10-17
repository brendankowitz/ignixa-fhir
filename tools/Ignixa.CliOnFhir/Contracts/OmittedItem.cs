// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Contracts;

/// <summary>Something left out of a response, so truncation is never silent.</summary>
/// <param name="What">What was left out, for example <c>12 more diagnostics</c>.</param>
/// <param name="Why">Why, for example <c>limit</c> or <c>size-budget</c>.</param>
public sealed record OmittedItem(string What, string Why);
