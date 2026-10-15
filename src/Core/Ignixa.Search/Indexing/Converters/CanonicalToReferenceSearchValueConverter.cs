// -------------------------------------------------------------------------------------------------
// Copyright (c) Ignixa Contributors. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

#nullable enable

using Ignixa.Abstractions;
using Ignixa.Search.Indexing.SearchValues;

namespace Ignixa.Search.Indexing.Converters;

/// <summary>
/// Converts a <c>canonical</c> selected by a reference search parameter (R5 <c>_profile</c> over <c>meta.profile</c>,
/// <c>instantiates-canonical</c>, <c>QuestionnaireResponse-questionnaire</c>) to a <see cref="ReferenceSearchValue"/>.
/// </summary>
/// <remarks>
/// <para>
/// The reference search section says "the reference search parameter is mostly used for resource elements of type
/// Reference or canonical" and matches a canonical by its URL (R4 §3.1.1.4.12, R5 §3.2.1.5.11). Before this converter
/// the pair (canonical, reference) had none, so every reference parameter over a canonical indexed nothing (#430).
/// </para>
/// <para>
/// The value is parsed by the same <see cref="IReferenceSearchValueParser"/> the query path uses, so a search names the
/// value in the form it was stored. A <c>|version</c> suffix is removed first: a canonical searched without a version
/// matches every version (R5 §3.2.1.6.2), and <see cref="ReferenceSearchValue"/> - like the SQL reference rows - has no
/// slot for a business version. The parser already ignores the suffix of a URL ending in <c>Type/id</c>; removing it
/// here makes a canonical of any other shape agree. A search that names a version therefore matches every version of
/// that canonical. Contained (<c>#</c>) canonicals are not searchable, as for references.
/// </para>
/// </remarks>
public class CanonicalToReferenceSearchValueConverter(IReferenceSearchValueParser referenceSearchValueParser)
    : FhirElementToSearchValueConverter<ReferenceSearchValue>("canonical")
{
    private readonly IReferenceSearchValueParser _referenceSearchValueParser =
        referenceSearchValueParser ?? throw new ArgumentNullException(nameof(referenceSearchValueParser));

    protected override IEnumerable<ISearchValue> Convert(IElement value)
    {
        string? canonical = value?.Value?.ToString();
        if (string.IsNullOrWhiteSpace(canonical) || canonical.StartsWith('#'))
        {
            return [];
        }

        var parts = new UriSearchValue(canonical, separateCanonicalComponents: true);
        string unversioned = parts.Fragment is null ? parts.Uri : $"{parts.Uri}#{parts.Fragment}";
        return [_referenceSearchValueParser.Parse(unversioned)];
    }
}
