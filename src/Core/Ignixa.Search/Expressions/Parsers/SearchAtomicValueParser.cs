// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Exceptions;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Specification.ValueSets.Normative;

namespace Ignixa.Search.Expressions.Parsers;

/// <summary>
/// Parses one atomic value's raw text into a typed <see cref="ISearchValue"/> for the search
/// parameter's type (string, token, reference, date, quantity, number, uri).
/// </summary>
internal sealed class SearchAtomicValueParser
{
    private readonly IReadOnlyDictionary<SearchParamType, Func<string, ISearchValue>> _parsers;

    internal SearchAtomicValueParser(
        IReferenceSearchValueParser referenceParser,
        IFhirSchemaProvider schemaProvider)
    {
        ArgumentNullException.ThrowIfNull(referenceParser);
        ArgumentNullException.ThrowIfNull(schemaProvider);

        _parsers = new Dictionary<SearchParamType, Func<string, ISearchValue>>
        {
            [SearchParamType.Date] = DateTimeSearchValue.Parse,
            [SearchParamType.Number] = NumberSearchValue.Parse,
            [SearchParamType.Quantity] = QuantitySearchValue.Parse,
            [SearchParamType.Reference] = referenceParser.Parse,
            [SearchParamType.String] = StringSearchValue.Parse,
            [SearchParamType.Token] = TokenSearchValue.Parse,
            [SearchParamType.Uri] = value => UriSearchValue.Parse(value, false, schemaProvider),
        };
    }

    internal ISearchValue Parse(SearchParamType type, string rawText)
    {
        return MapAtomicErrors(() => _parsers[type](rawText));
    }

    internal OfTypeTokenSearchValue ParseOfType(string rawText)
    {
        return MapAtomicErrors(() => OfTypeTokenSearchValue.Parse(rawText));
    }

    private static T MapAtomicErrors<T>(Func<T> parser)
    {
        try
        {
            return parser();
        }
        catch (FormatException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
        catch (OverflowException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
        catch (ArgumentException exception)
        {
            throw new BadSearchRequestException(exception.Message);
        }
    }
}
