// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Expressions;

/// <summary>
/// Which of a search parameter's two source strings a <see cref="SourceSpan"/> indexes into.
/// </summary>
public enum SourceOrigin
{
    Key,
    Value,
}

/// <summary>
/// A range within one search parameter's key or value string. Offsets are relative to that string —
/// the enclosing parameter's ordinal supplies which parameter instance it belongs to.
/// </summary>
public readonly record struct SourceSpan(SourceOrigin Origin, int Start, int Length);
