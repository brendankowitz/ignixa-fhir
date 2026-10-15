// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using Ignixa.Abstractions;
using Ignixa.FhirPath.Evaluation;
using Ignixa.Search.Indexing.Converters;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.CliOnFhir.Indexing;

/// <summary>
/// Indexes Reference like Ignixa's stock converter, except that a <c>urn:</c> reference (a Bundle's <c>urn:uuid</c>
/// fullUrl) is kept as an untyped reference whose id is the whole urn, where the stock converter drops it. Searches still
/// cannot follow it (it names no <c>Type/id</c>), but the index now shows, per reference parameter, that it exists, so a
/// chain or include through it is reported rather than silently empty, and <c>subject=urn:uuid:...</c> matches it.
/// Contained (<c>#</c>) references stay unindexed. Kept local to the tool: the server's rows are unchanged.
/// </summary>
internal sealed class UrnAwareReferenceConverter(IReferenceSearchValueParser referenceParser) : FhirElementToSearchValueConverter<ReferenceSearchValue>("Reference", "ResourceReference")
{
    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        if (value.Scalar("reference") is not string reference || reference.StartsWith('#'))
        {
            yield break;
        }

        yield return reference.StartsWith("urn:", StringComparison.Ordinal)
            ? new ReferenceSearchValue(ReferenceKind.InternalOrExternal, null!, null!, reference)
            : referenceParser.Parse(reference);
    }
}
