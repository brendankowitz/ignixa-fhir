// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>A conditional reference, as written, that was not resolved, and why.</summary>
public sealed record UnresolvedConditional(string Reference, ConditionalReferenceFailure Failure);
