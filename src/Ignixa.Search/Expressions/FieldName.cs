// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics.CodeAnalysis;

namespace Ignixa.Search.Expressions;

/// <summary>
/// Represents search field name.
/// </summary>
[SuppressMessage("Naming", "CA1720:Identifier contains type name", Justification = "Represents a search parameter types for FHIR")]
public enum FieldName
{
    DateTimeStart,
    DateTimeEnd,
    Number,
    ParamName,
    QuantityCode,
    QuantitySystem,
    Quantity,
    ReferenceBaseUri,
    ReferenceResourceType,
    ReferenceResourceId,
    String,
    TokenCode,
    TokenSystem,
    TokenText,
    Uri
}
