// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics.CodeAnalysis;
using EnsureThat;

namespace Ignixa.Search.Indexing.SearchValues;

/// <summary>
/// Represents a token search value for the :of-type modifier.
/// Contains both the identifier value and the identifier type (system/code).
/// </summary>
public class OfTypeTokenSearchValue : ISearchValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OfTypeTokenSearchValue"/> class.
    /// </summary>
    /// <param name="identifierValue">The identifier value to search for.</param>
    /// <param name="typeSystem">The identifier type system (from Identifier.type.coding.system).</param>
    /// <param name="typeCode">The identifier type code (from Identifier.type.coding.code).</param>
    public OfTypeTokenSearchValue(string identifierValue, string? typeSystem, string typeCode)
    {
        EnsureArg.IsNotNullOrWhiteSpace(identifierValue, nameof(identifierValue));
        EnsureArg.IsNotNullOrWhiteSpace(typeCode, nameof(typeCode));

        IdentifierValue = identifierValue;
        TypeSystem = typeSystem;
        TypeCode = typeCode;
    }

    /// <summary>
    /// Gets the identifier value to search for.
    /// </summary>
    public string IdentifierValue { get; }

    /// <summary>
    /// Gets the identifier type system (from Identifier.type.coding.system).
    /// May be null if searching by type code only.
    /// </summary>
    public string? TypeSystem { get; }

    /// <summary>
    /// Gets the identifier type code (from Identifier.type.coding.code).
    /// </summary>
    public string TypeCode { get; }

    /// <inheritdoc />
    public bool IsValidAsCompositeComponent => false;

    /// <inheritdoc />
    public void AcceptVisitor(ISearchValueVisitor visitor)
    {
        EnsureArg.IsNotNull(visitor, nameof(visitor));
        visitor.Visit(this);
    }

    public bool Equals([AllowNull] ISearchValue other)
    {
        if (other is not OfTypeTokenSearchValue otherValue) return false;

        return string.Equals(IdentifierValue, otherValue.IdentifierValue, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(TypeSystem, otherValue.TypeSystem, StringComparison.OrdinalIgnoreCase) &&
               string.Equals(TypeCode, otherValue.TypeCode, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Parses the string value for :of-type modifier to an instance of <see cref="OfTypeTokenSearchValue"/>.
    /// Format: [type-system]|[type-code]|[identifier-value]
    /// Examples:
    ///   http://terminology.hl7.org/CodeSystem/v2-0203|MR|12345
    ///   |MR|12345 (no type system - matches any system with code MR)
    /// </summary>
    /// <param name="s">The string to be parsed in format [type-system]|[type-code]|[identifier-value].</param>
    /// <returns>An instance of <see cref="OfTypeTokenSearchValue"/>.</returns>
    public static OfTypeTokenSearchValue Parse(string s)
    {
        EnsureArg.IsNotNullOrWhiteSpace(s, nameof(s));

        IReadOnlyList<string> parts = s.SplitByTokenSeparator();

        if (parts.Count != 3)
        {
            throw new FormatException(
                string.Format("The :of-type modifier requires three components: [type-system]|[type-code]|[identifier-value]. Got {0} component(s) in value '{1}'.", parts.Count, s));
        }

        var typeSystem = parts[0].UnescapeSearchParameterValue();
        var typeCode = parts[1].UnescapeSearchParameterValue();
        var identifierValue = parts[2].UnescapeSearchParameterValue();

        if (string.IsNullOrEmpty(typeCode))
        {
            throw new FormatException(
                "The :of-type modifier requires a non-empty type code in the second position.");
        }

        if (string.IsNullOrEmpty(identifierValue))
        {
            throw new FormatException(
                "The :of-type modifier requires a non-empty identifier value in the third position.");
        }

        return new OfTypeTokenSearchValue(
            identifierValue: identifierValue,
            typeSystem: string.IsNullOrEmpty(typeSystem) ? null : typeSystem,
            typeCode: typeCode);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        var systemPart = TypeSystem?.EscapeSearchParameterValue() ?? string.Empty;
        var codePart = TypeCode.EscapeSearchParameterValue();
        var valuePart = IdentifierValue.EscapeSearchParameterValue();
        return string.Format("{0}|{1}|{2}", systemPart, codePart, valuePart);
    }
}
