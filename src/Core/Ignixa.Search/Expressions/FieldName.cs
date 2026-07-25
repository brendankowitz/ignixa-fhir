// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents search field name.
/// <para>
/// Numeric, quantity and date-time values are all stored as a range, so each of those types names its two
/// bounds separately (<see cref="NumberLow"/>/<see cref="NumberHigh"/>,
/// <see cref="QuantityLow"/>/<see cref="QuantityHigh"/>, <see cref="DateTimeStart"/>/<see cref="DateTimeEnd"/>)
/// rather than exposing one "the value" field. That is a load-bearing invariant, not a naming preference:
/// the FHIR prefix table maps <c>gt</c> to the high bound but <c>sa</c> to the low bound (and <c>lt</c> to the
/// low bound but <c>eb</c> to the high bound), so a single field paired with a comparison operator cannot say
/// which column the operator belongs to. The builder that knows the comparator picks the bound; the query
/// generator applies the operator verbatim to the named column.
/// </para>
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Represents a search parameter types for FHIR")]
public enum FieldName
{
    DateTimeStart,
    DateTimeEnd,
    NumberLow,
    NumberHigh,
    ParamName,
    QuantityCode,
    QuantitySystem,
    QuantityLow,
    QuantityHigh,
    ReferenceBaseUri,
    ReferenceResourceType,
    ReferenceResourceId,
    String,
    TokenCode,
    TokenSystem,
    TokenText,
    Uri,
    UriVersion,
    UriFragment,
    IdentifierTypeSystem,
    IdentifierTypeCode
}
