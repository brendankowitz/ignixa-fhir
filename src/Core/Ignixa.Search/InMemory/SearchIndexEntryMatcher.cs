// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Globalization;
using System.Text;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.InMemory;

/// <summary>
/// Compiles the field-level expression beneath one search parameter (the shape
/// <see cref="LegacyExpressionLowerer"/> produces) into a test of a single <see cref="SearchIndexEntry"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every field of a multi-field criterion (token <c>system|code</c>, quantity <c>value|system|code</c>,
/// reference type and id, a date's start and end bounds) is tested against the same index entry, and within a
/// composite against the same value of the same component. Testing each field as its own "any entry" check and
/// intersecting the results lets <c>code=a|x</c> match a resource coded <c>a|y</c> and <c>b|x</c>.
/// </para>
/// <para>
/// Comparisons dispatch on <see cref="FieldName"/>, so a lowered bound such as <see cref="FieldName.DateTimeEnd"/>
/// or <see cref="FieldName.NumberLow"/> compares exactly the bound <see cref="DateRangeComparisonSemantics"/> and
/// <see cref="NumericRangeComparisonSemantics"/> chose. A bound the index value does not have (an open range)
/// never satisfies a comparison, as in the SQL backend where the column is NULL.
/// </para>
/// </remarks>
internal static class SearchIndexEntryMatcher
{
    /// <summary>Compiles <paramref name="expression"/>, the lowered body of one search parameter, into an entry test.</summary>
    /// <exception cref="SearchOperationNotSupportedException">The expression contains a node this matcher cannot evaluate.</exception>
    public static Func<SearchIndexEntry, bool> Compile(Expression expression)
    {
        ArgumentNullException.ThrowIfNull(expression);

        if (TryGetCommonComponent(expression, out int? component))
        {
            Func<ISearchValue, bool> matchesValue = CompileValueMatcher(expression);
            return entry => ResolveValues(entry.Value, component).Any(matchesValue);
        }

        switch (expression)
        {
            case MultiaryExpression multiary:
            {
                Func<SearchIndexEntry, bool>[] children = [.. multiary.Expressions.Select(Compile)];
                return multiary.MultiaryOperation switch
                {
                    MultiaryOperator.And => entry => children.All(child => child(entry)),
                    MultiaryOperator.Or => entry => children.Any(child => child(entry)),
                    _ => throw new SearchOperationNotSupportedException($"MultiaryOperator {multiary.MultiaryOperation} is not supported in in-memory search."),
                };
            }

            case UnionExpression union:
            {
                Func<SearchIndexEntry, bool>[] children = [.. union.Expressions.Select(Compile)];
                return entry => children.Any(child => child(entry));
            }

            case NotExpression not:
            {
                Func<SearchIndexEntry, bool> inner = Compile(not.Expression);
                return entry => !inner(entry);
            }

            default:
                throw new SearchOperationNotSupportedException($"{expression.GetType().Name} is not supported within a search parameter expression in in-memory search.");
        }
    }

    /// <summary>
    /// Folds a string for accent-insensitive comparison: decompose, drop combining marks, recompose. FHIR string
    /// search is case- and accent-insensitive by default.
    /// </summary>
    public static string Fold(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.All(char.IsAscii))
        {
            return value;
        }

        string decomposed = value.Normalize(NormalizationForm.FormD);
        var folded = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                folded.Append(c);
            }
        }

        return folded.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// The component every leaf of <paramref name="expression"/> addresses (null for a non-composite parameter).
    /// False when the leaves address different components or the expression has no leaves, in which case the
    /// combinators are evaluated per entry rather than per value.
    /// </summary>
    private static bool TryGetCommonComponent(Expression expression, out int? component)
    {
        List<int?> components = [];
        component = null;
        if (!CollectComponents(expression, components) || components.Count == 0 || components.Any(index => index != components[0]))
        {
            return false;
        }

        component = components[0];
        return true;
    }

    private static bool CollectComponents(Expression expression, List<int?> components)
    {
        switch (expression)
        {
            case MultiaryExpression multiary:
                return multiary.Expressions.All(child => CollectComponents(child, components));
            case UnionExpression union:
                return union.Expressions.All(child => CollectComponents(child, components));
            case NotExpression not:
                return CollectComponents(not.Expression, components);
            case IFieldExpression field:
                components.Add(field.ComponentIndex);
                return true;
            default:
                return false;
        }
    }

    private static Func<ISearchValue, bool> CompileValueMatcher(Expression expression)
    {
        switch (expression)
        {
            case MultiaryExpression multiary:
            {
                Func<ISearchValue, bool>[] children = [.. multiary.Expressions.Select(CompileValueMatcher)];
                return multiary.MultiaryOperation switch
                {
                    MultiaryOperator.And => value => children.All(child => child(value)),
                    MultiaryOperator.Or => value => children.Any(child => child(value)),
                    _ => throw new SearchOperationNotSupportedException($"MultiaryOperator {multiary.MultiaryOperation} is not supported in in-memory search."),
                };
            }

            case UnionExpression union:
            {
                Func<ISearchValue, bool>[] children = [.. union.Expressions.Select(CompileValueMatcher)];
                return value => children.Any(child => child(value));
            }

            case NotExpression not:
            {
                Func<ISearchValue, bool> inner = CompileValueMatcher(not.Expression);
                return value => !inner(value);
            }

            case StringExpression str:
                return value => MatchesString(str, value);

            case BinaryExpression binary:
                return value => MatchesBinary(binary, value);

            case MissingFieldExpression missing:
                return value => GetStringField(missing.FieldName, value) is null;

            default:
                throw new SearchOperationNotSupportedException($"{expression.GetType().Name} is not supported within a search parameter expression in in-memory search.");
        }
    }

    private static IReadOnlyList<ISearchValue> ResolveValues(ISearchValue value, int? component)
    {
        if (component is not int index)
        {
            return [value];
        }

        if (value is not CompositeIndexSearchValue composite)
        {
            throw new SearchOperationNotSupportedException(
                $"A composite component expression cannot be evaluated against a {value.GetType().Name} index value.");
        }

        return index < composite.Components.Count ? composite.Components[index] : [];
    }

    private static bool MatchesString(StringExpression expression, ISearchValue value)
    {
        string? fieldValue = GetStringField(expression.FieldName, value);
        if (fieldValue is null)
        {
            return false;
        }

        string expressionValue = expression.Value;
        if (expression.IgnoreCase && expression.FieldName is FieldName.String or FieldName.TokenText)
        {
            fieldValue = Fold(fieldValue);
            expressionValue = Fold(expressionValue);

            // A value that folds away entirely (a bare combining mark) would otherwise be a prefix of everything.
            if (expressionValue.Length == 0)
            {
                return false;
            }
        }

        StringComparison comparison = expression.IgnoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return expression.StringOperator switch
        {
            StringOperator.Equals => string.Equals(fieldValue, expressionValue, comparison),
            StringOperator.StartsWith => fieldValue.StartsWith(expressionValue, comparison),
            StringOperator.Contains => fieldValue.Contains(expressionValue, comparison),
            StringOperator.EndsWith => fieldValue.EndsWith(expressionValue, comparison),
            StringOperator.NotStartsWith => !fieldValue.StartsWith(expressionValue, comparison),
            StringOperator.NotContains => !fieldValue.Contains(expressionValue, comparison),
            StringOperator.NotEndsWith => !fieldValue.EndsWith(expressionValue, comparison),
            StringOperator.LeftSideStartsWith => expressionValue.StartsWith(fieldValue, comparison),
            _ => throw new SearchOperationNotSupportedException($"StringOperator {expression.StringOperator} is not supported in in-memory search."),
        };
    }

    /// <summary>The value of <paramref name="fieldName"/> on <paramref name="value"/>, or null when the value has none.</summary>
    /// <exception cref="SearchOperationNotSupportedException">The field does not exist on that kind of index value.</exception>
    private static string? GetStringField(FieldName fieldName, ISearchValue value) =>
        (fieldName, value) switch
        {
            (FieldName.String, StringSearchValue s) => s.String,
            (FieldName.TokenCode, TokenSearchValue token) => token.Code,
            (FieldName.TokenSystem, TokenSearchValue token) => token.System,
            (FieldName.TokenText, TokenSearchValue token) => token.Text,
            (FieldName.IdentifierTypeSystem, TokenSearchValue token) => token.IdentifierTypeSystem,
            (FieldName.IdentifierTypeCode, TokenSearchValue token) => token.IdentifierTypeCode,
            (FieldName.TokenCode, OfTypeTokenSearchValue ofType) => ofType.IdentifierValue,
            (FieldName.IdentifierTypeSystem, OfTypeTokenSearchValue ofType) => ofType.TypeSystem,
            (FieldName.IdentifierTypeCode, OfTypeTokenSearchValue ofType) => ofType.TypeCode,
            (FieldName.ReferenceResourceId, ReferenceSearchValue reference) => reference.ResourceId,
            (FieldName.ReferenceResourceType, ReferenceSearchValue reference) => reference.ResourceType,
            (FieldName.ReferenceBaseUri, ReferenceSearchValue reference) => reference.BaseUri?.ToString(),
            (FieldName.QuantitySystem, QuantitySearchValue quantity) => quantity.System,
            (FieldName.QuantityCode, QuantitySearchValue quantity) => quantity.Code,
            (FieldName.Uri, UriSearchValue uri) => uri.Uri,
            (FieldName.UriVersion, UriSearchValue uri) => uri.Version,
            (FieldName.UriFragment, UriSearchValue uri) => uri.Fragment,
            _ => throw FieldNotOnValue(fieldName, value),
        } is { Length: > 0 } present ? present : null;

    private static bool MatchesBinary(BinaryExpression expression, ISearchValue value) =>
        (expression.FieldName, value) switch
        {
            (FieldName.DateTimeStart, DateTimeSearchValue date) => Satisfies(date.Start.CompareTo((DateTimeOffset)expression.Value), expression.BinaryOperator),
            (FieldName.DateTimeEnd, DateTimeSearchValue date) => Satisfies(date.End.CompareTo((DateTimeOffset)expression.Value), expression.BinaryOperator),
            (FieldName.NumberLow, NumberSearchValue number) => Satisfies(number.Low, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.NumberHigh, NumberSearchValue number) => Satisfies(number.High, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.QuantityLow, QuantitySearchValue quantity) => Satisfies(quantity.Low, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.QuantityHigh, QuantitySearchValue quantity) => Satisfies(quantity.High, (decimal)expression.Value, expression.BinaryOperator),
            _ => throw FieldNotOnValue(expression.FieldName, value),
        };

    private static bool Satisfies(decimal? bound, decimal target, BinaryOperator binaryOperator) =>
        bound is decimal present && Satisfies(present.CompareTo(target), binaryOperator);

    private static bool Satisfies(int comparison, BinaryOperator binaryOperator) =>
        binaryOperator switch
        {
            BinaryOperator.Equal => comparison == 0,
            BinaryOperator.NotEqual => comparison != 0,
            BinaryOperator.GreaterThan => comparison > 0,
            BinaryOperator.GreaterThanOrEqual => comparison >= 0,
            BinaryOperator.LessThan => comparison < 0,
            BinaryOperator.LessThanOrEqual => comparison <= 0,
            _ => throw new SearchOperationNotSupportedException($"BinaryOperator {binaryOperator} is not supported in in-memory search."),
        };

    private static SearchOperationNotSupportedException FieldNotOnValue(FieldName fieldName, ISearchValue value) =>
        new($"Field {fieldName} cannot be compared with a {value.GetType().Name} index value in in-memory search.");
}
