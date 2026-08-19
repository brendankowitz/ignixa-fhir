// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;

namespace Ignixa.Search.Indexing.Converters;

public abstract class FhirElementToSearchValueConverter<T> : IElementToSearchValueConverter
{
    protected FhirElementToSearchValueConverter(params string[] fhirTypes)
    {
        EnsureArg.HasItems(fhirTypes, nameof(fhirTypes));

        FhirTypes = fhirTypes;
    }

    public IReadOnlyList<string> FhirTypes { get; }

    public Type SearchValueType { get; } = typeof(T);

    /// <summary>
    /// Converts an element, and materializes the result before returning it.
    /// <para>
    /// The <c>.ToList()</c> is the contract, not a convenience. Nearly every <see cref="Convert"/> override is
    /// either a <c>yield</c> iterator or an unterminated LINQ chain, so without it no conversion work happens
    /// until the caller enumerates - and the caller enumerates somewhere else entirely, typically outside
    /// whatever try block it set up around this call. A converter that throws on a malformed literal
    /// (<c>PartialDateTime.Parse</c> on a bad <c>Timing.event</c>, <c>decimal.Parse</c> on a bad Quantity)
    /// would surface that exception at the caller's enumeration point rather than here, which on the indexing
    /// path meant one bad element failed the whole resource write. Materializing here makes the throw happen
    /// where the call happens, so an ordinary try/catch around <c>ConvertTo</c> actually catches it.
    /// </para>
    /// </summary>
    public IEnumerable<ISearchValue> ConvertTo(IElement value)
    {
        if (value == null) return Enumerable.Empty<ISearchValue>();

        if (!FhirTypes.Contains(value.InstanceType)) throw new ArgumentOutOfRangeException(nameof(value));

        return Convert(value).ToList();
    }

    protected abstract IEnumerable<ISearchValue> Convert(IElement value);
}
