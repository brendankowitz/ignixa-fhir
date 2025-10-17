// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Serialization;

/// <summary>
/// Constants for compact search index serialization.
/// Abbreviated property names reduce storage size by 25-35%.
/// Based on Microsoft FHIR Server Cosmos DB implementation.
/// </summary>
public static class SearchValueConstants
{
    /// <summary>
    /// Search parameter code/name (used in legacy format, omitted in parameter-first structure).
    /// </summary>
    public const string ParamName = "p";

    /// <summary>
    /// DateTime range start.
    /// </summary>
    public const string DateTimeStartName = "st";

    /// <summary>
    /// DateTime range end.
    /// </summary>
    public const string DateTimeEndName = "et";

    /// <summary>
    /// Number exact value (when low == high).
    /// </summary>
    public const string NumberName = "n";

    /// <summary>
    /// Number range low.
    /// </summary>
    public const string LowNumberName = "ln";

    /// <summary>
    /// Number range high.
    /// </summary>
    public const string HighNumberName = "hn";

    /// <summary>
    /// Quantity exact value (when low == high).
    /// </summary>
    public const string QuantityName = "q";

    /// <summary>
    /// Quantity range low.
    /// </summary>
    public const string LowQuantityName = "lq";

    /// <summary>
    /// Quantity range high.
    /// </summary>
    public const string HighQuantityName = "hq";

    /// <summary>
    /// System URI (for tokens and quantities).
    /// </summary>
    public const string SystemName = "s";

    /// <summary>
    /// Code value (for tokens).
    /// </summary>
    public const string CodeName = "c";

    /// <summary>
    /// String value (original).
    /// </summary>
    public const string StringName = "str";

    /// <summary>
    /// Normalized string (uppercase, for case-insensitive search).
    /// </summary>
    public const string NormalizedStringName = "n_s";

    /// <summary>
    /// Display text (for tokens).
    /// </summary>
    public const string TextName = "t";

    /// <summary>
    /// Normalized text (uppercase, for case-insensitive search).
    /// </summary>
    public const string NormalizedTextName = "n_t";

    /// <summary>
    /// Reference base URI.
    /// </summary>
    public const string ReferenceBaseUriName = "rb";

    /// <summary>
    /// Reference resource type.
    /// </summary>
    public const string ReferenceResourceTypeName = "rt";

    /// <summary>
    /// Reference resource ID.
    /// </summary>
    public const string ReferenceResourceIdName = "ri";

    /// <summary>
    /// URI value.
    /// </summary>
    public const string UriName = "u";

    /// <summary>
    /// Composite component index suffix (e.g., "c_0", "c_1" for component indices).
    /// </summary>
    public const string ComponentIndexSuffix = "_";
}
