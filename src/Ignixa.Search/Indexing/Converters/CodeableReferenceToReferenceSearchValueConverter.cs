// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Serialization.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="CodeableReference "/> to a list of <see cref="ReferenceSearchValue"/>.
/// </summary>
public class CodeableReferenceToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
{
    private readonly ResourceReferenceToReferenceSearchValueConverter _referenceSearchValueParser;

    public CodeableReferenceToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
        : base("CodeableReference")
    {
        EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

        _referenceSearchValueParser = new ResourceReferenceToReferenceSearchValueConverter(referenceSearchValueParser);
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        var reference = value.Scalar("reference") as ITypedElement;

        if (reference == null) return Enumerable.Empty<ISearchValue>();

        return _referenceSearchValueParser.ConvertTo(reference);
    }
}
