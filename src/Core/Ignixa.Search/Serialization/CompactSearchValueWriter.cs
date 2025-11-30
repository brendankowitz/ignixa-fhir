// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Globalization;
using System.Text.Json;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Serialization;

/// <summary>
/// Writes search values in compact JSON format using abbreviated property names.
/// Implements visitor pattern to handle all ISearchValue types.
/// Reduces storage size by 25-35% compared to default serialization.
/// </summary>
public class CompactSearchValueWriter : ISearchValueVisitor
{
    private readonly Utf8JsonWriter _writer;
    private readonly int? _componentIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="CompactSearchValueWriter"/> class.
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="componentIndex">Optional component index for composite search values.</param>
    public CompactSearchValueWriter(Utf8JsonWriter writer, int? componentIndex = null)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _componentIndex = componentIndex;
    }

    /// <inheritdoc />
    public void Visit(CompositeSearchValue composite)
    {
        // For composite values, we need to flatten the components into a single object
        // with indexed property names (e.g., "c_0", "c_1" for component indices)
        // This matches the Cartesian product approach from Microsoft's implementation

        // Iterate through each component combination (Cartesian product)
        foreach (var componentValues in composite.Components.CartesianProduct())
        {
            int index = 0;
            foreach (var componentValue in componentValues)
            {
                // Create a new writer with component index for nested values
                var componentWriter = new CompactSearchValueWriter(_writer, index);
                componentValue.AcceptVisitor(componentWriter);
                index++;
            }
        }
    }

    /// <inheritdoc />
    public void Visit(DateTimeSearchValue dateTime)
    {
        // Use "o" format (ISO 8601 round-trip) to ensure consistent fractional seconds
        // This is critical for Cosmos DB range indexing to work correctly
        string propertyPrefix = _componentIndex.HasValue
            ? $"{SearchValueConstants.DateTimeStartName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
            : SearchValueConstants.DateTimeStartName;

        _writer.WriteString(
            _componentIndex.HasValue
                ? $"{SearchValueConstants.DateTimeStartName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                : SearchValueConstants.DateTimeStartName,
            dateTime.Start.ToString("o", CultureInfo.InvariantCulture));

        _writer.WriteString(
            _componentIndex.HasValue
                ? $"{SearchValueConstants.DateTimeEndName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                : SearchValueConstants.DateTimeEndName,
            dateTime.End.ToString("o", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public void Visit(NumberSearchValue number)
    {
        string propertyPrefix = _componentIndex.HasValue ? SearchValueConstants.ComponentIndexSuffix + _componentIndex : string.Empty;

        // Write exact value if low == high (optimization)
        if (number.Low.HasValue && number.High.HasValue && number.Low == number.High)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.NumberName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.NumberName,
                number.Low.Value);
        }

        // Always write range values for range queries (if present)
        if (number.Low.HasValue)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.LowNumberName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.LowNumberName,
                number.Low.Value);
        }

        if (number.High.HasValue)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.HighNumberName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.HighNumberName,
                number.High.Value);
        }
    }

    /// <inheritdoc />
    public void Visit(QuantitySearchValue quantity)
    {
        string propertyPrefix = _componentIndex.HasValue ? SearchValueConstants.ComponentIndexSuffix + _componentIndex : string.Empty;

        // Write system and code if present
        if (!string.IsNullOrEmpty(quantity.System))
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.SystemName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.SystemName,
                quantity.System);
        }

        if (!string.IsNullOrEmpty(quantity.Code))
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.CodeName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.CodeName,
                quantity.Code);
        }

        // Write exact value if low == high (optimization)
        if (quantity.Low.HasValue && quantity.High.HasValue && quantity.Low == quantity.High)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.QuantityName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.QuantityName,
                quantity.Low.Value);
        }

        // Always write range values for range queries (if present)
        if (quantity.Low.HasValue)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.LowQuantityName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.LowQuantityName,
                quantity.Low.Value);
        }

        if (quantity.High.HasValue)
        {
            _writer.WriteNumber(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.HighQuantityName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.HighQuantityName,
                quantity.High.Value);
        }
    }

    /// <inheritdoc />
    public void Visit(ReferenceSearchValue reference)
    {
        string propertyPrefix = _componentIndex.HasValue ? SearchValueConstants.ComponentIndexSuffix + _componentIndex : string.Empty;

        // Write base URI if present
        if (reference.BaseUri != null)
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.ReferenceBaseUriName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.ReferenceBaseUriName,
                reference.BaseUri.ToString());
        }

        // Write resource type if present
        if (!string.IsNullOrEmpty(reference.ResourceType))
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.ReferenceResourceTypeName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.ReferenceResourceTypeName,
                reference.ResourceType);
        }

        // Always write resource ID
        _writer.WriteString(
            _componentIndex.HasValue
                ? $"{SearchValueConstants.ReferenceResourceIdName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                : SearchValueConstants.ReferenceResourceIdName,
            reference.ResourceId);
    }

    /// <inheritdoc />
    public void Visit(StringSearchValue s)
    {
        // For non-composite components, write the original string
        if (!_componentIndex.HasValue)
        {
            _writer.WriteString(SearchValueConstants.StringName, s.String);
        }

        // Always write normalized string (uppercase) for case-insensitive search
        _writer.WriteString(
            _componentIndex.HasValue
                ? $"{SearchValueConstants.NormalizedStringName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                : SearchValueConstants.NormalizedStringName,
            s.String.ToUpperInvariant());
    }

    /// <inheritdoc />
    public void Visit(TokenSearchValue token)
    {
        string propertyPrefix = _componentIndex.HasValue ? SearchValueConstants.ComponentIndexSuffix + _componentIndex : string.Empty;

        // Write system if present
        if (!string.IsNullOrEmpty(token.System))
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.SystemName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.SystemName,
                token.System);
        }

        // Write code if present
        if (!string.IsNullOrEmpty(token.Code))
        {
            _writer.WriteString(
                _componentIndex.HasValue
                    ? $"{SearchValueConstants.CodeName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                    : SearchValueConstants.CodeName,
                token.Code);
        }

        // Write normalized text if present (for non-composite components only)
        if (!string.IsNullOrEmpty(token.Text) && !_componentIndex.HasValue)
        {
            _writer.WriteString(
                SearchValueConstants.NormalizedTextName,
                token.Text.ToUpperInvariant());
        }
    }

    /// <inheritdoc />
    public void Visit(UriSearchValue uri)
    {
        _writer.WriteString(
            _componentIndex.HasValue
                ? $"{SearchValueConstants.UriName}{SearchValueConstants.ComponentIndexSuffix}{_componentIndex}"
                : SearchValueConstants.UriName,
            uri.Uri);
    }
}

/// <summary>
/// Extension methods for Cartesian product calculation.
/// </summary>
internal static class CartesianProductExtensions
{
    /// <summary>
    /// Calculates the Cartesian product of component value collections.
    /// Used for composite search values where each component can have multiple values.
    /// </summary>
    public static IEnumerable<IEnumerable<T>> CartesianProduct<T>(this IEnumerable<IReadOnlyList<T>> sequences)
    {
        IEnumerable<IEnumerable<T>> emptyProduct = new[] { Enumerable.Empty<T>() };

        return sequences.Aggregate(
            emptyProduct,
            (accumulator, sequence) =>
                from accSeq in accumulator
                from item in sequence
                select accSeq.Concat(new[] { item }));
    }
}
