// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------
// Copied from fhir-candle src/FhirCandle.Engine/Search/CandleSearchParameterExpressionParser.cs at 6d0af1e (MIT); adapted for cli-on-fhir.

using System.Globalization;
using System.Text;
using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Expressions;
using Ignixa.Search.Expressions.Parsers;
using Ignixa.Search.Indexing;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Search.Models;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.CliOnFhir.Search.Candle;

/// <summary>Thrown instead of Ignixa's <see cref="BadSearchRequestException"/> so the parameter
/// that failed to parse can be reported (strict handling) or dropped (lenient handling) by name -
/// Ignixa's exception only carries the value-level message.</summary>
public sealed class CandleMalformedSearchParameterException(string parameterCode, string message)
    : Exception(message)
{
    public string ParameterCode { get; } = parameterCode;
}

/// <summary>
/// Wraps Ignixa's <see cref="SearchParameterExpressionParser"/> to restore pre-migration search
/// semantics its defaults diverge from:
/// <list type="bullet">
/// <item>date <c>ap</c> uses a precision-derived flat window (year ±365d, month ±65d, day ±31d,
/// time ±1d) with overlap matching, not Ignixa's ±10%-of-time-since-value containment;</item>
/// <item>token values with an explicit empty system (<c>|code</c>) match on code alone, not
/// "system must be absent";</item>
/// <item>composite values are parsed against each component's declared type - Ignixa's
/// value-shape inference misclassifies quantity components containing <c>|</c> as tokens and
/// throws "Only one token separator can be specified";</item>
/// <item>a reference type modifier contradicting a typed value (<c>subject:Device=Patient/x</c>)
/// matches nothing instead of throwing (which lenient handling would swallow, skipping the
/// filter entirely).</item>
/// </list>
/// </summary>
public sealed class CandleSearchParameterExpressionParser : ISearchParameterExpressionParser
{
    private readonly SearchParameterExpressionParser _inner;

    public CandleSearchParameterExpressionParser(IReferenceSearchValueParser referenceParser, IFhirSchemaProvider schema)
    {
        _inner = new SearchParameterExpressionParser(referenceParser, schema);
    }

    public Expression Parse(SearchParameterInfo searchParameter, SearchModifier modifier, string value)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        try
        {
            return ParseCore(searchParameter, modifier, value).Expression;
        }
        catch (Exception ex) when (ex is BadSearchRequestException or FormatException or OverflowException)
        {
            throw new CandleMalformedSearchParameterException(searchParameter.Code, ex.Message);
        }
    }

    /// <summary>
    /// Candle never asks for a search trace - <see cref="CandleSearchService"/> builds its search
    /// options without a trace collector, which is the only path that reaches this method - so it
    /// exists to satisfy the interface. Where candle defers to Ignixa the projection is Ignixa's;
    /// where candle overrides, the inner parser rejects the value outright (which is why it is
    /// overridden), so a flat span over the raw value is projected instead.
    /// </summary>
    public (Expression Expression, SyntaxNode ValueSyntax) ParseWithSyntax(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        ArgumentNullException.ThrowIfNull(searchParameter);

        try
        {
            (Expression expression, string? delegatedValue) = ParseCore(searchParameter, modifier, value);

            SyntaxNode syntax = delegatedValue is null
                ? new SyntaxNode("Atomic", new SourceSpan(SourceOrigin.Value, 0, value?.Length ?? 0), [])
                : _inner.ParseWithSyntax(searchParameter, modifier, delegatedValue).ValueSyntax;

            return (expression, syntax);
        }
        catch (Exception ex) when (ex is BadSearchRequestException or FormatException or OverflowException)
        {
            throw new CandleMalformedSearchParameterException(searchParameter.Code, ex.Message);
        }
    }

    /// <summary>Applies candle's overrides. <c>DelegatedValue</c> is the value actually handed to the
    /// inner parser, or null when candle built the expression itself.</summary>
    private (Expression Expression, string? DelegatedValue) ParseCore(
        SearchParameterInfo searchParameter,
        SearchModifier modifier,
        string value)
    {
        if (searchParameter.Type == SearchParamType.Composite && modifier is null)
        {
            return (ParseComposite(searchParameter, value), null);
        }

        if (searchParameter.Type == SearchParamType.Date &&
            modifier is null &&
            SplitEscaped(value, ',').Any(HasApPrefix))
        {
            return (ParseDateWithAp(searchParameter, value), null);
        }

        // Deviation from candle (cli-on-fhir): candle stripped the leading '|' of "|code" so it matched the code under any
        // system. FHIR (and Ignixa's parser) define "|code" as "no system", so the value is passed through unchanged.

        if (searchParameter.Type == SearchParamType.Reference &&
            modifier?.SearchModifierCode == SearchModifierCode.Type &&
            HasContradictoryTypePrefix(value, modifier.ResourceType))
        {
            return (Expression.SearchParameter(
                searchParameter,
                Expression.StringEquals(FieldName.ReferenceResourceId, null, "never-match", false)), null);
        }

        return (_inner.Parse(searchParameter, modifier, value), value);
    }

    private Expression ParseDateWithAp(SearchParameterInfo searchParameter, string value)
    {
        IReadOnlyList<string> parts = SplitEscaped(value, ',');
        Expression[] expressions = [.. parts.Select(part => HasApPrefix(part)
            ? BuildApExpression(part[2..])
            : ((SearchParameterExpression)_inner.Parse(searchParameter, null, part)).Expression)];

        return Expression.SearchParameter(
            searchParameter,
            expressions.Length == 1 ? expressions[0] : Expression.Or(expressions));
    }

    private static Expression BuildApExpression(string dateLiteral)
    {
        DateTimeSearchValue date = DateTimeSearchValue.Parse(dateLiteral);
        TimeSpan delta = dateLiteral.Length switch
        {
            4 => TimeSpan.FromDays(365),
            7 => TimeSpan.FromDays(65),
            10 => TimeSpan.FromDays(31),
            _ => TimeSpan.FromDays(1),
        };

        return Expression.And(
            Expression.LessThanOrEqual(FieldName.DateTimeStart, null, date.End + delta),
            Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, null, date.Start - delta));
    }

    private static bool HasApPrefix(string part) =>
        part.Length > 2 && part.StartsWith("ap", StringComparison.Ordinal) && char.IsDigit(part[2]);

    private static bool HasContradictoryTypePrefix(string value, string modifierResourceType)
    {
        int slash = value.IndexOf('/', StringComparison.Ordinal);
        if (slash <= 0)
        {
            return false;
        }

        string prefix = value[..slash];
        return prefix.All(char.IsLetter) &&
            char.IsUpper(prefix[0]) &&
            !prefix.Equals(modifierResourceType, StringComparison.OrdinalIgnoreCase);
    }

    // Splits on the composite separator FIRST, treating each component segment as its own
    // or-list - the pre-migration engine accepted per-component alternatives
    // (`code-value-quantity=codeA,codeB$qtyA,qtyB` means (codeA or codeB) and (qtyA or qtyB)),
    // which is what the regression suite encodes.
    private Expression ParseComposite(SearchParameterInfo searchParameter, string value)
    {
        IReadOnlyList<string> componentSegments = SplitEscaped(value, '$');

        if (componentSegments.Count > searchParameter.Component.Count)
        {
            throw new BadSearchRequestException(
                $"Composite search parameter '{searchParameter.Code}' accepts at most {searchParameter.Component.Count} components.");
        }

        var componentExpressions = new List<Expression>(componentSegments.Count);
        for (int i = 0; i < componentSegments.Count; i++)
        {
            SearchParameterInfo component = searchParameter.Component[i].ResolvedSearchParameter
                ?? throw new InvalidSearchOperationException(
                    $"Composite search parameter '{searchParameter.Code}' component {i} is not resolved.");

            int componentIndex = i;
            Expression[] alternatives = [.. SplitEscaped(componentSegments[i], ',')
                .Select(alternative => BuildComponentExpression(component.Type, componentIndex, alternative))];

            componentExpressions.Add(alternatives.Length == 1 ? alternatives[0] : Expression.Or(alternatives));
        }

        return Expression.SearchParameter(
            searchParameter,
            componentExpressions.Count == 1 ? componentExpressions[0] : Expression.And([.. componentExpressions]));
    }

    private static Expression BuildComponentExpression(SearchParamType componentType, int componentIndex, string value)
    {
        switch (componentType)
        {
            case SearchParamType.Token:
            {
                TokenSearchValue token = (TokenSearchValue)TokenSearchValue.Parse(value);

                // Deviation from candle (cli-on-fhir): "|code" means "no system" in FHIR, not "any system".
                if (token.System is { Length: 0 })
                {
                    return Expression.And(
                        Expression.Missing(FieldName.TokenSystem, componentIndex),
                        Expression.StringEquals(FieldName.TokenCode, componentIndex, token.Code, false));
                }

                if (string.IsNullOrEmpty(token.System))
                {
                    return Expression.StringEquals(FieldName.TokenCode, componentIndex, token.Code, false);
                }

                if (string.IsNullOrWhiteSpace(token.Code))
                {
                    return Expression.StringEquals(FieldName.TokenSystem, componentIndex, token.System, false);
                }

                return Expression.And(
                    Expression.StringEquals(FieldName.TokenSystem, componentIndex, token.System, false),
                    Expression.StringEquals(FieldName.TokenCode, componentIndex, token.Code, false));
            }

            case SearchParamType.Quantity:
            {
                (SearchComparator comparator, string literal) = SplitComparator(value);
                QuantitySearchValue quantity = (QuantitySearchValue)QuantitySearchValue.Parse(literal);

                var expressions = new List<Expression>(3);
                if (!string.IsNullOrWhiteSpace(quantity.System))
                {
                    expressions.Add(Expression.StringEquals(FieldName.QuantitySystem, componentIndex, quantity.System, false));
                }

                if (!string.IsNullOrWhiteSpace(quantity.Code))
                {
                    expressions.Add(Expression.StringEquals(FieldName.QuantityCode, componentIndex, quantity.Code, false));
                }

                string valueLiteral = SplitEscaped(literal, '|')[0];
                expressions.Add(BuildNumberExpression(FieldName.QuantityLow, FieldName.QuantityHigh, componentIndex, quantity.Low!.Value, valueLiteral, comparator));

                return expressions.Count == 1 ? expressions[0] : Expression.And([.. expressions]);
            }

            case SearchParamType.Number:
            {
                (SearchComparator comparator, string literal) = SplitComparator(value);
                NumberSearchValue number = (NumberSearchValue)NumberSearchValue.Parse(literal);
                return BuildNumberExpression(FieldName.NumberLow, FieldName.NumberHigh, componentIndex, number.Low!.Value, literal, comparator);
            }

            case SearchParamType.Date:
            {
                (SearchComparator comparator, string literal) = SplitComparator(value);
                DateTimeSearchValue date = DateTimeSearchValue.Parse(literal);

                return comparator switch
                {
                    SearchComparator.Eq => Expression.And(
                        Expression.LessThanOrEqual(FieldName.DateTimeStart, componentIndex, date.End),
                        Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, componentIndex, date.Start)),
                    SearchComparator.Ne => Expression.Or(
                        Expression.LessThan(FieldName.DateTimeStart, componentIndex, date.Start),
                        Expression.GreaterThan(FieldName.DateTimeEnd, componentIndex, date.End)),
                    SearchComparator.Lt => Expression.LessThan(FieldName.DateTimeStart, componentIndex, date.Start),
                    SearchComparator.Gt => Expression.GreaterThan(FieldName.DateTimeEnd, componentIndex, date.End),
                    SearchComparator.Le => Expression.LessThanOrEqual(FieldName.DateTimeStart, componentIndex, date.End),
                    SearchComparator.Ge => Expression.GreaterThanOrEqual(FieldName.DateTimeEnd, componentIndex, date.Start),
                    _ => throw new InvalidSearchOperationException($"Comparator {comparator} is not supported for composite date components."),
                };
            }

            case SearchParamType.String:
            {
                StringSearchValue s = (StringSearchValue)StringSearchValue.Parse(value);
                return Expression.StartsWith(FieldName.String, componentIndex, s.String, true);
            }

            default:
                throw new InvalidSearchOperationException(
                    $"Composite component type {componentType} is not supported.");
        }
    }

    // Ignixa split the single Number/Quantity field into Low/High bounds; which bound a comparator
    // tests follows Ignixa's own builder - greater-than tests the high bound, less-than the low one.
    private static Expression BuildNumberExpression(FieldName lowField, FieldName highField, int componentIndex, decimal number, string literal, SearchComparator comparator)
    {
        decimal precision = PrecisionModifier(literal);

        return comparator switch
        {
            SearchComparator.Eq => Expression.And(
                Expression.GreaterThanOrEqual(lowField, componentIndex, number - precision),
                Expression.LessThanOrEqual(highField, componentIndex, number + precision)),
            SearchComparator.Ne => Expression.Or(
                Expression.LessThan(lowField, componentIndex, number - precision),
                Expression.GreaterThan(highField, componentIndex, number + precision)),
            SearchComparator.Ge => Expression.GreaterThanOrEqual(highField, componentIndex, number),
            SearchComparator.Gt => Expression.GreaterThan(highField, componentIndex, number),
            SearchComparator.Le => Expression.LessThanOrEqual(lowField, componentIndex, number),
            SearchComparator.Lt => Expression.LessThan(lowField, componentIndex, number),
            _ => throw new InvalidSearchOperationException($"Comparator {comparator} is not supported for composite number components."),
        };
    }

    // half of the last significant digit's place, per FHIR's implicit-precision number search
    // (185 => ±0.5, 18.5 => ±0.05)
    private static decimal PrecisionModifier(string literal)
    {
        string digits = literal.Split('|')[0].Trim();
        int dot = digits.IndexOf('.', StringComparison.Ordinal);
        int decimals = dot < 0 ? 0 : digits.Length - dot - 1;
        return 0.5m / (decimal)Math.Pow(10, decimals);
    }

    private static (SearchComparator Comparator, string Literal) SplitComparator(string value)
    {
        if (value.Length > 2 && char.IsLetter(value[0]) && char.IsLetter(value[1]))
        {
            SearchComparator? comparator = value[..2] switch
            {
                "eq" => SearchComparator.Eq,
                "ne" => SearchComparator.Ne,
                "gt" => SearchComparator.Gt,
                "lt" => SearchComparator.Lt,
                "ge" => SearchComparator.Ge,
                "le" => SearchComparator.Le,
                "sa" => SearchComparator.Sa,
                "eb" => SearchComparator.Eb,
                "ap" => SearchComparator.Ap,
                _ => null,
            };

            if (comparator is not null)
            {
                return (comparator.Value, value[2..]);
            }
        }

        return (SearchComparator.Eq, value);
    }

    /// <summary>Splits on an unescaped separator, preserving backslash escapes for the downstream
    /// value parsers (which perform their own unescaping).</summary>
    private static IReadOnlyList<string> SplitEscaped(string value, char separator)
    {
        var parts = new List<string>();
        var current = new StringBuilder();
        bool escaped = false;

        foreach (char c in value)
        {
            if (escaped)
            {
                current.Append(c);
                escaped = false;
            }
            else if (c == '\\')
            {
                current.Append(c);
                escaped = true;
            }
            else if (c == separator)
            {
                parts.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        parts.Add(current.ToString());
        return parts;
    }
}
