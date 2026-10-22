// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions;

namespace Ignixa.Validation.Abstractions.Terminology;

public sealed record ConceptDetails(
    string? Name,
    string? Version,
    string? Display,
    string? Definition,
    IReadOnlyList<PropertyValue>? Properties,
    IReadOnlyList<Designation>? Designations);
