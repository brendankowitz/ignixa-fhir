// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using System.Diagnostics;
using System.Globalization;
using System.Text;
using Ignixa.Search.Expressions;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

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
/// <para>
/// What a query can ask is checked once, when it is compiled, against the parameter's definition; an index value
/// of another kind than the leaf compares (a composite component the indexer typed from the element, such as a
/// Quantity under a token component) simply does not match, so one unusual resource never fails a search.
/// </para>
/// </remarks>
internal static class SearchIndexEntryMatcher
{
    /// <summary>Compiles <paramref name="expression"/>, the lowered body of one search parameter, into an entry test.</summary>
    /// <param name="parameter">
    /// The searched parameter, whose definition (and, for a composite, whose components' definitions) bounds the fields
    /// the expression may compare; null when the caller does not know it, in which case only the expression's own
    /// shape is checked.
    /// </param>
    /// <exception cref="SearchOperationNotSupportedException">The expression contains a node this matcher cannot evaluate.</exception>
    public static Func<SearchIndexEntry, bool> Compile(Expression expression, SearchParameterInfo? parameter)
    {
        ArgumentNullException.ThrowIfNull(expression);

        Validate(expression, parameter);
        return CompileEntryMatcher(expression);
    }

    private static Func<SearchIndexEntry, bool> CompileEntryMatcher(Expression expression)
    {

        if (TryGetCommonComponent(expression, out int? component))
        {
            Func<ISearchValue, bool> matchesValue = CompileValueMatcher(expression);
            return entry => ResolveValues(entry.Value, component).Any(matchesValue);
        }

        switch (expression)
        {
            case MultiaryExpression multiary:
            {
                Func<SearchIndexEntry, bool>[] children = [.. multiary.Expressions.Select(CompileEntryMatcher)];
                return multiary.MultiaryOperation switch
                {
                    MultiaryOperator.And => entry => children.All(child => child(entry)),
                    MultiaryOperator.Or => entry => children.Any(child => child(entry)),
                    _ => throw new SearchOperationNotSupportedException($"MultiaryOperator {multiary.MultiaryOperation} is not supported in in-memory search."),
                };
            }

            case UnionExpression union:
            {
                Func<SearchIndexEntry, bool>[] children = [.. union.Expressions.Select(CompileEntryMatcher)];
                return entry => children.Any(child => child(entry));
            }

            case NotExpression not:
            {
                Func<SearchIndexEntry, bool> inner = CompileEntryMatcher(not.Expression);
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
                return value => TryGetStringField(missing.FieldName, value, out string? present) && present is null;

            default:
                throw new SearchOperationNotSupportedException($"{expression.GetType().Name} is not supported within a search parameter expression in in-memory search.");
        }
    }

    /// <summary>The values a leaf addressing <paramref name="component"/> is tested against; none when the entry has no such component.</summary>
    private static IReadOnlyList<ISearchValue> ResolveValues(ISearchValue value, int? component) =>
        (component, value) switch
        {
            (null, _) => [value],
            (int index, CompositeIndexSearchValue composite) when index < composite.Components.Count => composite.Components[index],
            _ => [],
        };

    private static bool MatchesString(StringExpression expression, ISearchValue value)
    {
        if (!TryGetStringField(expression.FieldName, value, out string? fieldValue) || fieldValue is null)
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
            _ => throw new UnreachableException($"StringOperator {expression.StringOperator} passed validation."),
        };
    }

    /// <summary>
    /// Reads <paramref name="fieldName"/> from <paramref name="value"/>: false when the value is not of a kind that has the
    /// field (it cannot match), otherwise true with the field's value, null when it is absent or empty.
    /// </summary>
    private static bool TryGetStringField(FieldName fieldName, ISearchValue value, out string? fieldValue)
    {
        (bool applies, string? read) = (fieldName, value) switch
        {
            (FieldName.String, StringSearchValue s) => (true, s.String),
            (FieldName.TokenCode, TokenSearchValue token) => (true, token.Code),
            (FieldName.TokenSystem, TokenSearchValue token) => (true, token.System),
            (FieldName.TokenText, TokenSearchValue token) => (true, token.Text),
            (FieldName.IdentifierTypeSystem, TokenSearchValue token) => (true, token.IdentifierTypeSystem),
            (FieldName.IdentifierTypeCode, TokenSearchValue token) => (true, token.IdentifierTypeCode),
            (FieldName.TokenCode, OfTypeTokenSearchValue ofType) => (true, ofType.IdentifierValue),
            (FieldName.IdentifierTypeSystem, OfTypeTokenSearchValue ofType) => (true, ofType.TypeSystem),
            (FieldName.IdentifierTypeCode, OfTypeTokenSearchValue ofType) => (true, ofType.TypeCode),
            (FieldName.ReferenceResourceId, ReferenceSearchValue reference) => (true, reference.ResourceId),
            (FieldName.ReferenceResourceType, ReferenceSearchValue reference) => (true, reference.ResourceType),
            (FieldName.ReferenceBaseUri, ReferenceSearchValue reference) => (true, reference.BaseUri?.ToString()),
            (FieldName.QuantitySystem, QuantitySearchValue quantity) => (true, quantity.System),
            (FieldName.QuantityCode, QuantitySearchValue quantity) => (true, quantity.Code),
            (FieldName.Uri, UriSearchValue uri) => (true, uri.Uri),
            (FieldName.UriVersion, UriSearchValue uri) => (true, uri.Version),
            (FieldName.UriFragment, UriSearchValue uri) => (true, uri.Fragment),
            _ => (false, null),
        };

        fieldValue = read is { Length: > 0 } ? read : null;
        return applies;
    }

    private static bool MatchesBinary(BinaryExpression expression, ISearchValue value) =>
        (expression.FieldName, value) switch
        {
            (FieldName.DateTimeStart, DateTimeSearchValue date) => Satisfies(date.Start.CompareTo((DateTimeOffset)expression.Value), expression.BinaryOperator),
            (FieldName.DateTimeEnd, DateTimeSearchValue date) => Satisfies(date.End.CompareTo((DateTimeOffset)expression.Value), expression.BinaryOperator),
            (FieldName.NumberLow, NumberSearchValue number) => Satisfies(number.Low, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.NumberHigh, NumberSearchValue number) => Satisfies(number.High, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.QuantityLow, QuantitySearchValue quantity) => Satisfies(quantity.Low, (decimal)expression.Value, expression.BinaryOperator),
            (FieldName.QuantityHigh, QuantitySearchValue quantity) => Satisfies(quantity.High, (decimal)expression.Value, expression.BinaryOperator),
            _ => false,
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
            _ => throw new UnreachableException($"BinaryOperator {binaryOperator} passed validation."),
        };

    /// <summary>
    /// Rejects, before any entry is read, an expression this matcher cannot evaluate: an unknown node, field or operator, a
    /// comparison the field does not support, or a field the parameter's definition can never hold. A composite component
    /// may also hold a token or reference, the kinds the parser infers from a component value's shape.
    /// </summary>
    private static void Validate(Expression expression, SearchParameterInfo? parameter)
    {
        switch (expression)
        {
            case MultiaryExpression multiary when multiary.MultiaryOperation is MultiaryOperator.And or MultiaryOperator.Or:
                multiary.Expressions.ToList().ForEach(child => Validate(child, parameter));
                return;
            case UnionExpression union:
                union.Expressions.ToList().ForEach(child => Validate(child, parameter));
                return;
            case NotExpression not:
                Validate(not.Expression, parameter);
                return;
            case BinaryExpression binary when IsBound(binary.FieldName) && Enum.IsDefined(binary.BinaryOperator) && HasValueFor(binary):
                ValidateField(binary, parameter);
                return;
            case StringExpression str when !IsBound(str.FieldName) && Enum.IsDefined(str.StringOperator) && str.Value is not null:
                ValidateField(str, parameter);
                return;
            case MissingFieldExpression missing when !IsBound(missing.FieldName):
                ValidateField(missing, parameter);
                return;
            default:
                throw new SearchOperationNotSupportedException($"{expression} is not supported within a search parameter expression in in-memory search.");
        }
    }

    private static void ValidateField(IFieldExpression field, SearchParameterInfo? parameter)
    {
        SearchParamType family = Family(field.FieldName)
            ?? throw new SearchOperationNotSupportedException($"Field {field.FieldName} is not supported in in-memory search.");
        if (parameter is null)
        {
            return;
        }

        bool allowed = field.ComponentIndex switch
        {
            null => parameter.Type == family,
            int index when parameter.Type == SearchParamType.Composite && index >= 0 && index < parameter.Component.Count =>
                family is SearchParamType.Token or SearchParamType.Reference
                || parameter.Component[index].ResolvedSearchParameter?.Type == family,
            _ => false,
        };

        if (!allowed)
        {
            string where = field.ComponentIndex is int component ? $"component {component} of " : string.Empty;
            throw new SearchOperationNotSupportedException(
                $"Field {field.FieldName} cannot be compared for {where}{parameter.Type} search parameter '{parameter.Code}' in in-memory search.");
        }
    }

    private static bool HasValueFor(BinaryExpression binary) =>
        binary.FieldName is FieldName.DateTimeStart or FieldName.DateTimeEnd ? binary.Value is DateTimeOffset : binary.Value is decimal;

    private static bool IsBound(FieldName fieldName) =>
        fieldName is FieldName.DateTimeStart or FieldName.DateTimeEnd
            or FieldName.NumberLow or FieldName.NumberHigh
            or FieldName.QuantityLow or FieldName.QuantityHigh;

    /// <summary>The parameter type whose index values carry <paramref name="fieldName"/>; null for a field this matcher does not read.</summary>
    private static SearchParamType? Family(FieldName fieldName) =>
        fieldName switch
        {
            FieldName.String => SearchParamType.String,
            FieldName.TokenCode or FieldName.TokenSystem or FieldName.TokenText
                or FieldName.IdentifierTypeSystem or FieldName.IdentifierTypeCode => SearchParamType.Token,
            FieldName.ReferenceBaseUri or FieldName.ReferenceResourceType or FieldName.ReferenceResourceId => SearchParamType.Reference,
            FieldName.QuantitySystem or FieldName.QuantityCode or FieldName.QuantityLow or FieldName.QuantityHigh => SearchParamType.Quantity,
            FieldName.NumberLow or FieldName.NumberHigh => SearchParamType.Number,
            FieldName.DateTimeStart or FieldName.DateTimeEnd => SearchParamType.Date,
            FieldName.Uri or FieldName.UriVersion or FieldName.UriFragment => SearchParamType.Uri,
            _ => null,
        };
}
