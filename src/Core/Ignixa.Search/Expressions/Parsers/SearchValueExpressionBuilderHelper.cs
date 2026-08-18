// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System.Diagnostics;
using EnsureThat;
using Ignixa.Search.Extensions;
using Ignixa.Specification.ValueSets.Normative;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Flattens a typed <see cref="ISearchValue"/> into the old field-level <see cref="Expression"/> shape
/// (via <see cref="ISearchValueVisitor"/>) and validates the modifier/comparator against the value type.
/// The counterpart to <see cref="SearchPredicateExpressionBuilder"/>, which keeps the typed value instead
/// of flattening it.
/// </summary>
internal sealed class SearchValueExpressionBuilderHelper : ISearchValueVisitor
{
    private SearchComparator _comparator;
    private int? _componentIndex;
    private SearchModifier _modifier;

    private Expression _outputExpression;

    private string _searchParameterName;

    void ISearchValueVisitor.Visit(CompositeIndexSearchValue composite)
    {
        // Composite search values will be broken down into individual components,
        // and therefore this method should not be called.
        throw new InvalidOperationException("The composite search value should have been broken down into components and handled individually.");
    }

    void ISearchValueVisitor.Visit(DateTimeSearchValue dateTime)
    {
        EnsureArg.IsNotNull(dateTime, nameof(dateTime));

        if (_modifier != null) ThrowModifierNotSupported();

        // The prefix table lives in DateRangeComparisonSemantics, which the SQL compiler renders too. It
        // used to be restated here, and the restatement had drifted: eq was an overlap ("practical
        // interpretation") rather than the spec's containment, which made eq and ne non-complementary --
        // a month-long Period row satisfied BOTH date=eq<one-day> and date=ne<one-day> -- and ap was a
        // containment rather than the spec's overlap. Do not reintroduce a local copy to "fix" a prefix.
        if (!Enum.IsDefined(typeof(SearchComparator), _comparator))
        {
            ThrowComparatorNotSupported();
            return;
        }

        _outputExpression = DateRangePredicateExpressionRenderer.Render(
            DateRangeComparisonSemantics.Build(_comparator, dateTime, DateTimeOffset.UtcNow),
            _componentIndex);
    }

    void ISearchValueVisitor.Visit(NumberSearchValue number)
    {
        EnsureArg.IsNotNull(number, nameof(number));

        if (_modifier != null) ThrowModifierNotSupported();

        Debug.Assert(number.Low.HasValue && number.Low == number.High, "number low and high should be the same and not null");
        _outputExpression = GenerateNumberExpression(FieldName.NumberLow, FieldName.NumberHigh, number.Low.Value);
    }

    void ISearchValueVisitor.Visit(QuantitySearchValue quantity)
    {
        EnsureArg.IsNotNull(quantity, nameof(quantity));

        if (_modifier != null) ThrowModifierNotSupported();

        var expressions = new List<Expression>(3);

        // Based on spec http://hl7.org/fhir/Stu3/search.html#quantity,
        // The system is handled differently in quantity than token.
        if (!string.IsNullOrWhiteSpace(quantity.System))
            expressions.Add(
                Expression.StringEquals(FieldName.QuantitySystem, _componentIndex, quantity.System, false));

        if (!string.IsNullOrWhiteSpace(quantity.Code))
            expressions.Add(
                Expression.StringEquals(FieldName.QuantityCode, _componentIndex, quantity.Code, false));

        Debug.Assert(quantity.Low.HasValue && quantity.Low == quantity.High, "quantity low and high should be the same and not null");
        expressions.Add(GenerateNumberExpression(FieldName.QuantityLow, FieldName.QuantityHigh, quantity.Low.Value));

        if (expressions.Count == 1)
            _outputExpression = expressions[0];
        else
            _outputExpression = Expression.And(expressions.ToArray());
    }

    void ISearchValueVisitor.Visit(ReferenceSearchValue reference)
    {
        EnsureArg.IsNotNull(reference, nameof(reference));

        if (_modifier != null && _modifier.SearchModifierCode != SearchModifierCode.Type) ThrowModifierNotSupported();

        EnsureOnlyEqualComparatorIsSupported();

        if (reference.BaseUri != null)
            // The reference is external.
            _outputExpression = Expression.And(
                Expression.StringEquals(FieldName.ReferenceBaseUri, _componentIndex, reference.BaseUri.ToString(), false),
                Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
        else if (reference.ResourceType == null)
            // Only resource id is specified.
            _outputExpression = Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false);
        else if (reference.Kind == ReferenceKind.Internal)
            // The reference must be internal.
            _outputExpression = Expression.And(
                Expression.Missing(FieldName.ReferenceBaseUri, _componentIndex),
                Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
        else
            // The reference can be internal or external.
            _outputExpression = Expression.And(
                Expression.StringEquals(FieldName.ReferenceResourceType, _componentIndex, reference.ResourceType, false),
                Expression.StringEquals(FieldName.ReferenceResourceId, _componentIndex, reference.ResourceId, false));
    }

    void ISearchValueVisitor.Visit(StringSearchValue s)
    {
        EnsureArg.IsNotNull(s, nameof(s));

        EnsureOnlyEqualComparatorIsSupported();

        if (_modifier == null)
            // Based on spec http://hl7.org/fhir/Stu3/search.html#string,
            // is case-insensitive search so we will normalize into lower case for search.
            _outputExpression = Expression.StartsWith(FieldName.String, _componentIndex, s.String, true);
        else if (_modifier.SearchModifierCode == SearchModifierCode.Exact)
            _outputExpression = Expression.StringEquals(FieldName.String, _componentIndex, s.String, false);
        else if (_modifier.SearchModifierCode == SearchModifierCode.Contains)
            // Based on spec http://hl7.org/fhir/Stu3/search.html#modifiers,
            // contains is case-insensitive search so we will normalize into lower case for search.
            _outputExpression = Expression.Contains(FieldName.String, _componentIndex, s.String, true);
        else
            ThrowModifierNotSupported();
    }

    void ISearchValueVisitor.Visit(TokenSearchValue token)
    {
        EnsureArg.IsNotNull(token, nameof(token));

        EnsureOnlyEqualComparatorIsSupported();

        if (_modifier == null)
            _outputExpression = BuildEqualityExpression();
        else if (_modifier.SearchModifierCode == SearchModifierCode.Not)
            _outputExpression = Expression.Not(BuildEqualityExpression());
        else if (_modifier.SearchModifierCode == SearchModifierCode.Above ||
                 _modifier.SearchModifierCode == SearchModifierCode.Below ||
                 _modifier.SearchModifierCode == SearchModifierCode.In ||
                 _modifier.SearchModifierCode == SearchModifierCode.NotIn)
            // These modifiers are not supported yet but will be supported eventually.
            ThrowModifierNotSupported();
        else
            ThrowModifierNotSupported();

        Expression BuildEqualityExpression()
        {
            // Based on spec http://hl7.org/fhir/search.html#token,
            // we need to make sure to test if system is missing or not based on how it is supplied.
            if (token.System == null)
                // If the system is not supplied, then the token code is matched irrespective of the value of system.
                return Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false);
            else if (token.System.Length == 0)
                // If the system is empty, then the token is matched if there is no system property.
                return Expression.And(
                    Expression.Missing(FieldName.TokenSystem, _componentIndex),
                    Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false));
            else if (string.IsNullOrWhiteSpace(token.Code))
                // If the code is empty, then the token is matched if system is matched.
                return Expression.StringEquals(FieldName.TokenSystem, _componentIndex, token.System, false);
            else
                return Expression.And(
                    Expression.StringEquals(FieldName.TokenSystem, _componentIndex, token.System, false),
                    Expression.StringEquals(FieldName.TokenCode, _componentIndex, token.Code, false));
        }
    }


    void ISearchValueVisitor.Visit(OfTypeTokenSearchValue ofTypeToken)
    {
        EnsureArg.IsNotNull(ofTypeToken, nameof(ofTypeToken));

        EnsureOnlyEqualComparatorIsSupported();

        var expressions = new List<Expression>();

        if (ofTypeToken.TypeSystem != null)
        {
            expressions.Add(Expression.StringEquals(FieldName.IdentifierTypeSystem, _componentIndex, ofTypeToken.TypeSystem, false));
        }

        expressions.Add(Expression.StringEquals(FieldName.IdentifierTypeCode, _componentIndex, ofTypeToken.TypeCode, false));
        expressions.Add(Expression.StringEquals(FieldName.TokenCode, _componentIndex, ofTypeToken.IdentifierValue, false));

        _outputExpression = Expression.And(expressions.ToArray());
    }

    void ISearchValueVisitor.Visit(UriSearchValue uri)
    {
        EnsureArg.IsNotNull(uri, nameof(uri));

        switch (_modifier?.SearchModifierCode)
        {
            case null:
                _outputExpression = BuildCanonicalExpression(uri);
                break;
            case SearchModifierCode.Above:
                _outputExpression = Expression.And(
                    Expression.LeftSideStartsWith(FieldName.Uri, _componentIndex, uri.Uri, false),
                    Expression.NotStartsWith(FieldName.Uri, _componentIndex, KnownUriSchemes.Urn, false));
                break;
            case SearchModifierCode.Below:
                _outputExpression = Expression.StartsWith(FieldName.Uri, _componentIndex, uri.Uri, false);
                break;
            default:
                ThrowModifierNotSupported();
                break;
        }

        Expression BuildCanonicalExpression(UriSearchValue uriValue)
        {
            var expressions = new List<Expression>
            {
                Expression.StringEquals(FieldName.Uri, _componentIndex, uriValue.Uri, false)
            };

            if (!string.IsNullOrWhiteSpace(uriValue.Version))
            {
                expressions.Add(Expression.StringEquals(FieldName.UriVersion, _componentIndex, uriValue.Version, false));
            }

            if (!string.IsNullOrWhiteSpace(uriValue.Fragment))
            {
                expressions.Add(Expression.StringEquals(FieldName.UriFragment, _componentIndex, uriValue.Fragment, false));
            }

            if (expressions.Count == 1)
                return expressions[0];
            else
                return Expression.And(expressions.ToArray());
        }
    }

    public Expression Build(
        string searchParameterName,
        SearchModifier modifier,
        SearchComparator comparator,
        int? componentIndex,
        ISearchValue searchValue)
    {
        EnsureArg.IsNotNullOrWhiteSpace(searchParameterName, nameof(searchParameterName));
        Debug.Assert(
            Enum.IsDefined(typeof(SearchComparator), comparator),
            "Invalid comparator.");
        EnsureArg.IsNotNull(searchValue, nameof(searchValue));

        _searchParameterName = searchParameterName;
        _modifier = modifier;
        _comparator = comparator;
        _componentIndex = componentIndex;

        searchValue.AcceptVisitor(this);

        return _outputExpression;
    }

    private void EnsureOnlyEqualComparatorIsSupported()
    {
        if (_comparator != SearchComparator.Eq) throw new InvalidSearchOperationException(Resources.OnlyEqualComparatorIsSupported);
    }

    private void ThrowModifierNotSupported()
    {
        throw new SearchModifierNotSupportedException(
            string.Format(Resources.ModifierNotSupported, _modifier, _searchParameterName));
    }

    private void ThrowComparatorNotSupported()
    {
        throw new InvalidSearchOperationException(
            string.Format(Resources.ComparatorNotSupported, _comparator, _searchParameterName));
    }

    /// <summary>
    /// Lowers a comparator over a stored range [<paramref name="lowField"/>, <paramref name="highField"/>] by
    /// rendering <see cref="NumericRangeComparisonSemantics"/>, which the SQL compiler renders too.
    /// </summary>
    /// <remarks>
    /// The prefix table used to be restated here, and the restatement had drifted: <c>ap</c> widened the
    /// window and then asked for containment rather than the spec's overlap, so it under-matched — a row
    /// whose extent straddled the edge of the tolerance window was dropped. This is the same defect
    /// <see cref="DateRangeComparisonSemantics"/> closed for dates. Do not reintroduce a local copy to "fix"
    /// a prefix.
    /// </remarks>
    private Expression GenerateNumberExpression(FieldName lowField, FieldName highField, decimal number)
    {
        if (!Enum.IsDefined(typeof(SearchComparator), _comparator))
        {
            ThrowComparatorNotSupported();
            return null;
        }

        return NumericRangePredicateExpressionRenderer.Render(
            NumericRangeComparisonSemantics.Build(_comparator, number),
            lowField,
            highField,
            _componentIndex);
    }
}
