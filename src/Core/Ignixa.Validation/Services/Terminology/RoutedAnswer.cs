// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Validation.Abstractions.Terminology;

namespace Ignixa.Validation.Services.Terminology;

/// <summary>A routed answer plus every reason a consulted source could not decide.</summary>
public sealed record RoutedAnswer<T>(T Answer, IReadOnlyList<UnknownReason> Unknowns)
    where T : ITerminologyAnswer;
