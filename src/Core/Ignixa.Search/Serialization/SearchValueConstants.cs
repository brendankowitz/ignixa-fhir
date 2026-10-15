// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

namespace Ignixa.Search.Serialization;

/// <summary>
/// Property names and kind discriminators of the compact search value format written by
/// <see cref="CompactSearchValueWriter"/> and read by <see cref="CompactSearchValueReader"/>.
/// </summary>
/// <remarks>
/// Every value is a JSON object whose <see cref="KindName"/> names its type and whose other properties are its fields,
/// each written only when it is not the field's default (null, false). Abbreviated names keep FileSystem sidecars small.
/// The format before this one told kinds apart by which properties were present and lost several fields; a value it
/// wrote has no <see cref="KindName"/>, so reading it fails rather than loading something other than what was indexed.
/// </remarks>
public static class SearchValueConstants
{
    /// <summary>The kind discriminator; its value is one of the <c>*Kind</c> constants.</summary>
    public const string KindName = "k";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.TokenSearchValue"/>.</summary>
    public const string TokenKind = "token";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.QuantitySearchValue"/>.</summary>
    public const string QuantityKind = "quantity";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.NumberSearchValue"/>.</summary>
    public const string NumberKind = "number";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.StringSearchValue"/>.</summary>
    public const string StringKind = "string";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.ReferenceSearchValue"/>.</summary>
    public const string ReferenceKind = "reference";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.DateTimeSearchValue"/>.</summary>
    public const string DateTimeKind = "date";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.UriSearchValue"/>.</summary>
    public const string UriKind = "uri";

    /// <summary>Kind of a <see cref="Indexing.SearchValues.CompositeIndexSearchValue"/>.</summary>
    public const string CompositeKind = "composite";

    /// <summary>DateTime range start.</summary>
    public const string DateTimeStartName = "st";

    /// <summary>DateTime range end.</summary>
    public const string DateTimeEndName = "et";

    /// <summary>The original partial date a DateTime value prints as, when it is not the start-end default.</summary>
    public const string DateTimeTextName = "dt";

    /// <summary>Number range low.</summary>
    public const string LowNumberName = "ln";

    /// <summary>Number range high.</summary>
    public const string HighNumberName = "hn";

    /// <summary>Quantity range low.</summary>
    public const string LowQuantityName = "lq";

    /// <summary>Quantity range high.</summary>
    public const string HighQuantityName = "hq";

    /// <summary>System URI (for tokens and quantities); an empty string is kept distinct from absent.</summary>
    public const string SystemName = "s";

    /// <summary>Code value (for tokens and quantities); an empty string is kept distinct from absent.</summary>
    public const string CodeName = "c";

    /// <summary>Token display text, as indexed (case preserved).</summary>
    public const string TextName = "t";

    /// <summary>Identifier type system (for tokens indexed from an Identifier).</summary>
    public const string IdentifierTypeSystemName = "its";

    /// <summary>Identifier type code (for tokens indexed from an Identifier).</summary>
    public const string IdentifierTypeCodeName = "itc";

    /// <summary>String value, unescaped as indexed.</summary>
    public const string StringName = "str";

    /// <summary>Sort flag: the value is the minimum of its parameter on the resource (strings and dates).</summary>
    public const string IsMinName = "min";

    /// <summary>Sort flag: the value is the maximum of its parameter on the resource (strings and dates).</summary>
    public const string IsMaxName = "max";

    /// <summary>Reference kind (<see cref="Indexing.SearchValues.ReferenceKind"/> member name).</summary>
    public const string ReferenceKindName = "rk";

    /// <summary>Reference base URI.</summary>
    public const string ReferenceBaseUriName = "rb";

    /// <summary>Reference resource type.</summary>
    public const string ReferenceResourceTypeName = "rt";

    /// <summary>Reference resource ID.</summary>
    public const string ReferenceResourceIdName = "ri";

    /// <summary>URI value (without a separated canonical version or fragment).</summary>
    public const string UriName = "u";

    /// <summary>Canonical version separated from the URI.</summary>
    public const string UriVersionName = "v";

    /// <summary>Canonical fragment separated from the URI.</summary>
    public const string UriFragmentName = "f";

    /// <summary>Composite components: an array with one array of values per component, in component order.</summary>
    public const string ComponentsName = "cs";
}
