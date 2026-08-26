// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.All rights reserved.
// Licensed under the MIT License (MIT).See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using EnsureThat;
using Ignixa.Search.Indexing.SearchValues;
using Ignixa.Abstractions;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// A converter used to convert from <see cref="ResourceReference"/> to a list of <see cref="ReferenceSearchValue"/>.
/// </summary>
/// <remarks>
/// Registered under both spellings because the rest of the pipeline already treats them as one type:
/// <c>ElementSearchIndexer.InferSearchParamTypeFromFhirType</c> maps both to
/// <c>SearchParamType.Reference</c>, its STU3 target-type filter matches both, and
/// <c>FhirSpecificFunctions</c> resolves both. Registering only <c>Reference</c> left the converter as
/// the single place that disagreed, so an element arriving with the Firely POCO spelling would be
/// inferred as a reference and then skipped as unsupported. Ignixa's own parser produces
/// <c>Reference</c>, so this closes a latent inconsistency rather than a measured gap.
/// </remarks>
public class ResourceReferenceToReferenceSearchValueConverter : FhirElementToSearchValueConverter<ReferenceSearchValue>
{
    private readonly IReferenceSearchValueParser _referenceSearchValueParser;

    public ResourceReferenceToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
        : base("Reference", "ResourceReference")
    {
        EnsureArg.IsNotNull(referenceSearchValueParser, nameof(referenceSearchValueParser));

        _referenceSearchValueParser = referenceSearchValueParser;
    }

    protected override IEnumerable<ISearchValue> Convert(IElement value)
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
