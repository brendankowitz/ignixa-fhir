// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

namespace Ignixa.Search.Parsing;

/// <summary>Which compilation stage produced an outcome.</summary>
public enum TraceStage
{
    Parse,
    Resolve,
    Lower,
    Emit,
}
