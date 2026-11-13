// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Models;

/// <summary>
/// How the semantic text that feeds embedding generation is extracted from a resource when more than
/// one value matches the <c>special</c> search parameter's FHIRPath expression.
/// </summary>
public enum VectorTextExtractionPolicy
{
    /// <summary>Use only the first matched value.</summary>
    FirstValue,

    /// <summary>Join every matched value into a single string. The default.</summary>
    Concatenate,

    /// <summary>Embed every matched value independently, producing one vector row per value.</summary>
    PerValueRow
}
