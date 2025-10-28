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
/// A converter used to convert from <see cref="ResourceReference"/> to a list of <see cref="ReferenceSearchValue"/>.
/// </summary>
public class ResourceReferenceToReferenceSearchValueConverter : FhirTypedElementToSearchValueConverter<ReferenceSearchValue>
{
    private readonly IReferenceSearchValueParser _referenceSearchValueParser;

    public ResourceReferenceToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
        : base("Reference")
    {
        EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

        _referenceSearchValueParser = referenceSearchValueParser;
    }

    protected override IEnumerable<ISearchValue> Convert(ITypedElement value)
    {
        string reference = value.Scalar("reference") as string;

        if (reference == null) yield break;

        // Contained resources will not be searchable.
        if (reference.StartsWith("#", StringComparison.Ordinal)
            || reference.StartsWith("urn:", StringComparison.Ordinal))
            yield break;

        yield return _referenceSearchValueParser.Parse(reference);
    }
}
